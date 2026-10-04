# Minos engineering rules

Minos is a single-developer personal project. Favor working application code and meaningful automated tests. Documentation records material decisions and evidence. M1–M5 are complete and approved; do not repeat their work without a concrete new correctness/security requirement. M6's approved verdict/checker/accounting recommendations are implemented and verified; M6 is complete. Stop before M7 until the project owner approves it.

## Production structure and dependencies

Keep exactly the existing five production projects and existing test projects. No additional layers or projects without a demonstrated requirement.

- Domain has no project dependencies. Keep EF Core, ASP.NET Core, transport DTOs, PostgreSQL APIs, Docker and external SDKs out.
- Application may depend only on Domain.
- Infrastructure may depend on Application and Domain.
- Api and JudgeWorker may depend on Application. References to Infrastructure are confined to DI, composition, startup and infrastructure-specific hosting concerns.
- JudgeWorker never depends on Api. No circular dependencies.

## Responsibilities

- Domain owns business rules, invariants, aggregate behavior and state transitions. Entities protect themselves. Use DDD patterns only when complexity justifies them; do not add base entities, events, factories, services or value objects for ceremony.
- Application organizes capabilities and use cases with lightweight commands/queries and handlers. Handlers orchestrate domain behavior. Define persistence and external-service ports here; never expose DbContext, IQueryable, EF types, HTTP types or infrastructure implementations. Enforce resource authorization inside use cases, including calls that bypass HTTP.
- Infrastructure owns EF Core, DbContext, migrations, focused repositories/queries, storage and execution adapters. Make transaction boundaries explicit and use PostgreSQL constraints as final protection against races and invalid data. Do not add generic CRUD repositories.
- Api endpoints are thin and expose transport contracts, never domain entities. Centralize safe exception/status mapping to ProblemDetails. Do not expose stack traces or sensitive data.
- Worker remains an independent Linux host. When jobs are introduced, create a DI scope per job. Submitted programs never execute directly in API/Worker processes.
- The local Compose stack runs the trusted Worker in a non-root Linux container with Docker control access. Sandbox containers are siblings managed by the host engine, and receive only a read-only subdirectory of the execution-workspace volume for their own stage. Never expose the whole volume, Docker socket, private tests/answers, session keys or credentials to a sandbox. Native Linux/WSL hosting remains supported; production hosting is still deferred.

## Engineering and validation

Use async I/O, CancellationToken, UTC timestamps, structured logging, validated startup configuration and secrets outside source control. Do not log credentials, tokens, hidden tests or submission source unnecessarily.

Keep Windows and WSL generated restore/build assets separate: Directory.Build.props selects artifacts/windows or artifacts/linux automatically. Explicit --artifacts-path overrides are supported for Linux-native CI/check workspaces. Never share project.assets.json between host operating systems.

Maintain Domain invariant/state-transition tests, Application use-case/authorization tests, real PostgreSQL/migration/HTTP integration tests and architecture tests. Keep them in the existing test projects.

Before edits: inspect code/references, identify file-level evidence, explain the smallest coherent change and its layer. Implement only the current slice, add relevant tests, run build/tests and review the diff for unnecessary dependencies or abstractions. Do not rewrite valid working architecture.

Do not introduce MediatR, Redis, brokers, event sourcing, microservices, generic repositories, outbox or more projects/layers without evidence that the current design is insufficient. Do not introduce real contestant execution, verdict policy, contests, ratings, SignalR or distributed workers before their approved milestone.

## Account and authentication boundaries

Domain accounts/profile/disabled/role state and immutable problem ownership remain independent of Identity, providers and HTTP. Infrastructure owns Identity credential persistence and stable provider/subject mappings. API owns middleware, framework cookies, CSRF and provider callbacks. JudgeWorker never registers web authentication services and has no credential-table privileges.

Use platform password hashing, OAuth state/correlation/PKCE and protected cookies. Never silently merge accounts by matching email. Explicit linking requires proof of the current account and provider. Enforce active-account, Setter/Admin and owner checks inside Application, even outside HTTP. Role changes are administrator-only and revoke target sessions; first-admin provisioning is a trusted maintenance operation. No invitations or administrator approval for normal registration.

## Problem publication boundaries

`Problem` is the stable aggregate and current editable authoring state. Published problem versions are immutable snapshots with distinct version IDs suitable for future submission pinning. Later edits create a new version; archive preserves exact historical versions and artifacts. Private test contents remain behind opaque artifact keys in Infrastructure and never enter public/Application projections as filesystem paths or contents. Publication verifies artifacts and structural prerequisites. M4 records trusted reference-validation checklist evidence; sandbox-backed reference execution and verdict semantics remain M6 concerns.

## Submission and job boundaries

