# ADR 0015: Separate stage quotas and multiple native boxes

Date: 2026-10-09
Status: Owner-authorized, implemented and verified with two native boxes. Supersedes the single-box restriction and scheduling defaults in ADR 0014; Docker remains the application default.

## Context

The owner requested separate compilation allocation and multiple Isolate boxes. Native runtime's single box is a throughput limit, while admitting only two whole submissions prevents new compilations during their runtime stages. A larger job count alone does not provide runtime parallelism. Source is untrusted even during compilation, so both stages retain sandboxing and bounded resource admission.

## Decision

Keep one Worker and the five existing production projects. Worker defaults to four active submissions, one compile stage and one runtime stage at a time, under up to two total sandbox slots, 2048 MiB stage memory and 512 MiB controller reserve. Operators can increase compile/runtime quotas and active jobs after validating capacity. Total slots cannot exceed available logical CPUs minus one; controller reserve remains at least `256 + 64 × active jobs` MiB. Both class quotas are positive and no larger than total slots. These are ceilings, not promises of dedicated cores or immediate compilation.

Infrastructure keeps FIFO order within each stage class. The oldest waiting stage whose class has capacity is considered first. A class at its quota cannot block the other class. An eligible older stage waiting for memory is not bypassed by newer smaller stages. Compile and runtime share total slot and worst-case memory limits; limits include scratch and are accounting reservations, not preallocated physical RAM. Permits remain held through provisioning, evidence and cleanup. The number of retained source/executable/result buffers is bounded by active jobs, whose independent leases continue to renew. No unbounded compile-ahead queue or persisted compiled-artifact state is introduced.

Native mode supports 1–16 configured boxes, with runtime quota no larger than box count. One-box installation remains compatible. Multiple boxes use unique box IDs, UID/GID `61000 + id`, cgroups `box-id` and one available logical CPU `id` each. The controller identity must be outside the reserved range. Every test still creates fresh namespaces/cgroup/process/writable state; tests inside a submission remain sequential and all are executed after successful compilation.

The unmodified pinned Isolate helper embeds its configuration file at build time. Multiple boxes therefore use copies built from the same version/commit, each targeting `<prefix>/helpers/<id>/isolate.cf`. Each config uses a separate `<prefix>/boxes/slot-<id>` eight-MiB noexec/nosuid/nodev tmpfs as its `box_root`; Isolate creates and removes its own `<id>` directory below that mount. Scratch capacity is independent per box, rather than one larger shared filesystem that one submission can fill. Helpers share the root-owned lock directory and delegated swap-disabled keeper cgroup. Startup validates every helper/configuration/mount, recovers every configured box and refuses unknown box/cgroup state. Runtime evidence also verifies effective CPU affinity.

The singleton backend assigns a free box only to one runtime stage and returns it after cleanup. A cleanup fault poisons the native pool and shared budget; pending/new stages and new deliveries fail closed until restart/recovery, while already active stages finish their own cleanup. Cancellation of one box does not clean or interrupt any other box. Startup recovery and the exclusive host lock remain mandatory. Core-dump policy, protected hardlinks, pinned runtime libraries, bounded independent output capture and private stage-only mounts are unchanged.

Compilation remains fresh Docker sandboxes with the pinned GCC toolchain and existing limits. PostgreSQL remains job/attempt/result authority. This does not add a broker, multiple Worker hosts, test-process reuse, new verdict semantics, production provisioning or M8 functionality.

## Verification and operations

Existing test projects add class-quota fairness/cancellation/total-memory tests, concurrent native identity/CPU/scratch isolation and independent cancellation. Mixed real-Worker HTTP/PostgreSQL workloads cover one/two/four active jobs. An opt-in twenty-job fixture records actual admission duration, queue/completion P95, stage concurrency, reserved memory and actual compiler memory peak; no timing assertion or public SLA is inferred.

Metrics add `minos.judging.active_stages` with only fixed `compile`/`run` labels and `minos.judging.compiler_memory_peak` in bytes. Safe test setup/reproduction and measured results belong in the performance report/runbook. Rollback uses one active job, one stage slot and one-box profile or Docker, followed by stop/restart and `--check`; durable results are preserved.

Final native full validation passed 30 Domain tests and 216 integration tests, with four opt-out/platform skips. The two-box fixture verifies distinct identities/CPU/scratch, independent cancellation, fresh reuse, full ordered outcomes, resource bounds and cleanup. Twenty mixed submissions with 100 tests each finished in 39.848 seconds at two active jobs and compile/run quotas 1/1, versus 28.498 seconds at eight jobs and quotas 2/2; queue P95 fell from 37.489 to 16.734 seconds. This validates a bounded pipeline, not a two-second burst latency promise. See [performance evidence](../judging-performance.md) for workload, clocks, compiler memory, failed diagnostic runs, build versions and cleanup. Other supported box counts require matching provisioned hosts and capacity validation; ten/sixteen-box performance was not measured.
