# Minos project design

Date: 2026-09-13\
Status: M0/M1/M2 complete; M3 awaits approval; D01/D02/D03/D10 baseline accepted

Related documents: [M0 closure](m0-closure.md), [Decision register](decisions.md), [Implementation roadmap](implementation-plan.md)

## 1. Purpose and product definition

Minos lets users discover programming problems, submit solutions, and receive trustworthy evaluation results. Competition features will follow once the core judge is reliable.

The platform manages two connected responsibilities:

1. Accounts, problem publication, submissions, and user-facing results.
2. Safe evaluation of untrusted programs under a reproducible judging configuration.

An official submission evaluates a particular source against a particular published problem version, toolchain, checker, and resource policy. A custom-input execution runs a program for feedback without creating an official problem result.

The first release should demonstrate that a solver can obtain a correct verdict and that interrupted work recovers predictably. A large feature catalog is not the first success criterion.

## 2. Confirmed direction and working assumptions

### Confirmed by the project brief

- Backend first: C#, ASP.NET Core, PostgreSQL, EF Core.
- Modular monolith, with Clean Architecture and selective DDD.
- Submitted programs must never execute directly inside the API or Worker host process.
- A judge boundary that supports later independent deployment.
- C++ and standard input/output problems first.
- Contests, ratings, additional languages, and advanced judging follow the core judge.
- No Redis, Kafka, Kubernetes, or microservices without a demonstrated need.
- Design and milestone planning precede implementation.

### Accepted D01: development access and future authentication

- Development is developer-only; there are no real users or private-pilot cohort.
- No enrollment management, normal-user administrator approval, or invitation workflow is planned.
- The intended finished product supports self-service email/password registration and Google sign-in as its first third-party provider.
- Normal users do not need administrator approval.
- Development access restrictions do not remove or change the intended registration behavior.
- A pilot may be planned only if the project owner explicitly chooses one later.
- D11 will resolve identity integration, sessions/tokens, verification, recovery, external-account linking, and provider setup; it will not reopen D01's accepted product behavior.

See [ADR 0001](adr/0001-initial-audience-and-access-policy.md).

### Accepted D02: development execution boundary

- The repository remains on Windows D:, and the trusted Worker runs in WSL2 Ubuntu.
- Docker is the first backend for isolated compilation and execution.
- Submission files are copied/materialized into private Linux-native workspaces; contestant programs do not see the Windows drive, repository, secrets, other tests, or Docker management interfaces.
- The application execution contract returns backend-independent evidence; Submission/Judging rules and durable job lifecycle do not depend on Docker.
- A future Isolate adapter runs directly on Linux, not nested inside the Docker runner.
- A dedicated development VM is not required. Production host selection and Isolate adoption remain deferred.
- M1 must validate the selected configuration; acceptance of the architecture does not claim sandbox correctness.

See [ADR 0002](adr/0002-judge-development-environment.md).

### Accepted D03: official judging first

Official judging is part of the first end-to-end MVP. Custom-input Run is deferred until reliable official judging is complete; it does not block M1 or the official-judging release. See [ADR 0003](adr/0003-official-judging-mvp.md).

### Accepted D10 minimum safety baseline

- Submitted code executes only through the isolated backend, never directly in API/Worker hosts.
- Network access is disabled by default.
- Wall time, CPU usage, memory, processes/threads, stdout/stderr, and writable scratch space are bounded.
- Execution is non-root, with unnecessary capabilities removed.
- The runner reports structured exit code, timeout, memory/resource failure, output limit, and infrastructure failure evidence.
- Compilation is also isolated and bounded under D02.
- Exact numeric limits are not fixed in M0; M1 validates/tunes bounded test values.
- Final contestant verdict precedence and resource-accounting semantics remain M6 decisions.

See [ADR 0004](adr/0004-minimum-execution-safety-baseline.md). This baseline does not approve the later candidate verdict/accounting rules below.

### Lean planning boundary

