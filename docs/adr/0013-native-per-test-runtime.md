# ADR 0013: Opt-in native runtime with a fresh sandbox per test

Date: 2026-10-08
Status: Implemented and validated for the owner-authorized direct-Linux experiment. Docker remains the default; public hosting remains deferred.

The owner subsequently authorized separate compile/runtime quotas and multiple boxes on 2026-10-09. [ADR 0015](0015-compile-runtime-allocation-and-isolate-boxes.md) supersedes this document's single-box installation restriction; all other per-test isolation and native host requirements remain.

## Context

After the Docker Engine API optimization, compilation plus 100 small serial tests still took 105.902 seconds. Provisioning and control operations dominate actual execution. The owner requested another optimization, including evaluating running all tests before checking once. The complete-test requirement in ADR 0012 still requires independent outcomes and resource evidence for every test.

Concatenating test inputs cannot preserve the existing C++ `main` contract: a submission may read one input and exit, or read until EOF. A crash or timeout would also prevent later tests from executing. Reusing a submitted process or its writable filesystem between tests changes state isolation and makes per-test accounting ambiguous. Buffering all outputs before exact checking removes little cost: checking 100 small outputs measured 0.12–0.36 milliseconds, while verified artifact reads measured 22–24 milliseconds.

## Decision

Implement `IsolateExecutionBackend` in Infrastructure behind the existing Application execution port. Select it explicitly through Worker configuration. Compile once using the existing digest-pinned GCC 14.3/C++20 Docker adapter, preserving full compilation evidence and its cleanup policy. Export only five required GCC/glibc runtime libraries from that same image once at Worker initialization, with bounded archive validation.

For each test, create and clean a fresh Isolate namespace/box and cgroup, running as reserved UID/GID 61000. Map only the current input/executable and pinned libraries read-only. Expected answers and trusted metadata remain in the non-root controller's private workspace. Writable scratch is the helper-owned box on an eight-MiB noexec/nosuid/nodev tmpfs, so privileged helper cleanup can remove hostile permissions without granting submitted code controller access. No whole host runtime tree, Docker socket or credentials are mounted.

Use independent bounded stdout/stderr pipe capture. Overflow interrupts the manager through a pidfd, including when submitted code ignores SIGXFSZ. Read trusted metadata and fresh cgroup CPU, peak memory and memory events after descendants exit; require the exact configured memory/swap limits. CPU includes descendants; wall time remains independent. Missing or malformed evidence, manager/cleanup faults and cancellation follow existing retry/lease semantics. All tests still execute after contestant failures; exact checking stays per test and the earliest published failure remains the primary verdict.

The tested helper is [ioi/isolate 2.7, commit 8f185bb37f3f23e29b33b0c7727c91c13429abe3](https://github.com/ioi/isolate/tree/8f185bb37f3f23e29b33b0c7727c91c13429abe3). Follow its [pinned manual](https://github.com/ioi/isolate/blob/8f185bb37f3f23e29b33b0c7727c91c13429abe3/isolate.1.txt) and the kernel's [cgroup v2 accounting contract](https://www.kernel.org/doc/html/latest/admin-guide/cgroup-v2.html).

## Hosting and limits

The native profile requires direct x86-64 Linux hosting, a non-root Worker distinct from UID 61000, a dedicated root-owned `/opt/minos-*` installation, mode-4755 pinned helper, one reserved box, private mode-700 controller workspace, dedicated delegated swap-disabled cgroup, protected hardlinks and the bounded box tmpfs. Startup validates these before claims and uses the existing exclusive Worker lock and Docker recovery. No native execution inside the Compose Worker is supported; its non-root Docker topology is unchanged.

Reject a piped host core-dump collector: `RLIMIT_CORE=0` does not prevent a pipe collector from processing submitted code. The experimental WSL host retained `/wsl-capture-crash`. Known trusted fixtures explicitly bypass this single check only through internal test composition; no Worker setting exposes that bypass. Therefore this experiment does not enable live native judging on that WSL host or change global kernel configuration.

One box is serialized. Bounded scheduling/concurrency remains Phase 3; brokers and distributed execution are not introduced. Switching the default runner or selecting a public host requires its own operational validation. An initialization failure prevents claims; a cleanup fault resets native initialization and prevents a partial result. Rollback selects `docker`, removes `IsolateRoot`, and uses the existing Docker host configuration while the Worker is stopped.

## Evidence

The actual adapter, HTTP test host and disposable PostgreSQL completed 100/100 Accepted in 2.096 seconds, and 67/100 WrongAnswer in 2.025 seconds. These include admission, immediate claim, compilation, artifact verification, every runtime/check, result commit and owner read, excluding initialization, polling wait and fixture setup. This is a lightweight development fixture, not a production SLA. Backend-only compilation plus 100 runs took 2.159 seconds; runtime median/P95 were 7.27/10.78 milliseconds. Different Docker Desktop/native WSL environments prevent a precise paired speedup claim.

Release builds passed with zero warnings/errors. Full native Linux validation passed 20 Domain and 172 integration tests; only Windows ACL validation was skipped. Existing Docker safety/resource/cancellation fixtures, malformed/missing evidence, independent output limits, ignored signals, descendant CPU, hostile scratch cleanup, fresh memory peaks, HTTP counts and historical results are covered. See [performance and reproduction](../judging-performance.md) and the [three-phase plan](../judging-improvement-plan.md).
