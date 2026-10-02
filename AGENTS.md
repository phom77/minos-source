# Minos engineering rules

Minos is a single-developer personal project. Favor working application code and meaningful automated tests. Documentation records material decisions and evidence. M1–M5 are complete and approved; do not repeat their work without a concrete new correctness/security requirement. Stop before M6 until the project owner approves it.

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

The M5 Worker uses a deterministic Infrastructure executor that never executes submitted source. Real compilation/execution, hidden-test access, checker behavior, per-test evidence, contestant verdicts and long-running lease renewal belong to M6. Manual rejudge remains deferred.

Update these rules when accepted architecture changes. ADRs are for decisions materially affecting correctness, security or long-term evolution, not every implementation choice. No pilot enrollment/admin approval workflow without an explicit pilot decision.

## Frontend visual reference

Before designing or changing the Minos UI, read [docs/design/minos-visual-reference.md](docs/design/minos-visual-reference.md) and inspect the original images in `docs/design/references/`. These record the owner's approved visual direction and take precedence over generic design suggestions and the earlier frontend scaffold styling. Keep originals unchanged; record intentional deviations. Features and data pictured on the brand board do not expand approved implementation scope or override milestone boundaries.
