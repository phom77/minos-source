# Minos

Minos is a web-based programming practice and future competition platform built around safe, reproducible evaluation of untrusted programs.

The backend will use C#, ASP.NET Core, PostgreSQL, and Entity Framework Core. It will start as a modular monolith with a separately hosted Judge Worker.

## Current status

**M0–M6 complete; M7 backend complete and verified. The development MVP candidate is ready for owner acceptance. M8 has not started.** The five-project backend has accounts, immutable published problems, private tests, durable PostgreSQL jobs and real C++20 compilation/judging. M7 adds global admission bounds, exclusive judge-host recovery, administrator diagnostics, hourly abandoned-upload cleanup and paired database/artifact backup/restore. Live Google consent validation remains deferred because no OAuth client exists. See [M7 results](docs/milestones/m7-reliability.md), [reliability decisions](docs/adr/0010-development-reliability-and-retention.md) and the [operating runbook](docs/operations/development-runbook.md).

These documents distinguish accepted decisions from proposed architecture and unresolved product choices. D01, D02, D03, the D10 safety baseline and the M6 verdict/checker/accounting policies are accepted. Routine choices are resolved in their relevant milestones.

Development is developer-only, with no real users, pilot cohort, enrollment management, or administrator approval workflow. The intended finished product supports self-service email/password registration and Google sign-in without approval for normal users. See [ADR 0001](docs/adr/0001-initial-audience-and-access-policy.md).

The repository stays on Windows D:. The local Compose stack runs the trusted Worker in a non-root Linux container and uses sibling Docker sandboxes for compile/run. Native Ubuntu/WSL hosting remains supported. Private Linux-native submission workspaces and the replaceable execution adapter are preserved. Production hosting and Isolate adoption remain deferred. See [ADR 0002](docs/adr/0002-judge-development-environment.md) and [the containerized Worker decision](docs/adr/0011-containerized-worker-development.md).

## Project documents

| Document | Purpose |
| --- | --- |
| [M0 closure](docs/m0-closure.md) | Accepted baseline, MVP boundary, deferred work, risks, M1 dependencies, and completion checklist |
| [Project design](docs/project-design.md) | Product requirements, MVP boundary, domain ownership, architecture, judging lifecycle, and engineering policies |
| [Decision register](docs/decisions.md) | Confirmed constraints, proposed choices, alternatives, and questions to resolve |
| [Implementation roadmap](docs/implementation-plan.md) | Ordered milestones, dependencies, deliverables, validation, and release gates |
| [Approved visual reference](docs/design/minos-visual-reference.md) | Owner-provided brand/UI images, approved palette, visual patterns, and future frontend design guidance |

## First milestone

**M0: Requirements and architecture baseline** is complete. See the [closure summary](docs/m0-closure.md) and [decision register](docs/decisions.md). Official judging is the first end-to-end MVP; custom Run follows reliable judging. Backend-only details remain scheduled for their dependent milestones.

The WSL2/Docker compile/run, resource/isolation, adapter replacement, cancellation and crash-recovery experiments passed. See [M1 evidence](docs/milestones/m1-execution-feasibility.md) and the [executable suite](spikes/m1/run-m1.sh).

## Start the complete local Docker stack

Docker Desktop must use Linux containers and the WSL2 engine. Native Linux Docker also works. Engine 28.5.2/API 1.51 and cgroup v2 are the current verified runner baseline. You do not need to install .NET or Node in Ubuntu to run this stack.

From the repository root, create local credentials once, then start everything:

```text
python scripts/prepare-docker.py
docker compose up --build -d --wait
```

Open **https://localhost:8443**. The first build downloads dependencies. Compose initializes private volumes and local certificates, provisions three distinct database roles, applies migrations/grants, and starts PostgreSQL, API, Worker and the Angular web server. The Worker prepares the pinned GCC image before accepting jobs. Google authentication is disabled in this local profile until a real client is configured.

