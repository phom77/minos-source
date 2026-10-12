# Backend development operations

Updated: 2026-10-09. Applies to the developer-only WSL2 Ubuntu/Docker profile and explicitly configured direct-Linux runtime; public hosting remains deferred.

## Release and fresh deployment

1. Run `bash scripts/check-foundation.sh` in WSL with the README's .NET path and pinned compiler image. The command builds a clean locked Release export, applies fresh/repeated/upgrade migrations, runs all tests and real official-judging/reliability/restore fixtures. Require `foundation_checks=passed` and `m7_checks=passed`. Keep TRX files and `artifacts/m7-workload.json` with the release revision. GitHub Actions runs the same script; a hosted run requires commit/push.
2. Build/publish the API and Worker with the pinned SDK and lockfiles. Keep Windows and Linux build artifacts separate. Retain the previous complete release, its configuration and a consistent backup. Pin the existing GCC and PostgreSQL image digests; pull GCC before starting the Worker. Do not tune limits or migrate packages during release.
3. On a fresh PostgreSQL instance, provision the three roles using `scripts/postgres-bootstrap.sql` as the bootstrap identity. Keep bootstrap/migrator/API/Worker passwords distinct. Provide credentials through private environment files, user secrets or the platform secret store. Never include secrets in commands committed to source, shell tracing, CI artifacts or logs. Keep runtime environment files mode 600 outside the repository; use equivalent private Windows ACLs for the API.
4. Stop every API/Worker using the database before migration. Validate a private Linux-native artifact root outside the repository, prepare HTTPS and protect data-protection keys. Run `Minos.Api --migrate` using `minos_migrator`, then `scripts/postgres-runtime-grants.sql` using that identity. M7 has no schema migration, but its new API job-read grant is required. Worker credentials remain excluded from account/credential tables.
5. Start API, require `/health/ready` 200, then start one Worker. `Minos.JudgeWorker --check` validates database, storage, pinned Docker image, host exclusivity and orphan recovery before exiting; run it while the Worker is stopped. A second worker process must fail. Compose healthchecks use `/minos/worker-healthcheck.sh` to inspect the startup marker, process and Docker without starting another host or invoking recovery. Stop the Worker before the Compose .NET checks suite because it shares the execution volume and recovery ownership; restart it afterward, including on test failure. Use the README's trusted administrator provisioning only if necessary. Rehearse register, publish, submit, owner result/history, and rejected cross-account access with disposable developer fixtures.

## Detect and recover failures

Authenticated active administrators poll `GET /api/operations/judging`. It reports current Pending/Leased counts, expired leases, oldest Pending age, configured capacity and completed/SystemFailed/failed-attempt/executor-failure counts for the last hour. Counters are computed from one repeatable-read database snapshot and include no submission contents. Process instruments are exposed through .NET's `System.Diagnostics.Metrics` diagnostic infrastructure: meters `Minos.JudgeWorker` and `Minos.Sandbox`, counters `minos.worker.claims`, `minos.worker.lost_leases`, `minos.worker.iteration_failures`, `minos.sandbox.recovered_containers` and `minos.sandbox.cleanup_failures`. Attach a local diagnostic collector when investigating; no public metrics listener or exporter is enabled.

Judging phase timing is available through meter `Minos.Judging`, histogram `minos.judging.phase_duration` (seconds). Its fixed `stage`/`phase` labels separate sandbox provisioning, execution, evidence and cleanup from private artifact reads/checking. Native `runtime/export` records initialization's pinned-library export. Total durations overlap component phases; do not sum them together or use them as contestant CPU/wall measurements. Native cleanup faults use the existing sandbox cleanup counter. See [judging performance evidence and the opt-in benchmarks](../judging-performance.md). Stop all Workers before sandbox benchmarking because startup recovery shares the daemon's judge label.

## Native runtime profile

