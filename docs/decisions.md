# Minos decision register

Date: 2026-09-13\
Status: M0–M6 complete; M7 backend complete and verified; development MVP owner acceptance pending

Related documents: [M0 closure](m0-closure.md), [Project design](project-design.md), [Implementation roadmap](implementation-plan.md)

## How to use this register

Confirmed constraints come from the project brief and subsequent explicit project-owner decisions. Proposed decisions are recommendations, not approvals. Open decisions identify missing product or deployment information. Record the chosen option, rationale, owner, and date when a decision is resolved.

M0 gates M1 using only the decisions that affect the initial scope, execution boundary, and minimum safety contract. Other choices are resolved before the work that depends on them. The former rule requiring acceptance of all 18 topics before any implementation is retired.

Keep ADRs for material correctness, security, or evolution decisions. Record ordinary implementation choices in their milestone notes; they do not need individual ADRs or a separate approval process.

## M0 closure status

- The project owner accepted all four before-M1 gates and explicitly requested M0 closure.
- Later decisions remain scheduled for their dependent work; they are not discussed or accepted by this closure.
- Record material accepted choices in ADRs and synchronize the design and roadmap. Use brief milestone notes for routine implementation choices.
- A proposed ADR remains pending; discussion or document creation does not count as acceptance.
- Accepted: [D01 — Development access and future registration behavior](adr/0001-initial-audience-and-access-policy.md).
- Accepted: [D02 — WSL2 Ubuntu with a Docker execution backend](adr/0002-judge-development-environment.md). The M1 executable development validation suite subsequently passed.
- Accepted: [D03 — Official judging in the first end-to-end MVP](adr/0003-official-judging-mvp.md); custom Run follows reliable official judging.
- Accepted baseline: [D10 — Minimum execution safety](adr/0004-minimum-execution-safety-baseline.md); M1 verified development bounds and [ADR 0009](adr/0009-official-judging-and-evidence.md) accepts official verdict/accounting policy.
- Accepted ADR records at M0 closure: **4 of 18**, with D10 counted only for its minimum baseline. This historical count is not an implementation gate; later implementation selections are tracked in the register below.
- Before-M1 gate: **4 of 4 accepted**.
- M0 status: **complete**. M1 subsequently passed compile/run, resource/isolation, replacement-adapter and recovery experiments. [M1 evidence](milestones/m1-execution-feasibility.md) records the results; M2 began after they passed.

The [M0 closure summary](m0-closure.md) records the historical accepted baseline. M1's tested development containment/evidence contract passed; later domain/product choices remain scoped to their dependent work.

## Confirmed constraints

| ID | Constraint | Source |
| --- | --- | --- |
| C01 | Backend first: C#, ASP.NET Core, PostgreSQL, EF Core | Project brief |
| C02 | Modular monolith with useful Clean Architecture and DDD boundaries | Project brief |
| C03 | Submitted programs never execute directly in the API or Worker host process | Project brief and accepted D10 baseline |
| C04 | Isolated execution and a worker boundary that can evolve independently | Project brief |
| C05 | C++ and standard input/output problems first | Project brief |
| C06 | Delay contests, ratings, advanced judging, and unnecessary distributed infrastructure | Project brief |
| C07 | Design, documents, and clear milestones before implementation | Project brief and documentation request |
| C08 | Developer-only development; no real-user cohort or enrollment/approval workflow | Accepted D01, project-owner clarification |
| C09 | Finished-product self-service email/password registration and Google as the first external sign-in provider; no normal-user approval | Accepted D01, project-owner clarification |
| C10 | Docker-hosted Linux Worker for local Compose; native WSL remains supported; repository stays on Windows D:; contestant workspaces use private Linux-native copies | Accepted D02 updated by [ADR 0011](adr/0011-containerized-worker-development.md), project-owner request |
| C11 | Execution contract is independent of Docker; a future Isolate adapter runs directly on Linux without changing domain/job lifecycle | Accepted D02, project-owner clarification |
| C12 | No dedicated development VM required; production judge-host choices deferred until execution/performance evidence exists | Accepted D02, project-owner clarification |
| C13 | Architectural rigor for correctness/security/evolution; no enterprise process or ADR per implementation detail | Project-owner scope clarification |
| C14 | Official judging in first end-to-end MVP; custom Run follows reliable official judging | Accepted D03 |
| C15 | Minimum containment/resource/evidence contract accepted; numeric tuning in M1, final verdict/accounting policy in M6 | Accepted D10 baseline |

## Complete decision register

All 18 existing decision IDs are preserved. Blocking means "must be clear before the work that depends on it," not "all future details must be fixed now." This register lists recommendations without accepting the unresolved ones.

### Must decide before M1

