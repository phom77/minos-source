"""Offline regressions for portable bank integrity, admission and classification."""
import csv
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


extract = load("extract-codecontests")
importer = load("import-codecontests")
classifier = load("classify-problem-bank")
classification_import = load("import-problem-classification")
TEMP_ROOT = Path(__file__).resolve().parents[2] / "artifacts/problem-bank-tests"
TEMP_ROOT.mkdir(parents=True, exist_ok=True)


class PortableBankTests(unittest.TestCase):
    def bank(self, root):
        identifier = "cf-1-a"
        directory = root / "candidates" / identifier
        directory.mkdir(parents=True)
        (root / "validation").mkdir()
        def file(name, data):
            path = directory / name
            path.write_bytes(data)
            return {"path": path.relative_to(root).as_posix(), "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}
        statement = file("statement.txt", b"Print the sum.")
        reference = file("reference.cpp", b"int main(){}")
        tests = [{"order": i, "group": "public_tests" if i == 1 else "private_tests",
                  "input": file(f"{i}.in", b"1 2\n"), "output": file(f"{i}.out", b"3\n")} for i in (1, 2)]
        problem = {"id": identifier, "title": "Sum", "checker": "Exact", "statement": statement,
                   "originalStatement": statement, "references": [reference], "tests": tests,
                   "referenceCheckOrders": [1, 2], "timeLimitMilliseconds": 1000, "memoryLimitMegabytes": 64}
        importer.save(directory / "manifest.json", problem)
        importer.save(root / "candidates.json", {"problems": [problem]})
        receipt = {"id": identifier, "accepted": True, "compilationMemoryMegabytes": 512, "checker": "Exact",
                   "reference": reference["path"], "checkedOrders": [1, 2], "totalAvailableTests": 2,
                   "manifestFileSha256": hashlib.sha256((directory / "manifest.json").read_bytes()).hexdigest(),
                   "attempts": [{"reference": reference["path"], "compilation": "Succeeded",
                                 "tests": [{"order": i, "status": "Passed", "cleanupComplete": True} for i in (1, 2)]}]}
        return problem, receipt

    def test_rejects_stale_manifest_evidence(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            problem, receipt = self.bank(root)
            importer.save(root / "validation/cf-1-a.json", receipt)
            self.assertEqual(1, len(importer.select(root, 1)[1]))
            problem["timeLimitMilliseconds"] = 2000
            importer.save(root / "candidates/cf-1-a/manifest.json", problem)
            with self.assertRaises(ValueError):
                importer.select(root, 1)

    def test_rejects_incomplete_or_failed_reference_measurements(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            _, receipt = self.bank(root)
            receipt["attempts"][0]["tests"].pop()
            importer.save(root / "validation/cf-1-a.json", receipt)
            with self.assertRaises(ValueError):
                importer.select(root, 1)
            receipt["attempts"][0]["tests"] = [{"order": 1, "status": "WrongAnswer", "cleanupComplete": True}]
            importer.save(root / "validation/cf-1-a.json", receipt)
            with self.assertRaises(ValueError):
                importer.select(root, 1)

    def test_reads_exact_bytes_and_rejects_modified_file(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            original = b"01\r\n\n"
            (root / "case.out").write_bytes(original)
            entry = {"path": "case.out", "sha256": hashlib.sha256(original).hexdigest(), "bytes": len(original)}
            self.assertEqual(original, importer.read_file(root, entry))
            (root / "case.out").write_bytes(b"1\n")
            with self.assertRaises(ValueError):
                importer.read_file(root, entry)

    def test_rejects_parent_and_absolute_paths(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            for path in ("../outside", str(root / "absolute")):
                with self.assertRaises(ValueError):
                    importer.read_file(root, {"path": path, "sha256": ""})

    def test_rejects_oversized_artifacts_before_read(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            (root / "input").write_bytes(b"123")
            with self.assertRaises(ValueError):
                importer.read_file(root, {"path": "input", "sha256": ""}, maximum=2)

    def test_exact_subset_excludes_token_checker_outputs(self):
        for output in ("1", "1 \n", "1\r\n", "1 2\n", "1.0\n", "yes\n\n"):
            self.assertIsNone(extract.SCALAR_LINES.fullmatch(output), repr(output))
        for output in ("-1\n", "YES\nNO\n", "1\n2\n"):
            self.assertIsNotNone(extract.SCALAR_LINES.fullmatch(output))

    def test_json_checkpoint_replaces_whole_file(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            path = Path(directory) / "state.json"
            importer.save(path, {"count": 1})
            importer.save(path, {"count": 2})
            self.assertIn('"count": 2', path.read_text())
            self.assertFalse(path.with_suffix(".json.tmp").exists())


class ClassificationTests(unittest.TestCase):
    def make_bank(self, root, tags=None, rating=1200, title="Sum"):
        folder = root / "candidates/cf-1-a"
        folder.mkdir(parents=True)
        statement = folder / "statement.txt"
        statement.write_text("Print the sum.", encoding="utf-8")
        self.problem = {"id": "cf-1-a", "title": title, "sourceUrl": "https://codeforces.com/problemset/problem/1/A",
                        "rating": rating, "tags": ["math"] if tags is None else tags,
                        "statement": {"path": "candidates/cf-1-a/statement.txt", "sha256": hashlib.sha256(statement.read_bytes()).hexdigest()},
                        "tests": [{"input": "PRIVATE_INPUT_SENTINEL", "output": "HIDDEN_ANSWER_SENTINEL"}]}
        self.save_bank(root)

    def save_bank(self, root):
        importer.save(root / "bank.json", {"formatVersion": 1, "problems": [self.problem]})
        importer.save(root / "candidates/cf-1-a/manifest.json", self.problem)

    def proposal_file(self, root):
        path = root.parent / "proposals.json"
        importer.save(path, {"formatVersion": 1, "problems": [{"sourceId": "cf-1-a", "tags": ["math"],
                      "confidence": "high", "rationale": "Direct sum of integers.", "evidence": [self.problem["statement"]]}]})
        return path

    def test_fixed_thresholds_missing_ratings_and_invalid_numeric_types(self):
        policy = classifier.load_policy(classifier.DEFAULT_POLICY)
        for rating, expected in ((800, "Easy"), (1200, "Easy"), (1201, "Medium"), (1900, "Medium"),
                                 (1901, "Hard"), (2100, "Hard"), (None, "Unclassified"), (0, "Unclassified")):
            self.assertEqual(expected, classifier.difficulty(rating, policy))
        for rating in (False, True, 0.0, 1200.5, -1, "1200"):
            with self.assertRaises(ValueError):
                classifier.difficulty(rating, policy)

    def test_configurable_policy_and_rejects_invalid_threshold_order(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            self.make_bank(root)
            policy = classifier.load_policy(classifier.DEFAULT_POLICY)
            policy["easyMaxRating"] = 1000
            path = root / "policy.json"
            importer.save(path, policy)
            self.assertEqual("Medium", classifier.classify(root, path)[0]["problems"][0]["difficulty"])
            policy["easyMaxRating"] = 2000
            importer.save(path, policy)
            with self.assertRaises(ValueError):
                classifier.classify(root, path)

    def test_unknown_and_non_algorithm_tags_are_preserved_and_flagged(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            self.make_bank(root, tags=["*special", "future tag"], rating=None)
            result, queue = classifier.classify(root)
            row = result["problems"][0]
            self.assertEqual(["future tag"], row["unmappedTags"])
            self.assertEqual(["*special"], row["excludedTags"])
            self.assertEqual([], row["topics"])
            self.assertCountEqual(["missing-rating", "missing-topics", "unmapped-source-tags"], queue["problems"][0]["reasons"])

    def test_rejects_selected_bank_manifest_drift_and_duplicate_source_ids(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            self.make_bank(root)
            self.problem["rating"] = 2100
            importer.save(root / "bank.json", {"formatVersion": 1, "problems": [self.problem]})
            with self.assertRaises(ValueError):
                classifier.classify(root)
            self.save_bank(root)
            importer.save(root / "bank.json", {"formatVersion": 1, "problems": [self.problem, self.problem]})
            with self.assertRaises(ValueError):
                classifier.classify(root)

    def test_ai_proposals_fill_marker_only_tags_and_remain_flagged(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory) / "bank"
            self.make_bank(root, tags=["*special"])
            result, queue = classifier.classify(root, proposals_path=self.proposal_file(root))
            row = result["problems"][0]
            self.assertEqual(["math"], row["topics"])
            self.assertEqual(["*special"], row["sourceTags"])
            self.assertEqual("ai-proposal", row["topicSource"])
            self.assertEqual(["ai-topic-proposal"], queue["problems"][0]["reasons"])
            self.assertEqual(0, result["summary"]["upstreamTopicProblems"])

    def test_proposals_cannot_replace_upstream_topics_or_use_stale_evidence(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory) / "bank"
            self.make_bank(root)
            path = self.proposal_file(root)
            with self.assertRaises(ValueError):
                classifier.classify(root, proposals_path=path)
            self.problem["tags"] = []
            self.save_bank(root)
            (root / "candidates/cf-1-a/statement.txt").write_text("Changed", encoding="utf-8")
            with self.assertRaises(ValueError):
                classifier.classify(root, proposals_path=path)

    def test_proposal_evidence_cannot_read_private_test_files(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory) / "bank"
            self.make_bank(root, tags=[])
            path = self.proposal_file(root)
            proposal = json.loads(path.read_text(encoding="utf-8"))
            proposal["problems"][0]["evidence"] = [{"path": "candidates/cf-1-a/private.in", "sha256": "0" * 64}]
            importer.save(path, proposal)
            with self.assertRaisesRegex(ValueError, "statement or reference"):
                classifier.classify(root, proposals_path=path)

    def test_import_ids_are_checksum_bound_and_sensitive_fields_not_exported(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory)
            self.make_bank(root)
            state = {"ownerId": "OWNER_SECRET_SENTINEL", "problems": {"cf-1-a": {
                "manifestSha256": hashlib.sha256((root / "candidates/cf-1-a/manifest.json").read_bytes()).hexdigest(),
                "problemId": "00000000-0000-0000-0000-000000000001", "versionId": "00000000-0000-0000-0000-000000000002",
                "credential": "CREDENTIAL_SECRET_SENTINEL"}}}
            path = root / "state.json"
            importer.save(path, state)
            result, _ = classifier.classify(root, state_path=path)
            self.assertEqual(1, result["summary"]["mappedProblems"])
            exported = json.dumps(result)
            for sentinel in ("OWNER_SECRET_SENTINEL", "CREDENTIAL_SECRET_SENTINEL", "PRIVATE_INPUT_SENTINEL", "HIDDEN_ANSWER_SENTINEL"):
                self.assertNotIn(sentinel, exported)
            state["problems"]["cf-1-a"]["manifestSha256"] = "0" * 64
            importer.save(path, state)
            with self.assertRaises(ValueError):
                classifier.classify(root, state_path=path)
            importer.save(path, {"problems": {}})
            with self.assertRaises(ValueError):
                classifier.classify(root, state_path=path)

    def test_outputs_are_repeatable_separate_and_spreadsheet_titles_are_safe(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root, output = Path(directory) / "bank", Path(directory) / "output"
            self.make_bank(root, title="=HYPERLINK(sensitive)")
            before = (root / "bank.json").read_bytes()
            result, queue = classifier.classify(root)
            classifier.write_outputs(root, output, result, queue)
            first = {p.name: p.read_bytes() for p in output.iterdir()}
            classifier.write_outputs(root, output, *classifier.classify(root))
            self.assertEqual(first, {p.name: p.read_bytes() for p in output.iterdir()})
            self.assertEqual(before, (root / "bank.json").read_bytes())
            csv_row = next(csv.DictReader(io.StringIO(first["classification.csv"].decode("utf-8"))))
            self.assertTrue(csv_row["title"].startswith("'="))
            self.assertEqual("=HYPERLINK(sensitive)", result["problems"][0]["title"])
            for unsafe in (root, root / "output", root.parent):
                with self.assertRaises(ValueError):
                    classifier.write_outputs(root, unsafe, result, queue)

    def test_rejects_paths_outside_the_bank(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory).resolve()
            for relative in ("../outside", str(root / "absolute")):
                with self.assertRaises(ValueError):
                    classifier.checked_path(root, relative)

    def test_import_preparation_retains_ai_provenance_and_pins_policy(self):
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            root = Path(directory) / "bank"
            self.make_bank(root, tags=[])
            result, _ = classifier.classify(root, proposals_path=self.proposal_file(root))
            result["datasetRevision"] = "a" * 40
            result["problems"][0].update({"problemId": "00000000-0000-0000-0000-000000000001", "versionId": "00000000-0000-0000-0000-000000000002"})
            plan = classification_import.prepare(result)
            payload = plan["requests"][0]["request"]
            self.assertEqual(0, payload["expectedRevision"])
            self.assertEqual("CodeforcesRating", payload["difficultyOrigin"])
            self.assertEqual("Inferred", payload["topics"][0]["origin"])
            self.assertEqual("High", payload["topics"][0]["confidence"])
            self.assertEqual([], payload["evidence"]["sourceTags"])
            self.assertEqual(64, len(payload["evidence"]["policySha256"]))
            self.assertEqual(plan, classification_import.prepare(result))

    def test_http_import_preflights_all_rows_and_preserves_existing_changes(self):
        plan = self.import_plan()
        second = copy.deepcopy(plan["requests"][0])
        second["problemId"] = "problem-two"
        plan["requests"].append(second)
        session = self.ImportSession()
        session.existing["problem-two"] = {"problemId": "problem-two", "revision": 2, "difficulty": "Hard"}
        with self.assertRaises(ValueError):
            classification_import.apply(session, plan)
        self.assertEqual(0, session.writes)

    def test_lost_http_response_is_reconciled_without_overwriting_or_retrying(self):
        plan = self.import_plan()
        session = self.ImportSession()
        session.lose_response = True
        with self.assertRaises(OSError):
            classification_import.apply(session, plan)
        self.assertEqual(1, session.writes)
        self.assertEqual({"created": 0, "unchanged": 1}, classification_import.apply(session, plan))
        self.assertEqual(1, session.writes)
        session.existing["problem-one"]["difficulty"] = "Hard"
        with self.assertRaises(ValueError):
            classification_import.apply(session, plan)
        self.assertEqual(1, session.writes)

    @staticmethod
    def import_plan():
        return {"requests": [{"problemId": "problem-one", "request": {"expectedRevision": 0, "basisVersionId": "version-one",
            "difficulty": "Easy", "topics": [{"slug": "math", "origin": "Upstream"}]}}]}

    class ImportSession:
        def __init__(self):
            self.existing = {}
            self.writes = 0
            self.lose_response = False

        def request(self, path, method="GET", value=None):
            if path == "/accounts/me":
                return {"role": "Setter"}
            if path == "/api/problems/topics":
                return [{"slug": "math"}]
            if path.endswith("/classification"):
                identifier = path.split("/")[-2]
                if method == "PUT":
                    self.writes += 1
                    self.existing[identifier] = {"problemId": identifier, "revision": 1,
                        **{k: v for k, v in value.items() if k != "expectedRevision"}}
                    if self.lose_response:
                        self.lose_response = False
                        raise OSError("Response lost after commit")
                return copy.deepcopy(self.existing.get(identifier, {"problemId": identifier, "revision": 0}))
            return {"versionId": "version-one"}


if __name__ == "__main__":
    unittest.main()
