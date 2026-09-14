# Minos engineering rules

Minos is a single-developer personal project. Favor working application code and meaningful automated tests. Documentation records material decisions and evidence. M1 is complete; do not repeat execution spikes without a concrete new requirement. Stop after the approved milestone; M3 requires approval.

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

Do not introduce MediatR, Redis, brokers, event sourcing, microservices, generic repositories, outbox or more projects/layers without evidence that the current design is insufficient. Do not introduce accounts, queues, judging lifecycle, contests, ratings, SignalR or distributed workers during M2.

Update these rules when accepted architecture changes. ADRs are for decisions materially affecting correctness, security or long-term evolution, not every implementation choice. No pilot enrollment/admin approval workflow without an explicit pilot decision.