All four before-M1 planning gates (D01, D02, D03, D10 baseline) are accepted and M0 is closed. Resolve domain, database, authentication, storage, and client details in their dependent milestones. Keep formal ADRs for material correctness, security, or evolution decisions; ordinary implementation choices use milestone notes.

### Proposed assumptions, awaiting discussion

- The completed MVP is validated by developer-run workflows and automated tests using a curated problem collection and multiple test identities.
- Visitors may browse published problems; submissions require an account.
- One pinned C++ compiler, language standard, and built-in output checker are sufficient initially.
- Polling provides initial result updates.
- Source code and ordinary result metadata fit in PostgreSQL under explicit size limits; large test files live in private artifact storage.

None of these assumptions is a silently accepted product decision. See the decision register before implementation.

## 3. Actors and permissions

| Actor | Responsibilities | Initial access boundary |
| --- | --- | --- |
| Visitor | Browse published problems | No drafts, hidden tests, or other users' private source |
| Solver | Submit code, inspect personal history, manage profile | Own submissions and executions |
| Problem setter | Author drafts, upload tests, publish authorized problems | Owned or explicitly assigned problems |
| Administrator | Grant permissions, disable accounts, manage content | Explicit privileged operations |
| Operator | Inspect worker health, investigate system failures, recover jobs | Restricted operational access |
| Judge Worker | Claim jobs, coordinate execution, persist outcomes | Narrow Evaluation and artifact permissions |

Setter and administrator permissions belong to accounts; they do not require different account entities. Administrator and operator can initially be the same person while retaining separate responsibilities.

These are intended product roles. During development, the developer and automated tests exercise them using separate test identities. There is no real-user cohort and no normal-user enrollment approval responsibility.

Reading private source or judging artifacts through privileged tools should be authorized and auditable. Setters do not automatically receive access to every solver's source code.

## 4. Product requirements

| ID | Requirement |
| --- | --- |
| R01 | Normal users can self-register with email/password or sign in with Google, without administrator approval; they can sign out and manage a minimal profile. Development tests exercise these flows. |
| R02 | A permitted setter can create and edit a problem draft. |
| R03 | A problem contains a statement, samples, limits, checker configuration, and ordered private tests. |
| R04 | Publication produces an immutable problem version; later judging changes produce another version. |
| R05 | Solvers can list published problems and inspect their public details. |
| R06 | An authenticated solver can submit supported source within configured size and admission limits. |
| R07 | The submission and its durable judge job are recorded atomically before the API acknowledges creation. |
| R08 | Compilation and execution occur outside the API in controlled isolation. |
| R09 | A submission completes with a contestant verdict or reaches an explicit platform-failure state. |
| R10 | The owner can inspect submission status, verdict, bounded diagnostics, and defined resource measurements. |
| R11 | Worker interruption does not permanently lose work; stale attempts cannot replace a newer result. |
| R12 | Hidden test data, expected answers, and hidden-test output remain private. |
| R13 | Operators can detect unhealthy workers and exhausted retries and perform controlled recovery. |
| R14 | Custom-input execution follows reliable official judging under accepted D03; it can reuse the runner but does not affect official history or solving credit. |

Email/password self-registration, Google sign-in, and the absence of normal-user approval are accepted in D01. Email verification, password recovery, and external-account linking are resolved before M3 authentication; intended visibility/retention rules are resolved before their relevant work. Public network deployment is separate from implementing the intended authentication behavior.

## 5. MVP boundary

### Core MVP product scope, validated in developer-only development

- Self-service email/password registration, Google sign-in, minimal profiles, and solver/setter/admin permissions. Normal registration requires no administrator approval.
- Problem drafts, statements, samples, limits, testcase upload, and versioned publication.
- Problem list and detail; no rich authoring editor is required.
- One pinned C++ toolchain and standard input/output execution.
- Official submissions, durable asynchronous judging, results, and personal history.
- Accepted, Wrong Answer, Time Limit Exceeded, Memory Limit Exceeded, Runtime Error, and Compilation Error.
- Proposed additional verdict: Output Limit Exceeded.
- An explicit platform-failure status distinct from contestant verdicts.
- Crash recovery, quotas, bounded concurrency, private artifacts, operational visibility, and backup/restore procedures.

