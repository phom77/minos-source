# Operations

These procedures cover local development on one Linux judge host. Production hosting, public capacity, key rotation and public retention policies require further design.

## Upgrade

1. Run the relevant [verification commands](development.md#verification). Retain the previous complete images/configuration and a consistent backup.
2. Stop API and Worker writers. Back up PostgreSQL and private artifacts together, including cookie-protection keys. Preserve `.env` and local certificates separately in private storage.
3. Build matching API/Worker/web images. Apply migrations with the migrator identity and then explicit runtime grants. Do not start an outdated migration image against new source.
4. Require API readiness, then start the sole Worker. A stopped-Worker `--check` validates database privileges, storage, toolchain, host lock and recovery. Do not run `--check` beside a live Worker.
5. Require healthy web/Worker and verify registration, publication, submission, owner results and denied cross-account access with temporary fixtures.

For a Compose upgrade, after the stopped-host backup:

```sh
docker compose up --build -d --wait
docker compose ps
```

Runtime grants are in [runtime-grants.sql](../scripts/database/runtime-grants.sql); bootstrap role setup is separate. API and Worker never use migrator/bootstrap credentials.

### Schema compatibility

Keep the 1,500-test schema and current binaries together when any published version exceeds 100 tests. Older binaries or the older constraint cannot represent that history; never truncate tests to force rollback.

Request identities, measurement profiles, classification and comments use additive migrations. Prefer retaining additive schema on binary rollback. Dropping request identities destroys replay guarantees; dropping profiles loses provenance; dropping comments deletes discussions. Do not backfill historical measurement profiles or rejudge to populate comparisons. Use forward fixes or restore a complete pre-upgrade backup to fresh targets.

## Health and diagnostics

- `GET /health/live` reports process liveness independently of dependency outages.
- `GET /health/ready` checks database/schema/privileges and private storage.
- `GET /api/operations/judging` requires an active Administrator and returns bounded queue/lease/result/failure aggregates.
- `docker compose exec worker /bin/sh /minos/worker-healthcheck.sh` checks the live host without taking its exclusive lock.

The admin snapshot includes Pending/Leased counts, expired leases, oldest Pending age, configured capacity and last-hour completion/SystemFailed/attempt failures. No source, tokens or hidden artifacts are included.

Use `System.Diagnostics.Metrics` meters `Minos.JudgeWorker`, `Minos.Sandbox` and `Minos.Judging`. Instruments include claims, lost leases, iteration/cleanup failures, recovered containers, active jobs/stages, job duration, reserved memory, compiler memory peak and phase durations. Stage/phase labels are fixed. No public metrics listener/exporter is enabled by default.

Follow trusted HTTP trace/submission IDs into job/attempt logs and persisted results. Request keys, lease tokens, source, hidden files and runtime output must remain outside logs. Phase totals overlap component timings; do not sum them or interpret them as contestant CPU usage.

| Condition | Action |
| --- | --- |
| Readiness fails | Check database availability, migrations, exact grants and private/key-root permissions. Restore dependencies before admitting work. |
| Pending age >120 s on two observations 30 s apart | Check Worker, pinned image, Docker and stuck leases; inspect the actual test count and workload. |
| Lease remains expired after 5 s of a running Worker's polling | Investigate renewal/host/database health, restart the sole faulty Worker and allow bounded reclaim. Do not mutate tokens/results. |
| Cleanup counter increases or cleanup-error log appears | Stop new work, investigate recognized container/workspace state, then restart through recovery. Never use broad Docker prune or delete an unrecognized directory. |
| SystemFailed increases, or 3 failed attempts within an hour | Check missing/corrupt artifacts and runner evidence; restore dependencies and validate with a new developer submission. Preserve attempts; manual rejudge is not implemented. |
| Private filesystem >80% used or <1 GiB free | Stop new developer admission, back up and inspect retained data. Remove only verified expired backups/unreferenced uploads; preserve referenced artifacts/history. |

The 120-second queue threshold comes from a bounded development workload, not arbitrary maximum-limit problems. Monitor PostgreSQL, Docker layers, workspaces and backups separately: queue bounds do not limit lifetime disk growth.

## Failure recovery

One Worker owns the execution host lock. Stop older processes before upgrades; older releases without this lock cannot safely coexist. Startup removes only recognized abandoned sandbox/workspace state and refuses unknown or unsafe contents. Lease/attempt tokens remain authoritative after recovery.

After a crash or database outage, restore dependencies and restart the current Worker. Expired work is reclaimed with bounded attempts. Active processing retains its scope and resource permits through sandbox cleanup; forced termination relies on startup recovery. Do not use a second `--check` as a live health probe.

Changing runner or execution quotas requires stopping/restarting the Worker. To leave native mode, select `Minos__Execution__Runner=docker`, remove `IsolateRoot`, restore the ordinary workspace configuration and validate the stopped host. Do not grant privileged mode to Compose for native execution. Native cleanup uncertainty blocks the whole pool until recovery.

## Backup and restore

Keep seven daily consistent backups during active development, outside the repository and artifact root, with private access. Database and private files have no online cross-store snapshot protocol: API and Worker must remain stopped for the whole backup. Backups include source, tests and cookie keys. Checksums detect corruption, not authenticity; restore trusted archives only.

The Linux scripts target Docker PostgreSQL and a directly accessible private artifact directory. For Compose named volumes, quiesce writers and export the matching private-artifacts volume through trusted tooling; do not point a host path at an unrelated directory.

Set these values privately: `MINOS_POSTGRES_CONTAINER`, `MINOS_MIGRATOR_PASSWORD`, `MINOS_ARTIFACT_ROOT`, `MINOS_BACKUP_ROOT`. Both filesystem roots must exist with mode 700 outside the repo. Only after stopping both writers, set `MINOS_BACKUP_QUIESCED=1` and run:

```sh
bash scripts/operations/backup.sh
```

The returned directory contains `database.dump`, `artifacts.tar` and `manifest.sha256`. An `.incomplete` marker prevents restoring a partial backup. Copy completed backups to private storage before restarting writers. Save `.env` and the certificate volume privately for machine moves; they are not part of the artifact directory.

For restore, provision a **fresh empty** database on the matching PostgreSQL version with the same three role names and restricted database/schema grants. If roles exist, grant access to the new database rather than recreating them. Create an empty mode-700 private directory. Set `MINOS_RESTORE_DATABASE`, `MINOS_RESTORE_ARTIFACT_ROOT`, the PostgreSQL container and migrator password, then run:

```sh
bash scripts/operations/restore.sh /absolute/private/completed-backup
```

The script rejects nonempty targets, incomplete/corrupt backups and unsafe archive members/links. Database restore uses one transaction. If later filesystem restoration fails, discard only these fresh targets and retry to new empty targets. Never point runtime hosts at partial restoration.

Grant `USAGE ON SCHEMA problems TO minos_api, minos_worker`, apply current runtime grants, configure runtime connections/storage and start the matching release. Verify readiness, exact owner history, immutable versions, request-key replay and a real judged submission on a restored problem before using the target.

## Retention

Keep submission source, bounded results/diagnostics, attempts, immutable published versions and referenced tests for the development project's lifetime. Startup/hourly API maintenance removes only unreferenced upload/staging files older than 24 hours. Stage scratch is cleaned after execution and recognized crash leftovers are recovered at startup. Cookie keys are durable and included in paired backups.

Schema rollback is not a cleanup mechanism. For an incompatible change, restore the complete previous release and paired backup to fresh targets, reapply that release's grants and keep writers stopped until verification succeeds.
