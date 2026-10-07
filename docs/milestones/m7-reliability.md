# M7 backend reliability and developer acceptance readiness

Date: 2026-10-03 (implementation authorized on 2026-10-02)
Status: M7 backend complete and verified; development MVP candidate ready for owner acceptance

The owner requested completion of M7 for the backend. This slice preserves the five production projects and two test projects, the M6 verdict contract and all immutable history. The development capacity/retention/recovery policy is recorded in [ADR 0010](../adr/0010-development-reliability-and-retention.md); operating procedures are in the [development runbook](../operations/development-runbook.md). M8 has not started.

## Implemented behavior

- PostgreSQL admission checks a default global bound of 32 active submissions alongside the existing five-per-account bound, under one short transaction advisory lock. Concurrent accounts cannot race past capacity. Terminal work releases capacity; rejection creates neither row and returns safe HTTP 429 with `Retry-After: 5`. Explicit configuration is validated at startup.
- The sole Linux Worker validates Docker/private native storage and owns an exclusive host lock. Before it claims jobs, recognized abandoned Docker containers and workspaces are removed. Cleanup failure, links, unknown workspace contents or an active runner prevent startup. The persisted lease/attempt token remains the only authority for results. Pre-M7 processes must be stopped before upgrade because they lack the lock.
- Active administrators can read aggregate queue, expired lease, pending age and hourly result/failure diagnostics at `/api/operations/judging`. Application enforces authorization even for non-HTTP calls; transport projections and process metrics contain no source, hidden tests, credentials or lease tokens. The API receives read-only job access for this query and still cannot claim or mutate jobs.
- Worker and sandbox process counters report claims, lost leases, iteration failures, recovered containers and cleanup failures through the standard .NET diagnostic infrastructure. The runbook defines concrete alert thresholds and recovery actions.
- API startup and hourly maintenance clean only unreferenced uploads/staging older than 24 hours. Durable submission source, bounded diagnostics/results, attempts, version history and referenced tests remain for the development project's lifetime. Scratch and containers are removed after stages and recovered after a crash. Operators monitor lifetime disk growth and keep seven daily consistent backups.
- Private backup/restore scripts combine an offline PostgreSQL custom dump with the artifact directory, including cookie protection keys. Incomplete backups, checksum corruption, unsafe archive members and nonempty restore destinations are rejected. Fresh-target restore is followed by explicit runtime grants, readiness/history verification and real restored-problem judging.

No schema migration or new package is required. Existing deployments need the updated runtime grants; API startup checks the new job-read permission. Stop old hosts and apply the grant before starting this release. For binary/schema rollback, follow the complete-release and consistent-backup procedure in the runbook. Public hosting, real users, distributed workers, manual rejudge and historical deletion remain deferred.

## Acceptance candidate and validation

The candidate is the backend source represented by these changes, .NET 10 with locked dependencies, PostgreSQL 18.6, the pinned GCC 14.3.0 Docker image, a private Linux-native artifact root, and one non-root WSL2 Ubuntu 22.04 worker. This is a development MVP candidate; it is not a production capacity or sandbox-certification claim.

Run `bash scripts/check-foundation.sh` after the README's pinned image preparation. The existing acceptance entry point now sources `scripts/check-m7.sh` within its disposable database/container/private-directory fixture. CI executes the same command and uploads TRX plus `artifacts/m7-workload.json`.

The required evidence covers:

| Requirement | Executable evidence |
| --- | --- |
| Queue bounds and concurrency | Real PostgreSQL concurrent admission across eight accounts with capacity three, per-account racing admission, released capacity, real HTTP 429/retry hint |
| Workload | Eight queued official submissions across two owners, three private tests each, serial worker, all Accepted in one attempt; every first claim under 120 seconds |
| Hard-killed worker | SIGKILL during an actual contestant process, observed orphan container/workspace, startup cleanup, expired attempt and authoritative second-attempt result |
| Lease loss/stale ownership | Existing renewed/expired-token/replacement-worker PostgreSQL tests plus interrupted processing under real database outage |
| Database failure | Failed atomic admission, PostgreSQL outage during runtime/renewal, sandbox cancellation, reclaim after recovery, no acknowledged create while unavailable |
| Result commit uncertainty | Injected failure immediately before commit rolls back every status/result/test row; injected lost acknowledgement after commit leaves one authoritative result and idempotent retry |
| Storage failure | Missing pinned private input exhausts three bounded infrastructure attempts to SystemFailed with no contestant verdict; readiness also detects unsafe private-root permissions |
| Adversarial sandbox | Current adapter tests for inaccessible network/management/host files and non-root runtime, descendants/PID exhaustion, cancellation and transport-independent long runs; all seven real API verdicts |
| Observability/privacy | Administrator-only HTTP/Application checks including disabled administrator and non-HTTP calls, coherent safe aggregate projection, exact runtime privilege checks and architecture tests |
| Restore/release | Clean locked build, fresh/repeated/upgrade migrations, offline paired backup, corrupt/linked archive rejection, fresh database/files restore, restored cookie keys and exact owner history, real judging afterward |

