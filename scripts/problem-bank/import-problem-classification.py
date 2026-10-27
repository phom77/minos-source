"""Prepare a checksum-verified classification import; --apply writes through HTTPS.

Uses an existing owner account only. Never provisions accounts, changes roles,
updates published content, or overwrites a different existing classification.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import ssl
from uuid import UUID


def module(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


classifier = module("classify-problem-bank")
bank_import = module("import-codecontests")


def prepare(result):
    if not isinstance(result.get("datasetRevision"), str) or not re.fullmatch(r"[0-9a-f]{40}", result["datasetRevision"]):
        raise ValueError("The dataset revision must be pinned")
    policy = result["policy"]
    policy_hash = hashlib.sha256(json.dumps(policy, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    requests = []
    seen = set()
    for row in result["problems"]:
        problem_id, version_id = str(UUID(row["problemId"])), str(UUID(row["versionId"]))
        if problem_id in seen or UUID(problem_id).int == 0 or UUID(version_id).int == 0:
            raise ValueError("Invalid or duplicate import mapping")
        seen.add(problem_id)
        if row["unmappedTags"] or not row["topics"]:
            raise ValueError("Resolve unmapped/missing topics before importing")
        upstream_topics = {policy["topics"][tag] for tag in row["sourceTags"] if tag in policy["topics"]}
        topics = []
        for slug in sorted(row["topics"]):
            if slug in upstream_topics:
                topics.append({"slug": slug, "origin": "Upstream", "confidence": None, "rationale": None})
            else:
                proposal = row["topicProposal"]
                topics.append({"slug": slug, "origin": "Inferred", "confidence": proposal["confidence"].title(), "rationale": proposal["rationale"]})
        requests.append({"problemId": problem_id, "request": {
            "expectedRevision": 0, "basisVersionId": version_id,
            "difficulty": None if row["difficulty"] == "Unclassified" else row["difficulty"],
            "difficultyOrigin": "Manual" if row["difficulty"] == "Unclassified" else "CodeforcesRating",
            "evidence": {"sourceProblemKey": row["sourceId"], "sourceRating": row["sourceRating"] or None, "sourceTags": row["sourceTags"],
                         "datasetRevision": result["datasetRevision"], "manifestSha256": row["manifestSha256"],
                         "policyVersion": policy["version"], "policySha256": policy_hash}, "topics": topics}})
    return {"formatVersion": 1, "bankSha256": result["bankSha256"], "requests": requests}


def same_payload(actual, expected):
    return all(actual.get(key) == value for key, value in expected.items() if key != "expectedRevision")


def apply(session, plan):
    account = session.request("/accounts/me")
    if account["role"] not in ("Setter", "Administrator"):
        raise ValueError("Import requires an active author account")
    known_topics = {topic["slug"] for topic in session.request("/api/problems/topics")}
    pending, unchanged = [], 0
    # Complete read-only authorization/identity/conflict preflight before the first write.
    for item in plan["requests"]:
        identifier, payload = item["problemId"], item["request"]
        if any(topic["slug"] not in known_topics for topic in payload["topics"]):
            raise ValueError("Target taxonomy differs from the prepared plan")
        version = session.request(f"/api/problems/{identifier}")
        if version["versionId"] != payload["basisVersionId"]:
            raise ValueError("The target's published version differs from the bank")
        existing = session.request(f"/api/authoring/problems/{identifier}/classification")
        if existing["problemId"] != identifier:
            raise ValueError("Target returned an unexpected problem")
        if existing["revision"] == 0:
            pending.append(item)
        elif same_payload(existing, payload):
            unchanged += 1
        else:
            raise ValueError("Existing metadata differs; refusing to overwrite author changes")
    for item in pending:
        response = session.request(f"/api/authoring/problems/{item['problemId']}/classification", "PUT", item["request"])
        if not same_payload(response, item["request"]):
            raise ValueError("Written classification differs from the prepared request")
    return {"created": len(pending), "unchanged": unchanged}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--state", type=Path, required=True)
    parser.add_argument("--proposals", type=Path)
    parser.add_argument("--policy", type=Path, default=classifier.DEFAULT_POLICY)
    parser.add_argument("--output", type=Path, required=True, help="Prepared request JSON, outside the source bank")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--base-url", default="https://127.0.0.1:8443")
    parser.add_argument("--credentials", type=Path)
    parser.add_argument("--ca", type=Path)
    args = parser.parse_args()
    if args.apply and (args.credentials is None or args.ca is None):
        parser.error("--apply requires an existing owner --credentials file and trusted --ca")
    try:
        output, root = args.output.resolve(), args.root.resolve()
        protected = [args.state, args.policy, args.proposals, args.credentials, args.ca]
        if output.is_relative_to(root) or args.output.is_symlink() or args.output.with_suffix(args.output.suffix + ".tmp").is_symlink() or any(
                path is not None and output == path.resolve() for path in protected):
            raise ValueError("Plan output must be separate from original metadata")
        result, _ = classifier.classify(root, args.policy, args.state, args.proposals)
        plan = prepare(result)
        output.parent.mkdir(parents=True, exist_ok=True)
        bank_import.save(output, plan)
        if not args.apply:
            print(json.dumps({"prepared": len(plan["requests"]), "applied": False}))
            return
        credentials = classifier.read_json(args.credentials)
        session = bank_import.Session(args.base_url, ssl.create_default_context(cafile=str(args.ca)))
        session.login(credentials["email"], credentials["password"])
        print(json.dumps(apply(session, plan)))
    except Exception as error:
        # Never print HTTP response bodies, credentials, cookies or connection settings.
        parser.exit(1, f"Classification import stopped ({type(error).__name__}). No automatic HTTP retries; rerun to reconcile completed writes.\n")


if __name__ == "__main__":
    main()