Opening `http://localhost:8443` redirects to HTTPS. `init` prepares private storage/certificates, `migrate` creates or updates database tables, and `grants` gives API/Worker their required database permissions. These setup containers finish with `Exited (0)`; the four runtime services keep running. The Python preparation script only writes `.env` once and never starts services. Once `.env` exists, use `docker compose up --build -d --wait` directly. You can also prepare `.env` manually from `.env.example` by supplying distinct passwords.

The generated `.env` is ignored by Git and Docker builds. It contains random passwords; preserve it while keeping the database volume. `MINOS_HTTPS_PORT` changes the browser port and `MINOS_POSTGRES_PORT` changes the local database port (default 5433). Existing `.env` files can omit the new port setting and use its default. On native Linux, `MINOS_DOCKER_GID` must match the group owning `/var/run/docker.sock`; the preparation script detects it. Docker Desktop defaults to group 0. The Worker still runs as UID 1654, not root.

For trusted browser HTTPS, export the development CA and import **only that public certificate** into your local trusted certificate authorities:

```text
docker compose cp web:/certs/ca.crt artifacts/minos-local-ca.crt
```

Create `artifacts/` first if needed. On Windows, open the certificate and use Install Certificate → Current User → Trusted Root Certification Authorities. Restart the browser afterward. Trust is a manual local step; startup never changes your operating system's trust store. Certificates and authentication keys persist across container restarts.

All browser requests use the same HTTPS origin. Nginx serves Angular and forwards `/api`, `/accounts`, `/signin-google` and `/health` to `https://api:8443`, validating its certificate and preserving the browser host. It refreshes Docker DNS when the API container changes address. API and Worker have no published host ports. PostgreSQL publishes only `127.0.0.1:5433` for local database tools; container-to-container connections still use `postgres:5432`. Hidden artifacts and keys are never served by Nginx.

For DBeaver, choose PostgreSQL and use host `127.0.0.1`, port `5433`, database `minos`, username `minos_migrator`, and the value of `MINOS_MIGRATOR_PASSWORD` from your private `.env`. Database SSL is not configured in this local profile; leave that connection's SSL option disabled. This developer identity can modify the application schema/data. The previous `minos-development-postgres` database uses a separate volume and is not copied into this stack automatically.

For later starts, run `docker compose up --build -d --wait` again. The `migrate` service applies pending EF migrations included in the rebuilt backend, then `grants` updates runtime permissions. Applied migrations are recorded and skipped on later runs. Editing C# entities alone does not create a migration; create the migration in Infrastructure first. Reusing the Compose volumes preserves existing data.

```text
docker compose ps
docker compose logs --tail 50 worker
docker compose exec worker /bin/sh /minos/worker-healthcheck.sh
docker compose down
```

`down` preserves database, artifacts and certificates. Adding `--volumes` deletes that local data. The trusted Worker alone receives the Docker socket. Each judging stage mounts only its own workspace subdirectory, read-only, and retains the existing network/resource/privilege restrictions. No Docker daemon runs inside the Worker container.

To check the containerized profile after startup:

```text
docker compose -f compose.yaml -f compose.checks.yaml --profile checks build checks
docker compose stop worker
docker compose -f compose.yaml -f compose.checks.yaml --profile checks run --rm checks
docker compose start worker
python scripts/check-docker-http.py
docker compose -f compose.yaml -f compose.checks.yaml --profile checks rm -s -f -v checks-grants checks-migrate checks-postgres
```

The .NET suite uses a separate disposable PostgreSQL database and real sibling sandboxes, including cancellation and workspace isolation. Stop the Worker before running this suite: the tests share its workspace volume and Docker recovery ownership, and M7 permits only one runner. Restart the Worker even if the tests fail; wait for it to become healthy before the HTTP check. Worker health probes inspect its startup marker and running process without acquiring its exclusive lock. `--check` is a startup/recovery check for a stopped Worker, not a live health probe. The HTTP script trusts the exported development CA directly, creates temporary author/solver accounts and a problem, checks all seven verdicts through the running API/Worker, then archives its problem and disables its accounts. Existing accounts are never changed. Archived fixture evidence remains in the development database. If you changed the port, pass `--base-url https://localhost:<port>` to the HTTP script. The final command removes only the check services and their disposable database.