Docker remains the default, including the non-root Compose Worker. The opt-in native adapter keeps Docker compilation and replaces runtime containers with fresh Isolate boxes/cgroups. Provision a separate direct x86-64 Linux host profile before enabling it; never make the Compose Worker privileged to run this adapter. [ADR 0013](../adr/0013-native-per-test-runtime.md) records the security/correctness requirements and [performance evidence](../judging-performance.md) records trusted-fixture reproduction.

Use Isolate 2.7 at commit `8f185bb37f3f23e29b33b0c7727c91c13429abe3`, built with its configuration path in a dedicated root-owned `/opt/minos-*` prefix. Required files are mode-4755 `isolate`, root-owned `isolate.cf` and the keeper-generated `cgroup` path. The config must bind `box_root`/`lock_root` to that prefix's `boxes`/`locks`, use `cg_root=auto:<prefix>/cgroup`, `num_boxes=1`, `box0.cpus=0`, and reserved unused UID/GID 61000. Root installation paths must not be symlinks or group/other writable. Give the non-root Worker its own mode-700 `<prefix>/work`; do not run it as UID 61000. Mount `boxes` as an eight-MiB noexec/nosuid/nodev tmpfs and delegate a dedicated cgroup v2 tree through the keeper, with `memory.swap.max=0`. Retain the pinned GCC image and the ordinary Docker/execution-workspace permissions and exclusive host lock.

Native startup also requires `fs.protected_hardlinks=1` and a non-piped `kernel.core_pattern`. A piped crash collector can execute outside the sandbox even with core size zero. The tested WSL host's `/wsl-capture-crash` pipe was not changed; live native startup on that host is expected to fail. The fixture harness bypasses this single gate only in internal test composition for known programs. There is no runtime configuration bypass. Select and validate a suitable host policy through a separate hosting decision before running untrusted submissions.

With every Worker stopped, configure `Minos__Execution__Runner=isolate` and `Minos__Execution__IsolateRoot=<prefix>` on the direct-Linux Worker; omit `Minos__Execution__WorkspaceVolume`. Database/private artifact configuration is unchanged. Run `Minos.JudgeWorker --check` first. It validates native hosting, acquires the existing lock, recovers the reserved box and recognized workspaces, exports only the pinned runtime libraries and prevents claims if any step fails. Then start the sole Worker. The default one-box profile serializes runtimes. Multiple boxes require the explicit installation below; PostgreSQL remains the queue. Runtime evidence requires descendants gone and the exact configured cgroup memory/swap limits; cleanup failure follows existing bounded retries.

To roll back, stop the Worker, select `Minos__Execution__Runner=docker`, remove `IsolateRoot`, restore the ordinary Docker workspace configuration and run `--check` before starting. Do not delete private tests or rewrite durable results. The experiment setup/cleanup scripts target only the explicitly approved temporary WSL prefix; they are not general deployment scripts. Always remove that temporary helper, service and mounts after trusted-fixture validation. Full experiment cleanup refuses unexpected ownership, active sandbox processes or surviving cgroups/mounts.

## Multiple native boxes and compile allocation

[ADR 0015](../adr/0015-compile-runtime-allocation-and-isolate-boxes.md) records the owner-authorized extension. `Minos__Execution__CompileSlots` and `RuntimeSlots` default to one each; both must be 1-`SandboxSlots`. `Minos__Execution__IsolateBoxCount` defaults to one, allows 1-16, and requires the native runner when greater than one. Native runtime quota must not exceed box count. Keep total stage slots and worst-case memory bounded even when the two class quotas add up to more than total capacity. These ceilings do not guarantee immediate compilation or preallocate RAM.

A candidate two-box profile uses eight active jobs, four total slots, two compilers, two runtimes, 4096 MiB stage memory and 768 MiB controller reserve. This requires at least five available logical CPUs and enough memory on both Docker and the native host, plus separate headroom for other workloads. Benchmark before adopting it; the default application runner remains Docker. For example:

```text
Minos__Judging__MaximumConcurrentJobs=8
Minos__Execution__SandboxSlots=4
Minos__Execution__CompileSlots=2
Minos__Execution__RuntimeSlots=2
Minos__Execution__MemoryBudgetMegabytes=4096
Minos__Execution__ControllerReserveMegabytes=768
Minos__Execution__IsolateBoxCount=2
Minos__Execution__Runner=isolate
Minos__Execution__IsolateRoot=<validated-two-box-prefix>
```

For N > 1, build each unmodified pinned helper with `CONFIG=<prefix>/helpers/<id>/isolate.cf` and install it root-owned mode 4755. Its root-owned configuration has `box_root=<prefix>/boxes/slot-<id>`, `lock_root=<prefix>/locks`, `cg_root=auto:<prefix>/cgroup`, `first_uid=61000`, `first_gid=61000`, `num_boxes=N`, and `box<id>.cpus=<id>`. Every slot root is a separate eight-MiB noexec/nosuid/nodev tmpfs; Isolate creates/removes its `<id>` directory below that mount. Reserve unused UID/GIDs 61000 through 61000+N-1. All helpers share the root-owned keeper delegation, with swap disabled. `<prefix>/helpers`, each helper parent, `<prefix>/boxes`, slot roots and locks must be root-owned and not writable by group/others. The controller remains a distinct non-root identity, with private `<prefix>/work`. CPU IDs 0 through N-1 must be available to the controller. Other native host/core-dump requirements above remain mandatory.

Before claims, startup validates every helper and recovers every box; unknown scratch/cgroup state prevents startup. A box is returned only after cleanup. Cleanup uncertainty stops the entire pool and new job deliveries until restart/recovery. Per-job cancellation only interrupts/cleans its assigned box. To revert to a one-box installation, stop the Worker, change the installation/root/count and stage quotas consistently, then run `--check`; merely changing the count against a multi-box installation fails validation.

Temporary trusted WSL reproduction, requiring the existing owner-authorized root experiment and current built checks image:

```bash
source_dir="$PWD/artifacts/judging-phase2-20261008/isolate-source"
MINOS_ISOLATE_BOXES=2 bash scripts/experiments/build-isolate-boxes.sh "$source_dir"
sudo env MINOS_ISOLATE_BOXES=2 bash scripts/experiments/isolate-multibox-host.sh setup "$source_dir"
MINOS_TEST_RUNNER=isolate MINOS_TEST_ISOLATE_BOXES=2 \
MINOS_TEST_ISOLATE_ROOT=/opt/minos-isolate-multibox-20261009-8f185bb \
MINOS_TEST_BENCHMARK=0 MINOS_TEST_BURST=1 MINOS_TEST_ALL=1 \
bash scripts/benchmark-native-judging.sh minos-judging-multibox-20261009-checks:local
sudo bash scripts/experiments/isolate-multibox-host.sh cleanup
```

These scripts install only the marked temporary prefix/unit and mounts, do not provision a production host or change the application's runner/core policy, and only execute known fixtures with the internal test-only core-policy override. Run cleanup even after a failed check. The twenty-job benchmark records admission duration rather than assuming every run admits within two seconds; its baseline and larger pipeline preserve counts and resource bounds.

## Operational alerts

Phase 3 keeps one Worker process with bounded jobs and a shared execution budget. Configure `Minos__Judging__MaximumConcurrentJobs` (default 4, range 1–16), `Minos__Execution__SandboxSlots` (default up to 2, at most processor count minus one), `Minos__Execution__MemoryBudgetMegabytes` (default 2048, range 1152–65536), and `Minos__Execution__ControllerReserveMegabytes` (default 512, range 256–16384 and at least `256 + 64 × MaximumConcurrentJobs`). Use private host environment/configuration; for Compose supply these keys under the Worker's `environment` in an override. `--check` validates the Docker execution host's CPU/RAM headroom before claims; native mode also validates the direct host. At least two CPUs are required, with one CPU reserved outside the sandbox slot budget. Account/global admission limits remain 5/32. Do not size the budget to all available host RAM; reserve more for the API/database and other workloads as needed.