The development problem collection must be checked with known solutions in the sandbox. Reference-solution validation is recommended; whether it is an automated publication gate or a controlled operator checklist is an open decision.

### Next increment

Custom-input execution, with separate admission limits, short retention, bounded output, and a scheduling policy that protects official judging capacity.

### Deferred scope

Contests, ratings, standings, discussions, editorials, plagiarism detection, multiple languages, interactive problems, custom checkers, grouped tests, partial scoring, multiple judge hosts, and real-time push updates.

Enrollment management and approval workflows are outside the current scope. Any pilot and its access requirements need an explicit later product decision. Public hosting and real-user capacity targets remain deferred; select bounded smoke-test targets during M1 and synthetic capacity targets before M7.

### MVP completion definition

A developer-controlled solver identity can complete the intended registration/sign-in flows, solve a curated published problem, and inspect a correct result. API and worker restarts recover pending work. Adversarial programs remain within the validated execution policy. Hidden data stays private. Developer-run and automated operational checks can investigate failures and restore durable data and artifacts.

MVP completion does not require a real-user pilot. Public deployment requires a separate readiness review; developer-only validation does not establish public-service capacity or security.

## 6. Core workflows

### Authoring and publication

1. An authorized setter creates a draft and supplies statement, samples, limits, and tests.
2. Uploads are validated for size, filenames, path safety, and package structure; upload processing never runs supplied code in the API.
3. Artifact files are staged and verified before the database commits references to a published version.
4. Publication validates the required configuration and records immutable artifact references and checksums.
5. Subsequent changes create a new version. Existing submissions retain their original references.

Storage writes and PostgreSQL commits do not share a transaction. Use staging, verification, and cleanup of abandoned uploads rather than pretending they are atomic.

### Official submission

1. Authorize the solver and validate language, source size, problem availability, and admission limits.
2. Select and pin the published problem version and execution configuration.
3. Create the submission and queue row in one PostgreSQL transaction.
4. Return its identifier and status immediately; do not wait for execution.
5. A worker claims a lease and creates a judging attempt.
6. The worker compiles inside isolation, runs required tests, and checks output outside the contestant sandbox.
7. Evaluation rules calculate the outcome; guarded completion commits the result and finishes the job atomically.
8. The client retrieves the durable result through the API.

### Recovery

A worker renews its lease while working. An expired lease allows another attempt. Completion must match the currently authorized attempt and lease token. Failed infrastructure attempts retry with bounded backoff; exhausted retries produce a visible platform failure.

### Deferred custom execution after reliable official judging

Create an ExecutionRequest with source, custom input, configuration, and expiry. Run through the same isolation adapter. Return bounded output and execution diagnostics. Do not manufacture Accepted/Wrong Answer without an official checking specification.

## 7. Bounded contexts and ownership

| Context | Owns | Why |
| --- | --- | --- |
| Accounts | Authentication integration, profiles, account status, permissions | These share account identity and straightforward access rules initially. |
| Problems | Drafts, publication, versions, tests, limits, checker settings | Test data and publication must agree on the specification being published. |
| Evaluation | Submissions, execution requests, attempts, judging lifecycle, verdicts, results | These share evaluation rules and recovery semantics. |
| Competition, later | Contests, participation, assigned problem versions, scoring, freezes, standings | Contest rules differ from ordinary practice evaluation. |
| Ratings, tentative | Rating calculations and history | Extraction depends on independent rules, lifecycle, and operational needs. |

Testcases are not an initial separate context. Judging is an internal Evaluation responsibility with a separately hosted runtime. Standings initially form a Competition projection. Queues, storage, and sandbox integration are infrastructure capabilities.

Bounded contexts define model ownership. They do not imply one project, service, or database per context.

## 8. Candidate domain model

These candidates guide later aggregate design; they are not a final class hierarchy.

