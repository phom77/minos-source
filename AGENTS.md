# Minos engineering rules

## Scope and workflow

Minos is a single-developer project. Favor working code and meaningful tests; keep documentation focused on current contracts and material decisions. The core backend/reliability implementation is complete. Development-MVP acceptance remains an explicit owner decision. Custom Run/M8, manual rejudge, ratings, distributed execution, brokers and service extraction are not approved by repository cleanup or routine maintenance.

Before edits, inspect code/references and identify the smallest coherent change and its layer. Preserve unrelated working-tree changes. Run relevant build/tests and review the diff for unnecessary abstractions/dependencies. Do not recreate milestone reports, chronological AI work logs, release receipts or test output in source control. Local outputs belong in ignored `artifacts/`; secrets, backups and datasets stay private.

## Project boundaries

- Keep exactly the existing five production projects and two test projects.
- Domain has no project dependencies or EF/web/transport/Docker/SDK types.
- Application depends only on Domain and owns use cases, authorization and persistence/execution ports. Never expose DbContext, IQueryable, HTTP or infrastructure implementations.
- Infrastructure depends on Application/Domain and owns EF, migrations, focused repositories, storage, Identity and execution adapters.
- API and JudgeWorker use Infrastructure only for DI/composition/startup/hosting. Worker never depends on API or registers web authentication.
- Entities protect their invariants and transitions. Avoid ceremonial DDD, generic repositories, MediatR, Redis, outbox or additional layers without evidence.
- Use async I/O, CancellationToken, UTC, structured safe logging and validated startup configuration. Never log credentials, tokens, hidden tests or source unnecessarily. API returns transport DTOs and safe centralized ProblemDetails.
- Keep Windows/Linux restore assets separate through Directory.Build.props; explicit artifacts-path overrides are supported.

## Authorization and data

- Account/profile/disabled/role state is independent from Identity/providers/HTTP. Infrastructure owns credentials and provider/subject mappings; API owns cookies/CSRF/OAuth. Use platform hashing/protected cookies/state/correlation/PKCE. Never silently merge by email.
- Enforce active-account, Setter/Admin and ownership inside Application, including non-HTTP callers. Administrator-only role changes revoke target sessions; first-admin provisioning is trusted maintenance. Normal registration needs no approval. Explicit provider linking requires current-account and provider proof.
- Stable Problem owns editable authoring state and immutable ownership. Published versions are immutable snapshots; archive preserves history/artifacts. Publication verifies prerequisites/artifacts and records manual reference checklist evidence, not automatic reference execution.
- Difficulty/topics are mutable stable-problem metadata with source rating/tags, policy evidence and basis version. New publication flags reassessment without clearing labels. Author changes require ownership/revision checks; public projections omit source evidence/rationale. Worker has no taxonomy/classification privileges.
- Library public numbers are stable positive values; gaps are allowed. Search ANDs a bounded All/Any filter group with per-field exclusion. Any personal-status criterion requires an active account. Progress spans versions; any completed Accepted retains Solved. Public topic totals exclude drafts/archives; personalized reads are not shared-cacheable.
- Practice contests allow only active Setter/Admin organizers. Published schedules/selections freeze and pin versions. Registration is unique/idempotent; active members submit only in the server-authoritative start-inclusive/end-exclusive window. Association/submission/job admission is atomic and shares normal capacity. Public problems remain public; no secret-contest promise or organizer application workflow.
- Comments are independent stable-problem aggregates with immutable viewed-version context. Public unarchived problems share discussion across versions/contests. Authors alone edit/delete; active Admins alone hide. Replies attach only to visible roots, two levels total. Terminal transitions erase body/spoiler and preserve structure. Require bounded cursor reads, revision checks, CSRF, safe Markdown and account write limits. Worker has no comment privileges. Likes, articles, realtime and notifications are separate scope.

## Submission and execution