One slot is reserved per stage, with requested memory plus scratch; output/input controller buffers are covered by the bounded jobs and management reserve. Waiters use FIFO within each stage class. A class at its quota cannot block the other class, while an eligible older memory-heavy stage is not bypassed by smaller stages. Leases renew independently while waiting. Native runtime uses its configured box pool (default one CPU-0 box); compilation remains Docker and can overlap with runtimes across active jobs. Tests within each submission remain sequential and every contestant failure still allows later tests. Set one concurrent job and one sandbox slot for serial scheduling; stop/restart the Worker when changing policy.

New local instruments include `minos.judging.active_stages` with fixed `compile`/`run` labels, `minos.judging.compiler_memory_peak` in bytes, `minos.worker.active_jobs`, `minos.worker.job_duration` (seconds), `minos.judging.active_sandboxes` (reserved stage slots, including provisioning/native box wait/cleanup), `minos.judging.reserved_memory` (bytes) and phase `budget-wait`. These report operational resource waiting, not contestant CPU/wall. A sandbox cleanup fault blocks new stage reservations and job delivery until restart/recovery. Preserve the failed attempt and pending jobs; investigate before restart. The Worker awaits active job cleanup on shutdown with a 40-second host timeout inside Compose's 45-second grace period. Forced termination remains recoverable through leases/startup cleanup.

For repeatable correctness/load checks, stop every Worker/sandbox first and run `bash scripts/check-judging-concurrency.sh minos-checks:local` on the documented WSL controller UID/GID 1000 profile with a trusted current checks image. The existing disposable harness runs Domain/full integration tests over private PostgreSQL and artifacts, including eight mixed jobs at concurrency one/two. `JUDGING_CONCURRENCY_JSON` reports queue P95 from persisted timestamps, owner-observed completion P95, peak slots/memory and counts. No machine-dependent performance threshold is enforced. It cleans only marked resources and leaves failed private workspaces for inspection. `MINOS_TEST_RUNNER=isolate` requires the explicitly approved temporary helper and only executes known fixtures with the internal core-policy override. See [ADR 0014](../adr/0014-bounded-worker-scheduling.md); a future MQ still needs durable handoff/acknowledgement/retry work, beyond replacing the ID source.

| Condition | Development alert and action |
| --- | --- |
| Readiness fails | Alert immediately; retain liveness for diagnosis. Check database availability/privileges/current migrations and private-root/key-root permissions. Restore the dependency and require readiness before accepting new work. |
| Oldest pending exceeds 120 seconds | Alert on two observations 30 seconds apart. Verify Worker is alive, Docker/image is available and there is no duplicate host lock owner. A full queue returns 429; retry after the hint rather than bypassing admission. The target applies to the recorded eight-job workload, not arbitrary maximum-limit jobs. |
| Expired lease | Alert if still expired after five seconds of a running worker's polling. Stop the faulty process, restart the sole Worker, and let bounded reclaim occur. Never manually overwrite results or attempt tokens. |
| SystemFailed increases, or three failed attempts in one hour | Inspect safe stage/failure metadata and dependencies; correct missing/corrupt artifacts from a consistent backup. Do not fabricate a verdict or rejudge immutable history. Validate with a new developer submission after repair. |
| Cleanup counter increases or cleanup-error log appears | Stop Worker and investigate Docker availability/private workspace permissions. Restart only after recovery succeeds. It removes exact recognized names, rejects links/unexpected contents and prevents claims on startup failure. Never issue broad Docker prune or delete the configured artifact root. |
| Worker terminates unexpectedly | Restart the same release after confirming the process exited. Kernel releases the host lock; startup removes abandoned containers/workspaces. The job is reclaimable after its persisted 30-second lease expires. A result-commit acknowledgement can be lost: inspect the durable owner result; duplicate guarded completion is idempotent. |
| Private filesystem uses more than 80%, or has less than 1 GiB free | Stop admitting new developer work, back up and inspect retained data. Remove verified expired backups/unreferenced uploads through the documented policy. Preserve every published artifact and pinned version. Queue bounds do not cap lifetime history. |

