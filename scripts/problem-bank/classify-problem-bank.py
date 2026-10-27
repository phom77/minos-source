"""Classify an extracted bank offline; never modify the bank or call Minos APIs.

Reads metadata, manifests, optional import IDs and checksum-bound topic proposals.
No private test/source contents or credentials are copied to the output.
"""
import argparse
from collections import Counter
import csv
import hashlib
import io
import json
from pathlib import Path
import re
from uuid import UUID


DEFAULT_POLICY = Path(__file__).with_name("fixtures") / "problem-classification-policy.json"
SOURCE_ID = re.compile(r"cf-[1-9][0-9]*-[a-z]{1,2}[0-9]?\Z")
SLUG = re.compile(r"[a-z]+(?:-[a-z]+)*\Z")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read_json(path):
    if path.stat().st_size > 32 * 1024 * 1024:
        raise ValueError("Metadata file exceeds 32 MiB")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def checked_path(root, relative):
    if not isinstance(relative, str) or Path(relative).is_absolute():
        raise ValueError("Expected a relative bank path")
    path = (root / relative).resolve()
    if not path.is_relative_to(root):
        raise ValueError("Bank path escapes its root")
    return path


def load_policy(path):
    policy = read_json(path)
    easy, medium = policy["easyMaxRating"], policy["mediumMaxRating"]
    if not isinstance(policy["version"], str) or not policy["version"]:
        raise ValueError("Policy needs a version")
    if type(easy) is not int or type(medium) is not int or not 0 < easy < medium:
        raise ValueError("Difficulty thresholds must be positive and increasing")
    topics, excluded = policy["topics"], policy["excludedTags"]
    if not isinstance(topics, dict) or not isinstance(excluded, dict) or topics.keys() & excluded.keys():
        raise ValueError("Topic mappings and exclusions must be disjoint dictionaries")
    if any(not isinstance(k, str) or not k or not isinstance(v, str) or not SLUG.fullmatch(v)
           for k, v in topics.items()):
        raise ValueError("Invalid topic mapping")
    if any(not isinstance(k, str) or not k or not isinstance(v, str) or not v for k, v in excluded.items()):
        raise ValueError("Excluded tags need a reason")
    return policy


def difficulty(rating, policy):
    if rating is None:
        return "Unclassified"
    if type(rating) is not int or rating < 0:
        raise ValueError("Rating must be a non-negative integer or null")
    if rating == 0:
        return "Unclassified"
    if rating <= policy["easyMaxRating"]:
        return "Easy"
    return "Medium" if rating <= policy["mediumMaxRating"] else "Hard"


def load_proposals(path, root, problems, policy):
    if path is None:
        return {}
    document = read_json(path)
    if document.get("formatVersion") != 1:
        raise ValueError("Unsupported proposal format")
    proposals = {}
    for proposal in document["problems"]:
        identifier = proposal["sourceId"]
        if identifier not in problems or identifier in proposals:
            raise ValueError("Unknown or duplicate proposal source ID")
        problem = problems[identifier]
        if any(t not in policy["excludedTags"] for t in problem.get("tags", [])):
            raise ValueError("A proposal cannot override upstream tags")
        tags = proposal["tags"]
        if not isinstance(tags, list) or not tags or any(not isinstance(t, str) or t not in policy["topics"] for t in tags):
            raise ValueError("Proposal needs known algorithm tags")
        if proposal["confidence"] not in ("high", "medium", "low") or not isinstance(proposal["rationale"], str) or not proposal["rationale"].strip():
            raise ValueError("Proposal needs confidence and rationale")
        originals = [problem["statement"], *problem.get("references", [])]
        if problem.get("originalStatement"):
            originals.append(problem["originalStatement"])
        allowed = {entry["path"] for entry in originals}
        evidence = proposal["evidence"]
        if not isinstance(evidence, list) or not evidence:
            raise ValueError("Proposal needs statement/reference evidence")
        for entry in evidence:
            if entry["path"] not in allowed:
                raise ValueError("Proposal evidence must be a statement or reference")
            file = checked_path(root, entry["path"])
            if file.stat().st_size > 1024 * 1024 or digest(file.read_bytes()) != entry["sha256"]:
                raise ValueError("Proposal evidence checksum mismatch")
            original = next(e for e in originals if e["path"] == entry["path"])
            if original["sha256"] != entry["sha256"]:
                raise ValueError("Proposal evidence differs from original manifest")
        proposals[identifier] = proposal
    return proposals


