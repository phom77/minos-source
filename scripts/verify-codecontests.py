"""Verify published bank metadata, optional private-file backup, and a real submission."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import ssl
import subprocess
import tarfile
import time
import uuid

REPOSITORY = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("bank_import", Path(__file__).with_name("import-codecontests.py"))
bank = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bank)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--artifact-tar", type=Path)
    parser.add_argument("--submit-smoke", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    manifest = json.loads((root / "bank.json").read_text(encoding="utf8"))
    state = json.loads((root / "import-state.json").read_text(encoding="utf8"))
    if state.get("pending") or len(manifest["problems"]) != state["publishedProblems"]:
        raise ValueError("Import is incomplete")
    version_ids = [str(uuid.UUID(state["problems"][p["id"]]["versionId"])) for p in manifest["problems"]]
    literals = ",".join("'" + value + "'::uuid" for value in version_ids)
    sql = '''SELECT coalesce(json_agg(json_build_object('versionId',problem_version_id,'order',"order",'inputKey',input_artifact_key,'outputKey',output_artifact_key,'inputSha256',input_sha256,'outputSha256',output_sha256,'inputBytes',input_size_bytes,'outputBytes',output_size_bytes) ORDER BY problem_version_id,"order"),'[]'::json) FROM problems.published_problem_test_cases WHERE problem_version_id IN (%s);''' % literals
    result = subprocess.run(["docker", "compose", "exec", "-T", "postgres", "sh", "-c",
        'PGPASSWORD="$POSTGRES_PASSWORD" psql -X -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1'],
        cwd=REPOSITORY, input=sql, capture_output=True, text=True, check=True)
    rows = json.loads(result.stdout)
    lookup = {(row["versionId"], row["order"]): row for row in rows}
    if len(lookup) != len(rows):
        raise ValueError("Duplicate published order")
    expected_artifacts = {}
    for problem in manifest["problems"]:
        local = state["problems"][problem["id"]]
        assert hashlib.sha256((root / "candidates" / problem["id"] / "manifest.json").read_bytes()).hexdigest() == local["manifestSha256"]
        for test in problem["tests"]:
            row = lookup.pop((local["versionId"], test["order"]))
            for kind in ("input", "output"):
                assert row[kind + "Sha256"] == test[kind]["sha256"] and row[kind + "Bytes"] == test[kind]["bytes"]
                key = row[kind + "Key"]
                assert re.fullmatch(r"tests/[0-9a-f]{32}/(?:input|output)\.bin", key)
                assert key not in expected_artifacts
                expected_artifacts[key] = test[kind]
    assert not lookup
    verified_files = 0
    if args.artifact_tar:
        remaining = dict(expected_artifacts)
        with tarfile.open(args.artifact_tar, "r:*") as archive:
            for member in archive:
                # The full private volume also contains cookie keys; artifact keys
                # are relative to its problem-artifacts subdirectory.
                key = member.name.removeprefix("./").removeprefix("problem-artifacts/")
                if key not in remaining:
                    continue
                expected = remaining.pop(key)
                assert member.isfile() and not member.issym() and not member.islnk() and member.size == expected["bytes"]
                assert member.size <= 8 * 1024 * 1024
                data = archive.extractfile(member).read(8 * 1024 * 1024 + 1)
                assert hashlib.sha256(data).hexdigest() == expected["sha256"]
                verified_files += 1
        assert not remaining, "Backup is missing referenced files"
    context = ssl.create_default_context(cafile=str(REPOSITORY / "artifacts/minos-local-ca.crt"))
    public = bank.Session(state["baseUrl"], context)
    catalog = []
    for page in range(1, 1 + (state["publishedProblems"] + state["baselineCatalogCount"] + 99) // 100):
        response = public.request(f"/api/problems/?page={page}&pageSize=100")
        catalog.extend(response["items"])
    assert {state["problems"][p["id"]]["problemId"] for p in manifest["problems"]}.issubset({p["problemId"] for p in catalog})
    report = {"verifiedAtUtc": datetime.now(timezone.utc).isoformat(), "problems": len(manifest["problems"]),
              "publishedTestPairs": len(rows), "verifiedArtifactFiles": verified_files, "catalogTotal": response["total"],
              "bankManifestSha256": hashlib.sha256((root / "bank.json").read_bytes()).hexdigest()}
    previous_path = root / "verification.json"
    if previous_path.exists() and not args.submit_smoke:
        previous = json.loads(previous_path.read_text(encoding="utf8"))
        if previous.get("bankManifestSha256") == report["bankManifestSha256"] and "submissionSmoke" in previous:
            report["submissionSmoke"] = previous["submissionSmoke"]
    if args.submit_smoke:
        owner = bank.Session(state["baseUrl"], context)
        credential = json.loads((REPOSITORY / "artifacts/codecontests-private/owner.json").read_text(encoding="utf8"))
        owner.login(credential["email"], credential["password"])
        assert owner.request("/accounts/me")["id"] == state["ownerId"]
        problem = min(manifest["problems"], key=lambda p: len(p["tests"]))
        receipt = json.loads((root / "validation" / (problem["id"] + ".json")).read_text(encoding="utf8"))
        reference = next(r for r in problem["references"] if r["path"] == receipt["reference"])
        source = bank.read_file(root, reference, 65536).decode("utf8")
        local = state["problems"][problem["id"]]
        created = owner.request("/api/problems/" + local["problemId"] + "/submissions", "POST",
                                {"language": "cpp20", "source": source}, expected=201)
        path = "/api/submissions/" + created["id"]
        deadline = time.monotonic() + 300
        while True:
            submission = owner.request(path)
            if submission["status"] in ("Completed", "SystemFailed"):
                break
            assert time.monotonic() < deadline, "Worker did not complete the smoke submission"
            time.sleep(0.5)
        assert submission["status"] == "Completed" and submission["result"]["verdict"] == "Accepted"
        assert submission["problemVersionId"] == local["versionId"]
        tests = submission["result"]["tests"]
        assert len(tests) == len(problem["tests"]) and all(t["status"] == "Passed" for t in tests)
        assert all(t["cpuTimeMilliseconds"] is not None and t["memoryPeakBytes"] is not None for t in tests)
        public.request(path, expected=401)
        history = owner.request("/api/me/submissions?page=1&pageSize=20")
        assert any(item["id"] == created["id"] for item in history["items"])
        assert all("source" not in item for item in history["items"])
        report["submissionSmoke"] = {"sourceId": problem["id"], "submissionId": created["id"], "verdict": "Accepted", "passedTests": len(tests)}
    bank.save(root / "verification.json", report)
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
