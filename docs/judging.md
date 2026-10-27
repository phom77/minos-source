# Judging

Minos supports C++20 stdin/stdout programs with a digest-pinned GCC 14.3 toolchain. The default backend compiles and runs in sibling Docker containers managed by a trusted, non-root Linux Worker. Submitted programs never execute inside API or Worker processes.

## Lifecycle

1. API atomically admits a Submission and JudgeJob with the published version and toolchain pinned.
2. Worker receives a job ID and claims it through PostgreSQL. Delivery itself does not confer ownership.
3. Executor validates the complete ordered test set and checks private artifact integrity.
4. Source is compiled once. Successful compilation yields the executable for runtime stages.
5. Each test runs sequentially in a fresh sandbox with only its current input and executable.
6. The checker classifies each test; result publication requires complete resource, exit and cleanup evidence.
7. PostgreSQL accepts the result only from the current, unexpired attempt.

Submission states are `Pending`, `Judging`, `Completed` and `SystemFailed`. Completed is not synonymous with Accepted. Historical completion markers without verdicts remain neutral.

## Verdict policy

After successful compilation, every pinned test executes despite earlier contestant failures. Accepted requires all tests to pass. Otherwise, the earliest failed test in published order determines the main verdict.

Within one test, precedence is:

`OutputLimitExceeded → MemoryLimitExceeded → TimeLimitExceeded → RuntimeError → WrongAnswer → Accepted`

The `Exact` checker compares all stdout bytes with expected bytes; it does not normalize whitespace, newlines or case. Compilation failures produce `CompilationError` with all tests `NotExecuted`. Earlier historical first-failure results retain their original unexecuted tests.

Missing artifacts, incomplete measurements, runner faults, cancellation or uncertain cleanup cannot create contestant verdicts. Infrastructure failures use bounded retries; exhaustion produces SystemFailed with no contestant result.

## Bounds and evidence

| Resource | Bound |
| --- | --- |
| Source | 65,536 UTF-8 bytes, nonempty, no null characters |
| Published tests | 1–1,500 input/output pairs |
| Each input or answer artifact | 8 MiB |
| Compilation | 512 MiB RAM, 8 s CPU, 15 s wall watchdog, 64 MiB scratch |
| Runtime CPU | Published limit, 100–10,000 ms per test |
| Runtime RAM | Published limit, 16–1,024 MiB per test |
| Runtime wall watchdog | `max(2 × CPU limit, CPU limit + 1 s)` |
| Runtime scratch | 8 MiB per test |
| Process count | 32 per stage |
| Runtime stdout capture | 8 MiB |
| Runtime stderr / returned compiler diagnostics | 32 KiB |

CPU accounting includes descendants. Wall time is an independent watchdog. Submission CPU/wall/memory summaries take the maximum executed-test value and exclude compilation, queue delay and other tests' totals. Compiler memory is separate so ordinary `bits/stdc++.h` programs can compile.

Owner detail may include bounded compiler diagnostics and per-test status/measurements. Runtime stdout/stderr, hidden input and expected answers remain private.

## Leases and recovery

Leases last 30 seconds and renew about every 10 seconds in scopes independent from execution. A processing scope remains alive through cancellation until sandbox cleanup finishes. The Worker stops execution after lease loss and never publishes a stale result.

There are at most three attempts. Infrastructure retry delays are one then five seconds; expired leases can be reclaimed. Guarded idempotent completion handles lost commit acknowledgements. Startup holds an exclusive host lock and recovers only recognized abandoned sandboxes/workspaces before taking new work. Cleanup uncertainty prevents new stage reservations and deliveries until recovery.

## Scheduling

One Worker defaults to four active submissions, with a fresh processing scope per job. Compile/runtime each default to one stage slot. Their queues are FIFO within each class, with a shared total slot/memory budget; a full compile quota must not block eligible runtimes. An eligible older memory-heavy stage is not bypassed by smaller stages. Tests within a submission are always sequential.

| Setting, prefixed `Minos__` | Default / constraint |
| --- | --- |
| `Judging__MaximumConcurrentJobs` | 4; 1–16 |
| `Execution__SandboxSlots` | Up to 2; at most available CPUs minus one |
| `Execution__CompileSlots` / `RuntimeSlots` | 1 each; cannot exceed total slots |
| `Execution__MemoryBudgetMegabytes` | 2048; 1152–65536 |
| `Execution__ControllerReserveMegabytes` | 512; 256–16384 and at least `256 + 64 × active jobs` |

Memory reservations include stage memory and scratch; bounded controller buffers use the controller reserve. Permits cover provisioning, execution and cleanup. Reserve CPU/RAM for the API, database and other host workloads; a larger quota is not a capacity guarantee.

## Sandbox boundary

Only the trusted Worker has Docker control access. A sandbox receives its own stage subdirectory read-only, never the whole execution volume, Docker socket, private answers, cookie keys or credentials. Sandboxes have restricted networking, privileges, processes and resource budgets. Fresh per-test isolation prevents one test's writable state or process from affecting another.

Docker/WSL development is not a sandbox certification or a production-hosting claim. Use the validated engine/API and cgroup profile; run the execution suite when changing it.

## Optional native runtime

`Minos__Execution__Runner=isolate` retains Docker compilation and uses a fresh Isolate namespace/cgroup for each test on a direct x86-64 Linux host. It is not a privileged Compose mode.

Use Isolate 2.7, commit `8f185bb37f3f23e29b33b0c7727c91c13429abe3`, with root-owned helpers/configuration in a non-symlinked, non-public-writable prefix. Helpers are mode 4755. A dedicated keeper delegates cgroup v2 with swap disabled; `fs.protected_hardlinks=1` and a non-piped host `kernel.core_pattern` are mandatory. Worker and sandbox identities must be distinct. There is no live bypass for these gates.

For one box, use `<prefix>/isolate`, `isolate.cf`, `boxes`, `locks`, keeper-written `cgroup`, private Worker `work`, reserved UID/GID 61000 and CPU 0. Configuration binds paths to that prefix, `cg_root=auto:<prefix>/cgroup`, `num_boxes=1`, and `box0.cpus=0`. The box root is an eight-MiB noexec/nosuid/nodev tmpfs.

For N boxes (1–16), reserve UID/GIDs 61000 through 61000+N-1 and available logical CPUs 0 through N-1. Each helper has `<prefix>/helpers/<id>/isolate.cf`, its own `<prefix>/boxes/slot-<id>` eight-MiB tmpfs, shared locks/keeper delegation, `num_boxes=N` and its CPU binding. Runtime quota cannot exceed box count. Startup validates and recovers every configured box; a cleanup fault blocks the pool, while one job's cancellation only cleans its assigned box.

Set `Minos__Execution__IsolateRoot` and `IsolateBoxCount`, omit the Docker workspace-volume setting on a direct host, and run the stopped Worker's `--check` before enabling claims. See [operations](operations.md) for rollback and [development](development.md) for trusted-fixture benchmarks.