| Decision ID | Decision topic | Why it matters | Blocks implementation? | Current recommendation / accepted choice | Status |
| --- | --- | --- | --- | --- | --- |
| D01 | Development access and future registration | Keeps temporary development access separate from product behavior | Resolved | Developer-only; future email/password and Google self-service; no approval workflow | Accepted |
| D02 | Development judge environment and execution boundary | Establishes filesystem isolation and replaceable execution | Resolved; M1 validation passed | Docker-hosted Linux Worker with sibling sandboxes; native WSL profile supported; private Linux-native copies; future direct-Linux Isolate adapter; no dedicated VM | Accepted; local hosting updated in [ADR 0011](adr/0011-containerized-worker-development.md) |
| D03 | Core MVP versus custom Run | Defines the first end-to-end outcome and acceptance scope | Resolved | Official judging first; custom Run follows reliable judging, planned at M8 | Accepted |
| D10 | Minimum execution safety and resource contract | Prevents uncontrolled compilation/execution and ambiguous evidence | Baseline resolved; validation in M1/M6 | No direct host execution; network off by default; bounded wall/CPU/memory/processes/threads/stdout/stderr/scratch; non-root, reduced capabilities; complete structured evidence | Accepted baseline; development bounds verified in M1, official accounting accepted in [ADR 0009](adr/0009-official-judging-and-evidence.md) |

### Can decide during later milestones

| Decision ID | Decision topic | Why it matters | Blocks implementation? | Current recommendation | Status |
| --- | --- | --- | --- | --- | --- |
| D04 | Context boundaries | Assigns business-rule and model ownership | No for M1; initial ownership selected in M2 | Accounts, Problems, Evaluation within the five-layer project structure | Working selection implemented through M5; later models evolve in their milestones |
| D05 | Publication/version semantics | Keeps queued and historical evaluation specifications stable | No for M1; before M4 publication | Stable Problem ID plus immutable version ID and unique version number; future submissions pin version ID | Accepted and implemented in [ADR 0007](adr/0007-versioned-problems-and-private-tests.md) |
| D06 | Durable job transport | Prevents lost submissions and enqueueing gaps | No for M1; before M5 jobs | PostgreSQL queue; submission and job in one transaction | Accepted and implemented in [ADR 0008](adr/0008-postgresql-submission-jobs-and-leases.md) |
| D07 | Recovery and completion ownership | Prevents stuck jobs and stale result overwrite | No for M1; before M5 claims/results | Leases, bounded retries, attempt tokens, guarded completion | Accepted and implemented in [ADR 0008](adr/0008-postgresql-submission-jobs-and-leases.md) |
| D08 | Processing status versus verdict | Distinguishes progress, contestant outcomes, and platform faults | No for M1; during M5/M6 | Separate processing state, contestant verdict and infrastructure failure; approved limit precedence | Accepted in [ADR 0008](adr/0008-postgresql-submission-jobs-and-leases.md) and [ADR 0009](adr/0009-official-judging-and-evidence.md) |
| D09 | C++ toolchain and output checker | Makes compilation reproducible and comparison predictable | Toolchain selected in M1; checker in M6 | Digest-pinned GCC 14.3.0, C++20, fixed compilation; exact byte comparison including whitespace/newlines | Accepted in [ADR 0009](adr/0009-official-judging-and-evidence.md) |
| D11 | Authentication mechanics and account lifecycle | Implements accepted product sign-in securely | No for M1; before M3 auth | Separate Domain account/Identity persistence; protected web cookies; explicit provider-subject linking, no email merge; recovery delivery deferred | Selected and implemented in [ADR 0006](adr/0006-account-authentication-and-linking.md); live Google check pending |
| D12 | Reference-solution validation | Detects incorrect problem/test packages | No for M1; during M4/M6 | Trusted publication checklist plus developer-run real sandbox fixture validation; no stored reference solution or new publication gate | Accepted in [ADR 0007](adr/0007-versioned-problems-and-private-tests.md) and [ADR 0009](adr/0009-official-judging-and-evidence.md) |
| D13 | Source and testcase storage | Protects private artifacts and supports reliable access | No M0 block; workspace mechanics in M1, storage in M4 | Bounded source in PostgreSQL; bounded private testcase files behind an opaque-key storage port | Testcase portion implemented in [ADR 0007](adr/0007-versioned-problems-and-private-tests.md); bounded submission source implemented in [ADR 0008](adr/0008-postgresql-submission-jobs-and-leases.md) |
| D14 | Client result updates | Determines how clients observe committed results | No for M1; during M5 | Polling; no separate ADR needed for polling intervals | Selected and implemented in M5 |
| D15 | Layer/module packaging | Supports understandable dependencies and tests | No for M1; selected in M2 | Domain/Application/Infrastructure/API/Worker projects, module folders, two meaningful test projects; sandbox experiments remain separate | Initial scaffold implemented and built |
| D16 | Official test execution order | Determines diagnostic depth, cost, and verdict consistency | Resolved for complete judging | Execute every test after successful compilation; all-pass Accepted, otherwise earliest published failure; accurate owner counts | Historical M6 policy in ADR 0009 superseded for new results by [ADR 0012](adr/0012-complete-private-test-judging.md) |
| D19 | Lightweight native runtime | Removes measured per-container overhead while preserving per-test evidence | Resolved for opt-in implementation | Compile once in pinned Docker; fresh Isolate box/cgroup per test on a validated direct-Linux host; Docker remains default | Implemented and fixture-validated in [ADR 0013](adr/0013-native-per-test-runtime.md); live host validation/default switch remain separate |
| D20 | Bounded scheduling and ID delivery | Allows compilation/runtime overlap without exhausting resources or losing attempts | Owner authorized Phase 3 on 2026-10-08 | One Worker, bounded independent job scopes, shared FIFO execution budget, PostgreSQL ID source plus targeted durable claim | Implemented in [ADR 0014](adr/0014-bounded-worker-scheduling.md); validation evidence in the improvement plan; broker/distributed scope deferred |
| D21 | Separate compile/runtime allocation and multiple native boxes | Removes the single runtime box bottleneck without an unbounded compile-ahead queue | Owner authorized implementation on 2026-10-09 | Separate class quotas under shared slot/memory bounds; per-box UID/GID, CPU, fresh cgroup and independent scratch | Implemented and verified with two boxes in [ADR 0015](adr/0015-compile-runtime-allocation-and-isolate-boxes.md); Docker remains default |
| D17 | Visibility, retention, and history | Prevents data leakage and uncontrolled storage growth | Resolved for developer-only M7 | Retain durable history and referenced tests for project lifetime; unreferenced uploads expire after 24 hours; immediate stage cleanup/startup recovery; seven daily paired backups | Implemented in [ADR 0010](adr/0010-development-reliability-and-retention.md); public retention remains deferred |

