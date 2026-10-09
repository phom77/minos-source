# Judging performance evidence

Date: 2026-10-08

## Supported runner optimization

The default direct Linux/WSL and non-root Compose profiles keep fresh Docker sandboxes for compilation and each test. Docker Engine API v1.51 is used directly for container creation/start, cgroup telemetry commands and executable extraction. The existing interactive process guard remains a CLI attachment so it can stop descendants even after the submitted program exhausts its process budget. Runtime stages reduce trusted CLI processes from five to one; compilation additionally avoids the executable-extraction CLI process. An opt-in direct-Linux Isolate runtime is now implemented and validated below; Compose retains its default topology.

The API request preserves the pinned image, network isolation, dropped capabilities, no-new-privileges, read-only root/input, per-stage volume subpath, bounded tmpfs, CPU/memory/PID/file limits and disabled Docker logging. Submitted code still never runs in the Worker. The expected answer stays in the trusted executor. Failure to provision, capture bounded control output, read evidence or clean up remains an infrastructure fault.

Baseline and final cgroup reads remain distinct trusted control commands under UID 0, with the existing dropped capabilities. Do not merge their output into the same-UID interactive process guard merely to save an exec: submitted processes must not be able to spoof trusted evidence through a sibling process's file descriptors. The guard's job is descendant termination; cgroup evidence has a separate trust boundary.