| Concept | Candidate role | Important rule |
| --- | --- | --- |
| Account/Profile | Simple identity-linked model | Credentials use a supported identity implementation; profile edits cannot grant privileges. |
| Problem | Aggregate candidate for problem identity and authoring/publication controls | Only authorized setters edit drafts or publish. |
| ProblemVersion | Immutable published model; aggregate boundary to resolve | Statement, limits, tests, and checker references cannot change underneath submissions. |
| TestCase | Version-owned metadata | Stable order, private artifact references, verified content. |
| Submission | Aggregate candidate | Source and pinned judging specification do not change after acceptance. |
| JudgingAttempt | Separately persisted execution record | Only the current authorized attempt can finalize the submission. |
| SubmissionResult | Result record/value candidate | Verdict and summary derive from the accepted attempt. |
| TestCaseResult | Attempt-owned result record | A test failure remains distinct from an infrastructure fault. |
| ExecutionRequest | Separate request model for custom runs | No official acceptance or solving credit. |
| TimeLimit/MemoryLimit | Validated values with explicit units | Positive supported values; measurement semantics are documented. |
| Verdict | Finite enum | It represents contestant outcomes, not queue state. |
| Toolchain/CheckerConfiguration | Versioned configuration | Reproducible identities and fixed permitted options; no client-controlled commands. |

Do not load all testcase files or every result into an aggregate. Enforce invariants through bounded metadata, domain operations, transactions, and database constraints.

DDD is useful for publication, submission transitions, attempt ownership, and future contest scoring. Profiles and catalog reads can use simple application logic and DTO projections. Avoid generic repositories, mandatory mediator pipelines, event sourcing, or a shared domain framework without a concrete requirement.

Potential domain events include ProblemPublished and SubmissionCompleted. Introduce them when a consumer exists; in-process events do not imply an external broker.

## 9. Judging lifecycle

### Submission status, proposed

```text
Pending -- current attempt starts --> Judging
Judging -- contestant outcome ------> Completed + verdict
Judging -- retryable system fault ---> Pending, with failed attempt retained
Pending/Judging -- retries exhausted -> SystemFailed, with no contestant verdict
```

Compiling and Running can be attempt phases shown to clients without becoming permanent submission states. Queue scheduling and leases belong to jobs; execution diagnostics belong to attempts. Do not collapse all three lifecycles into one enum.

### Outcome policy, proposed

- Compilation Error: compilation fails for a contestant reason under the supported toolchain.
- Accepted: all required tests pass.
- Wrong Answer: completed execution fails the built-in comparison policy.
- Time/Memory/Output Limit Exceeded: the corresponding enforced limit is reached, using authoritative runner evidence.
- Runtime Error: abnormal program termination that is not already explained by a more specific limit outcome.
- SystemFailed: infrastructure cannot produce a trustworthy outcome after its retry policy.

For the first release, sequential tests with first-failure stopping are proposed under D16. Store results for executed tests and represent remaining tests as not executed. Resolve overlapping limit/termination precedence before integrating official judging in M6.

Automatic interrupted-job recovery belongs to D07 and remains necessary. Manual rejudging/API workflows are deferred under D18. Preserve attempt evidence now; if rejudging is added, it must use explicit authorized attempts and a defined version policy. A completed result is not changed by an arbitrary late worker callback.

## 10. High-level architecture and worker boundary

```text
Browser / future frontend
          |
          | HTTPS; polling initially
          v
+--------------------------------------------------+
| Main ASP.NET Core backend                        |
| HTTP: authentication, authorization, endpoints    |
| Application: validation, use cases, orchestration |
| Domain: Accounts | Problems | Evaluation          |
| Infrastructure: EF Core, jobs, artifact storage   |
+----------------------+---------------------------+
                       |
             +---------+-----------+
             |                     |
             v                     v
     +----------------+   +----------------------+
     | PostgreSQL     |   | Private artifact     |
     | Business data  |   | storage              |
     | Durable jobs   |   | Immutable test files |
     | Attempts       |   | Inputs and answers   |
     | Results        |   +----------+-----------+
     +-------+--------+              |
             ^                       |
             | leases and guarded    | trusted access
             | result commits        |
     +-------+-----------------------+-------------+
     | WSL2 Ubuntu Worker / Docker execution        |
     | Trusted Judge Worker and trusted checker    |
     |                  |                          |
     |                  v                          |
     | Isolated compilation / execution            |
     | No credentials or expected answers          |
     | Bounded resources and filesystem access     |
     +---------------------------------------------+
```