### Can safely defer

| Decision ID | Decision topic | Why it matters | Blocks implementation? | Current recommendation | Status |
| --- | --- | --- | --- | --- | --- |
| D18 | Manual rejudging and administrative recovery workflow | Governs intentional result replacement and audit history | No; automatic crash recovery is D07 | Defer the rejudge API/workflow; preserve attempt evidence and original specifications now | Deferred proposal |

Production judge-host selection, external brokers/caches, push updates, distributed workers, additional languages, contests, and ratings remain outside the M0 gate. Isolate compatibility was accepted at M0; its opt-in implementation was subsequently authorized on 2026-10-08 and is recorded in D19. This does not select a production host or change the Compose default.

## M0 closure checklist

- [x] D01 accepted and synchronized.
- [x] D02 development topology and replaceable runner boundary accepted; real validation remains M1 work.
- [x] D03 release boundary resolved.
- [x] D10 minimum safety/evidence contract accepted for the controlled M1 spike; numeric and M6 policies remain pending.
- [x] Later choices have relevant milestone deadlines; production hosting is deferred.
- [x] Final assumptions, MVP boundary, risks, deferred work, and M1 dependencies reconciled in the M0 closure summary.
- [x] Project owner accepts the baseline and explicitly requests M0 closure. Starting M1 is a separate next action, not a prerequisite for completing M0.

**M0–M6 complete; M7 backend authorized and implemented.** Live Google validation remains deferred because no OAuth client exists. [ADR 0009](adr/0009-official-judging-and-evidence.md) records the accepted M6 verdict/checker/resource policies; [M7 evidence](milestones/m7-reliability.md) records reliability implementation and acceptance checks. Historical M0 closure remains unchanged. Development MVP owner acceptance and M8 approval remain separate decisions.

## Decision completion record

For each resolved decision, append or update:

- Decision ID and selected option.
- Reason and meaningful alternatives considered.
- Product owner or technical owner.
- Resolution date.
- Consequences for scope, deployment, milestones, and acceptance checks.

Do not mark a decision accepted because its recommendation was documented. Update the roadmap when decisions change; unresolved scope must not appear as completed implementation.

## Technology admission rule

The owner approved complete private-test judging and phased performance work on 2026-10-07. [ADR 0012](adr/0012-complete-private-test-judging.md) supersedes the first-failure execution policy for new results; [the three-phase plan](judging-improvement-plan.md) records implementation slices and acceptance evidence. This does not approve M8, a broker, public deployment or multiple worker hosts.

Before adding Redis, a broker, Kubernetes, another service, or a caching layer, record:

1. The observed problem and supporting measurement or operational evidence.
2. Why the present solution cannot reasonably solve it.
3. What the proposed component solves and the new failure modes it introduces.
4. Deployment, monitoring, recovery, and maintenance cost.
5. A validation plan and, where practical, a rollback path.