Frontend checks can also use the packaged Node version:

```text
docker build --target build -t minos-web-build:local -f docker/web.Dockerfile .
docker run --rm minos-web-build:local npm test
```

## Run development checks

Inside WSL2 Ubuntu:

```bash
export PATH="$HOME/.local/share/minos/dotnet:$PATH"
cd /mnt/d/Projects/PetProject/Minos
# Prepare the pinned compiler image before the runner applies short stage deadlines.
docker pull gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15
bash scripts/check-foundation.sh
```

The foundation script and GitHub Actions use the same acceptance pipeline: clean locked Release build, isolated PostgreSQL 18.6 identities, fresh/repeated/upgrade migrations, tests, real HTTPS author/solver flows, all seven official verdicts, a job exceeding its initial lease, readiness/recovery, an eight-job workload, Worker SIGKILL, runtime database interruption and restored-problem judging. Docker execution tests are enabled by default in this Linux profile. This is also the trusted development fixture validation command; it uses temporary problems and known programs, without storing reference source or adding a publication execution gate. Google tests use a fake backchannel. Per-run container/volume/private files are removed afterward. Docker, .NET 10, Bash, Python 3, curl and OpenSSL are required. The SDK baseline is 10.0.301 with feature-band roll-forward; current Windows/WSL checks use 10.0.401. M1 spikes remain archived evidence.

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
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Minos.Api
```

In a second WSL terminal, configure the same artifact-root variable and read a connection string for `minos_worker`, then run `dotnet run --project src/Minos.JudgeWorker`. Start only one Worker; `-- --check` validates startup, Docker and recovery and exits while the Worker is stopped. For Windows API hosting, set the equivalent environment variables or API user secrets and use a private Windows directory ACL permitting only the current account, SYSTEM and Administrators. The Worker always runs in Linux; Linux roots must use a native filesystem, private permissions and no symlinked/writable-public ancestors. Migration mode needs only the migrator connection, not storage. API admission defaults to 32 active submissions globally and five per account; `Minos__Submissions__MaximumActive` explicitly overrides the global development bound. Apply M7's updated runtime grants before starting an existing database. Active administrators can inspect safe aggregates at `/api/operations/judging`. See the runbook for private backup/restore, failure alerts and rollback.

API health endpoints are `/health/live` and `/health/ready`. The `Minos.Api` launch profile uses `https://localhost:49674` and `http://localhost:49675`; account and authoring endpoints require HTTPS. Startup/readiness validate runtime privileges, migrations and private storage; liveness remains available during outages. Self-service `/accounts` endpoints use protected cookies and `X-CSRF-TOKEN` from `/accounts/csrf`; obtain a new token after signing in. New accounts are Solvers. With the migrator connection, `dotnet run --project src/Minos.Api -- --provision-admin <account-guid>` performs the trusted first-administrator operation; administrators assign named roles through `PUT /accounts/{id}/role`. Setter/Admin authoring routes are under `/api/authoring/problems`; public current/version projections are under `/api/problems`. Authenticated solvers submit through `POST /api/problems/{problemId}/submissions`, read owner detail at `GET /api/submissions/{id}`, and poll history at `GET /api/me/submissions`. Errors use safe ProblemDetails. Private storage, hidden tests and persisted session keys are never served as static content or exposed through API contracts. The Worker processes jobs through real Docker compilation and fresh testcase execution. Owner detail includes pinned configuration, accepted attempt, verdict and bounded compiler diagnostics; runtime input/output and expected answers remain private. Prepare the pinned compiler image before starting the Worker. [ADR 0009](docs/adr/0009-official-judging-and-evidence.md) records the execution bounds and accounting limitations.

## Engineering principles

- Keep business rules explicit and infrastructure replaceable at real boundaries.
- Use DDD where invariants and lifecycle rules justify it; use simple models for straightforward data management.
- Compile and execute submitted source only in an isolated execution environment.
- Persist jobs before acknowledging submissions and recover work after interruption.
- Introduce additional technology only when a concrete limitation warrants it.
- Separate developer-only access from intended product authentication; any pilot or public deployment requires an explicit later decision.
