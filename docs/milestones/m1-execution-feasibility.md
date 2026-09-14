# M1 execution evidence

Date: 2026-09-13. **M1 complete; development Docker adapter feasible.**

Executable entry point: [run-m1.sh](../../spikes/m1/run-m1.sh). Recorded run: [m1-results.log](../../spikes/m1/results/m1-results.log), ending in `m1_exit_criteria=passed`. These are development experiments; official verdict policy remains M6.

## Environment and toolchain

- Repository remains on Windows D:. Worker experiments ran in WSL2 Ubuntu 22.04.5, kernel 6.6.87.2-microsoft-standard-WSL2, UID/GID 1000, Linux-native /tmp and cgroup v2.
- Existing Docker Desktop 4.51.0 / Linux Engine 28.5.2; Ubuntu integration verified. No second daemon, Isolate, or dedicated VM installed.
- GCC 14.3.0, C++20, fixed `g++ -std=c++20 -O2 -pipe` command. Image pinned to `gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15`.
- Linux .NET SDK 10.0.401, archive SHA-512 verified against [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json). Persistent SDK installed at `$HOME/.local/share/minos/dotnet`.

## Important results

| Checkpoint | Executed experiment | Result |
| --- | --- | --- |
| M1.2 | Existing private-copy boundary probe | Passed: Linux-native mount, UID 1000, no capabilities, no-new-privileges, seccomp, network off, read-only input/rootfs and applied cgroup limits |
| M1.3 | Separate bounded C++ compile and fresh execution; trusted sum checker | Passed: 19 + 23 produced 42; invalid source returned exit 1 with diagnostics |
| M1.4 | C# execution port plus Docker adapter and replacement fixture adapter | Passed: same compile/run orchestration uses either backend; evidence contains no Docker lifecycle/domain concepts |
| M1.5 | Runtime exit, sleeping loop, CPU loop and CPU-consuming children | Passed: exit 17 preserved; 800 ms wall watchdog stopped around 923 ms; 400 ms aggregate CPU experiments stopped at observed 410–429 ms CPU |
| M1.5 | Memory allocation and resource-heavy compilation | Passed: 64 MiB run and 32 MiB compiler budgets produced cgroup OOM-kill evidence and cleanup |
| M1.5 | Infinite stdout and stderr | Passed: each stream truncated at its 4096-byte test budget and container terminated |
| M1.5 | Many small scratch files | Passed: ENOSPC at 8,388,608 bytes, proving aggregate /scratch capacity rather than only per-file limits |
| M1.5 | Fork and thread exhaustion | Passed: creation denied and pids.events recorded limit hits |
| M1.5 | Network attempt and filesystem/environment probes | Passed: ENETUNREACH; no Windows drive/repository/socket/host sentinel/answers/other tests/source in run workspace; host secret environment removed |
| M1.5 | Three repeated runs with surviving children | Passed: children observed before removal; no container or private job directory survived cleanup |
| M1.5 | Two concurrent private workspaces | Passed: each run saw only its own payload/input and returned its distinct private marker |
| M1.5 | Pre-cancellation, in-flight cancellation, external container removal | Passed: bounded cleanup; disappearing container reported infrastructure failure rather than a contestant result |
| M1.5 | SIGKILL of the C# controller during execution | Passed: next controller removed abandoned containers/workspaces and executed successfully again |

Default experiment budgets: compile 15 s wall / 8 s aggregate CPU / 256 MiB memory / 64 MiB scratch; run 3 s wall / 1 s aggregate CPU / 64 MiB memory / 8 MiB scratch. Both use one CPU of quota, no swap, 32 processes/threads, 32 KiB per output stream, 8 MiB per-file/artifact limit, and 64 open files. Individual tests deliberately lower budgets.