def classify(root, policy_path=DEFAULT_POLICY, state_path=None, proposals_path=None):
    root = root.resolve()
    policy = load_policy(policy_path)
    bank_path = root / "bank.json"
    bank = read_json(bank_path)
    if bank.get("formatVersion") != 1 or not isinstance(bank.get("problems"), list) or not bank["problems"]:
        raise ValueError("Expected a non-empty version-1 selected bank")
    problems, manifests = {}, {}
    for problem in bank["problems"]:
        identifier = problem["id"]
        if not isinstance(identifier, str) or not SOURCE_ID.fullmatch(identifier) or identifier in problems:
            raise ValueError("Invalid or duplicate bank source ID")
        manifest_path = checked_path(root, f"candidates/{identifier}/manifest.json")
        if read_json(manifest_path) != problem:
            raise ValueError(f"Selected bank differs from manifest: {identifier}")
        if not isinstance(problem["title"], str) or not problem["title"].strip():
            raise ValueError("Problem needs a title")
        origin = identifier.split("-")
        if problem["sourceUrl"] != f"https://codeforces.com/problemset/problem/{origin[1]}/{origin[2].upper()}":
            raise ValueError("Source URL does not match source ID")
        tags = problem.get("tags", [])
        if not isinstance(tags, list) or any(not isinstance(t, str) or not t.strip() for t in tags):
            raise ValueError("Tags must be a list of non-empty strings")
        # Validate before missing-value handling (bool and float are not ratings).
        rating = problem.get("rating")
        if rating is not None and (type(rating) is not int or rating < 0):
            raise ValueError("Rating must be a non-negative integer or null")
        problems[identifier] = problem
        manifests[identifier] = digest(manifest_path.read_bytes())
    state = read_json(state_path)["problems"] if state_path else {}
    if state_path and set(state) != set(problems):
        raise ValueError("Import state must match the entire selected bank")
    seen_problem_ids, seen_version_ids = set(), set()
    proposals = load_proposals(proposals_path, root, problems, policy)
    rows, queue = [], []
    for identifier, problem in problems.items():
        mapping = state.get(identifier)
        if mapping:
            if mapping["manifestSha256"] != manifests[identifier]:
                raise ValueError(f"Import state manifest checksum mismatch: {identifier}")
            for field, seen in (("problemId", seen_problem_ids), ("versionId", seen_version_ids)):
                canonical = str(UUID(mapping[field]))
                if canonical in seen:
                    raise ValueError("Duplicate imported problem/version ID")
                seen.add(canonical)
        elif state_path:
            raise ValueError("Incomplete import mapping")
        source_tags = sorted(set(problem.get("tags", [])))
        proposal = proposals.get(identifier)
        tags = source_tags + (proposal["tags"] if proposal else [])
        unknown = sorted(t for t in tags if t not in policy["topics"] and t not in policy["excludedTags"])
        topics = sorted({policy["topics"][t] for t in tags if t in policy["topics"]})
        level = difficulty(problem.get("rating"), policy)
        reasons = []
        if level == "Unclassified":
            reasons.append("missing-rating")
        if not topics:
            reasons.append("missing-topics")
        if unknown:
            reasons.append("unmapped-source-tags")
        if proposal:
            reasons.append("ai-topic-proposal")
        row = {"sourceId": identifier, "title": problem["title"], "sourceUrl": problem["sourceUrl"],
               "manifestSha256": manifests[identifier], "sourceRating": problem.get("rating"),
               "difficulty": level, "difficultySource": "codeforces-rating-policy" if level != "Unclassified" else None,
               "sourceTags": source_tags, "topics": topics,
               "topicSource": "ai-proposal" if proposal else ("codeforces-tags" if topics else None),
               "unmappedTags": unknown, "excludedTags": [t for t in source_tags if t in policy["excludedTags"]],
               "reviewReasons": reasons}
        if mapping:
            row.update({field: str(UUID(mapping[field])) for field in ("problemId", "versionId")})
        if proposal:
            row["topicProposal"] = {k: proposal[k] for k in ("confidence", "rationale", "evidence")}
        rows.append(row)
        if reasons:
            queue.append({"sourceId": identifier, "reasons": reasons})
    summary = {"total": len(rows), "difficultyCounts": dict(sorted(Counter(r["difficulty"] for r in rows).items())),
               "ratingCounts": dict(sorted(Counter(str(r["sourceRating"]) for r in rows).items(), key=lambda x: int(x[0]) if x[0] != "None" else -1)),
               "topicCounts": dict(sorted(Counter(t for r in rows for t in r["topics"]).items())),
               "upstreamTopicProblems": sum(any(t in policy["topics"] for t in r["sourceTags"]) for r in rows),
               "missingSourceTagProblems": sum(not r["sourceTags"] for r in rows),
               "aiTopicProposals": len(proposals), "missingTopicProblems": sum(not r["topics"] for r in rows),
               "reviewProblems": len(queue), "mappedProblems": sum("problemId" in r for r in rows)}
    result = {"formatVersion": 1, "bankSha256": digest(bank_path.read_bytes()), "dataset": bank.get("dataset"),
              "datasetRevision": bank.get("revision"), "policy": policy, "summary": summary, "problems": rows}
    return result, {"formatVersion": 1, "bankSha256": result["bankSha256"], "problems": queue}