The modular monolith describes business ownership. The Worker is a separate executable in WSL2 Ubuntu and invokes a Docker execution adapter. Initially API and Worker can share Evaluation application/domain code and one coordinated release. Runner workspaces are private Linux-native copies, despite the repository staying on Windows D:. Future physical hosting is not selected.

| Boundary | Responsibility |
| --- | --- |
| HTTP/API | Request translation, transport contracts, identity adapter/composition, safe ProblemDetails |
| Application | Capability/use-case orchestration, resource authorization, persistence/external-service ports; depends only on Domain |
| Domain | Publication invariants, legal transitions, verdict aggregation, explicit business policies |
| Infrastructure | PostgreSQL/EF Core, job claims, artifact storage, runner adapters, identity integration |
| Worker host | Independent Linux composition/startup; per-job scope and cancellation when jobs arrive; no dependency on Api |
| Sandbox adapter | Fixed execution configuration, isolation, measurement, cleanup, structured execution evidence |
| Trusted checker | Output comparison outside the contestant environment |

Module contracts use identifiers and explicit DTOs rather than tracked EF entities. The worker gets only necessary permissions. Contestant processes inherit no worker secrets or database/storage credentials.

An external result HTTP service is unnecessary initially. The worker can invoke shared Evaluation completion use cases through its own infrastructure adapter. A future extraction can replace job/result transport without moving business rules into controllers.

## 11. Conceptual persistence and storage

Use one PostgreSQL database initially, with clear table ownership. M2 places drafts in `problems` and EF migration history in `public`; the migrator owns these tables and runtime grants are explicit. Remaining module tables are introduced with their workflows, without automatic access grants.

| Owner | Conceptual records |
| --- | --- |
| Accounts | Identity-managed records, profile, permission assignments |
| Problems | Problem identity, draft metadata, published versions, version-owned testcase metadata |
| Evaluation | Submissions, judge jobs, attempts, final result summaries, per-test results |
| Evaluation, later | Custom execution requests and their bounded results |

Submissions reference account ID, problem/version ID, toolchain/checker identity, immutable source, and admission time. Attempts retain ownership token, timing, diagnostics, and outcome evidence. Jobs retain scheduling, lease, and retry metadata.

Useful constraints include unique account handles, unique version numbers within a problem, unique testcase order within a version, and unique attempt numbers within a submission. Indexes must support problem listing, owner submission history, and eligible job claims; choose exact indexes from the implemented queries.

Cross-context identifiers may use database foreign keys in the shared database. Avoid cross-context EF navigation graphs and cascades that erase historical submissions. Account deactivation and problem archival need explicit history-preserving rules.

Keep large test contents outside ordinary relational rows. The initial artifact adapter may use a private filesystem if deployment supports it; a remote worker needs a deliberate transfer/shared-storage solution. Artifact references use server-controlled keys, not arbitrary user paths. Database backups alone cannot restore file-backed tests.

Retention policies must cover source, compiler diagnostics, test results, scratch files, and future custom executions. Establish smoke-test bounds in M1, product-specific limits with their workflows, and retention durations before M7 rather than inventing capacity figures during M0.

## 12. Initial Clean Architecture structure

This layout is now implemented in [Minos.sln](../Minos.sln) after M1 passed. Only the Problems draft slice has application behavior so far; other modules remain scheduled for their milestones.

```text
Minos/
  docs/
  src/
    Minos.Domain/           module folders: Accounts, Problems, Evaluation
    Minos.Application/      module use cases, DTOs, validation, ports
    Minos.Infrastructure/   EF Core, identity, queue, storage, runner adapters
    Minos.Api/              HTTP host and endpoint contracts
    Minos.JudgeWorker/      separately hosted worker entry point
  tests/
    Minos.Domain.Tests/
    Minos.IntegrationTests/
    Minos.JudgeTests/
```

