"""Extract a portable, checksum-pinned CodeContests candidate bank (no execution).

Requires pyarrow==25.0.1. Raw shards are retained; extraction never trims test data.
The conservative subset targets deterministic integer/yes-no output under Exact.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import sys

REVISION = "802411c3010cb00d1b05bad57ca77365a3c699d6"
GROUPS = ("public_tests", "private_tests", "generated_tests")
SCALAR_LINES = re.compile(r"(?:(?:-?\d+|YES|NO|Yes|No|yes|no)\n)+\Z")
SPECIAL = re.compile(r"interactive|absolute (?:or|and) relative|relative error|print any|output any|any (?:valid|correct)|any order|any of (?:them|the)|several (?:possible|valid|correct)|multiple (?:possible|valid|correct)|different (?:possible|valid|correct) answers", re.I)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def utf16_length(text):
    return len(text.encode("utf-16-le")) // 2


def json_write(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def inspect(row):
    if row["source"] != 2 or not 0 < row["cf_contest_id"] < 1000000 or not re.fullmatch(r"[A-Z]{1,2}[0-9]?", row["cf_index"]):
        return "source"
    if row["input_file"] or row["output_file"]:
        return "file-io"
    text = row["description"]
    if not text.strip() or "<image>" in text.lower() or "\ufffd" in text or "\0" in text:
        return "statement"
    match = re.search(r"\nOutput\s*\n(.*?)(?:\nExamples?\s*\n|\nSample|\Z)", text, re.S | re.I)
    if not match or SPECIAL.search(text):
        return "checker-or-interactive"
    if any(w in match[1].lower() for w in ("permutation", "any", "real number", "floating", "precision", "error", "lexicograph", "index", "indices", "one of", "solution", "construction")):
        return "output-not-proven-deterministic"
    limit = row["time_limit"]
    milliseconds = math.ceil(limit["seconds"] * 1000 + limit["nanos"] / 1_000_000) if limit else 0
    memory = row["memory_limit_bytes"] // (1024 * 1024)
    if not 100 <= milliseconds <= 10000 or not 16 <= memory <= 1024:
        return "limits"
    count = 0
    for group in GROUPS:
        tests = row[group]
        if len(tests["input"]) != len(tests["output"]):
            return "unpaired-tests"
        count += len(tests["input"])
        for input_text, output in zip(tests["input"], tests["output"]):
            if not SCALAR_LINES.fullmatch(output) or any(len(t.encode("utf-8")) > 8 * 1024 * 1024 or "\0" in t for t in (input_text, output)):
                return "exact-output-or-artifact-bound"
    public = row["public_tests"]
    if not 1 <= len(public["input"]) <= 20 or not 2 <= count <= 1500:
        return "test-count"
    if any(utf16_length(t) > 65536 for t in public["input"] + public["output"]):
        return "sample-bound"
    refs = [(i, source) for i, (language, source) in enumerate(zip(row["solutions"]["language"], row["solutions"]["solution"]))
            if language == 2 and source.strip() and len(source.encode("utf-8")) <= 65536 and "\0" not in source]
    if not refs:
        return "cpp-reference"
    return {"timeLimitMilliseconds": milliseconds, "memoryLimitMegabytes": memory,
            "references": sorted(refs, key=lambda x: (not ("\\n" in x[1] or "endl" in x[1]), len(x[1])))[:5]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--reader-path", type=Path)
    parser.add_argument("--candidate-count", type=int, default=300)
    parser.add_argument("--shards", type=int, nargs="+")
    args = parser.parse_args()
    if args.reader_path:
        sys.path.insert(0, str(args.reader_path.resolve()))
    import pyarrow.parquet as pq
    root = args.root.resolve()
    tree = json.loads((root / "raw/tree.json").read_text(encoding="utf-8-sig"))
    candidates, excluded, shards = [], {}, []
    for file in sorted((root / "raw").glob("train-*.parquet")):
        shard_number = int(file.stem.rsplit("-", 1)[1])
        if args.shards is not None and shard_number not in args.shards:
            continue
        entry = next(e for e in tree if re.search(f"train-{shard_number:05d}-", e["path"]))
        digest = sha(file.read_bytes())
        if digest != entry["lfs"]["oid"] or file.stat().st_size != entry["size"]:
            raise ValueError("Raw shard checksum/size mismatch")
        parquet = pq.ParquetFile(file)
        shards.append({"path": file.relative_to(root).as_posix(), "upstreamPath": entry["path"], "sha256": digest, "rows": parquet.metadata.num_rows})
        columns = [n for n in parquet.schema_arrow.names if n != "incorrect_solutions"]
        for index, batch in enumerate(parquet.iter_batches(batch_size=1, columns=columns)):
            row = batch.to_pylist()[0]
            suitability = inspect(row)
            if isinstance(suitability, str):
                excluded[suitability] = excluded.get(suitability, 0) + 1
                continue
            candidates.append((row, suitability, entry["path"], index))
    candidates.sort(key=lambda x: (x[0]["cf_rating"] or 9999, x[0]["cf_contest_id"], x[0]["cf_index"]))
    entries, seen = [], set()
    for row, config, shard, row_index in candidates:
        identifier = f"cf-{row['cf_contest_id']}-{row['cf_index'].lower()}"
        if identifier in seen or len(entries) >= args.candidate_count:
            continue
        seen.add(identifier)
        directory = root / "candidates" / identifier
        directory.mkdir(parents=True, exist_ok=True)
        origin = f"https://codeforces.com/problemset/problem/{row['cf_contest_id']}/{row['cf_index']}"
        title = re.sub(r"^\d+_[A-Z]\d?\.\s*", "", row["name"]).strip()
        if utf16_length(title) > 120:
            excluded["title-bound"] = excluded.get("title-bound", 0) + 1
            continue
        text = row["description"]
        # Public examples have their own UI block; preserve the original statement in statement.txt.
        display = re.sub(r"\nExamples?\s*\n.*?(?=\nNote\s*\n|\Z)", "\n", text, flags=re.S)
        display += f"\n\nOutput format: use the exact letter case shown in the examples; end each output line with a newline.\n\nSource: {origin}\nDataset: DeepMind CodeContests, CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Original-source terms also apply."
        if utf16_length(display) > 65536:
            excluded["statement-bound"] = excluded.get("statement-bound", 0) + 1
            continue
        (directory / "statement.txt").write_bytes(text.encode("utf-8"))
        (directory / "minos-statement.txt").write_bytes(display.encode("utf-8"))
        tests = []
        for group in GROUPS:
            group_dir = directory / "tests" / group
            group_dir.mkdir(parents=True, exist_ok=True)
            for i, (input_text, output) in enumerate(zip(row[group]["input"], row[group]["output"]), 1):
                files = {}
                for extension, value in (("in", input_text), ("out", output)):
                    data = value.encode("utf-8")
                    file = group_dir / f"{i:04d}.{extension}"
                    file.write_bytes(data)
                    files[extension] = {"path": file.relative_to(root).as_posix(), "sha256": sha(data), "bytes": len(data)}
                tests.append({"order": len(tests) + 1, "group": group, "groupOrder": i, "input": files["in"], "output": files["out"]})
        references = []
        for i, (source_index, source) in enumerate(config.pop("references"), 1):
            file = directory / f"reference-{i:02d}.cpp"
            data = source.encode("utf-8")
            file.write_bytes(data)
            references.append({"path": file.relative_to(root).as_posix(), "sha256": sha(data), "bytes": len(data), "upstreamSolutionIndex": source_index})
        # Samples plus both ends of private/generated groups; full judging suite is retained.
        checks = set(t["order"] for t in tests if t["group"] == "public_tests")
        for group in GROUPS[1:]:
            group_tests = [t for t in tests if t["group"] == group]
            if group_tests:
                checks.update((group_tests[0]["order"], group_tests[-1]["order"]))
        item = {"id": identifier, "title": title, "sourceUrl": origin, "datasetRevision": REVISION,
                "upstreamShard": shard, "upstreamRow": row_index, "rating": row["cf_rating"], "tags": row["cf_tags"],
                "statement": {"path": (directory / "minos-statement.txt").relative_to(root).as_posix(), "sha256": sha(display.encode("utf-8"))},
                "originalStatement": {"path": (directory / "statement.txt").relative_to(root).as_posix(), "sha256": sha(text.encode("utf-8"))},
                "checker": "Exact", "outputAdaptation": "Deterministic scalar lines; original IO bytes unchanged; newline/case requirement disclosed in statement.",
                **config, "references": references, "tests": tests, "referenceCheckOrders": sorted(checks),
                "referenceCheckScope": "All public samples and first/last private/generated cases; not a full-suite execution."}
        json_write(directory / "manifest.json", item)
        entries.append(item)
    json_write(root / "candidates.json", {"formatVersion": 1, "dataset": "deepmind/code_contests", "revision": REVISION,
        "license": "CC-BY-4.0 for dataset non-code; Apache-2.0 for code; original-source terms apply", "shards": shards,
        "selection": "Conservative Codeforces deterministic scalar-line output; bounded stdin/stdout; complete available IO groups retained.",
        "excluded": excluded, "problems": entries})
    print(json.dumps({"candidates": len(entries), "tests": sum(len(x["tests"]) for x in entries), "excluded": excluded}))


if __name__ == "__main__":
    main()
