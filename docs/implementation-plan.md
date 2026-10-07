# Minos implementation roadmap

Date: 2026-09-13\
Status: M0–M6 complete; M7 backend complete and verified; development MVP owner acceptance pending; M8 not started

Related documents: [M0 closure](m0-closure.md), [Project design](project-design.md), [Decision register](decisions.md)

## Planning rules

- This is an ordered milestone plan, not a commitment to calendar dates.
- M0 approval precedes implementation, including the sandbox spike.
- Only the before-M1 gates in the decision register block M1. Resolve other choices before their dependent work; do not require 18 ADR approvals or formalize routine implementation details.
- Each milestone has dependencies, concrete deliverables, and observable exit criteria.
- A milestone is complete only when its required checks pass and evidence is recorded.
- Test doubles establish contracts; they do not establish real sandbox or PostgreSQL correctness.
- Linux-specific execution checks run on the intended class of judge host.
- Development is developer-only, with no real users or private-pilot cohort. Validate the completed MVP with developer-run and automated acceptance checks.
- Implement the intended self-service email/password and Google authentication behavior; developer-only access does not imply an enrollment approval workflow.
- Any pilot or public hosting requires an explicit later decision and is not a prerequisite for completing the development MVP.
- Estimates follow M0/M1, when scope and deployment feasibility are known.

## Milestone sequence

```text
M0 Requirements and architecture baseline
 |
 v
M1 Secure execution feasibility
 |
 v
M2 Backend foundation and persistence
 |
 v
M3 Accounts and authorization
 |
 v
M4 Problem authoring, tests, and publication
 |
 v
M5 Submissions and durable jobs
 |
 v
M6 Complete official judging workflow
 |
 v
M7 Reliability and developer acceptance readiness
 |
 +--> Completed development MVP
 |
 v
M8 Custom-input execution, proposed next increment
 |
 v
Future: competitions, additional languages, and measured scaling
```

The sequence prioritizes sandbox feasibility because a failed execution environment invalidates the central product. The remaining milestones build a complete vertical workflow before adding features.

## Milestone overview

| Milestone | Outcome | Dependency | Current status |
| --- | --- | --- | --- |
| M0 | Agreed MVP scope, execution boundary, and minimum safety contract | None | Complete: D01/D02/D03/D10 baseline accepted; closure documented |
| M1 | Verified Linux compile/run/check feasibility | M0 | Complete: executable resource/isolation/recovery suite passed; findings recorded |
| M2 | Buildable architecture, two hosts, real PostgreSQL persistence, CI foundation | M1 | Complete: executable Release pipeline and operational boundaries validated locally |
| M3 | Working account lifecycle and permission boundaries | M2 | Complete for the approved development scope; live Google smoke check remains deferred |
| M4 | Versioned problems and private tests | M3 | Complete: immutable versions, private tests, permissions and publication pipeline validated |
| M5 | Durable submission admission and recoverable job ownership | M4 | Complete: atomic admission, leases, retries, authorization and recovery validated |
| M6 | Correct real submission-to-verdict execution | M1, M5 | Complete |
| M7 | Demonstrated reliability and developer acceptance readiness | M6 | Backend complete and verified; owner MVP acceptance pending |
| M8 | Bounded custom-input execution | Reliable official judging and M7 acceptance | Deferred scope accepted in D03; implementation not started |

## M0 — Requirements and architecture baseline

**Goal:** Agree on the scope and execution/safety baseline needed to begin the controlled M1 spike. Keep the planning process appropriate to one developer.

### Deliverables

- Concise decision log, accepted development access and WSL2/Docker topology, MVP boundary, assumptions, risks, deferred work, and M1 dependencies.
- D03 official-judging/custom-Run release boundary and D10 minimum safety/evidence contract.
- Clear M1 pass/fail checks for private Linux-native workspaces, isolated bounded compile/run, cleanup, and backend-independent evidence.
- Milestone deadlines for later domain, persistence, authentication, storage, and result policies rather than premature approval.
- Numeric smoke-test limits/toolchain details chosen and recorded during M1; broader synthetic workload/service targets established before M7. No real-user cohort forecast is required.

### Exit criteria

- D01, D02, D03, and the D10 minimum safety contract are resolved sufficiently for M1.
- No unresolved choice prevents the controlled WSL2/Docker feasibility checks.
- Later choices have relevant milestone deadlines and do not silently become missing release requirements.
- Accepted assumptions, MVP boundary, risks, deferred decisions, and M1 dependencies agree across the documents.
- The project owner accepts the baseline and requests M0 closure. M1 start is a separate next action.

**Current evidence:** D01, D02, D03 and D10 baseline were accepted at M0 closure. M1 subsequently passed the executable development validation suite, with numeric bounds and limitations recorded. M2 began afterward. Final verdict/accounting remains M6 work.