- Owner comes from the active account. Pin version, supported C++20 toolchain and bounded source permanently. Admit Submission/JudgeJob atomically in PostgreSQL. Default bounds are five active/account and 32 global; global overrides validate 1–1000. Reject safely with 429/retry hint.
- Account-scoped UUID request keys bind exact kind/target/language/source. Persist bounded fingerprints atomically for submission lifetime. Replay preserves the original pin, bypasses capacity without new work and still enforces authorization. Authorized contest replay may occur after end; changed payloads conflict. Worker has no key-table privileges. Browser retry identity is bounded, account-owned and in memory; reload/edits require history reconciliation after lost responses.
- Claims use short SKIP LOCKED transactions, expiring leases, unique attempt tokens, bounded retries and guarded idempotent completion. Stale attempts cannot finalize. Exhaustion is SystemFailed without contestant verdict.
- Compile once; run every pinned test after successful compilation, sequentially in fresh sandboxes. Never concatenate inputs or reuse processes/writable state. Up to 1,500 tests; each input/output remains bounded. Apply capacity migration before matching binaries; old 100-test schema/binaries cannot represent larger suites.
- Exact checking compares every byte. Accepted requires all tests pass; otherwise earliest published-order failure wins. Within-test precedence is OutputLimitExceeded, MemoryLimitExceeded, TimeLimitExceeded, RuntimeError, WrongAnswer, Accepted. Compilation errors need complete evidence and leave tests NotExecuted. Preserve historical first-failure/unexecuted tests and legacy completion markers without inventing verdicts.
- Missing artifacts/measurements, runner faults and cleanup failures are infrastructure retries, never partial contestant verdicts. Compiler RAM is separately 512 MiB; runtime uses published limits. CPU includes descendants, wall is a separate watchdog; summaries take maximum test CPU/wall/memory excluding compilation.
- Return only owner source, accepted attempt/configuration, bounded compiler diagnostics and per-test measurements. Never return hidden inputs/answers or runtime stdout/stderr.
- One non-root Linux Worker holds an exclusive host lock and recovers only recognized abandoned sandboxes/workspaces before claims. Docker is default, including Compose; sandboxes are siblings with only their stage subdirectory read-only. Never expose the whole volume, Docker socket, private tests/answers, keys or credentials. Stop older non-locking binaries before upgrade.
- One Worker defaults to four active jobs with per-job scopes and independent renewal scopes. Compile/runtime quotas default one each, FIFO by class, with shared CPU/slot/memory bounds. A full class cannot block the other. Keep scopes/permits until cleanup completes. Cleanup uncertainty blocks new stages/delivery until recovery. PostgreSQL polling remains the delivery mechanism.
- Native Isolate is opt-in direct Linux only. Require pinned root-owned helpers/config, distinct controller/sandbox identities, 1–16 boxes with reserved UIDs/GIDs/available CPUs, fresh namespaces/cgroups, swap-disabled delegation, protected hardlinks, bounded per-box eight-MiB tmpfs and non-piped core policy. No live bypass or privileged Compose Worker. Validate/recover every box; one cancellation never cleans another. Runtime quota cannot exceed box count.
- Comparisons use completed Accepted with identical version/language/toolchain/measurement profile, latest eligible result per other account, excluding the owner's entire history. Require 20 peers; Beats counts strictly larger values and ties separately. Historical profiles stay null. Aggregate bounded results in PostgreSQL without peer identity/artifact leakage. Do not rejudge to fill charts.

## Operations and validation

Active Admin authorization is required inside Application for safe aggregate diagnostics. Metrics use fixed labels; IDs stay in trusted logs. Retain durable source/results/attempts/versions/referenced tests for the development project's lifetime. Remove only unreferenced uploads/staging older than 24 hours at startup/hourly; clean stage scratch and recover at startup.

Back up PostgreSQL/private artifacts/cookie keys together with writers stopped; restore only fresh targets and reapply explicit grants. Keep seven daily backups and monitor disk/queue/failures. Do not run destructive down migrations on retained history. Read [operations](docs/operations.md) before deployment/recovery.

Keep Domain invariant/transition, Application authorization, real PostgreSQL/migration/HTTP, execution and architecture tests in existing projects. Sandbox checks require stopped application Workers. Use [development commands](docs/development.md), and distinguish fixture/browser checks from live evidence or production claims.

## Frontend

Read [UI conventions](docs/ui.md) before UI changes. Preserve original brand assets. Owner-supplied reference images, when available locally, are kept unchanged in ignored `.agents/design-references/`; they do not expand feature scope. Current neutral light/graphite dark surfaces supersede older green-heavy designs. Green is for brand/actions/restrained emphasis.

Use compact top navigation and Settings from the user menu; local Settings navigation/contextual editors. Design around user tasks. Omit filler, internal UUIDs, default roles, irrelevant timestamps and unsupported controls. Preserve destructive consequences/confirmation and backend constraints. Implement only verified capabilities.

Unchanged focus/blur must not reveal errors or aria-invalid=true. Before first submit, show errors after edit+blur; once shown, update during correction. Submit validates/focuses accessible feedback; reset/cancel/success reset interaction state. Do not disable submit solely for invalidity.

When available, use ui-ux-pro-max for relevant UX, frontend-ui-engineering for implementation and browser-testing-with-devtools for browser verification. These product rules take precedence. Verify keyboard, responsive, identity/session and interaction journeys, with meaningful validation/settings regression coverage.