Domain has no dependency on EF Core, ASP.NET Core, or process-launch APIs. Application depends on Domain. Infrastructure implements application ports. API and Worker compose dependencies; domain rules do not belong in their entry points.

Start with module folders inside the layer projects. Separate module assemblies only if enforcement or team ownership warrants them. Reuse application use cases and small boundary contracts; a speculative plugin system or universal shared kernel is unnecessary.

## 13. Candidate API surface

These resource shapes are proposed; schemas, status codes, pagination, and authorization contracts must be finalized in the implementation milestones.

| Workflow | Candidate endpoints |
| --- | --- |
| Account lifecycle | POST /api/accounts/register; POST /api/auth/login; POST /api/auth/logout |
| Profile | GET /api/me; PATCH /api/me |
| Problem browsing | GET /api/problems; GET /api/problems/{problemId} |
| Problem authoring | POST /api/authoring/problems; PATCH /api/authoring/problems/{problemId}/draft |
| Test management | POST /api/authoring/problems/{problemId}/draft/tests; DELETE /api/authoring/problems/{problemId}/draft/tests/{testId} |
| Publication | POST /api/authoring/problems/{problemId}/versions |
| Official submission | POST /api/problems/{problemId}/submissions |
| Submission status/history | GET /api/submissions/{submissionId}; GET /api/me/submissions |
| Manual rejudge, deferred D18 | POST /api/operations/submissions/{submissionId}/rejudge, only if later included |
| Custom execution, later | POST /api/executions; GET /api/executions/{executionId} |

The create-submission response returns an identifier and result location after persistence. Resource creation does not imply judging has completed. Result endpoints expose sanitized, authorized projections rather than raw internal records or private artifacts.

## 14. Durable job design

Proposed initial queue: a PostgreSQL table behind a small Evaluation job interface.

- Commit the submission and job in one transaction.
- Claim eligible work using a short transaction, record the lease/attempt token, and release locks before execution.
- Renew leases for active work and recover expired leases.
- Accept completion only from the current authorized attempt, even if an old worker finishes later.
- Commit accepted results and job completion atomically.
- Retry infrastructure faults with bounded backoff; do not automatically retry ordinary contestant failures.
- Bound worker concurrency independently from HTTP request concurrency.
- Expect at-least-once execution and design completion to be idempotent.
- Keep operational retry/rejudge actions authorized and auditable.