## M1 — Secure execution feasibility

**Goal:** Prove the accepted WSL2 Ubuntu plus Docker compile/run backend, private Linux-native workspaces, and replaceable execution contract before investing in the full backend.

### Deliverables

- A bounded Docker execution spike from WSL2 Ubuntu, following the required checks in ADR 0002. Do not install Isolate or require a dedicated VM.
- A pinned C++ toolchain and fixed server-controlled compilation configuration.
- Defined backend-independent runner input/output evidence contract, smoke-test measurement collection, and observed limitations. Final product verdict/accounting semantics remain M6 work.
- Demonstrated isolated compilation, isolated execution, and trusted output checking.
- Recorded WSL/kernel/Docker configuration, actual resource support, contestant permissions, private Linux workspace staging, and cleanup behavior.
- A decision to proceed with the adapter or revise the execution approach.

### Exit criteria

- Known programs demonstrate success, compile failure, runtime failure, timeout, excessive memory, and excessive output with interpretable evidence.
- A resource-heavy compilation is bounded and cleaned up.
- Network attempts, filesystem probes, process/thread exhaustion, and child-process persistence are blocked or contained by the documented policy.
- Contestant processes cannot see secrets, expected answers, unrelated test files, Windows D:, the Minos repository, or Docker/host management interfaces.
- Repeated executions leave no surviving contestant processes or uncontrolled scratch growth.
- The selected smoke-test resource/evidence contract can actually be enforced on WSL2/Docker; final product verdict precedence is resolved in M6.
- Wall/CPU usage, memory, processes/threads, stdout/stderr, writable scratch, non-root execution, and reduced capabilities meet D10's baseline with bounded test values recorded during M1.
- Adapter evidence and orchestration remain independent of Docker-specific details; a test adapter demonstrates replaceability without implementing Isolate.

**Boundary:** A controlled development spike only. Production judge-host selection, public hosting, and Isolate adoption remain deferred. Exact backend setup choices are milestone notes, not separate architectural approvals.

**Progress:** [M1 evidence](milestones/m1-execution-feasibility.md). The executable suite passed private staging, isolated C++ compilation/run/checking, resource/output exhaustion, forbidden-access probes, child persistence, adapter replacement, cancellation, infrastructure-fault classification and controller-crash recovery. M1 is complete; final verdict/accounting remains M6. M2 started after these checks passed.

## M2 — Backend foundation and persistence

**Goal:** Establish the agreed layer boundaries, API host, worker host, and real database workflow.

### Deliverables

- Supported .NET LTS SDK and compatible EF Core/PostgreSQL provider pinned after checking current support.
- Domain, Application, Infrastructure, API, and Judge Worker projects using the approved module organization.
- API and Worker dependency composition, configuration validation, and separate operational identities.
- PostgreSQL setup, initial migrations, and private artifact configuration.
- Structured logging, health/readiness endpoints, and a consistent HTTP error contract.
- CI build/unit/integration foundation; documented local startup, including the Linux execution requirement.

### Exit criteria

- A clean checkout can build and start both hosts using documented configuration without committed secrets.
- Migrations create a fresh real PostgreSQL database reproducibly.
- Invalid required configuration fails clearly at startup.
- Domain has no ASP.NET Core, EF Core, or process-launch dependency; hosts do not contain business rules.
- CI establishes build and relevant database integration checks.

**Boundary:** Infrastructure foundation only. Starting the Worker does not yet expose user-code execution.

**Progress:** M2 complete, 2026-09-14. [Executable results](milestones/m2-foundation.md) cover clean Release builds, fresh/repeat migrations, explicit operational privileges, private storage, Application/HTTP/architecture tests, both hosts and dependency recovery. M3 was subsequently approved and implemented.

## M3 — Accounts and authorization

**Goal:** Establish identity and ownership before exposing authoring and submission workflows.

### Deliverables

- Supported Identity integration and protected same-origin web cookies selected during M3 in [ADR 0006](adr/0006-account-authentication-and-linking.md).
- Self-service email/password registration, login, logout, Google sign-in, minimal profile and the M3-selected account lifecycle. Normal users require no administrator approval.
- Active-account identity and resource ownership enforced inside Application; complete Setter/Admin policies and privileged provisioning belong to M4 under the approved M3 scope.
- Sign-in abuse limits and session/token invalidation behavior appropriate to the intended product, validated during developer-only development.

### Exit criteria

- A solver can complete the agreed account lifecycle and access their profile.
- Invalid credentials and duplicate identity fields produce controlled errors without exposing secrets.
- Profile mutation cannot grant privileges.
- API/Application tests demonstrate unauthenticated/disabled rejection, safe profile mutation and ownership enforcement. Privileged workflow tests accompany M4.
- Disabled-account and logout behavior match the chosen authentication policy.
- Verification/recovery delivery is deferred in ADR 0006 and remains a public-release dependency.
- Self-service email/password and Google sign-in are demonstrated by the developer; external-provider callbacks and account linking follow the D11 policy. No real-user cohort or enrollment approval gate is needed.

