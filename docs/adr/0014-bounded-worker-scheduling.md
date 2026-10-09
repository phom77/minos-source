# ADR 0014: Bounded job scheduling and replaceable ID delivery

Date: 2026-10-08
Status: Owner-authorized, implemented and validated. Evidence is recorded in the improvement plan; application deployment/default runner remain unchanged.

The subsequent owner request for separate stage quotas and multiple native boxes is recorded in [ADR 0015](0015-compile-runtime-allocation-and-isolate-boxes.md). Its current configuration supersedes the defaults and single-box restriction below; the Phase 3 measurements remain historical evidence.

## Context

The Worker awaits an entire submission before claiming another. Native runtime reduced the control overhead for 100 small tests to under one second, while compilation still takes about 1–2 seconds. Multiple submissions can overlap compilation and runtime without creating another Worker host, queue, production project or application layer. Unbounded tasks would exhaust resources, retain too much input/output and put leases at risk.

## Decision

One Worker dispatches at most `MaximumConcurrentJobs` submissions, default two, with a retained independent DI scope per job. It receives an opaque job ID, then claims that exact ID with PostgreSQL `SKIP LOCKED` and the existing attempt-token transaction. No job is received/claimed until a processing slot is available. Receiving an ID alone changes no durable state; duplicate, completed, unavailable and already-owned IDs cannot acquire another live attempt. Expired IDs use the same bounded recovery/exhaustion rules as polling did previously.

`IJudgeJobDelivery` is the Application port for obtaining an ID. The current Infrastructure implementation peeks at the oldest eligible PostgreSQL job; the Worker invokes `JudgeJobUseCases.ClaimAsync(jobId, ...)`, followed by unchanged processing/renewal/completion use cases. Keep the existing claim-next method for maintenance and existing callers. Admission still commits submission plus PostgreSQL job atomically; no broker or outbox is installed.

All compiler/runtime stages share one Infrastructure execution budget. Each stage reserves one sandbox slot and its requested memory plus scratch, in FIFO order. Defaults are up to two slots, 2048 MiB sandbox memory and 512 MiB management/controller reserve. Configured capacity is validated against the Docker execution host before claims, and against the direct host for native runtime. At least two CPUs are required; retain one CPU outside the stage slot budget. Each Docker sandbox remains limited to one CPU; the current one-box native configuration explicitly binds box 0 to CPU 0. Retain controller reserve for the bounded number of active jobs (`256 + 64 × job count` MiB minimum). Static capacity checks do not replace operating-system limits or host monitoring.

Keep a permit through provisioning, execution, evidence and cleanup. Resource waiting does not consume contestant CPU/wall limits, and lease renewal continues through its own scopes every ten seconds. FIFO prevents newer small stages from indefinitely bypassing an older large-memory stage. Cancellation removes only its own pending reservation. A cleanup failure blocks new reservations and deliveries until Worker restart and successful host recovery, rather than lending uncertain resources to another sandbox. Existing active jobs still finish/cancel and await their cleanup.

Tests within a submission remain sequential, preserving fresh state and published order. Each queued stage yields the budget after cleanup, so another active submission can get its turn. The native helper still has one reserved UID/box; multiple jobs can overlap Docker compilation with native runtimes, but native runtimes remain serialized. Its measured low overhead does not justify a privileged multi-box redesign in this slice.

Shutdown cancels all active work and awaits its cleanup before disposing processing scopes. Configure the host shutdown timeout to 40 seconds within Compose's existing 45-second grace period; forced termination still relies on startup recovery and durable lease expiry. Failure in one job is logged safely and does not tear down unrelated job scopes. Metrics use only fixed names and stage/phase values.

## Delivery evolution and operating consequences

An alternate source of IDs uses the same targeted claim path; the test delivery exercises duplicates through the actual scheduler. A real message queue additionally needs atomic database-to-delivery handoff, publisher confirmation, consumer acknowledgement/requeue policy, delayed retries and expired-lease recovery. That is a later concrete broker implementation, not something this port alone supplies. Never put source, hidden tests or lease credentials into delivery messages. PostgreSQL remains the result/attempt authority.

One exclusive Worker host lock remains mandatory. This change does not introduce distributed workers, public capacity promises, M8 custom Run or new verdict rules. Global/account admission limits and owner authorization are unchanged. Operators can select one job/one slot to restore serial scheduling while retaining the same binaries and results; restart while switching configuration.

See the [three-phase plan](../judging-improvement-plan.md), [native decision](0013-native-per-test-runtime.md) and [development operations](../operations/development-runbook.md). Existing test projects cover admission/resource bounds, duplicate IDs, scoped processing, stale leases, shutdown/cleanup, real PostgreSQL/HTTP owner counts, ordered claims and mixed real sandboxes. Workload evidence reports queue and completion P95 without imposing machine-dependent performance assertions.
