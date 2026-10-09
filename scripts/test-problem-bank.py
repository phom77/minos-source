"""Offline regressions for portable bank integrity and admission filtering."""
import hashlib
import importlib.util
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
TEMP_ROOT = Path(__file__).resolve().parent.parent / "artifacts/problem-bank-tests"
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


if __name__ == "__main__":
    unittest.main()
