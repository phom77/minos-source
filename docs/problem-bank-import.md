# Local CodeContests problem bank

The owner's approximately 200-problem import uses the existing authoring API, publication checklist, immutable published versions and private artifact store. No direct DB insertion, new project, reference-solution storage in production, checker change or new publication gate is introduced.

## Source and portable files

`data/codecontests-200/` is ignored by Git and Docker build contexts. Six original Hugging Face training Parquet shards are pinned to `deepmind/code_contests` revision `802411c3010cb00d1b05bad57ca77365a3c699d6`; each file's size/SHA-256 is checked against the revision's tree before extraction. PyArrow 25.0.1 is an isolated developer tool under ignored `artifacts/codecontests-python`, not an application dependency.

Extraction initially yields 292 candidates and 28,817 input/output pairs. `raw/` retains the source, `candidates/<source-id>/` contains original/Minos statements, developer reference sources, and separate public/private/generated `.in`/`.out` files. Manifests retain source URLs, shard/row provenance, limits, reference indices and per-file SHA-256. Original UTF-8 test bytes, group order and all available cases are preserved. No 100-test truncation or synthesized tests are applied. `bank.json` records the final selection; `import-state.json` maps source IDs to local problem/version IDs and `imports/<source-id>.json` retains uploaded test IDs.

Only bounded stdin/stdout Codeforces problems with text statements and deterministic integer/yes-no lines enter this bank. Image-dependent, interactive, floating-point, constructive, file-based and ambiguous-checker problems are excluded. The upstream checker is unavailable. Minos retains Exact checking and explicitly adds newline/case requirements to the statement; original statements are preserved separately. Examples are supplied as public samples rather than duplicated in the statement.

[The upstream repository](https://github.com/google-deepmind/code_contests) identifies Apache-2.0 for code and CC-BY-4.0 for other material, with original-source terms also applicable. Each imported statement credits its Codeforces URL and the dataset. This is the requested local personal-project bank; the import does not establish permission for unrestricted public/commercial redistribution.

## Reference evidence and import

`ProblemBankReferenceTests` executes candidate C++ programs only in the existing digest-pinned Docker compiler and fresh runtime sandboxes. It checks every public sample and the first/last private/generated cases, records exact verdicts, CPU/wall/memory and cleanup, and binds evidence to the manifest. This is representative reference checking, **not execution of the entire retained suite**. Only passing candidates are published. Official submissions execute every imported test under the existing complete-test policy.

Compiler memory is now 512 MiB after a measured 256 MiB OOM for an otherwise valid minimal `bits/stdc++.h` program; see ADR 0009. The shared stage budget still controls concurrency and memory. Runtime limits are derived from original limits, with memory rounded down to MiB, and the bound is visible in Minos.

`scripts/import-codecontests.py` preflights evidence and all selected file hashes, uses verified HTTPS/cookies/CSRF, creates a dedicated active Setter, and uploads `input.txt`/`output.txt` ZIP packages through Application authorization. The dedicated owner is provisioned through the trusted maintenance operation and immediately reduced to Setter; existing accounts are unchanged. Its credentials remain in a restricted, ignored directory outside the portable bank. Checkpoints avoid duplicate operations on successful resume; an ambiguous POST response stops for reconciliation instead of automatic retry. Reusing `import-state.json` assumes the same database; a fresh target requires a separate state file and newly provisioned owner.

For the same already-preflighted bank/DB, `--resume` avoids a second full scan of original private files; each remaining upload still verifies its path, size and SHA-256 before sending. `--verify-only` always performs the full file scan. Persistent HTTPS avoids repeated TLS handshakes and IPv4 loopback avoids the local IPv6 connection fallback. Local atomic checkpoint replacement has bounded retries for transient Windows reader locks; HTTP writes are never replayed automatically.

## Reproduce and move

With the six pinned shards present:

```text
python scripts/extract-codecontests.py --root data/codecontests-200 --reader-path artifacts/codecontests-python --candidate-count 400 --shards 0 1 2 3 4 5
MINOS_TEST_RUNNER=docker MINOS_TEST_CLASS=ProblemBankReferenceTests MINOS_TEST_PROBLEM_BANK=<absolute-bank-root> MINOS_TEST_BANK_TARGET=200 bash scripts/benchmark-native-judging.sh <current-checks-image>
python scripts/import-codecontests.py --root data/codecontests-200 --count 200 --verify-only
python scripts/import-codecontests.py --root data/codecontests-200 --count 200
python scripts/test-problem-bank.py
python scripts/verify-codecontests.py --root data/codecontests-200 --artifact-tar D:/MinosData/backups/codecontests-200-20261009/artifacts.tar
```

Use the bundled Python or another Python with the pinned developer-only PyArrow package. Run sandbox validation from the approved Linux/WSL host with the Worker stopped. The import requires the existing healthy Compose API/web/PostgreSQL and exported local CA. Do not run downloaded reference programs directly on the host.

Copy the entire bank folder to move extracted data; all manifest paths are relative. To move the working Minos installation, separately back up PostgreSQL, the private-artifacts volume (including cookie keys) and owner credentials while API/Worker are stopped, then restore to fresh targets using the development runbook. The portable bank alone does not include accounts, submission history or authentication keys.

## Operational evidence

Before the schema migration, the existing local DB had three problems and three published versions, with no active public catalog entries. With API/Worker stopped, a consistent DB dump and private-artifact/cookie-key archive were saved in `D:\MinosData\backups\pre-codecontests-20261008`, protected by a current-user-only NTFS ACL, with a SHA-256 manifest. `ExpandProblemTestCapacity` was applied through the Compose migration host and runtime grants reapplied. Existing problems are retained.

The import completed at `2026-10-08T18:02:02Z` (2026-10-09 in Vietnam): **200 published problems and 22,167 test pairs**, with 9–267 tests per problem and 121 problems exceeding 100 tests. All original public/private/generated cases of each selected problem were uploaded; public examples are also available as samples. The anonymous catalog exposes all 200 imported problems. Published version/order/count and input/output size/SHA-256 were checked against the portable manifests.

A real HTTP submission for `cf-319-b` completed through the running Worker with **Accepted on all nine tests**, complete per-test resource evidence and the expected immutable version. The owner's history includes the submission without source, and anonymous detail access returns 401. The receipt in `verification.json` binds this check to the final bank manifest. Reference validation passed for 205 candidates before selection of exactly 200; it covers samples and representative private/generated cases, not all 22,167 retained cases.

Validation also included a successful solution build, 22 passing targeted Domain tests, seven passing offline bank regressions, and the opt-in official-judging regression run (21 passed, one skipped). The latter includes a real accepted `bits/stdc++.h` submission under the corrected compiler memory bound. These checks apply to the tested import/judging implementation; concurrent contest and scheduling changes were not deployed by this import.

After import and the real submission, API/Worker were stopped together for a consistent backup at `D:\MinosData\backups\codecontests-200-20261009`. It contains `database.dump` (5,039,585 bytes), `artifacts.tar` (63,262,720 bytes, including cookie keys), and a SHA-256 manifest, protected by a current-user-only NTFS ACL. The database archive is readable with `pg_restore --list`; no restore rehearsal to a fresh target has been performed. The archive verification checks all 44,334 referenced input/output files against the published DB and original file hashes without extracting cookie keys or private test contents. Detailed results are in `data/codecontests-200/verification.json`.

The existing API/Worker containers were resumed without rebuilding unrelated work. With pending source/migration changes, resume those containers directly rather than invoking Compose dependency startup: current grants may require schemas that the existing migration image does not yet contain. Deploy migration, grants and matching binaries together when those separate changes are ready.
