# ADR 0002: Judge development execution environment

Decision register ID: D02\
Created: 2026-09-13\
Status: Accepted — M1 development runtime validation passed\
Decision owner: Project owner\
Accepted date: 2026-09-13

Related documents: [ADR 0001](0001-initial-audience-and-access-policy.md), [Decision register](../decisions.md), [Project design](../project-design.md), [Implementation roadmap](../implementation-plan.md)

## Context

Minos is a single-developer project on Windows, with the repository remaining on the Windows D: drive. WSL2 is already available. There are no real users during development. The API must not execute submitted programs.

The project owner selected WSL2 Ubuntu for the Worker and Docker as the initial compile/run backend. The execution boundary must support a future direct-Linux Isolate adapter without changing Submission/Judging rules or durable job behavior. Development convenience must not expose the Windows repository or personal files to contestant processes.

This decision establishes the development topology and execution contract. Production host selection remains deferred until real execution and performance evidence exists.

## Options considered

| Option | Benefits | Trade-offs |
| --- | --- | --- |
| WSL2 Ubuntu Worker with Docker compile/run | Reuses the existing environment; reproducible toolchain packaging | Resource enforcement, private Linux workspace access, and cleanup require actual tests |
| Dedicated local Linux VM | Additional guest configuration control | Unnecessary setup and resource overhead at this development stage |
| Direct-Linux Isolate execution | Potential later contest-focused backend | Adoption and kernel-specific validation are deferred; no nested Isolate-in-Docker design |

The earlier dedicated-VM proposal was not accepted. The project owner's explicit WSL2/Docker choice replaces it. Remote production options are not being reconsidered here.

## Decision

- The repository stays on the Windows D: drive.
- WSL2 Ubuntu is the Linux development environment for the trusted Judge Worker.
- Docker is the first execution backend for both compilation and execution of untrusted submissions.
- Each submission's required files are copied/materialized into a private Linux-native workspace. Runner containers receive only explicitly selected files/workspace mounts, never the D: drive, Minos repository, or unrelated host directories.
- Submitted processes receive no Worker credentials, expected answers, Docker socket, or privileged host management access. Compilation is subject to isolation and limits too.
- The execution abstraction accepts source/artifacts, supported toolchain configuration, input, and limits, and returns structured exit/resource/limit evidence. Docker container IDs, daemon calls, mount configuration, and Docker-specific errors stay in the infrastructure adapter.
- Submission/Judging domain rules and durable claims, leases, attempts, retries, and result completion do not depend on Docker.
- A future Isolate adapter uses the same application contract and runs directly against the Linux kernel rather than inside the Docker runner.
- A dedicated Linux VM is not required at this stage.
- Production judge-host choices remain deferred until sandbox and performance evidence warrants them.

## Rationale and MVP fit

This uses the developer's existing Windows/WSL2 workflow while retaining a real isolation boundary for untrusted work. Private Linux-native copies separate repository location from contestant access. A small replaceable execution adapter preserves future evolution without implementing two runners or adding service infrastructure now.

Acceptance records the selected architecture, not a claim that Docker defaults enforce the MVP policy. Runtime validation is required in M1. Local measurements are development evidence, not a production benchmark.

## Required M1 validation only

| Check | Evidence required |
| --- | --- |
| WSL2/Docker compatibility | Ubuntu Worker can start and inspect Linux containers with the intended resource controls. Record the actual WSL/kernel/Docker versions and daemon location; Docker Desktop integration versus an Ubuntu daemon is a setup note, not another ADR. |
| Workspace isolation | Container mount/path inspection plus filesystem probes show only the private Linux-native submission files. Windows D:, /mnt/d, the repository, personal files, answers, and other submissions are unreachable. Check source staging for symlink/path traversal. |
| Compile/run containment | Both phases run without privileged containers, host namespace sharing, secrets, Docker socket, or network access; use an unprivileged contestant user and reviewed capability/syscall restrictions. |
| Resource enforcement | Known CPU-loop, sleeping, memory-heavy, process-heavy, output-heavy, disk-heavy, and expensive-compilation cases terminate or fail within the configured bounds. CPU rate throttling is not treated as a total CPU-time deadline. Record actual time and process-tree memory evidence, including short runs and OOM/timeout distinctions. |
| Output and cleanup | Compiler/program output and container logs stay bounded. Completion, timeout, cancellation, and Worker interruption leave no running contestant containers/processes; restart cleanup removes abandoned private workspaces without exposing other files. |
| Backend-independent evidence | The adapter returns structured success, compile failure, abnormal exit, limits, and infrastructure failure. Docker-specific transport details do not enter the domain/job lifecycle. Demonstrate orchestration against a test adapter; implementing Isolate is not required. |
| Repeatable minimal workflow | Compile and run known correct/failing C++ programs and two isolated concurrent workspaces; repeat to check contamination and scratch/resource growth. Record limitations relevant to MVP judging. |

These checks cover the selected development backend. They do not require production hosting, load forecasting for real users, a full security certification, an Isolate installation, or the durable PostgreSQL queue, which is implemented at M5.

Docker resource controls require explicit configuration and host support; validate them rather than relying on defaults. [Docker resource constraints](https://docs.docker.com/engine/containers/resource_constraints/)

## Consequences and deferred details

- M1 proves this backend and selects routine setup/image/measurement details based on evidence.
- M2 retains distinct API and Worker entry points within the modular monolith.
- D09 toolchain/checker details, D10 final limits/verdict policy, and D13 durable artifact storage are resolved with their dependent work.
- A later Isolate adapter may change infrastructure setup but must preserve the application evidence contract and domain/job semantics.
- Local physical resource contention is recorded when measuring behavior.
- Production topology and Isolate adoption remain deferred; neither is an M1 dependency.

## Acceptance evidence and follow-up

The project owner explicitly selected Windows D: repository storage, WSL2 Ubuntu, Docker first, private Linux-native submission copies, a backend-independent execution contract, and future direct-Linux Isolate compatibility. The owner explicitly rejected a dedicated-VM requirement and deferred production host choices.

The project owner accepted D03 and the D10 minimum safety baseline and requested closure. [M0 is complete](../m0-closure.md). The complete executable M1 suite passed isolated compile/run, concurrent private workspaces, resource/output exhaustion, forbidden-access probes, adapter replacement, repeated cleanup, cancellation and controller-crash recovery. Findings and numeric development bounds are recorded in [M1 evidence](../milestones/m1-execution-feasibility.md). This does not finalize production hosting or M6 verdict/accounting policy.
