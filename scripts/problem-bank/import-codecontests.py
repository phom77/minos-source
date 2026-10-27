"""Import a validated portable bank through Minos's authorized HTTPS use cases.

No direct DB/artifact writes, source execution, hidden-test normalization or POST retries.
Credentials stay outside the portable bank. Interrupted ambiguous writes stop for review.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import http.client
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import secrets
import ssl
import stat
import subprocess
import time
import urllib.parse
import urllib.request
import zipfile

REPOSITORY = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("minos_https", REPOSITORY / "scripts/checks/http-smoke.py")
https = importlib.util.module_from_spec(spec)
spec.loader.exec_module(https)
class Session(https.Session):
    """Reuse one verified HTTPS connection; never replay an ambiguous request."""
    def __init__(self, base_url, context):
        super().__init__(base_url, context)
        address = urllib.parse.urlsplit(base_url)
        if address.scheme != "https" or address.path or address.query or address.fragment:
            raise ValueError("Use an HTTPS origin without a path")
        self.connection = http.client.HTTPSConnection(address.hostname, address.port or 443, context=context, timeout=20)

    def request(self, path, method="GET", value=None, data=None, content_type="application/json", expected=200):
        headers = {}
        if value is not None:
            data = json.dumps(value).encode()
        if data is not None:
            headers["Content-Type"] = content_type
        if method not in ("GET", "HEAD", "OPTIONS"):
            if self.token is None:
                self.token = self.request("/accounts/csrf")["requestToken"]
            headers["X-CSRF-TOKEN"] = self.token
        request = urllib.request.Request(self.base_url + path, data=data, headers=headers, method=method)
        self.cookies.add_cookie_header(request)
        self.connection.request(method, path, body=data, headers=dict(request.header_items()))
        response = self.connection.getresponse()
        self.cookies.extract_cookies(response, request)
        body = response.read(2 * 1024 * 1024 + 1)
        if len(body) > 2 * 1024 * 1024:
            self.connection.close()
            raise ValueError("API response exceeds the bounded import contract")
        if response.status != expected:
            raise AssertionError(f"{method} {path}: HTTP {response.status}, expected {expected}")
        if response.status >= 400 or not body:
            return None
        return json.loads(body) if response.headers.get_content_type() == "application/json" else body.decode()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def save(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for attempt in range(50):
        try:
            temporary.replace(path)
            break
        except PermissionError:
            if attempt == 49:
                raise
            time.sleep(0.1)  # Windows readers can briefly hold a handle without delete sharing.


def read_file(root, entry, maximum=8 * 1024 * 1024):
    relative = Path(entry["path"])
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError("Unsafe bank path")
    path = root / relative
    if not path.absolute().is_relative_to(root.absolute()):
        raise ValueError("Bank path escapes root")
    metadata = None
    for component in (path, *path.parents):
        if component == root:
            break
        current = component.lstat()
        if metadata is None:
            metadata = current
        if stat.S_ISLNK(current.st_mode) or getattr(current, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Bank links are not supported")
    if metadata is None or metadata.st_size > maximum:
        raise ValueError("Bank file exceeds size bound")
    data = path.read_bytes()
    if digest(data) != entry["sha256"] or ("bytes" in entry and len(data) != entry["bytes"]):
        raise ValueError("Bank file checksum/size mismatch")
    return data


def select(root, count, preferred=(), verify_files=True):
    candidates = json.loads((root / "candidates.json").read_text(encoding="utf-8"))
    by_id = {p["id"]: p for p in candidates["problems"]}
    if len(by_id) != len(candidates["problems"]) or any(identifier not in by_id for identifier in preferred):
        raise ValueError("Duplicate or missing source problem")
    ordered = [by_id[identifier] for identifier in preferred] + [p for p in candidates["problems"] if p["id"] not in preferred]
    selected = []
    seen = set()
    for problem in ordered:
        identifier = problem["id"]
        if not re.fullmatch(r"cf-[0-9]+-[a-z]{1,2}[0-9]?", identifier):
            raise ValueError("Unsafe source identifier")
        if identifier in seen:
            raise ValueError("Duplicate source problem")
        seen.add(identifier)
        receipt_path = root / "validation" / (identifier + ".json")
        if not receipt_path.is_file():
            continue
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        if not receipt["accepted"] or receipt.get("compilationMemoryMegabytes") != 512:
            continue
        manifest_path = root / "candidates" / identifier / "manifest.json"
        if digest(manifest_path.read_bytes()) != receipt["manifestFileSha256"] or json.loads(manifest_path.read_text(encoding="utf-8")) != problem:
            raise ValueError("Reference evidence no longer matches manifest")
        if receipt["checker"] != "Exact" or receipt["checkedOrders"] != problem["referenceCheckOrders"]:
            raise ValueError("Invalid reference evidence")
        if receipt["id"] != identifier or receipt["totalAvailableTests"] != len(problem["tests"]) or problem["checker"] != "Exact":
            raise ValueError("Reference evidence belongs to a different problem configuration")
        chosen = next(a for a in receipt["attempts"] if a["reference"] == receipt["reference"])
        if chosen["compilation"] != "Succeeded" or any(t["status"] != "Passed" or not t["cleanupComplete"] for t in chosen["tests"]):
            raise ValueError("Reference checks did not pass")
        if sorted(t["order"] for t in chosen["tests"]) != receipt["checkedOrders"]:
            raise ValueError("Missing reference measurements")
        if not 2 <= len(problem["tests"]) <= 1500:
            raise ValueError("Unsupported test count")
        if [test["order"] for test in problem["tests"]] != list(range(1, len(problem["tests"]) + 1)):
            raise ValueError("Test order is not contiguous")
        read_file(root, problem["originalStatement"], 256 * 1024)
        reference = next(reference for reference in problem["references"] if reference["path"] == receipt["reference"])
        read_file(root, reference, 65536)
        statement = read_file(root, problem["statement"], 256 * 1024).decode("utf-8")
        if verify_files:
            for test in problem["tests"]:
                read_file(root, test["input"])
                read_file(root, test["output"])
        samples = [{"input": read_file(root, t["input"]).decode("utf-8"), "output": read_file(root, t["output"]).decode("utf-8")}
                   for t in problem["tests"] if t["group"] == "public_tests"]
        body = {"title": problem["title"], "statement": statement, "timeLimitMilliseconds": problem["timeLimitMilliseconds"],
                "memoryLimitMegabytes": problem["memoryLimitMegabytes"], "checker": "Exact", "samples": samples}
        if len(json.dumps(body).encode()) > 256 * 1024:
            raise ValueError("Authoring payload exceeds HTTP size bound")
        selected.append(problem)
        if len(selected) == count:
            break
    if len(selected) != count:
        raise ValueError(f"Only {len(selected)} validated problems available; need {count}")
    return candidates, selected


def protect(directory):
    directory.mkdir(parents=True, exist_ok=True)
    if os.name == "nt":
        identity = subprocess.check_output(["whoami"], text=True).strip()
        subprocess.run(["icacls", str(directory), "/inheritance:r", "/grant:r", identity + ":(OI)(CI)F"],
                       check=True, capture_output=True)
    else:
        directory.chmod(0o700)


def owner(session, path):
    def login():
        session.token = None  # Role changes revoke the old cookie and identity-bound CSRF token.
        session.login(credential["email"], credential["password"])

    protect(path.parent)
    if path.exists():
        credential = json.loads(path.read_text(encoding="utf-8"))
    else:
        credential = {"email": "codecontests-library-" + secrets.token_hex(12) + "@example.test",
                      "password": secrets.token_urlsafe(36)}
        save(path, credential)
    if "id" not in credential:
        if credential.get("registrationPending"):
            raise RuntimeError("An account registration has an ambiguous response; reconcile before retrying")
        credential["registrationPending"] = True
        save(path, credential)
        credential["id"] = session.register(credential["email"], credential["password"], "CodeContests Library")
        credential.pop("registrationPending")
        save(path, credential)
    login()
    account = session.request("/accounts/me")
    if account["role"] == "Solver":
        result = subprocess.run(["docker", "compose", "run", "--rm", "-T", "--no-deps", "migrate", "--provision-admin", credential["id"]],
                                cwd=REPOSITORY, capture_output=True)
        if result.returncode:
            raise RuntimeError("Trusted import-owner provisioning failed")
        login()
        account = session.request("/accounts/me")
    if account["role"] == "Administrator":
        session.request("/accounts/" + credential["id"] + "/role", "PUT", {"role": "Setter"}, expected=204)
        login()
    if session.request("/accounts/me")["role"] != "Setter":
        raise RuntimeError("Import owner must be an active Setter")
    return credential["id"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--count", type=int, default=200)
    parser.add_argument("--base-url", default="https://127.0.0.1:8443")
    parser.add_argument("--ca", type=Path, default=REPOSITORY / "artifacts/minos-local-ca.crt")
    parser.add_argument("--credentials", type=Path, default=REPOSITORY / "artifacts/codecontests-private/owner.json")
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--resume", action="store_true", help="Resume the same previously preflighted bank; each remaining upload still verifies its file hashes")
    args = parser.parse_args()
    if not 1 <= args.count <= 500 or not args.base_url.startswith("https://"):
        parser.error("Use HTTPS and a count between 1 and 500")
    root = args.root.resolve()
    state_path = root / "import-state.json"
    prior = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else None
    if args.resume and not prior:
        raise ValueError("Resume requires an existing import journal")
    if prior and len(prior["problems"]) > args.count:
        raise ValueError("Requested count is smaller than the existing import")
    candidates, selected = select(root, args.count, tuple(prior["problems"]) if prior else (),
                                  verify_files=not args.resume or args.verify_only)
    total_tests = sum(len(p["tests"]) for p in selected)
    print(json.dumps({"validatedProblems": len(selected), "testPairs": total_tests}), flush=True)
    if args.verify_only:
        return
    session = Session(args.base_url, ssl.create_default_context(cafile=str(args.ca)))
    session.request("/health/ready")
    state = prior or {
        "baseUrl": args.base_url, "baselineCatalogCount": session.request("/api/problems/?page=1&pageSize=100")["total"], "problems": {}}
    if state["baseUrl"] != args.base_url or state.get("pending"):
        raise RuntimeError("Import target differs or a write has an ambiguous response; reconcile import-state.json")
    state["ownerId"] = owner(session, args.credentials.resolve())
    checkpoints = root / "imports"
    checkpoints.mkdir(exist_ok=True)
    # Separate per-problem test IDs from the compact global operation journal.
    for identifier, previous in tuple(state["problems"].items()):
        if "tests" in previous:
            save(checkpoints / (identifier + ".json"), previous)
            state["problems"][identifier] = {key: value for key, value in previous.items() if key != "tests"}
    save(state_path, state)

    def write(identifier, operation, path, method="POST", **kwargs):
        state["pending"] = {"sourceId": identifier, "operation": operation, "path": path}
        save(state_path, state)
        return session.request(path, method, **kwargs)

    def confirmed(identifier, item):
        save(checkpoints / (identifier + ".json"), item)
        state["problems"][identifier] = {key: value for key, value in item.items() if key != "tests"}
        state["problems"][identifier]["testCount"] = len(item["tests"])
        state.pop("pending", None)
        save(state_path, state)

    for number, problem in enumerate(selected, 1):
        identifier = problem["id"]
        checkpoint = checkpoints / (identifier + ".json")
        item = json.loads(checkpoint.read_text(encoding="utf-8")) if checkpoint.exists() else {
            "manifestSha256": digest((root / "candidates" / identifier / "manifest.json").read_bytes()), "tests": []}
        state["problems"].setdefault(identifier, {"manifestSha256": item["manifestSha256"]})
        if item["manifestSha256"] != digest((root / "candidates" / identifier / "manifest.json").read_bytes()):
            raise RuntimeError("Imported source manifest changed")
        statement = read_file(root, problem["statement"], 256 * 1024).decode("utf-8")
        samples = [{"input": read_file(root, t["input"]).decode("utf-8"), "output": read_file(root, t["output"]).decode("utf-8")}
                   for t in problem["tests"] if t["group"] == "public_tests"]
        if "problemId" not in item:
            draft = write(identifier, "create", "/api/authoring/problems/", value={"title": problem["title"], "statement": statement}, expected=201)
            item["problemId"] = draft["id"]
            confirmed(identifier, item)
        base = "/api/authoring/problems/" + item["problemId"]
        draft = session.request(base + "/draft")
        if len(draft["testCases"]) != len(item["tests"]) or [t["id"] for t in draft["testCases"]] != [t["id"] for t in item["tests"]]:
            raise RuntimeError("Remote draft changed outside this import")
        if "versionId" not in item:
            session.request(base + "/draft", "PUT", {"title": problem["title"], "statement": statement,
                "timeLimitMilliseconds": problem["timeLimitMilliseconds"], "memoryLimitMegabytes": problem["memoryLimitMegabytes"], "checker": "Exact", "samples": samples})
            for test in problem["tests"][len(item["tests"]):]:
                package = io.BytesIO()
                with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as archive:
                    archive.writestr("input.txt", read_file(root, test["input"]))
                    archive.writestr("output.txt", read_file(root, test["output"]))
                response = write(identifier, "upload-test-" + str(test["order"]), base + "/draft/tests", data=package.getvalue(), content_type="application/zip")
                uploaded = response["testCases"][-1]
                assert uploaded["order"] == test["order"] and uploaded["inputSizeBytes"] == test["input"]["bytes"] and uploaded["outputSizeBytes"] == test["output"]["bytes"]
                item["tests"].append(uploaded)
                confirmed(identifier, item)
            published = write(identifier, "publish", base + "/publish", value={"statementReviewed": True, "samplesVerified": True, "referenceSolutionChecked": True}, expected=201)
            item["versionId"] = published["versionId"]
            confirmed(identifier, item)
        public = session.request("/api/problems/" + item["problemId"])
        assert public["versionId"] == item["versionId"] and public["privateTestCount"] == len(problem["tests"])
        assert public["samples"] == samples and public["statement"] == statement
        assert set(public) == {"problemId", "versionId", "versionNumber", "title", "statement", "timeLimitMilliseconds",
                              "memoryLimitMegabytes", "checker", "privateTestCount", "publishedAtUtc", "samples"}
        print(f"IMPORTED {number}/{len(selected)} {identifier} tests={len(item['tests'])}", flush=True)
    state["completedAtUtc"] = datetime.now(timezone.utc).isoformat()
    state["publishedProblems"] = len(selected)
    state["testPairs"] = total_tests
    save(state_path, state)
    save(root / "bank.json", {"formatVersion": 1, "dataset": candidates["dataset"], "revision": candidates["revision"],
        "license": candidates["license"], "selection": candidates["selection"], "problems": selected})
    print(json.dumps({"publishedProblems": len(selected), "testPairs": total_tests,
                      "catalogTotal": session.request("/api/problems/?page=1&pageSize=100")["total"]}), flush=True)


if __name__ == "__main__":
    main()