**Progress:** [M3 implementation/results](milestones/m3-accounts.md) pass automated validation and M3 is approved. Live Google consent remains a deferred external smoke check because no OAuth client exists.

## M4 — Problem authoring, tests, and publication

**Goal:** Produce stable, private judging specifications that solvers can browse.

### Deliverables

- Problem draft management and setter ownership checks.
- The Setter/Admin permissions and privileged provisioning required by those workflows.
- Statement, samples, approved limits, checker configuration, and ordered test metadata.
- Size-bounded private artifact upload with safe keys, staging, verification, and abandoned-upload cleanup.
- Immutable published versions and history-preserving archival rules.
- Paginated problem list and public problem detail projections.
- The selected reference-validation mechanism or checklist definition. If automatic validation is selected, publication remains gated until its real execution support passes M6.

### Exit criteria

- A permitted setter can create a valid draft and publish under the approved publication policy.
- Missing/inconsistent tests or unsupported configurations prevent publication.
- Published versions cannot be edited in place; new versions retain old artifacts.
- Browsing reveals statements and samples without private test contents or storage paths.
- Cross-owner edits, path traversal, unsafe package entries, and oversized uploads are rejected.
- A failed database/storage operation does not create a published version referencing unverified or missing files.

**Boundary:** Structural publication checks are testable here. Any reference-code execution occurs through the Linux sandbox, never the API process. Automated execution gates are not considered verified until M6.

**Progress:** Complete. [M4 implementation/results](milestones/m4-problems.md) and [ADR 0007](adr/0007-versioned-problems-and-private-tests.md) record the implemented lifecycle and passing evidence. M5 was subsequently approved and implemented.

## M5 — Submissions and durable jobs

**Goal:** Accept official submissions durably and establish correct ownership/recovery before integrating execution.

### Deliverables

- Submission admission, version/toolchain pinning, source limits, and per-account admission controls.
- Atomic submission-plus-job persistence.
- PostgreSQL job eligibility, short claims, leases, renewal, attempt records, retry scheduling, and bounded concurrency.
- Guarded atomic result completion and idempotent handling of duplicate completion.
- Submission status/history endpoints and automatic crash recovery. A manual rejudge API/workflow is deferred under D18.
- A deterministic test executor to exercise lifecycle behavior without executing user source.

### Exit criteria

- A rolled-back transaction leaves neither an acknowledged submission nor a stray job.
- API restart preserves pending jobs and submission history.
- Concurrent claim tests against real PostgreSQL grant only one current lease for a job.
- Expired work can be reclaimed; stale-attempt completion is rejected.
- Duplicate completion does not create duplicate authoritative results or overwrite accepted data.
- Retries are bounded; exhaustion reaches SystemFailed rather than a contestant verdict.
- Owners can retrieve their status/history; unauthorized users cannot inspect private source or results.

**Boundary:** Passing with a test executor verifies scheduling and persistence. It does not count as asynchronous judging completion or sandbox validation.

**Progress:** Complete. [M5 implementation/results](milestones/m5-submissions.md) and [ADR 0008](adr/0008-postgresql-submission-jobs-and-leases.md) record the atomic admission, PostgreSQL ownership/recovery model, owner API and passing executable evidence. M6 subsequently received approval.

## M6 — Complete official judging workflow

**Goal:** Connect the durable lifecycle to the real sandbox and produce trustworthy official results.

### Deliverables

- Worker integration with the verified runner, immutable test artifacts, toolchain, and checker.
- Compile-once workflow with fresh bounded testcase execution and defined first-failure behavior.
- Structured per-test results, authoritative verdict calculation, and documented time/memory summary semantics.
- Bounded compiler diagnostics and public result projections without hidden-test output leakage.
- A curated development problem set verified with known correct and incorrect solutions.
- Automated reference-validation support if M0 selected it; otherwise recorded operator validation evidence.

### Exit criteria

- Real API submission of C++ reaches the expected durable outcome for every supported verdict.
- All required tests must pass for Accepted; checker and overlapping-limit precedence match the approved policy.
- An edited/new problem version does not change a queued submission's test specification.
- A missing/corrupt artifact or runner fault follows the system-failure/retry path rather than assigning Wrong Answer or Runtime Error.
- Historical results show the accepted attempt, pinned configuration, and defined resource measurements.
- API polling reflects committed states and never exposes hidden input, answers, or hidden-test output.
- Required reference validation passes before development problems become usable.

**Milestone demonstration:** Publish one verified problem, register a solver, submit a correct solution and a failing solution, then inspect both results through the API.