PostgreSQL documents SKIP LOCKED as useful for queue-like consumers. It is not a substitute for leases and recovery. [PostgreSQL SELECT documentation](https://www.postgresql.org/docs/current/sql-select.html)

No outbox is required solely to insert a job into the same database transaction. If jobs later move to an external broker, an outbox or equivalent durable handoff becomes necessary. Introduce a broker only after measured database contention, routing requirements, or delivery features justify it.

## 15. Sandboxed execution policy

Docker is the accepted first compile/run backend in WSL2 Ubuntu. Validate its actual resource, filesystem, and cleanup behavior using the M1 checks in ADR 0002. A future Isolate adapter remains possible behind the same execution contract and would run directly on Linux rather than inside the Docker runner; adopting or validating Isolate is not required for M1.

The adapter must enforce and demonstrate:

- Isolation for compilation as well as execution.
- No network access or worker/application secrets.
- No expected-answer files, other testcase files, host mounts, or container-management sockets in the contestant environment.
- Fixed server-selected compiler arguments and executable paths; no user-controlled shell command construction.
- Bounded wall time and CPU usage, memory, processes/threads, stdout/stderr, and aggregate writable scratch use. Numeric test bounds are tuned in M1; official resource-accounting semantics remain M6 work.
- Non-root contestant execution with unnecessary capabilities removed.
- Bounded compilation time, memory, diagnostics, and generated artifacts.
- Fresh writable scratch space and complete process-tree cleanup after every execution.
- Structured evidence for exits, signals, resource measurements, and limit events.
- A trusted checker outside the contestant sandbox, processing bounded output.

Docker is useful for reproducible environments, but its default configuration supplies no resource limits. Container deployment must not be treated as evidence that the above policy is enforced. [Docker resource constraints](https://docs.docker.com/engine/containers/resource_constraints/), [Docker security](https://docs.docker.com/engine/security/)

The actual WSL2/Docker configuration must support the required controls. Windows repository access by the developer does not imply contestant access: stage only submission files into a private Linux-native workspace. Backend-specific validation cannot be replaced by Windows-only unit tests.

## 16. Observability, failure handling, and testing

### Observability

Use structured logs with request ID, submission ID, job ID, attempt ID, and worker ID. Record state changes and failure categories. Do not log passwords, tokens, full source, private testcase contents, or unbounded program output.

Track queue depth, oldest eligible job age, claim latency, active attempts, end-to-end judging latency, retry counts, platform-failure counts, compilation duration, verdict distribution, and scratch cleanup failures. Start with existing .NET logging and simple metric export; choose a monitoring backend when deployment needs are clear.

Separate API liveness from readiness. Worker health must report execution capability and recent progress. An empty queue is not evidence of an unhealthy worker.

### Failure categories

| Category | Behavior |
| --- | --- |
| Invalid request or missing permission | Reject before creating work; return a consistent HTTP error contract |
| Queue/persistence failure | Do not acknowledge a submission that has not committed |
| Contestant compile/run/check outcome | Complete with the corresponding verdict |
| Worker/storage/runner infrastructure fault | Retain diagnostic evidence; retry if appropriate; eventually expose SystemFailed |
| Unexpected HTTP failure | Return sanitized error details with a correlation identifier |

### Testing strategy

- Unit-test publication invariants, state transitions, verdict precedence, and stale-attempt rejection.
- Use real PostgreSQL integration tests for atomic enqueueing, concurrent claims, leases, migrations, and result commits; an in-memory provider cannot establish those behaviors.
- Test authentication, ownership, setter permissions, and hidden-data exposure through the API.
- Exercise the real Linux sandbox with known verdict programs and adversarial resource/escape attempts in the controlled judge environment.
- Test crash points around claims, execution, lease renewal, and result persistence.
- Run one end-to-end problem-publication-to-verdict workflow and verify durable history.
- Test backup/restore for PostgreSQL and artifacts together, then run a restored problem through judging.
- Use synthetic/developer-run workloads established before M7 to validate queue stability and resource usage without requiring real users; M1 needs only bounded execution smoke tests.

Passing a finite malicious-program suite is evidence for the tested policy, not a proof of universal sandbox security. Public deployment needs an additional threat and operational review.

## 17. Evolution rules

| Potential addition | Introduce when | What it addresses |
| --- | --- | --- |
| Object storage | Local artifacts cannot satisfy worker access, durability, or capacity needs | Shared durable artifact access |
| External message broker | Measured database queue load or delivery/routing needs exceed the current design | Independent transport and richer scheduling/delivery capabilities |
| SignalR | User experience or measured polling traffic warrants push updates | Timely client notification; database results remain authoritative |
| Redis/cache | A measured read bottleneck benefits from a defined cache/expiry policy | Reduced repeated reads; not authoritative submission state |
| Additional worker hosts | One validated host cannot meet measured throughput requirements | Horizontal execution capacity and resilience |
| Separate Evaluation service | Deployment, scaling, ownership, or reliability needs justify extraction | Independent operation behind existing contracts |

Each proposal must document the observed problem, why the current approach is insufficient, the new component's benefit, and its operating cost.

## 18. Next step

M0, M1 and M2 are complete. The five-project solution has a development-only problem-draft use case, entity invariants, use-case access checks, focused EF persistence, migrations, least-privilege operational identities, private-root validation and separate API/idle Worker hosts. [AGENTS.md](../AGENTS.md) records the enforced layer rules; [M2 evidence](milestones/m2-foundation.md) records validation. The execution spike remains archived separately. M3 introduces accounts/authorization only after approval; publication and durable judging remain in their dependent milestones.
