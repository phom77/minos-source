# Backend development operations

Date: 2026-10-02. Applies to the developer-only WSL2 Ubuntu/Docker profile; public hosting remains deferred.

## Release and fresh deployment

1. Run `bash scripts/check-foundation.sh` in WSL with the README's .NET path and pinned compiler image. The command builds a clean locked Release export, applies fresh/repeated/upgrade migrations, runs all tests and real official-judging/reliability/restore fixtures. Require `foundation_checks=passed` and `m7_checks=passed`. Keep TRX files and `artifacts/m7-workload.json` with the release revision. GitHub Actions runs the same script; a hosted run requires commit/push.
2. Build/publish the API and Worker with the pinned SDK and lockfiles. Keep Windows and Linux build artifacts separate. Retain the previous complete release, its configuration and a consistent backup. Pin the existing GCC and PostgreSQL image digests; pull GCC before starting the Worker. Do not tune limits or migrate packages during release.
3. On a fresh PostgreSQL instance, provision the three roles using `scripts/postgres-bootstrap.sql` as the bootstrap identity. Keep bootstrap/migrator/API/Worker passwords distinct. Provide credentials through private environment files, user secrets or the platform secret store. Never include secrets in commands committed to source, shell tracing, CI artifacts or logs. Keep runtime environment files mode 600 outside the repository; use equivalent private Windows ACLs for the API.
4. Stop every API/Worker using the database before migration. Validate a private Linux-native artifact root outside the repository, prepare HTTPS and protect data-protection keys. Run `Minos.Api --migrate` using `minos_migrator`, then `scripts/postgres-runtime-grants.sql` using that identity. M7 has no schema migration, but its new API job-read grant is required. Worker credentials remain excluded from account/credential tables.
5. Start API, require `/health/ready` 200, then start one Worker. `Minos.JudgeWorker --check` validates database, storage, pinned Docker image, host exclusivity and orphan recovery before exiting; run it while the Worker is stopped. A second worker process must fail. Compose healthchecks use `/minos/worker-healthcheck.sh` to inspect the startup marker, process and Docker without starting another host or invoking recovery. Stop the Worker before the Compose .NET checks suite because it shares the execution volume and recovery ownership; restart it afterward, including on test failure. Use the README's trusted administrator provisioning only if necessary. Rehearse register, publish, submit, owner result/history, and rejected cross-account access with disposable developer fixtures.

## Detect and recover failures

Authenticated active administrators poll `GET /api/operations/judging`. It reports current Pending/Leased counts, expired leases, oldest Pending age, configured capacity and completed/SystemFailed/failed-attempt/executor-failure counts for the last hour. Counters are computed from one repeatable-read database snapshot and include no submission contents. Process instruments are exposed through .NET's `System.Diagnostics.Metrics` diagnostic infrastructure: meters `Minos.JudgeWorker` and `Minos.Sandbox`, counters `minos.worker.claims`, `minos.worker.lost_leases`, `minos.worker.iteration_failures`, `minos.sandbox.recovered_containers` and `minos.sandbox.cleanup_failures`. Attach a local diagnostic collector when investigating; no public metrics listener or exporter is enabled.

| Condition | Development alert and action |
| --- | --- |
| Readiness fails | Alert immediately; retain liveness for diagnosis. Check database availability/privileges/current migrations and private-root/key-root permissions. Restore the dependency and require readiness before accepting new work. |
| Oldest pending exceeds 120 seconds | Alert on two observations 30 seconds apart. Verify Worker is alive, Docker/image is available and there is no duplicate host lock owner. A full queue returns 429; retry after the hint rather than bypassing admission. The target applies to the recorded eight-job workload, not arbitrary maximum-limit jobs. |
| Expired lease | Alert if still expired after five seconds of a running worker's polling. Stop the faulty process, restart the sole Worker, and let bounded reclaim occur. Never manually overwrite results or attempt tokens. |
| SystemFailed increases, or three failed attempts in one hour | Inspect safe stage/failure metadata and dependencies; correct missing/corrupt artifacts from a consistent backup. Do not fabricate a verdict or rejudge immutable history. Validate with a new developer submission after repair. |
| Cleanup counter increases or cleanup-error log appears | Stop Worker and investigate Docker availability/private workspace permissions. Restart only after recovery succeeds. It removes exact recognized names, rejects links/unexpected contents and prevents claims on startup failure. Never issue broad Docker prune or delete the configured artifact root. |
| Worker terminates unexpectedly | Restart the same release after confirming the process exited. Kernel releases the host lock; startup removes abandoned containers/workspaces. The job is reclaimable after its persisted 30-second lease expires. A result-commit acknowledgement can be lost: inspect the durable owner result; duplicate guarded completion is idempotent. |
| Private filesystem uses more than 80%, or has less than 1 GiB free | Stop admitting new developer work, back up and inspect retained data. Remove verified expired backups/unreferenced uploads through the documented policy. Preserve every published artifact and pinned version. Queue bounds do not cap lifetime history. |

