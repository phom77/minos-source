# ADR 0008: PostgreSQL submission jobs and lease ownership

- Status: Accepted and implemented for M5
- Date: 2026-09-25
- Decisions: D06, D07, M5 portion of D08, submission-source portion of D13, D14

## Context

Minos must acknowledge an official submission only after both its immutable input and recoverable work record are durable. Worker interruption must not lose work, and an expired worker must not overwrite the result of a replacement attempt. M5 establishes those guarantees before M6 introduces compilation, testcase execution, checker behavior, and contestant verdicts.

## Decision

Store submissions and judge jobs in PostgreSQL. Admission derives the owner from the authenticated account, selects the latest published problem version, pins its version ID and the supported C++20 toolchain identity, validates a 65,536-byte UTF-8 source limit, and inserts the submission and one job in a single transaction. A transaction-scoped advisory lock serializes the initial limit check for one account; at most five of that account's submissions may be Pending or Judging. These numeric admission settings are initial product limits, not a new distributed quota system.

`Submission` owns the solver-visible processing state: Pending, Judging, Completed, or SystemFailed. `JudgeJob` owns scheduling, lease, and retry state. Each claim creates a retained `JudgingAttempt` evidence row whose lease and terminal state may be updated but which runtime roles cannot delete. M5 deliberately has no contestant verdict: `SubmissionResult` records only which successful attempt authoritatively completed the lifecycle. M6 will add contestant outcomes and execution evidence without changing submission, job, or attempt ownership.

Workers claim eligible or expired work with a short PostgreSQL transaction using row locking and `SKIP LOCKED`. A claim creates a unique attempt ID and opaque lease token, records the worker identity, and grants a 30-second lease. Renewal, completion, and failure all lock the job and require the current attempt, token, and worker. Expired work may be reclaimed. Completion by a stale attempt is rejected; repeating completion for the already accepted attempt is idempotently accepted and cannot create another result.

Infrastructure failures retain their attempt records. M5 retries at most three attempts with bounded delays; exhaustion produces SystemFailed and no contestant verdict. Cancellation leaves the lease for normal expiry and recovery. Manual rejudge remains deferred under D18.

The API exposes authenticated creation, owner-only detail, and owner history. Detail includes the owner's source and attempt history; list projections omit source. Clients poll committed state. The API runtime role can admit and read only the evaluation data its use cases require. The Worker can claim/update jobs and attempts and insert completion markers, but it has no account, credential, or hidden-test access in M5. Neither runtime role can delete evaluation history.

M5 uses an Infrastructure deterministic executor that immediately reports lifecycle success and never executes source. It proves host orchestration and durable completion only. Real sandbox execution, private-test loading, compiler/checker evidence, verdict precedence, and resource accounting remain M6.

## Consequences and alternatives

Keeping submission and job in one database avoids an enqueue gap and does not require a broker or outbox. PostgreSQL row locks, leases, and guarded writes add explicit lifecycle state, but they make crash recovery and ownership testable with the existing deployment. A process-memory queue would lose work on restart. A broker would add another failure boundary without a demonstrated routing or throughput need.

Source remains bounded relational data because it is small, transactional with admission, and owner-private. Hidden testcase contents remain private artifacts under ADR 0007. Lease and retry constants can be tuned from operational evidence without changing the ownership model.

## Evidence

[M5 results](../milestones/m5-submissions.md) cover atomic rollback, concurrent admission and claims, renewal/reclaim, stale and duplicate completion, bounded retries, restart persistence, owner authorization, PostgreSQL constraints, least-privilege roles, and migration from the M4 schema.
