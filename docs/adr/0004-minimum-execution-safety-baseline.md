# ADR 0004: Minimum execution safety baseline

Decision register ID: D10\
Created: 2026-09-13\
Status: Accepted baseline; M1 development bounds validated; final verdict/accounting policy pending M6\
Decision owner: Project owner\
Accepted date: 2026-09-13

Related documents: [ADR 0002](0002-judge-development-environment.md), [Decision register](../decisions.md), [M0 closure](../m0-closure.md), [Implementation roadmap](../implementation-plan.md)

## Context

The Docker execution backend needs a minimum containment and evidence contract before the controlled M1 spike begins. The baseline must protect the development environment without prematurely deciding product verdict precedence or final resource-accounting semantics.

## Accepted baseline

- Submitted code never executes directly in the API or Worker host process. The Worker orchestrates the isolated runner and never launches a contestant executable directly on its host.
- Network access is disabled by default.
- Execution has bounded wall time and CPU usage.
- Memory usage is bounded.
- Process/thread creation is bounded.
- stdout and stderr output are bounded.
- Writable scratch space is bounded.
- Execution runs without root privileges, with unnecessary capabilities removed.
- The runner returns structured evidence describing exit code, timeout, memory/resource failure, output limit, and infrastructure failure.

Apply containment and resource controls to compilation too, as required by D02. Contestant containers receive only private Linux-native submission workspaces, with no Windows D: drive, repository, host secrets, expected answers, or Docker socket.

## Explicitly not finalized in M0

- Exact numeric limits are not fixed. Choose bounded test values and validate/tune them during M1 before running each resource experiment.
- Final contestant verdict precedence remains an M6 decision.
- Final resource-accounting semantics, including the official interpretation and reporting of CPU/time/memory evidence, remain an M6 decision.
- Concrete Docker configuration, image selection, logging limits, scratch enforcement, and collection mechanisms are implementation/validation work in M1, not new mandatory ADRs.

The baseline requires enforceable bounds and useful evidence. It does not prescribe a total CPU-time policy, equate CPU throttling with an execution deadline, classify every abnormal exit as a particular verdict, or accept a peak-memory reporting definition.

## Rationale

These controls materially affect correctness and security even with one developer. Structured evidence keeps the backend replaceable and allows later Evaluation rules to calculate a verdict without depending on Docker-specific status codes.

Accepting numeric limits or final verdict mapping before observing the runner would create unsupported assumptions. M1 produces enforcement/evidence findings; M6 resolves the user-visible policy.

## Consequences and validation

- M1 starts with environment/configuration verification and a trusted smoke test, then controlled bounded resource fixtures.
- Before untrusted-program experiments, verify the configured workspace, network, privilege, and resource restrictions. Do not substitute host execution if a container cannot start.
- Validate CPU/wall bounds, memory, processes/threads, stdout/stderr, writable scratch, and compilation limits on the actual WSL2/Docker configuration.
- Demonstrate structured normal exit, timeout, resource/output failure, and runner infrastructure failure without prematurely translating them into final contestant verdicts.
- Cleanup and interruption behavior remain part of the required D02 M1 checks.
- Preserve backend-independent evidence; Docker IDs, daemon errors, and configuration stay in the infrastructure adapter.
- M1 completion demonstrates the tested development policy; production host readiness remains deferred.

## Acceptance evidence

The project owner explicitly accepted these safety requirements and directed that numeric tuning stay in M1 and final verdict/accounting policy stay in M6. The owner requested M0 closure.

M1 subsequently passed the executable development safety suite. [Recorded evidence](../milestones/m1-execution-feasibility.md) includes applied numeric bounds, resource failures, output/scratch exhaustion and cleanup/recovery. Sampling, attachment overhead and unavailable counters remain explicit limitations; no final contestant verdict policy is selected here.