Log error types, stage flags and opaque IDs only. Credentials, source, hidden answers and runtime output must not be copied into diagnostic tickets. `Completed` requires a committed authoritative result; a failed/unacknowledged admission must not be presented as accepted. Client POST retry after a lost network response can create a second submission: admission is atomic but has no client idempotency key; inspect owner history before retrying.

## Backup and restore

Maintain seven daily backups for active development, on a private filesystem outside the repository/artifact root. Quiesce both hosts for the entire backup; there is no online cross-store snapshot protocol. Backups contain credentials/source/tests/protected-cookie keys and require trusted private access. The scripts target the documented local Docker PostgreSQL profile, not a general production backup system.

Set these environment variables privately (do not enable `set -x`): `MINOS_POSTGRES_CONTAINER`, `MINOS_MIGRATOR_PASSWORD`, `MINOS_ARTIFACT_ROOT` and `MINOS_BACKUP_ROOT`. Both roots must already exist with mode 700. Set `MINOS_BACKUP_QUIESCED=1` only after stopping API and Worker, and run:

```bash
bash scripts/backup-development.sh
```

The returned directory contains `database.dump`, `artifacts.tar` and `manifest.sha256`. An `.incomplete` marker prevents partial backups being restored. Copy the completed directory to trusted private backup storage, preserving mode 700, before restarting the hosts. A checksum is corruption detection, not proof of authenticity; never restore untrusted archives.

For restoration, create a new empty database on the matching PostgreSQL version, with the same three stable role names. Provision their restricted database/schema access using the bootstrap procedure. When roles already exist on the cluster, do not rerun its `CREATE ROLE` statements: revoke PUBLIC database/schema rights and grant migrator CONNECT/CREATE, runtimes CONNECT, migrator public-schema USAGE/CREATE and runtime public-schema USAGE instead. The restore supplies application schemas. Create an empty mode-700 private directory. Set `MINOS_RESTORE_DATABASE` and `MINOS_RESTORE_ARTIFACT_ROOT` and run:

```bash
bash scripts/restore-development.sh /absolute/private/completed-backup
```

Restore refuses a nonempty database or artifact directory, incomplete/corrupt backup, links or unsafe archive members. It restores database content in one transaction. If any later step fails, discard only the newly created restore targets and retry to fresh targets; never point runtime hosts at a partial restore. Grant `USAGE ON SCHEMA problems TO minos_api, minos_worker` and reapply `scripts/postgres-runtime-grants.sql` as migrator. Supply updated runtime connections/artifact root, start the same release, require readiness, compare owner history and immutable versions, then submit and judge a restored problem through Docker. The M7 fixture performs this rehearsal, including restored data-protection keys and exact result-history equality.

## Retention and rollback

Source, bounded diagnostics, attempts/results and referenced immutable tests are retained for the development project's lifetime. API startup and its hourly maintenance remove only unreferenced uploads/staging older than 24 hours. Containers and transient execution workspaces are deleted after each stage; startup recovery removes recognized hard-crash leftovers before new work. Cookie keys follow platform key management and are included in backups. Keep originals of visual references unchanged. Future custom executions have no retention policy because M8 has not started.

For this M7 release there is no schema change. Stop both hosts and restore the prior binaries/configuration if necessary. The additional read grant can remain safely while rolling back, or be revoked only after the M7 API is stopped and the old runtime readiness policy is restored. Never run an old binary concurrently with a new Worker; pre-M7 runners have no host lock. For a later incompatible schema change, stop writers, restore the complete pre-migration backup to fresh targets, reapply that release's grants and start the previous release. Forward fixes are preferred; do not run destructive down migrations against retained history. Rehearse every release-blocking recovery change with disposable targets before use.
