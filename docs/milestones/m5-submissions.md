# M5 submissions and durable jobs

Date completed: 2026-09-25

**M5 is complete. M6 has not started.** M5 exists so Minos can durably accept an official submission, recover its work after interruption, and protect authoritative completion before any contestant program is executed.

## Implemented model and lifecycle

`Submission` is owner-private and immutable in its accepted inputs: account, stable problem ID, exact `PublishedProblemVersion.Id`, language, toolchain identity, source, and admission time. Its processing state is Pending, Judging, Completed, or SystemFailed. Completed in M5 means the deterministic lifecycle fixture finished; it is not Accepted and carries no contestant verdict.

`JudgeJob` holds eligibility, active lease, retry count, and terminal attempt identity. Every claim creates a persisted `JudgingAttempt` with a unique token, worker, attempt number, lease expiry, status, and infrastructure-failure category. `SubmissionResult` is the one-per-submission authoritative completion marker. PostgreSQL foreign keys, unique indexes, state constraints, and guarded repository transactions reinforce the Domain transitions.

Admission pins the current published version and the M1 GCC 14.3.0/C++20 toolchain, enforces a 65,536-byte UTF-8 source bound, and atomically inserts submission plus job. A per-account PostgreSQL advisory lock makes the five-active-submission limit race-safe. The Worker claims with `FOR UPDATE ... SKIP LOCKED`, releases the transaction before processing, and uses attempt/token/worker ownership for renewal, failure, and completion. Leases last 30 seconds. Infrastructure failures retry at most three attempts with bounded delays; exhausted work becomes SystemFailed without a contestant verdict.

The deterministic M5 executor returns success without compiling, running, parsing, or logging source. `SubmissionJobWorker` creates a DI scope for each poll/claimed job and processes one job at a time. Cancellation leaves an active lease recoverable after expiry.

## Request and persistence flow

```text
authenticated POST /api/problems/{problemId}/submissions
  -> SubmissionUseCases derives active account and latest published version
  -> Submission + JudgeJob created with pinned inputs
  -> SubmissionRepository advisory-locks the account
  -> one PostgreSQL transaction inserts both rows
  -> 201 with owner projection

JudgeWorker
  -> short SKIP LOCKED claim transaction creates JudgingAttempt + lease
  -> deterministic M5 executor (no source execution)
  -> guarded completion transaction finishes attempt/job/submission
     and inserts the one authoritative SubmissionResult
```

Owners retrieve exact detail from `GET /api/submissions/{id}` and paged history from `GET /api/me/submissions`. Application authorization blocks cross-account detail even when the use case is invoked outside HTTP. History omits source; no normal API exposes another account's source or internal lease tokens.

## Schema and operational boundaries

Migration `20260924174220_M5SubmissionJobs` adds the `evaluation` schema with `submissions`, `judge_jobs`, `judging_attempts`, and `submission_results`. It preserves M4 problem/version data and adds no destructive change to earlier schemas.

The API role may select/insert submissions, insert jobs, and read attempt history. It cannot update jobs or evaluation state after admission. The Worker may select/update submissions and jobs, select/insert/update attempts, and insert completion markers. It cannot read account/credential tables, published hidden-test metadata, or delete history. Startup validates the exact grants.

## Executable evidence

The clean WSL2 foundation pipeline passed with the Linux-native .NET 10.0.401 SDK, Docker, and an isolated PostgreSQL 18.6 instance:

- locked Release restore/build: zero warnings and zero errors;
- fresh migration and repeated migration: passed;
- migration from an M4 database containing a published problem: passed with data preserved;
- Domain tests: 14 passed;
- integration/Application/architecture tests: 110 passed, with the unchanged Windows-only storage ACL test skipped on Linux;
- Worker startup check and hosted cancellation: passed;
- real HTTPS problem publication, private-storage readiness, and database outage/recovery checks: passed;
- EF model drift: no pending model changes;
- `git diff --check`: passed.

Real PostgreSQL tests prove that a failed job insert rolls back the submission, six concurrent admissions accept only five, two concurrent claimants yield one winner, renewal extends ownership, expired work is reclaimed, stale completion is rejected, duplicate completion leaves one result, and the third infrastructure failure ends as SystemFailed. HTTP tests prove version pinning, restart persistence, owner history, and cross-account denial without source leakage.

## Known limits and M6 boundary

M5 does not compile or execute source, load private tests, calculate AC/WA/CE/TLE/MLE/OLE, record compiler or per-test evidence, or define verdict/resource precedence. The deterministic job is intentionally short, so the host does not yet run a periodic lease-renewal loop around a long sandbox operation; the persisted renewal contract is implemented and tested for M6 to use. Manual rejudge, distributed worker deployment, broad retention/quotas, contests, ratings, SignalR, and custom-input Run remain deferred.

M6 will connect a claimed immutable submission/version to the M1 execution boundary, safely materialize source and private tests in Linux-native workspaces, compile once, run ordered official tests, check output, retain bounded evidence, renew long-running leases, and atomically store a contestant verdict. M6 is required because M5 proves ownership and recovery, not judging correctness or sandbox safety in the application workflow.