`Submission` derives its owner from the active account and permanently pins the published problem version, supported language/toolchain and bounded source. Submission plus `JudgeJob` are admitted atomically in PostgreSQL. Jobs use short `SKIP LOCKED` claims, expiring leases, unique attempt tokens, bounded retries and guarded idempotent completion. Stale attempts never finalize a submission; retry exhaustion is SystemFailed without a contestant verdict. Owner detail may return source; history omits it and cross-account access is denied in Application.

The M6 Worker compiles once using the pinned C++20 toolchain and runs ordered private tests in fresh Docker sandboxes behind an Application execution port. Exact checking compares every byte. Stop at the first failed test and persist later tests as NotExecuted. Runtime precedence is OutputLimitExceeded, MemoryLimitExceeded, TimeLimitExceeded, RuntimeError, WrongAnswer, Accepted. Compilation failures require complete resource/exit/cleanup evidence. Missing artifacts, missing measurements, runner faults and cleanup failures follow the bounded infrastructure retry path, never a contestant verdict.

The owner result exposes the accepted attempt, pinned configuration, bounded compiler diagnostics and per-test resource measurements. Hidden input, expected answers and runtime stdout/stderr are never returned. Per-test cgroup CPU includes descendants; wall time is a separate watchdog; summaries take the maximum executed-test CPU/wall/memory and exclude compilation. Worker renewal uses independent scopes and processing scopes survive cancellation until sandbox cleanup finishes. Preserve pre-M6 completion markers without fabricating verdicts. Trusted reference validation uses developer-run sandbox fixtures; no stored reference solution or new publication execution gate. Manual rejudge, broad retention and distributed execution remain deferred.

Update these rules when accepted architecture changes. ADRs are for decisions materially affecting correctness, security or long-term evolution, not every implementation choice. No pilot enrollment/admin approval workflow without an explicit pilot decision.

## Frontend visual reference

Before designing or changing the Minos UI, read [docs/design/minos-visual-reference.md](docs/design/minos-visual-reference.md) and inspect the original images in `docs/design/references/`. These record the owner's approved visual direction and take precedence over generic design suggestions and the earlier frontend scaffold styling. Keep originals unchanged; record intentional deviations. Features and data pictured on the brand board do not expand approved implementation scope or override milestone boundaries.

## Frontend product UX

- Design screens around user tasks, not API response shapes. Before implementation, identify the screen's purpose, primary action, essential information and relevant interaction states. Backend field availability alone does not justify displaying a field.
- Use a compact top header for primary application navigation; replace the persistent global left sidebar. Keep Settings reachable from the user menu. Settings may use local section navigation, with an accessible compact presentation on smaller screens.
- Use the owner-supplied LeetCode Settings screenshots in `docs/design/references/` as references for information hierarchy, grouped setting rows and contextual editing. Preserve Minos branding, palette and components. This approved navigation direction supersedes the global left rail pictured on the original brand board.
- Put account configuration in Settings. Group profile, security and account operations by purpose. Open editing controls in context instead of displaying every form at once. Separate destructive operations from routine settings; preserve accurate consequence explanations and confirmation.
- Display information only when it helps users act, understand an outcome or avoid a mistake. Omit internal account UUIDs, default role labels, irrelevant joined timestamps, implementation details and redundant permission explanations from ordinary account flows. Explain permissions or limitations at the relevant action when needed.
- Remove filler quotes, slogans, decorative captions, repeated introductions and explanatory text with no task value from working screens and application chrome. Keep concise labels, useful input guidance, actionable errors and material consequence warnings. Prefer plain language over transport or implementation terminology.
- Separate data validity from error visibility. Focus/blur alone must not show an error, error styling or `aria-invalid=true` on an unchanged field before submission. Before first submit, show errors after edit and blur; once a field has shown an error, update it as the user corrects the input. Submit validates the whole form and focuses an accessible error summary or the first invalid field. Reset, cancel and successful completion reset interaction/error state. Do not use `touched` alone as the error-visibility condition.
- Keep backend constraints and authorization intact. Present useful constraints concisely, such as a minimum password length; show exceptional limits when violated rather than dumping the entire validation contract into permanent helper text. Do not disable submit solely because the form is invalid, preventing users from requesting validation feedback.
- Implement only settings supported by verified current capabilities. Record missing backend capabilities as future planning candidates with their user need and required contract; do not add fake values, empty sections or nonfunctional controls. Reference screenshots do not approve MFA, billing, notifications, new providers or account-linking features, and do not approve M7.
- Use `ui-ux-pro-max` for relevant UX decisions, `frontend-ui-engineering` for implementation, and `browser-testing-with-devtools` for browser verification when its tools are available. Apply these product rules over generic skill recommendations. Verify actual user journeys, keyboard navigation, responsive behavior and interaction states; visual polish alone does not establish usability. Add meaningful regression tests for validation and settings navigation when changing their behavior.