Log error types, stage flags and opaque IDs only. Credentials, source, hidden answers and runtime output must not be copied into diagnostic tickets. `Completed` requires a committed authoritative result; a failed/unacknowledged admission must not be presented as accepted. Updated first-party submission forms send account-scoped `requestKey` UUIDs. Retry with unchanged source/target returns the same accepted submission, including after contest end for a registered active owner, without another job or capacity reservation. Changed requests with an already-used key return safe 409. Keys/fingerprints are retained with durable submissions; Worker has no key-table access. Keyless clients retain the old duplicate-POST risk. Browser retry keys are in memory: after reload/closing the browser or editing source following a lost response, inspect owner history before a new submission. See [ADR 0017](../adr/0017-submission-request-idempotency.md).

### Phase 1 idempotency release

Stop both hosts, back up consistently, apply `SubmissionRequestIdempotency` and `scripts/postgres-runtime-grants.sql`, then start matching API/Worker binaries. Use the existing stopped-host upgrade procedure; do not merely restart stale migration containers. The additive migration does not rewrite historical submissions or create keys for legacy requests. Verify keyed practice and contest admission/replay, runtime grants and readiness before resuming work. Paired backups include the new table automatically; a fresh-target restore must reapply current explicit grants and verify historical key replay as well as owner results.

Binary rollback should retain the additive key table and metadata. Older first-party clients/binaries cannot promise idempotent POST retry; document this limitation while they run. Schema rollback drops replay metadata and must not be treated as compatible with later replay of old keys. Do not drop the table to resolve an application error; use the complete-release/backup procedure.

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

The 2026-10-08 test-capacity change adds migration `20261008160526_ExpandProblemTestCapacity`: up to 1,500 private test files per problem instead of 100. Stop API and Worker, back up both stores, apply the migration with the new release's migrator and reapply runtime grants before starting updated binaries. Existing published versions/results remain unchanged. The down migration restores the 100-test constraint and fails transactionally if any version exceeds 100; never truncate retained tests to make it pass. Old binaries also reject results above 100, so keep the new schema/binaries together or restore the complete pre-change backup to fresh targets.

Capacity is not a latency target. Per-file input/answer limits remain eight MiB each, and every test still executes in a fresh sandbox after one compilation. The theoretical input/answer storage ceiling at 1,500 full-size pairs is about 23.44 GiB per suite, before history and backups; memory is bounded per stage because files execute serially. Retain the existing queue, sandbox budgets, lease renewal and disk alerts. Published order continues to determine the primary failed verdict while all tests retain evidence.

Source, bounded diagnostics, attempts/results and referenced immutable tests are retained for the development project's lifetime. API startup and its hourly maintenance remove only unreferenced uploads/staging older than 24 hours. Containers and transient execution workspaces are deleted after each stage; startup recovery removes recognized hard-crash leftovers before new work. Cookie keys follow platform key management and are included in backups. Keep originals of visual references unchanged. Future custom executions have no retention policy because M8 has not started.

For this M7 release there is no schema change. Stop both hosts and restore the prior binaries/configuration if necessary. The additional read grant can remain safely while rolling back, or be revoked only after the M7 API is stopped and the old runtime readiness policy is restored. Never run an old binary concurrently with a new Worker; pre-M7 runners have no host lock. For a later incompatible schema change, stop writers, restore the complete pre-migration backup to fresh targets, reapply that release's grants and start the previous release. Forward fixes are preferred; do not run destructive down migrations against retained history. Rehearse every release-blocking recovery change with disposable targets before use.
