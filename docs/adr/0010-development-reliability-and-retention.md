# Development reliability, capacity and retention

- Date: 2026-10-02
- Status: Implemented for the owner-authorized M7 backend milestone
- Decisions: D17 development retention; single-host recovery and admission capacity

## Context

Official judging already has immutable inputs, atomic admission, renewable leases and guarded results. M7 must bound development load, recover a hard-killed runner and demonstrate restoration of the database together with its private files. The project remains developer-only; these limits make no public-service capacity promise.

## Decision

Keep PostgreSQL as the queue. All runtime admissions acquire one transaction advisory lock before checking both the existing five-active-submissions-per-account bound and a global active bound of 32. Pending and Judging both consume capacity. `Minos:Submissions:MaximumActive` may explicitly select 1–1000 at startup; 32 is the acceptance profile. Rejected admission writes neither submission nor job and returns safe HTTP 429 with `Retry-After: 5`. Capacity releases only when a submission reaches a terminal state. The development workload is eight simultaneous C++20 A+B submissions from two accounts, three private tests each, one serial worker, with every first claim within 120 seconds and every result Accepted in one attempt.

Support one worker process on one judge host. Its Docker adapter holds an exclusive Linux file lock for its lifetime, validates the pinned image and native private workspace, and removes only recognized Minos sandboxes and workspaces before claims begin. Startup fails on unrecognized recovery directories or containers, or when cleanup cannot finish. The existing `minos.judge=m6` label identifies this adapter's containers across upgrades. Stop all older binaries before upgrading: pre-M7 binaries do not participate in the lock. Expired PostgreSQL leases still determine which attempt can publish a result; filesystem recovery cannot grant result ownership.

Retain accounts, bounded source, compiler diagnostics, attempts, results, immutable versions and referenced tests for the development project's lifetime. Do not automatically delete historical evidence or protected-cookie keys. Delete unreferenced upload/staging directories only after 24 hours, on API startup and hourly afterward. Per-stage scratch/output/source limits remain enforced, and normal stage cleanup removes workspaces immediately. A restart removes abandoned scratch before processing. Operators monitor private disk use and retain seven daily consistent backups; they remove older backups manually after verifying a usable successor. Public retention, automated historical deletion, backup encryption/key rotation and production service objectives require a later product/deployment decision.

Expose aggregate queue/lease/result/failure diagnostics only to active administrators, with authorization inside Application and `no-store` HTTP responses. The API role may read job rows for this query, but cannot mutate them. Add process counters for claims, lease loss, iteration failures, recovered containers and failed cleanup. No account/job IDs, source, compiler output, hidden files or lease tokens become metric labels or operation responses. Recovery and alert thresholds are in the [runbook](../operations/development-runbook.md).

Take an offline PostgreSQL custom dump and private artifact archive after quiescing both hosts. Include data-protection keys so protected sessions have a coherent restore. Checksums detect accidental corruption; backups remain private trusted material. Restore to a fresh, provisioned database and empty private directory, reapply explicit runtime grants, start the same release, verify history, and judge a restored version. Never combine independently timed database and file snapshots. Runtime hosts do not apply migrations. Rollback uses the complete previous release and its consistent pre-migration backup when a backward-compatible binary rollback is unavailable.

## Consequences

Admission's short critical section serializes writes across API instances; it is intentionally sufficient for measured development load. The global bound limits active work, while retained history still needs disk monitoring. The host lock prevents a replacement process from deleting live sandboxes and explicitly defers distributed workers. Backup downtime is acceptable for a single developer. These choices add no production project, broker, cache, generic repository or execution language.