Final Windows and clean Linux Release builds pass with zero warnings/errors. Linux passes 19 Domain tests and 132 integration tests; its single expected skip is the Windows ACL check, covered by the nine passing selected Windows architecture/configuration/Application authorization/private ACL checks. Final Linux TRX files are dated `2026-10-03_00_00_28` and `00_00_29`; selected Windows evidence is `artifacts/tests/m7-windows.trx`. The complete rehearsal log is `artifacts/m7-acceptance.log`, ending in `m7_checks=passed` and `foundation_checks=passed`. All fault/recovery/restore rows above pass, including corrupted-checksum and linked-archive rejection before restoration.

The final eight-job workload records a maximum queue wait of **43.754 seconds** against **120 seconds**, maximum end-to-end latency of **47.412 seconds**, maximum executed-test CPU of **61 ms**, and maximum executed-test memory of **6,455,296 bytes**. All eight submissions complete Accepted with one attempt. Compilation is excluded from test resource summaries; the existing compilation bounds still apply. After SIGKILL, startup removes observed orphan containers/workspaces and the second attempt alone owns the Accepted result. Database interruption cancels the old execution, recovers an expired lease and completes a replacement attempt. A missing published input exhausts exactly three infrastructure attempts to SystemFailed with no result. Restored history matches exactly, cookie keys remain usable, and a newly submitted restored problem is judged Accepted.

Evidence logs/TRX and `artifacts/m7-workload.json` remain under ignored `artifacts/`; this document records their final results without retaining fixture source, hidden contents, tokens or credentials. Disposable database/volume/private fixture cleanup completes successfully. The final diff retains all existing production dependencies and project boundaries.

The 2026-10-07 Docker/M7 merge repair preserves both global admission configuration and the named execution-workspace volume, sharing one backend instance with startup recovery. Compose health probes inspect the ready process without taking its exclusive lock; containerized sandbox checks run with the Worker stopped. Invalid-volume regression cases are restored, and the isolation fixture creates its sentinel only after recovery. Windows Release builds with zero warnings/errors and passes 19 Domain plus 22 selected regression/architecture/Application tests. Clean Linux and the Compose checks image each pass 19 Domain and 138 integration tests, with only the expected Windows ACL skip. The complete Linux pipeline ends in `m7_checks=passed` and `foundation_checks=passed`, including live health probes around a rejected second Worker, SIGKILL recovery, database interruption and backup/restore judging. The Docker Worker becomes healthy and rejects a second `--check` process while live health probes continue to pass. Logs are `artifacts/conflict-fix-acceptance.log`, `artifacts/conflict-fix-docker-build.log`, `artifacts/conflict-fix-docker-tests.log` and `artifacts/conflict-fix-docker-runtime.log`; all checks use disposable targets rather than the existing development database.

## Completion checklist

- [x] Owner authorizes the M7 backend slice; implementation and operating policies are recorded.
- [x] Existing production/test project boundaries and locked dependencies retained.
- [x] Atomic global/per-account admission and protected operational diagnostics implemented.
- [x] Exclusive single-host recovery, process counters and hourly orphan-upload cleanup implemented.
- [x] Private offline backup/restore and migration/release/rollback/recovery procedures implemented.
- [x] Final full Linux acceptance pipeline passes with workload/recovery/restore evidence recorded.
- [ ] Owner accepts the development MVP candidate after reviewing the evidence.
- [ ] GitHub-hosted CI executes for the committed/pushed revision.

Live Google consent validation remains the disclosed M3 dependency because no OAuth client exists. Password recovery/verification delivery, public-host topology, encryption/key-rotation strategy and production workload/retention policies remain public-release work. Owner acceptance and a hosted CI run cannot be inferred from local checks. No existing persistent development database or private user artifact root is modified by the acceptance fixture.
