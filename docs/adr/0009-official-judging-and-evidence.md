# ADR 0009: Official judging and trustworthy execution evidence

- Status: Accepted and implemented for M6
- Date: 2026-09-30
- Decisions: M6 portions of D08/D09/D10/D12, D16

## Context

M5 established durable admission, version pinning and attempt ownership. M6 must produce contestant results from real sandbox execution without confusing infrastructure failure with an incorrect program. The project owner accepted the checker, verdict, test-order, accounting and trusted-validation recommendations before implementation.

## Decision

Compile C++20 once using the existing digest-pinned GCC toolchain and fixed `-O2 -pipe` flags. Execute each private test in a fresh bounded Docker container. Domain and Application have no Docker dependencies; `IExecutionBackend` carries payloads, limits and structured evidence. An eventual Isolate adapter can replace Docker without changing submissions, jobs or leases.

The Exact checker compares every byte, including whitespace and line endings. Ordered tests run sequentially and stop at the first failure. Later tests are recorded as NotExecuted with no fabricated measurements. Accepted requires all published tests to pass. Runtime precedence is OutputLimitExceeded, MemoryLimitExceeded, TimeLimitExceeded, RuntimeError, WrongAnswer, Accepted. Compilation failures, including resource limits, produce CompilationError only with trustworthy exit/resource/cleanup evidence. Infrastructure failures, unavailable artifacts, incomplete measurements and failed cleanup retry through the existing bounded job policy; exhaustion is SystemFailed without a contestant verdict.

Measure cumulative cgroup CPU for the testcase container, including descendants, against the CPU limit. Independently bound wall time. CPU is measured between pre-execution and final cgroup snapshots; peak memory is the fresh container's `memory.peak`. These measurements include small trusted supervisor/guard overhead rather than subtracting estimates. Wall time covers execution startup and process-status observation, excludes container provisioning and artifact copying, and stops when the main process exits; remaining descendants are terminated before the final resource snapshot. Docker output attachment can outlive the main process, so daemon PID/state evidence determines main-process completion. Polling resolution is approximate; these are development measurements, not calibrated production performance guarantees.

The submission summary records maximum executed-test CPU, wall time and peak memory, excluding compilation. Missing measurements remain missing and prevent a verdict. Persist exit code, limit flags and per-test measurements with the accepted attempt. Domain validates outcome consistency and first-failure order. PostgreSQL reinforces required measurements, unique test order and the result/attempt association. Final submission, job, attempt and result writes remain one guarded transaction. Independent scopes renew the lease every ten seconds; cancellation retains the processing scope until sandbox cleanup finishes.

Return only owner-authorized result projections with the pinned configuration, accepted attempt, test statuses/measurements and at most 32 KiB of compiler diagnostics. Do not return runtime stdin, expected answers, stdout or stderr. Verify all required artifacts before compiling and verify their bounded contents again when loading a test. Opaque keys never become API paths. Pre-M6 deterministic completion markers remain intact with a null verdict, empty diagnostics and no measurements/tests; the migration must not fabricate a judging result for code that never ran.

Trusted reference validation remains a developer-run fixture suite using the real sandbox and known correct/incorrect programs. There is no stored reference solution, contestant-accessible validation endpoint or new publication execution gate. The M4 checklist remains the publication record. The isolated acceptance suite must pass before its development fixtures are considered verified.

## Initial execution bounds

Compilation uses 15 seconds wall, 8 seconds CPU, 256 MiB memory, 64 MiB writable scratch, 32 submitted processes/threads, and 32 KiB per stdout/stderr stream. The executable is bounded to 16 MiB. Test runs use published CPU/memory limits, wall `max(2 × CPU limit, CPU limit + 1 second)`, 8 MiB scratch, 32 submitted processes/threads, 8 MiB stdout and 32 KiB stderr. Two additional container PIDs belong to the trusted supervisor and a reserved non-root termination guard. These are tunable development bounds.

No submitted program runs in the API or Worker process. Only the fresh Linux-native private workspace is mounted read-only. Network access is disabled; the container root filesystem is read-only; swap, capabilities, privilege escalation, output and scratch are restricted. The trusted root supervisor does not execute submissions. Compilation and submitted programs run as the non-root Worker UID/GID with an empty environment except fixed PATH/HOME/TMPDIR. The termination guard reserves a PID before execution so fork exhaustion cannot prevent cleanup. A UID-wide RLIMIT_NPROC was rejected after it interfered with unrelated WSL user processes; containment uses the container PID controller instead.

## Consequences

Exact checking is predictable but problem authors must supply correct whitespace. Fresh containers and sequential tests favor isolation and reproducibility over throughput. Docker cgroup evidence and daemon process status are infrastructure dependencies; losing them is a system failure, not a guessed contestant result. Production host selection, calibrated performance, hard-crash storage recovery, broad retention, distributed workers and manual rejudge remain later work. [M6 evidence](../milestones/m6-official-judging.md) records the executable acceptance results.
