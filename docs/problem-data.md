# Problem data

Git contains importer/classifier tools and small policy fixtures. It does not contain downloaded datasets, private test contents, reference solutions, account credentials or database backups. A fresh Minos stack has an empty library.

## Provenance and supported problems

The local development bank was imported from the training split of `deepmind/code_contests`, revision `802411c3010cb00d1b05bad57ca77365a3c699d6`. Selected records retain source URLs, shard/row provenance, original limits, manifest checksums and original statements. The importer preserves every available public/private/generated test pair in order; it does not truncate to 100 tests or synthesize missing tests.

Only bounded stdin/stdout problems with deterministic Exact-compatible output are selected. Interactive, image-dependent, floating-point, constructive, file-based and ambiguous-checker problems are excluded. The upstream checker is unavailable. Adapted statements specify exact newline/case requirements; original statements remain in the portable dataset.

The [upstream project](https://github.com/google-deepmind/code_contests) identifies Apache-2.0 for code and CC-BY-4.0 for other material, with original-source terms also applicable. Stored statements retain dataset/Codeforces attribution even where the reader omits the known import footer. Importing a personal development bank does not establish unrestricted redistribution rights. Do not commit dataset contents or claim a license for third-party material.

## Extract and validate

Tools live in `scripts/problem-bank/`. Extraction needs the pinned source shards and PyArrow, installed separately as a developer tool; PyArrow is not an application dependency.

```sh
python scripts/problem-bank/extract-codecontests.py --root data/codecontests-200 --reader-path /path/to/pyarrow --candidate-count 400 --shards 0 1 2 3 4 5
python scripts/problem-bank/test-problem-bank.py
```

The portable bank holds `bank.json`, per-candidate manifests, source/Minos statements, developer reference programs and test files. Hashes bind these files to the selected bank. Keep the complete portable directory when moving it.

Reference validation runs trusted known programs through the existing pinned compiler and fresh sandboxes, never directly on the host. Stop application Workers first, provide the absolute extracted bank path and use the current checks image:

```sh
MINOS_TEST_RUNNER=docker MINOS_TEST_CLASS=ProblemBankReferenceTests MINOS_TEST_PROBLEM_BANK=/absolute/bank MINOS_TEST_BANK_TARGET=200 bash scripts/benchmarks/native.sh minos-checks:local
```

Validation covers public samples and representative first/last private/generated cases. It does not prove the reference passed the entire retained suite. Official user submissions execute every pinned test. Publication records trusted checklist evidence; there is no stored-reference execution gate.

## Import and verify

With a healthy trusted-HTTPS local stack and verified receipts:

```sh
python scripts/problem-bank/import-codecontests.py --root data/codecontests-200 --count 200 --verify-only
python scripts/problem-bank/import-codecontests.py --root data/codecontests-200 --count 200
python scripts/problem-bank/verify-codecontests.py --root data/codecontests-200
```

The importer uses the actual account/authoring/publication API with cookies and CSRF. It provisions a dedicated author through trusted maintenance, stores its credentials privately and uploads one ZIP per test. It checks manifests/hashes before writes and checkpoints completed operations. Ambiguous POST responses stop for reconciliation; they are not blindly retried.

`import-state.json` belongs to one database and maps source IDs to local problem/version IDs. A fresh target requires a separate journal and owner. `--resume` is only for the same previously preflighted bank/database; each remaining upload still verifies its files. `--verify-only` performs the full scan. Verification can also check a private artifact archive with `--artifact-tar`.

The recorded local bank contains 200 published problems and 22,167 input/output pairs, with 9–267 tests per problem. These are imported development data, not repository seed data. Some source statements need editorial review; metadata coverage and checksum integrity do not establish statement quality.

## Classification

```sh
python scripts/problem-bank/classify-problem-bank.py --root data/codecontests-200 --state data/codecontests-200/import-state.json --output artifacts/classification
python scripts/problem-bank/import-problem-classification.py --root data/codecontests-200 --state data/codecontests-200/import-state.json --output artifacts/classification/import-plan.json
```

The policy in `fixtures/problem-classification-policy.json` maps source ratings to Easy at ≤1200, Medium at 1201–1900 and Hard above 1900. Missing/zero ratings remain Unclassified. These are local practice bands, not calibrated contestant ratings or LeetCode equivalents. Source tags map to 24 normalized topics; special/unknown tags are preserved as source evidence.

Optional `--proposals scripts/problem-bank/fixtures/problem-topic-proposals.json` supplies checksum-bound inferred topics with confidence/rationale. These remain explicitly inferred and cannot replace upstream algorithm tags. No private test contents are used as classification evidence.

Classification exports are deterministic for the same bank/policy/evidence. CSV is protected against formula interpretation. The import-plan command is offline by default. Add `--apply --credentials /private/owner.json --ca /trusted/ca.crt` to write through the existing owner's API. It preflights all targets, skips identical metadata, refuses conflicting classifications and reconciles partial runs before retry.

Difficulty/topics are mutable stable-problem metadata. Published judging snapshots remain immutable. Public catalog responses omit source ratings, policy evidence and inferred rationale. See [architecture](architecture.md) for revision and reassessment behavior.
