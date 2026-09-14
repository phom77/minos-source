# Minos

Minos is a web-based programming practice and future competition platform built around safe, reproducible evaluation of untrusted programs.

The backend will use C#, ASP.NET Core, PostgreSQL, and Entity Framework Core. It will start as a modular monolith with a separately hosted Judge Worker.

## Current status

**M0, M1 and M2 complete. M3 awaits approval.** The five-project backend has a working development-only draft use case, real PostgreSQL migrations, separate operational identities and private artifact-root validation. See [M2 results](docs/milestones/m2-foundation.md).

These documents distinguish accepted decisions from proposed architecture and unresolved product choices. D01, D02, D03, and the D10 minimum safety baseline are accepted. Exact numeric limits are validated/tuned in M1; final contestant verdict precedence and resource-accounting semantics remain M6 decisions. Routine choices are resolved in their relevant milestones.

Development is developer-only, with no real users, pilot cohort, enrollment management, or administrator approval workflow. The intended finished product supports self-service email/password registration and Google sign-in without approval for normal users. See [ADR 0001](docs/adr/0001-initial-audience-and-access-policy.md).

The repository stays on Windows D:. The Worker runs in WSL2 Ubuntu and uses Docker first for compile/run, with private Linux-native submission workspaces and a replaceable execution adapter. A dedicated VM is unnecessary; production hosting and Isolate adoption are deferred. See [ADR 0002](docs/adr/0002-judge-development-environment.md).

## Project documents

| Document | Purpose |
| --- | --- |
| [M0 closure](docs/m0-closure.md) | Accepted baseline, MVP boundary, deferred work, risks, M1 dependencies, and completion checklist |
| [Project design](docs/project-design.md) | Product requirements, MVP boundary, domain ownership, architecture, judging lifecycle, and engineering policies |
| [Decision register](docs/decisions.md) | Confirmed constraints, proposed choices, alternatives, and questions to resolve |
| [Implementation roadmap](docs/implementation-plan.md) | Ordered milestones, dependencies, deliverables, validation, and release gates |

## First milestone

**M0: Requirements and architecture baseline** is complete. See the [closure summary](docs/m0-closure.md) and [decision register](docs/decisions.md). Official judging is the first end-to-end MVP; custom Run follows reliable judging. Backend-only details remain scheduled for their dependent milestones.

The WSL2/Docker compile/run, resource/isolation, adapter replacement, cancellation and crash-recovery experiments passed. See [M1 evidence](docs/milestones/m1-execution-feasibility.md) and the [executable suite](spikes/m1/run-m1.sh).

## Run development checks

Inside WSL2 Ubuntu:

```bash
export PATH="$HOME/.local/share/minos/dotnet:$PATH"
cd /mnt/d/Projects/PetProject/Minos
bash scripts/check-foundation.sh
```

The foundation script and GitHub Actions use the same pipeline: export a clean source tree, restore in locked mode, build Release, provision PostgreSQL 18.6 with separate bootstrap/migrator/API/Worker identities, apply migrations twice, run tests, start both hosts and prove readiness/recovery. Its private Linux Unix socket is not network-accessible. A per-run Docker volume preserves the database across the tested restart; the volume, container and temporary workspace are removed afterward. No real credentials are needed. Docker, .NET 10, Bash, Python 3 and curl are required. Windows builds use SDK 10.0.301; WSL checks use 10.0.401. The M1 spike is archived evidence and is not part of M2 checks.

Normal `dotnet restore`/`dotnet build` and IDE builds automatically use `artifacts/windows` on Windows and `artifacts/linux` in WSL. Restore assets are kept separate because they contain absolute paths to each host's NuGet cache. Explicit `--artifacts-path` overrides remain available for CI/private Linux build directories. After pulling build-configuration changes, restore the solution once on each host before using `--no-restore`.

For a persistent PostgreSQL 18.6 instance, create database `minos` under a bootstrap owner. Set its `PGHOST`, `PGUSER` and `PGPASSWORD` outside source control. On an empty database, read three different passwords privately and provision roles with psql:

```bash
read -rsp 'Migrator password: ' MINOS_MIGRATOR_PASSWORD; printf '\n'
read -rsp 'API password: ' MINOS_API_PASSWORD; printf '\n'
read -rsp 'Worker password: ' MINOS_WORKER_PASSWORD; printf '\n'
psql -X -d minos -v ON_ERROR_STOP=1 -v database=minos \
  -v migrator_password="$MINOS_MIGRATOR_PASSWORD" -v api_password="$MINOS_API_PASSWORD" \
  -v worker_password="$MINOS_WORKER_PASSWORD" -f scripts/postgres-bootstrap.sql
```

Supply `ConnectionStrings__Minos` privately as a complete Npgsql connection string with Host, Database, Username and Password. Use `minos_migrator` for `dotnet run --project src/Minos.Api -- --migrate`, then run `scripts/postgres-runtime-grants.sql` through psql as that migrator. The migrator owns application tables but cannot create roles/databases and is never used by a runtime host. Runtime grants are explicit for existing tables; future tables receive no automatic access.

Create a private root outside the repository before starting either host. In WSL:

```bash
install -d -m 700 "$HOME/.local/share/minos/artifacts"
export Minos__Artifacts__Root="$HOME/.local/share/minos/artifacts"
# Read the API connection string without putting credentials in shell history.
read -rsp 'API connection string (minos_api): ' ConnectionStrings__Minos; printf '\n'
export ConnectionStrings__Minos
ASPNETCORE_ENVIRONMENT=Development Minos__EnableDevelopmentEndpoints=true dotnet run --project src/Minos.Api
```

In a second WSL terminal, configure the same artifact-root variable and read a connection string for `minos_worker`, then run `dotnet run --project src/Minos.JudgeWorker`. `-- --check` validates startup and exits. For Windows API hosting, set the equivalent environment variables or API user secrets and use a private Windows directory ACL permitting only the current account, SYSTEM and Administrators. The Worker always runs in Linux; Linux roots must use a native filesystem, private permissions and no symlinked/writable-public ancestors. Migration mode needs only the migrator connection, not storage.

API health endpoints are `/health/live` and `/health/ready` at `http://127.0.0.1:5080`. Startup checks runtime identity/grants, applied migrations and real private-storage read/write access. Readiness covers the same dependencies; liveness stays available during dependency outages. Opt-in `/_development/problem-drafts/` POST and GET-by-ID endpoints exercise persistence and use-case access checks. They require Development plus `Minos__EnableDevelopmentEndpoints=true`; transport errors use safe ProblemDetails. No artifact directory is served over HTTP. These routes provide no product authentication. Artifact upload, ownership and sharing between Windows/WSL roots are later workflow work; this Worker remains idle and executes no submissions.

## Engineering principles

- Keep business rules explicit and infrastructure replaceable at real boundaries.
- Use DDD where invariants and lifecycle rules justify it; use simple models for straightforward data management.
- Compile and execute submitted source only in an isolated execution environment.
- Persist jobs before acknowledging submissions and recover work after interruption.
- Introduce additional technology only when a concrete limitation warrants it.
- Separate developer-only access from intended product authentication; any pilot or public deployment requires an explicit later decision.