The Engine API is already used for execution, inspection and cleanup, so this adds no SDK, package, project or alternate queue. API details are documented by [Docker Engine v1.51](https://docs.docker.com/reference/api/engine/version/v1.51/).

## Measurements

The `Minos.Judging` meter emits `minos.judging.phase_duration` in seconds. Labels contain only fixed `stage` and `phase` values. Stages are `compile`, `run`, `runtime` and `submission`. Sandbox phases are total, initialize, workspace, provision, guard, baseline-evidence, execution, evidence and cleanup; native run does not need guard/baseline commands. `runtime/export` records the once-per-initialization pinned-library export. Submission phases include artifact-preflight, artifact-read, check and total. Total measurements overlap component measurements and must not be added to them.

The reported contestant CPU/wall/memory evidence remains separate from operational timing. First initialization is recorded separately in the benchmark; the 100 subsequent tests execute sequentially. Queue wait and API/database time are excluded explicitly. These numbers are fixture measurements, not a production P95 or a guarantee for CPU-heavy submissions.

The earlier 2026-10-07 measurement was 88.833 seconds for compile plus 100 small sum tests. The final paired 2026-10-08 measurement ran sequentially after builds, correctness checks and disposable PostgreSQL cleanup. Both images run the same opt-in test. Production Infrastructure in the baseline image is byte-identical to the Phase 1 checks image (SHA-256 `f5486952f90055756ed3b5c6b00543b75c84d2174f065e7aada4c5fd84371cdc`); only the benchmark test is added.

| Measurement | Unchanged runner | Engine API optimization |
| --- | ---: | ---: |
| First initialization, excluded from total | 0.664 s | 0.723 s |
| Compile | 2.059 s | 2.013 s |
| 100 serial runtimes | 144.506 s | 103.889 s |
| Compile + 100 runtimes | 146.565 s | 105.902 s |
| Median runtime stage, including control overhead | 1.259 s | 1.018 s |
| Runtime-stage P95 | 2.438 s | 1.256 s |
| Sum of execution stopwatches | 14.557 s | 13.291 s |
| Runtime duration outside those stopwatches | 129.938 s | 90.581 s |

This pair reduced compile-plus-100-test time by 27.74%. It is one development-machine comparison, not a universal throughput or latency guarantee. Preliminary runs varied substantially: the original runner's first uncaptured run took about 118 seconds, and an earlier measured run took 193.096 seconds while a Windows build occurred during that baseline. Its optimized counterpart took 111.591 seconds. Do not substitute that larger preliminary percentage for the final pair or compare a new image directly against the previous day's 88.833 seconds.

The final optimized runtime phase totals were provisioning 36.785 s, guard 12.858 s, baseline evidence 11.307 s, execution/inspection 15.589 s, final evidence 11.749 s, cleanup 15.497 s and workspace writes 0.081 s. Fresh-container provisioning remains the largest single phase. This still does not approach a 2–3 second submission target. The native adapter below removes most of that control overhead.

Evidence is retained in ignored `artifacts/judging-phase2-20261008/`: `baseline-final.log`, `optimized-final.log`, `paired-result.json` (image IDs, source hashes and numbers), and `linux-checks.log`. Registry DNS was unavailable, so checks were built from the cached Phase 1 SDK image with its existing locked Linux restore assets; current source was rebuilt, without sharing Windows assets or changing dependency versions.

Release builds passed with zero warnings/errors. Windows Domain tests passed (20), with 15 selected architecture/policy/use-case/configuration tests. The dedicated Linux suite passed 20 Domain and 155 integration tests, including every previous native execution fixture, six resource/failure cases and four real sandbox-to-PostgreSQL/HTTP count cases. Windows ACL and the opt-in benchmark were the only skips in that full suite; the benchmark passed separately. The updated foundation and benchmark scripts passed shell syntax checks, and the benchmark script was exercised end to end. The complete standalone foundation rehearsal was not run.

## Artifact handling

Keep the preflight verification and per-test verified read. Preflight ensures missing or corrupt later tests cannot be hidden behind a compilation failure; the later hash verifies the exact bytes used for judging. Published metadata is immutable, but private files can still be lost or corrupted. The earlier warm-cache measurement for verifying then reading 200 one-MiB files was 0.513 seconds, far below sandbox overhead. An unbounded preload or an extra attempt snapshot would add memory/disk cost without evidence of a worthwhile improvement. Each runtime holds only its current input/answer pair, and only input enters the sandbox.

## Native candidate evaluation

Isolate is an applicable candidate for a dedicated Linux execution profile. The [manual at the tested commit](https://github.com/ioi/isolate/blob/8f185bb37f3f23e29b33b0c7727c91c13429abe3/isolate.1.txt) describes its setuid-root helper, cgroup v2 setup/delegation and a keeper service under systemd for descendant accounting. It advises against container deployment and notes that containers may need privileged access. A non-cgroup speed comparison would omit required descendant CPU/memory guarantees and would not be an acceptable adapter benchmark.

The owner explicitly approved the WSL experiment on 2026-10-08, without changing the application default. The tested source is `ioi/isolate`, commit `8f185bb37f3f23e29b33b0c7727c91c13429abe3` (2.7). Ubuntu-22.04 uses kernel 6.6.87.2, systemd 249 and cgroup v2. A temporary root-owned helper, one reserved sandbox UID/GID 61000, one delegated transient keeper service and an 8-MiB bounded scratch tmpfs were configured under `/opt/minos-isolate-phase2-20261008-8f185bb`. Both the experiment's cgroup and per-test cgroups disable swap. No persistent unit, account, global sysctl or application configuration was changed.

Compilation uses the same pinned GCC image and C++20 flags as the Docker fixture. The trusted controller exports its executable and dereferenced GCC/glibc runtime libraries from that image, instead of mixing in Ubuntu's compiler/runtime. Every native test creates and cleans a new Isolate box and cgroup. Only its input, executable and pinned libraries are read-only mounts; bounded scratch is writable. Expected answers and metadata stay outside the sandbox. The submitted process runs under UID 61000, while the benchmark controller remains UID 1000.

| Native prototype measurement | Result |
| --- | ---: |
| Compile and Docker export of executable/runtime libraries | 1.378 s |
| 100 serial fresh-box init/run/cleanup stages | 0.905 s |
| Sum of the above | 2.283 s |
| Median stage / P95 | 8.72 ms / 10.93 ms |
| Sum of cgroup CPU / reported execution wall | 0.206 s / 0.206 s |

Measured at 2026-10-08T02:52:33Z. The sum excludes API, PostgreSQL, queue wait, initial helper setup and Python unpacking after the compiler export. The compiler uses a prototype Docker CLI path rather than the official adapter. Docker Desktop and native WSL also use different Linux environments. Therefore this shows that lightweight fresh sandboxes can remove substantial control overhead; it does not establish an application latency SLA or a precisely comparable speedup ratio against the Docker pair.

Twelve native probes passed: mount/UID/network isolation, WrongAnswer, nonzero exit, CPU timeout, stdout/file limit, cgroup OOM, parent exit with descendant cleanup, process exhaustion, CPU work, wall time independent of CPU, descendant CPU accounting, and cancellation with cleanup. Cgroup CPU/wall/memory metadata was required in every probe, and each cleaned box left no `box-0` cgroup.

Additional probes deliberately exposed two verdict gaps in this prototype: stderr reaching the file bound reports RuntimeError, and ignoring SIGXFSZ while continuing writes reports TimeLimitExceeded. Minos requires OutputLimitExceeded in both cases. An 8-MiB stdout/scratch-boundary probe did report OutputLimitExceeded; do not assume it failed. These are limitations of the prototype's output handling and classification, not evidence of an Isolate escape. Its per-file `--fsize` mechanism is not a replacement for independently enforced stdout/stderr capture limits.

This Python prototype did not meet the correctness exit criterion. The C# adapter implemented next addresses its output gaps and uses the existing execution port, policy and integration suite. The old gap probe is retained as historical evidence, not the current runner's behavior.

The first prototype environment was removed after verification, then provisioned again for the C# validation below. Four signed Ubuntu build packages remain installed: `pkg-config` 0.29.2-1ubuntu3, `libcap-dev` 1:2.44-1ubuntu0.22.04.3, `libseccomp-dev` 2.5.3-2ubuntu3~22.04.1 and `libsystemd-dev` 249.11-0ubuntu3.22. No existing packages were upgraded. Prototype logs and corrected scope metadata are retained in ignored `artifacts/judging-phase2-20261008/isolate-benchmark.log`, `isolate-result.json` and `isolate-gaps.log`.

## Implemented native adapter and feasibility

`IsolateExecutionBackend` now implements the existing Infrastructure execution adapter. Worker configuration explicitly selects `isolate`; otherwise it selects `docker`. Compilation still uses the official Docker compiler, with its complete evidence and cleanup. Every runtime gets a fresh box, cgroup and submitted process. Five pinned runtime libraries are exported once at initialization; input/executable writes, init/run/evidence/cleanup remain per test. One native box is serialized; scheduling concurrency remains Phase 3.

Stdout and stderr use independent bounded pipes, with a pidfd interrupt on overflow. Both prototype output gaps now classify as OutputLimitExceeded. CPU comes from the complete cgroup, peak memory from its fresh `memory.peak`, and memory events preserve existing precedence. Cancellation drains output and waits for cleanup. Missing/duplicate metadata, unexpected manager exits or incomplete cgroup evidence fail closed. Private parent directories remain mode 700; only current read-only stage mounts enter the namespace. Writable scratch stays in the root helper's box on a separate eight-MiB tmpfs so hostile chmod/symlink state is cleaned safely.

Concatenating all inputs into one program invocation is incompatible with existing submissions: some read one case and exit; others read to EOF. A crash/timeout could prevent subsequent tests. Reusing a process or writable state changes isolation and per-test resource semantics. Comparing outputs once at the end is technically possible but requires retaining more data and saves only 0.12–0.36 ms in this fixture. Keep exact checking and verified artifact reads per test; optimize the sandbox mechanism instead.

| Current C# adapter measurement | Result |
| --- | ---: |
| Official admission through owner result, Accepted 100/100 | 2.096 s |
| Official admission through owner result, WrongAnswer 67/100 | 2.025 s |
| Official profile initialization, excluded from those totals | 0.800 s |
| Backend-only initialization, excluded | 1.581 s |
| Backend-only compilation | 1.379 s |
| Backend-only 100 runtimes / compile + runtimes | 0.781 s / 2.159 s |
| Runtime median / P95 | 7.27 ms / 10.78 ms |
| Official verified reads for 100 small tests | 21.75–23.89 ms |
| Official exact checking/classification for 100 tests | 0.12–0.36 ms |

The official timer includes real HTTP admission/read through the in-process ASP.NET test host, disposable PostgreSQL, immediate claim, compilation, all artifact verification/reads, all 100 executions/checks, result commit and owner result retrieval. It excludes Worker startup, queue polling wait, problem/account setup and external network transport. Both cases persist complete ordered resource evidence; the mixed case passes later tests after failures. These are local lightweight fixture measurements, not a production SLA or a precise paired speedup ratio against Docker Desktop. CPU-heavy tests still need their actual execution time; complete judging cannot avoid that cost.

The committed reproduction script was exercised against the same checks image. It passed another 20 Domain and 51 focused integration tests without skips/failures. Its official Accepted/WrongAnswer runs took 2.554/2.520 seconds; backend-only compile plus 100 runs took 1.925 seconds, with runtime median/P95 6.86/8.69 ms. Across these final runs, official fixture timing is about 2.0–2.6 seconds rather than a single promised value. Evidence: ignored `artifacts/judging-native-20261008/reproduce.log`.

The final checks image also passed 16 default-Docker execution tests in its non-root container/volume profile; only the opt-in 100-test benchmark was skipped in that regression run. Evidence: `docker-smoke.log`. Both experimental tmpfs mounts, root helper/prefix, transient keeper/cgroup and private extracted SDK/test workspaces were removed after verification; no UID-61000 process or fixture PostgreSQL/export container/volume remained (`cleanup.log`). Existing application configuration and its previously stopped Worker state were preserved.

Release builds passed with zero warnings/errors. The full native Linux suite passed 20 Domain and 172 integration tests (173 total; Windows ACL alone skipped). It includes architecture, grants/migrations/HTTP, accepted/mixed/compilation/runtime results, historical evidence, leases/retries, every existing execution safety/resource/cancellation fixture, overlapping-limit precedence, both output gaps, descendant CPU, bounded hostile scratch, fresh memory peaks, missing metadata followed by recovery, and rejecting unsafe live core-dump policy. Evidence: ignored `artifacts/judging-native-20261008/native-final.log` and `result.json`. Linux SDK/compiled tests were extracted from a cached checks image into private disposable WSL directories; no Windows/Linux restore assets were shared and no dependencies were upgraded.

The live native profile validates a root-owned pinned helper/configuration, dedicated one-box delegated swap-disabled cgroup, non-root controller distinct from UID/GID 61000, protected hardlinks and bounded noexec/nosuid/nodev box tmpfs. It rejects container hosting and piped host core collectors. WSL's existing `/wsl-capture-crash` pipe was left unchanged; only explicitly trusted fixtures bypass that single gate via internal test composition. Worker configuration has no bypass. Therefore the current application's default remains Docker and live native judging is not enabled on this WSL host. See [ADR 0013](adr/0013-native-per-test-runtime.md) and the [native host operating requirements](operations/development-runbook.md#native-runtime-profile).

## Reproduce

Phase 3 adds bounded job scheduling and a shared FIFO execution budget; `budget-wait` is operational waiting, excluded from contestant resource limits. `minos.worker.active_jobs`/`job_duration` and `minos.judging.active_sandboxes`/`reserved_memory` report bounded job/stage reservations without sensitive labels. The native box remains serialized; compilations can overlap across jobs. Configuration, cleanup blocking and delivery evolution are recorded in [ADR 0014](adr/0014-bounded-worker-scheduling.md).

The Phase 3 disposable full Docker suite passed 22 Domain and 188 integration tests, with seven skips. Its eight mixed jobs (Accepted/WrongAnswer/RuntimeError/CompilationError, two accounts, three private tests per problem) preserved all result counts, published test order, oldest-ready claim order and owner authorization. One versus two active jobs finished the workload in 47.061/25.105 seconds; queue P95 was 48.414/26.876 seconds and completion P95 46.641/24.910 seconds. Peak reserved slots were 1/2 and peak reserved memory stayed below 1152 MiB. These fixtures do not establish public capacity; compilation failures execute zero private tests. Queue uses persisted UTC timestamps; completion uses a monotonic timer from the admission response through owner-read polling, so the two clocks/polling are not strictly additive. Evidence: ignored `artifacts/judging-phase3-20261008/docker-full.log`.

The final native focused suite passed 22 Domain and 66 integration tests (68 total; the two separate Phase 2 benchmarks were opt-out skipped). It exercises the final scope-disposal handling, FIFO/cancellation/cleanup failure, targeted ID claims/recovery, the same execution safety fixtures and eight actual mixed native submissions with 100 private tests each. Each workload executes 600 runtimes: two compilation failures leave their 200 tests NotExecuted; the other six submissions execute every test. Each Accepted result is 100/100 and each WrongAnswer result 67/100; RuntimeError is 0/100. Both one/two-job workloads preserve every ordered outcome and resource measurement.

| Eight-job workload | One active job | Two active jobs |
| --- | ---: | ---: |
| Docker, 3 tests/problem: total elapsed | 47.061 s | 25.105 s |
| Docker: queue / completion P95 | 48.414 / 46.641 s | 26.876 / 24.910 s |
| Isolate, 100 tests/problem: total elapsed | 16.550 s | 10.981 s |
| Isolate: queue / completion P95 | 18.678 / 16.392 s | 9.826 / 10.873 s |

Compare one/two jobs within each row; Docker and native use different test counts and Linux environments. Timers exclude problem/account and runner initialization; total workload includes submission admission, processing and owner reads. Queue is creation-to-attempt-start using persisted UTC, whereas completion begins after admission returns and uses a monotonic timer. Admission processing, wall-clock changes and polling can make these values differ; do not subtract them as if they were the same measurement window. Both profiles stayed at/below two reserved stages and the 1152-MiB fixture budget, with zero reservations after shutdown. Native still serializes runtime boxes; overlap is chiefly compilation and other jobs' stages. Evidence: `native-focused.log`, `windows-build-final.log` (zero warnings/errors) and `windows-unit-final.log` (13 scheduler/budget/architecture tests passed). The final Linux build also passed with zero warnings/errors; the test reference to the existing Worker adds only project metadata to the lockfile, without changing package versions.

`bash scripts/check-judging-concurrency.sh minos-checks:local` reuses the existing private disposable SDK/PostgreSQL harness, defaults to the full Docker suite, and outputs `JUDGING_CONCURRENCY_JSON` for paired one/two-job workloads. `MINOS_TEST_RUNNER=isolate MINOS_TEST_ALL=0` selects the trusted native fixture profile and focused execution/scheduling tests; native mixed jobs have 100 tests each. The script refuses active Workers/sandboxes. The explicit temporary root helper authorization and live core-policy restriction still apply. This does not install an MQ or deploy the application.

Phase 3 experiment cleanup passed: root helper/prefix, delegated transient service/cgroup, both tmpfs mounts and extracted SDK/fixture workspaces were removed; no sandbox UID process or fixture PostgreSQL/export resources remained (`cleanup.log`). Application configuration and its existing stopped Worker state were preserved. The source also contains an independent 1500-test capacity migration added while checks were being prepared; it was preserved and validated by fresh PostgreSQL migration. Phase 3 adds no migration. The standalone foundation release rehearsal was not rerun; the full disposable suite and focused actual-Worker workloads above are the validation scope. `result.json` retains image ID, source hashes and all workload measurements.

Build a trusted checks image with `docker/backend.Dockerfile`, target `checks`. Stop native/Compose Workers and finish other sandbox checks first because startup recovery uses the daemon's judge label. On Linux/WSL run:

```bash
bash scripts/benchmark-judging.sh minos-checks:local
```

The script creates a uniquely named labeled volume, initializes its ownership and runs the existing integration-test project as UID 1654. It uses no database. Its output contains `JUDGING_BENCHMARK_JSON` with compile, initialization, 100-test duration, median/P95 and phase totals. Cleanup removes only its generated runner and sandboxes attached to its owned volume. Run before/after images sequentially with no concurrent build or judging workload. The benchmark is opt-in via `MINOS_TEST_BENCHMARK=1`; ordinary correctness checks do not enforce machine-dependent timing thresholds.

Temporary host provisioning uses `scripts/experiments/isolate-phase2-host.sh`. It targets this WSL profile (controller UID/GID 1000, unused sandbox UID/GID 61000, cpuset CPU 0), not arbitrary machines. With signed build prerequisites already installed, a pinned source checkout under ignored artifacts, Docker's pinned compiler image present, a trusted current checks image, and explicit authorization for root helper setup:

```bash
source_dir="$PWD/artifacts/judging-phase2-20261008/isolate-source"
test "$(git -C "$source_dir" rev-parse HEAD)" = 8f185bb37f3f23e29b33b0c7727c91c13429abe3
make -C "$source_dir" -B isolate isolate-cg-keeper \
  CONFIG=/opt/minos-isolate-phase2-20261008-8f185bb/isolate.cf
sudo bash scripts/experiments/isolate-phase2-host.sh setup "$source_dir"
bash scripts/benchmark-native-judging.sh minos-checks:local
sudo bash scripts/experiments/isolate-phase2-host.sh cleanup
```

The native checks script extracts the cached SDK and compiled test/API assets into a private disposable directory, bootstraps a disposable PostgreSQL over a Unix socket, and runs Domain plus execution/official-judging/scheduling fixtures. `JUDGING_BENCHMARK_JSON` reports backend timings; `OFFICIAL_NATIVE_BENCHMARK_JSON` reports HTTP/PostgreSQL all-test counts and timings; `JUDGING_CONCURRENCY_JSON` reports mixed-job scheduling. It refuses active Compose Workers or sandboxes, does not start/stop the application, and removes only its marked resources. Ordinary test runs do not enforce machine-dependent timing thresholds. The internal core-policy test override is limited to these known fixtures.

Cleanup refuses deletion unless the literal resolved prefix, root ownership and experiment marker match, all sandbox processes/boxes are gone, the keeper has stopped and both scratch/box mounts have been unmounted. Run cleanup even if a benchmark fails, after resolving any surviving experiment box; do not leave an experimental setuid helper installed. The old `scripts/experiments/benchmark-isolate.py --probe-gaps` separately verifies known Python prototype mismatches; its successful exit does not mean those verdicts are correct. These scripts do not accept contestant submissions.

## Separate compile allocation and two native boxes (2026-10-09)

[ADR 0015](adr/0015-compile-runtime-allocation-and-isolate-boxes.md) adds separate compile/runtime quotas, shared worst-case stage reservations and a native pool. The application default remains Docker. Two helpers were built from the unmodified pinned Isolate 2.7 commit `8f185bb37f3f23e29b33b0c7727c91c13429abe3`, each with a fixed configuration, distinct UID/GID/CPU/cgroup and independent eight-MiB scratch tmpfs. The trusted WSL fixtures retain the internal core-policy override; live hosting restrictions remain enforced. Tests inside each submission stay sequential and fresh.

The twenty-job fixture admits four accounts' five submissions each before starting the real Worker. Every problem has 100 private tests. Eight Accepted, four WrongAnswer and four RuntimeError submissions execute all 1,600 runtime stages; four CompilationError submissions leave 400 tests NotExecuted. Accepted is 100/100, WrongAnswer 67/100 and RuntimeError 0/100. Both profiles preserve ordered results, owner authorization, one attempt per job, oldest-ready claim order and zero reservations after shutdown.

| Twenty-job native workload | Baseline: 2 active jobs, compile/run 1/1 | Pipeline: 8 active jobs, compile/run 2/2 |
| --- | ---: | ---: |
| Actual admission duration | 0.306 s | 0.256 s |
| Total elapsed including admission, owner reads and validation | 39.848 s | 28.498 s |
| Queue P95: persisted creation to first claim | 37.489 s | 16.734 s |
| Completion P95: admission response to owner read | 36.370 s | 26.213 s |
| Peak compile / runtime stages | 1 / 1 | 2 / 2 |
| Peak total reserved stages | 2 | 4 |
| Peak reserved stage memory | 648 MiB | 1296 MiB |
| Largest measured compiler memory peak | 53.03 MiB | 52.95 MiB |

Both profiles install two boxes; the baseline runtime quota is one. Baseline stage budget is 1152 MiB with a 384-MiB controller reserve, and the larger pipeline uses 2048/768 MiB. Reservations include maximum memory plus scratch and are ceilings, not physical RAM allocations. The measured compiler peak is for these small programs; the 512-MiB compile bound remains necessary for broader programs. Raising active jobs alone cannot bypass the class/box/CPU/memory caps.

Eight-job native workloads at one/two/four active jobs finished in 20.942/15.324/11.457 seconds, with queue P95 22.343/16.573/5.924 seconds. The four-job profile uses compile/run quotas 2/2 and reaches both native boxes. Docker's eight-job, three-test workloads at one/two/four jobs are recorded in `docker-full-final.log`; compare profiles within their own workload, not Docker three-test results against native hundred-test results.

This is one local paired run, not a public capacity or two-second latency promise. Queue and completion use different clock origins and polling; do not subtract them. Twenty jobs were admitted in less than two seconds, but their last results still take tens of seconds on this two-box profile. Additional capacity needs host measurements and validated quota/box increases.

Evidence is under ignored `artifacts/judging-multibox-20261009/`. The first Docker run had two unexpected CompilationError results with empty diagnostics while other development checks were running; this does not establish their precise cause. The private harness now starts maintenance/tests from Linux fixture directories rather than the Windows repository root. The rerun passed the full Docker suite with unchanged compile limits (30 Domain tests; 208 integration passes and 11 configuration/platform skips). Retain the failed `docker-full.log` beside `docker-full-final.log`; their timing difference demonstrates environment sensitivity. A subsequent native run caught a missing runtime-initialization histogram after the pool refactor. The native adapter restores that measurement; the interrupted diagnostic run is retained as `native-metrics-fix-aborted.log`. Windows and final Linux Release builds pass with zero warnings/errors.

The final full native suite passes 30 Domain and 216 integration tests, with four opt-out/platform skips (`native-full-final.log`). It covers the corrected histogram, concurrent boxes, independent cancellation, CPU/descendant/memory/output/evidence rules, fresh reuse and the mixed workloads above. The full Docker run precedes the final native-only histogram correction; the final native suite exercises the unchanged Docker compiler as well. `result.json` records checks-image IDs, implementation/test hashes, measured workloads and the cleanup scope. The root-owned helper prefix, transient keeper service/cgroup, every scratch mount, sandbox UID process and labeled fixture containers/volumes were removed. Retained failed fixture workspaces were removed after inspection. The exact original application Worker container was restarted and its default Docker configuration preserved (`cleanup.log`). The standalone foundation release/restore rehearsal was not rerun as part of this scheduling change.
