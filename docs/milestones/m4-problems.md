# M4 problem authoring and publication results

Date: 2026-09-19. Owner: project developer.

**M4 is complete.** M4 replaced the foundation draft slice with stable problem identity, editable authoring state, private ordered tests and immutable published versions. M5 subsequently added submissions that pin those exact versions.

## Implemented model and lifecycle

| Layer / class | Responsibility |
| --- | --- |
| Domain `Problem` | Aggregate root, immutable owner, editable content/configuration, ordered samples/tests, version increment, archive and optimistic revision |
| Domain `PublishedProblemVersion` | Immutable snapshot identified by version GUID and unique problem/version number |
| Domain `ProblemArtifactReference` | Opaque generated key, lowercase SHA-256 and bounded size invariants |
| Application `ProblemAuthoringUseCases` | Active Setter/Admin and owner enforcement, artifact orchestration, publish and archive |
| Application `ProblemCatalogUseCases` | Paginated current catalog, latest detail and exact historical version projections |
| Application account administration | Administrator-only role assignment and non-HTTP trusted first-admin provisioning |
| Infrastructure `ProblemRepository` | Focused EF persistence, atomic publication and concurrency-conflict mapping |
| Infrastructure `ProblemArtifactStore` | Bounded ZIP staging/extraction, hashing, path/link protection, verification and cleanup |
| API problem endpoints | Thin authoring/upload/publication/catalog contracts with safe ProblemDetails |

`Problem.Id` remains stable across versions. `PublishedProblemVersion.Id` identifies the exact specification future M5 submissions will store. Publishing v1, editing the authoring state and publishing again produces v2 without changing v1. Archive blocks further edits and current discovery but retains exact versions and all referenced artifacts.

Publication requires supported limits and checker, at least one public sample, at least one verified private test and all three trusted checklist statements. The checklist records manual reference-solution validation for development; no reference or contestant source executes in API/Worker. M6 owns sandbox execution and final verdict semantics.

## Persistence and storage

Migration `20260918163404_M4ProblemPublishing` renames the M3 draft table in place and preserves IDs, owners, text and creation timestamps. It adds authoring configuration/revision/archive state, ordered sample/test tables, immutable version/sample/test tables, account roles, foreign keys, unique ordering/version indexes and range/key/checklist constraints. Fresh/repeated migration and an explicit existing-M3-schema upgrade both pass.

The API role may update current authoring rows and insert published snapshots, but cannot update/delete published rows. The Worker gains no account, artifact-metadata or authoring permissions. Startup validates the exact grant set.

Test packages contain exactly `input.txt` and `output.txt`, are capped at 16 MiB compressed, and each artifact is capped at 8 MiB extracted. Generated keys never use client filenames as paths. Metadata commits only after files are promoted; failed commits delete them best-effort, and startup cleans unreferenced directories older than 24 hours. Public contracts contain only private-test counts and byte sizes where relevant, never keys, paths, inputs or expected outputs.

## Validation

The clean WSL foundation pipeline passed locked Release restore/build with zero warnings, fresh/repeated PostgreSQL migrations, 10 Domain tests and 93 runnable integration/Application/architecture tests, Worker startup/cancellation, trusted first-admin provisioning, real HTTPS authoring/upload/publication, storage readiness and database outage/recovery. Its one Windows-only ACL test was skipped as expected and passed separately on Windows. Linux permission/symlink checks passed. EF reports no pending model changes, and `git diff --check` reports no content errors.

Coverage includes Domain invariants and snapshots; authorization outside HTTP; anonymous/solver/cross-owner rejection; administrator role assignment and revocation; missing publication state; storage/database failure compensation; traversal, unsafe entries and decompression limits; checksum verification; abandoned cleanup; PostgreSQL constraints and grants; concurrent publication; fresh and M3 upgrade migrations; archive/history/restart persistence; safe errors; hidden-data projections; and project dependency rules.

## Known limits and deferred work

- Reference validation is a trusted checklist until M6 supplies sandbox-backed automation.
- Only the exact built-in checker is supported; final checker/verdict behavior belongs to M6.
- Storage is one private host-local root. Broad quotas, retention policy, backup/restore and production topology belong to M7.
- There is no frontend, contest configuration, scoring, subtasks, interactive judging, special judge or additional language support.
- M5 subsequently added submissions that pin `PublishedProblemVersion.Id`, bounded source admission, an atomic PostgreSQL submission/job transaction, leases/retries and private owner status/history. Its deterministic executor does not execute contestant code; real judging remains M6.