**Progress:** Complete. [M6 evidence](milestones/m6-official-judging.md) records the passing foundation suite, all real API verdicts, private result projections, preserved history and long-running renewed leases. [ADR 0009](adr/0009-official-judging-and-evidence.md) records the accepted exact checker, verdict precedence, first-failure order, resource accounting and developer fixture validation. The owner subsequently authorized M7 backend completion.

## M7 — Reliability and developer acceptance readiness

**Goal:** Establish evidence that the complete workflow meets the agreed development-validation workload and operating policies, without requiring real users.

### Deliverables

- Fault-injection coverage for worker termination, lease loss, database interruption, storage errors, and failure around result commits.
- Linux sandbox regression/adversarial checks on the intended deployment profile.
- Agreed-workload performance results and enforced overload/admission behavior.
- Queue/worker/result metrics, meaningful alert conditions, and operational recovery runbooks.
- Backup/restore procedure covering both PostgreSQL and private artifacts.
- Retention, cleanup, secret provisioning, release pipeline, migration, and rollback procedures.
- A documented development MVP acceptance candidate and completion checklist.

### Exit criteria

- Killing a worker during judging recovers work; an abandoned worker cannot later replace the result.
- Database failure does not produce falsely acknowledged admission or falsely completed submissions.
- The agreed workload meets the accepted queue-wait target without uncontrolled resource growth.
- Overload returns the chosen admission response rather than exhausting API or judge resources.
- Operators can detect stale work, repeated platform failures, and cleanup problems without reading secrets in logs.
- Restore produces usable versioned problems and durable submission history; a restored problem can be judged.
- Required CI checks pass; real sandbox checks have recorded Linux-host evidence even if they require a restricted test runner.
- Fresh deployment and recovery procedures are rehearsed; all release-blocking issues are closed.
- The owner accepts the completed development MVP using developer-run and automated evidence. A real-user pilot, public hosting, and production readiness are separate later decisions.

**Progress:** M7 backend implementation and its acceptance evidence are recorded in [M7 results](milestones/m7-reliability.md), with [ADR 0010](adr/0010-development-reliability-and-retention.md) and the [development runbook](operations/development-runbook.md). Owner acceptance of the development MVP remains explicit. M8 requires separate approval.

## M8 — Custom-input execution, proposed next increment

**Goal:** Reuse the reliable runner for feedback without creating official results.

### Deliverables

- ExecutionRequest lifecycle and create/result endpoints.
- Bounded custom input, output, compilation diagnostics, and short retention.
- Separate admission and scheduling policies protecting official judging capacity.
- Clear client semantics: execution outcomes and output, with no official acceptance or solved-problem effect.

### Exit criteria

- A user runs supported source against custom input and retrieves only their bounded result.
- The request creates no official submission, acceptance, rating, or solving credit.
- Excess input/output, infinite loops, and worker interruption obey the same validated isolation/recovery rules.
- Expired results and scratch artifacts are removed according to policy.
- A burst of custom runs cannot violate the agreed official-judging service target.

D03 fixes custom Run after reliable official judging. It is not part of the current M1 spike or the first official-judging release.

## Post-MVP milestones

| Increment | Prerequisite | Required planning before implementation |
| --- | --- | --- |
| Additional languages | Reliable official/custom runner | Toolchain-specific compilation, isolation, accounting, startup overhead, and language-limit policy |
| Contests | Reliable submissions and immutable versions | Scoring model, registration, submission-time semantics, freezes, finalization, and deterministic standings |
| Ratings | Finalized contest results | Rating algorithm, eligibility, recalculation policy, and historical auditability |
| Push updates | Demonstrated user/traffic need | Authorization, reconnection, ordering, and durable-result reconciliation |
| Multiple worker hosts | Measured single-host capacity limitation | Artifact access, lease behavior across hosts, fairness, reproducibility, and host observability |
| Advanced checkers/scoring | Explicit product demand | Checker trust, isolation, result protocol, group semantics, and versioned scoring rules |

These increments have no committed dates or implementation approval.

## Evidence and completion tracking

For each milestone, record its owner, implementation references, test results, relevant host/environment, observed limitations, and completion date. Update the overview status only when the exit criteria pass. A deferred check must remain visible and cannot be counted as completion.

M0–M6 are complete. [M3 evidence](milestones/m3-accounts.md) records accounts/authentication/ownership; live Google validation remains deferred. [M4 evidence](milestones/m4-problems.md) records immutable problem versions and private tests. [M5 evidence](milestones/m5-submissions.md) records durable submission/job ownership and recovery. [M6 evidence](milestones/m6-official-judging.md) records verified official judging. The owner authorized M7 backend completion; [M7 evidence](milestones/m7-reliability.md) records implementation, validation and remaining owner acceptance. A GitHub-hosted CI run for the current revision is pending commit/push. M8 has no implementation approval.