def report(result):
    summary, policy = result["summary"], result["policy"]
    lines = ["# Offline problem classification", "",
             "Candidate metadata only; not applied to Minos. No application or database changes.", "",
             f"Bank SHA-256: `{result['bankSha256']}`", "",
             f"Policy `{policy['version']}`: Easy <= {policy['easyMaxRating']}; "
             f"Medium <= {policy['mediumMaxRating']}; Hard above that; missing rating is Unclassified.", "",
             "These are Minos heuristics over historical Codeforces ratings, not LeetCode equivalence or contestant ratings.",
             "Thresholds are fixed and configurable; they are not percentiles of this bank.", "",
             f"Problems: {summary['total']}; mapped import IDs: {summary['mappedProblems']}.",
             f"Upstream topic metadata: {summary['upstreamTopicProblems']}; AI proposals: {summary['aiTopicProposals']}; "
             f"without topics: {summary['missingTopicProblems']}; review flags: {summary['reviewProblems']}.", "",
             "| Difficulty | Problems |", "| --- | ---: |"]
    lines.extend(f"| {level} | {summary['difficultyCounts'][level]} |" for level in
                 ("Easy", "Medium", "Hard", "Unclassified") if level in summary["difficultyCounts"])
    lines.extend(["", "Topic counts overlap because a problem may have several topics; counts include marked AI proposals.", "",
                  "| Topic | Problems |", "| --- | ---: |"])
    lines.extend(f"| {topic} | {count} |" for topic, count in summary["topicCounts"].items())
    lines.extend(["", "## Review flags", "",
                  "AI topics remain proposals even at high confidence. Upstream metadata is preserved separately.", ""])
    for row in result["problems"]:
        if row["reviewReasons"]:
            lines.append(f"- {row['sourceId']}: {', '.join(row['reviewReasons'])}.")
            if "topicProposal" in row:
                p = row["topicProposal"]
                lines.append(f"  Suggested topics: {', '.join(row['topics'])}; confidence: {p['confidence']}. {p['rationale']}")
    if not summary["reviewProblems"]:
        lines.append("None.")
    return "\n".join(lines) + "\n"


def write_outputs(root, output, result, queue):
    root, output = root.resolve(), output.resolve()
    if output.is_relative_to(root) or root.is_relative_to(output):
        raise ValueError("Output must be separate from the original bank directory")
    output.mkdir(parents=True, exist_ok=True)
    buffer = io.StringIO(newline="")
    fields = ["sourceId", "problemId", "versionId", "title", "sourceRating", "difficulty", "topics", "topicSource",
              "topicConfidence", "reviewReasons"]
    writer = csv.DictWriter(buffer, fieldnames=fields)
    writer.writeheader()
    for row in result["problems"]:
        values = {k: row.get(k, "") for k in fields}
        values["topicConfidence"] = row.get("topicProposal", {}).get("confidence", "")
        for key in ("topics", "reviewReasons"):
            values[key] = ";".join(values[key])
        # CSV is intended for spreadsheets; prevent imported titles becoming formulas.
        for key, value in values.items():
            if isinstance(value, str) and value.lstrip().startswith(("=", "+", "-", "@")):
                values[key] = "'" + value
        writer.writerow(values)
    outputs = {"classification.json": json.dumps(result, ensure_ascii=False, indent=2) + "\n",
               "review-queue.json": json.dumps(queue, ensure_ascii=False, indent=2) + "\n",
               "classification.csv": buffer.getvalue(), "report.md": report(result)}
    for name, content in outputs.items():
        path = output / name
        if path.is_symlink():
            raise ValueError("Output files must not be symbolic links")
        temporary = path.with_suffix(path.suffix + ".tmp")
        if temporary.is_symlink():
            raise ValueError("Temporary output must not be a symbolic link")
        with temporary.open("w", encoding="utf-8", newline="") as file:
            file.write(content)
        temporary.replace(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--policy", type=Path, default=DEFAULT_POLICY)
    parser.add_argument("--state", type=Path, help="Optional import-state.json from the intended database")
    parser.add_argument("--proposals", type=Path, help="Optional evidence-bound AI topic proposals")
    args = parser.parse_args()
    try:
        result, queue = classify(args.root, args.policy, args.state, args.proposals)
        write_outputs(args.root, args.output, result, queue)
    except (ValueError, KeyError, TypeError, OSError) as error:
        parser.exit(1, f"Classification failed: {error}\n")
    print(json.dumps(result["summary"], ensure_ascii=False))


if __name__ == "__main__":
    main()