Scratch is tmpfs and root/input mounts are read-only. Auxiliary Docker /dev tmpfs is bounded at 64 MiB and /dev/shm at 1 MiB; writable tmpfs pages also count against container memory. Configured tmpfs capacities total at most 129 MiB during compilation and 73 MiB during execution. No writable host directory or persistent contestant log is mounted. [Docker resource controls](https://docs.docker.com/engine/containers/resource_constraints/)

## Findings that affect implementation

- `docker cp` could not retrieve the tmpfs artifact here. The adapter streams only a regular, non-symlink executable through a capped trusted command into a fresh Linux-private run workspace.
- CPU monitoring uses cumulative cgroup usage, including descendants; quota, wall watchdog and inherited CPU rlimit provide additional bounds. Sampling and termination have measurable overshoot. These values are not official time-limit verdict semantics.
- Successful runs retain the container briefly to read cgroup CPU/memory/event counters. Measurements include the idle parent and collector overhead. Forced termination may make final counters unavailable: `ResourceSnapshotComplete=false`, actual memory peak stays null, and sampled memory is separate. Missing evidence is not zero.
- Children inheriting output descriptors added roughly two seconds of Docker attachment overhead. They remained contained and were removed. M6 must define timing independently of this overhead.
- Crash recovery uses an exclusive single-controller lock and narrowly labeled development namespace. This proves sandbox cleanup; submission leases, retries and durable ownership remain M5.
- Fault injection found concurrent Docker deletion can return 409 while removal is in progress. Cleanup now retries within a five-second deadline and requires confirmed removal; it does not treat conflict as success.
- Forced cancellation can produce a Docker attachment status different from the contestant's exit status. The cancellation/timeout/resource flags and incomplete snapshot marker must drive later interpretation; attachment status alone is not an authoritative contestant verdict.
- Proceed with Docker for development. Production judge-host selection, Isolate adoption and M6 verdict/accounting remain deferred.

## Reproduce

Inside Ubuntu, with the repository still on D:

```bash
export PATH="$HOME/.local/share/minos/dotnet:$PATH"
bash /mnt/d/Projects/PetProject/Minos/spikes/m1/run-m1.sh
```

The pinned Alpine and GCC images must be cached; pull the digests specified in the scripts if needed. The suite builds into Linux-native /tmp and records assertions/evidence, not arbitrary submission output.

## Completion

- [x] Private Linux-native staging and all D10 minimum controls exercised.
- [x] Isolated compilation, execution and trusted checking demonstrated.
- [x] Success, compile/runtime failure, timeout, resource/output failure and infrastructure failure distinguished.
- [x] Resource-heavy compilation, process/thread exhaustion and child persistence contained.
- [x] Cancellation, repeated cleanup and controller-crash recovery demonstrated.
- [x] Replacement adapter proves orchestration depends on the execution port.
- [x] Numeric test bounds, accounting limitations and proceed decision recorded.

M1 passed before creating the real [Minos solution](../../Minos.sln).

## Application foundation results

M2 began with Domain/Application/Infrastructure/API/Worker projects. Initial module ownership follows Accounts/Problems/Evaluation, with only the Problems draft slice implemented. EF Core 10.0.12 and Npgsql provider 10.0.3 are pinned; EF relational assemblies are pinned explicitly to prevent the provider's minimum dependency from producing version conflicts. Package locks and EF tooling are included.

The initial use case validates a draft in Domain, saves through an Application repository port, and reads through EF Core/PostgreSQL. It has no publication or privileged-user workflow. API endpoints are gated by both Development and explicit opt-in; the Worker starts independently and performs database/schema readiness checks, without claiming submissions.

[check-foundation.sh](../../scripts/check-foundation.sh) executes against ephemeral PostgreSQL 18.6 with a private Linux socket and its own ephemeral HTTP port. Results:

- Linux and Windows solution builds passed with zero warnings/errors.
- Five domain tests and six integration tests passed: PostgreSQL persistence across API restart, invalid draft/no insert, not-found response, development endpoint gates, and startup configuration rejection without echoing values.
- Fresh migrations applied; real HTTP create/read roundtrip passed.
- Worker startup check passed. Database-down readiness returned 503, liveness stayed healthy, and Worker startup failed clearly with a nonzero exit code.
- Database bootstrap readiness checks require the final PID 1 PostgreSQL server, avoiding false readiness from its temporary initialization server.
- GitHub CI build/test configuration is prepared; no remote run occurred.

**Subsequent status:** M2 completed on 2026-09-14; [M2 evidence](m2-foundation.md) records separate database privileges, private configuration and the locally passing CI pipeline. Production hosting and M6 verdict/accounting remain deferred.
