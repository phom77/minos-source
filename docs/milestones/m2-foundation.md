# M2 foundation: executable results

Status: **Complete**, 2026-09-14. M3 has not started and requires approval.

## Architecture and request flow

Exactly five production projects remain. Domain has no project/package dependencies. Application references only Domain. Infrastructure references Application (and uses Domain transitively). Api and JudgeWorker reference Application/Infrastructure, with Infrastructure usage confined to composition/startup/health. Worker never references Api. No new production/test projects, brokers, mediator, generic repository, domain events or outbox were added. [AGENTS.md](../../AGENTS.md) contains the guardrails; architecture tests check references, cycles, compiled Domain/Application dependencies and host source boundaries.

`POST /_development/problem-drafts/` → API request contract → `CreateProblemDraft` → `ProblemDraftUseCases` access check → `ProblemDraft.Create` validates/normalizes → focused repository → one atomic EF SaveChanges insert → PostgreSQL constraints → Application projection → API response contract/201 Location.

GET checks access for the requested draft ID before repository access, uses an explicit no-tracking query and returns a response projection or 404 ProblemDetails. Development access requires both Development and explicit opt-in. Calls bypassing HTTP also pass the Application access port. This is a developer-only policy, not user authentication or setter ownership.

| Important file/class | Responsibility |
| --- | --- |
| `Domain/Problems/ProblemDraft` | Owns title/statement invariants, trimming and UTC creation |
| `Application/Problems/ProblemDraftUseCases` | Orchestrates access, domain creation, persistence and projections |
| `IProblemDraftRepository`, `IProblemDraftAccess` | Focused persistence and resource-access ports |
| `Infrastructure/Persistence/MinosDbContext`, migrations | EF mapping and database constraints; Domain stays framework-free |
| `ProblemDraftRepository` | Async insert/read, cancellation and the single-insert transaction boundary |
| `Infrastructure/Hosting/DatabaseOperationalCheck` | Validates operational login, forbidden privileges, exact current table grants and migration readiness |
| `PrivateArtifactDirectory` | Existing private-root validation and cancellable real read/write/delete probe; no upload API |
| `Api/Program`, `ProblemDraftContracts`, `DomainExceptionHandler` | HTTP composition, transport contracts, safe validation/403/404/500 ProblemDetails with trace IDs |
| API/Worker startup checks; API health check | Scoped infrastructure validation; idle Linux Worker; dependency readiness distinct from liveness |
| `scripts/postgres-*.sql`, `check-foundation.sh` | Separate principals, explicit object grants and the reproducible Release pipeline |

## Tests and evidence

`bash scripts/check-foundation.sh` finished with `foundation_checks=passed` in WSL2 Ubuntu 22.04, .NET SDK 10.0.401, PostgreSQL 18.6 pinned by digest. It builds a clean source export with fresh Linux-native build outputs, restores locked packages and starts both actual hosts. No M1 execution spike ran.

- Linux Release: **55 passed, 1 Windows-only test skipped** (6 Domain; 49 Application/architecture/PostgreSQL/HTTP/storage tests).
- Windows Release build: no errors/warnings. **20 focused tests passed**, including Domain, Application, architecture, required configuration and actual Windows ACL/read-write checks. Windows ACL mutation tests run outside Codex's restricted process token; no administrator access is required in normal developer execution.
- Migrator creates the fresh schema and three migration-history entries; applying migrations again succeeds without changing schema.
- Actual PostgreSQL sessions demonstrate API insert/read and Worker read, and deny temporary/schema/table/role creation, destructive draft operations, Worker insert and runtime migration-history writes. Bootstrap credentials and excessive grants are rejected by the operational validator. Grant-drift tests restore their changes afterward.
- Direct SQL rejects whitespace-only/untrimmed titles and whitespace-only statements, including Unicode whitespace. Non-BMP boundary tests prove PostgreSQL matches Domain UTF-16 length limits rather than counting Unicode scalars alone.
- HTTP tests prove restart persistence, 201/400/403/404/500 contracts, invalid JSON, route opt-in and use-case denial outside the developer mode. Unexpected errors do not expose the sensitive sentinel or stack traces; exception logging records safe type/trace metadata.
- Private probes leave no files, propagate cancellation and reject repository roots, public permissions/ACLs, writable-public Linux ancestors and symlinks. No storage HTTP route exists. Changing privacy permissions produces readiness 503 while liveness remains 200, then readiness recovers.
- Worker starts normally, remains hosted, shuts down on SIGTERM, and refuses startup without PostgreSQL. A stopped database produces API readiness 503/liveness 200. After restart, readiness recovers and the previously created draft still exists.
- Final review checks production references, no unnecessary abstractions/new dependencies and whitespace. Per-run fixtures are removed; TRX evidence remains under ignored `artifacts/tests/`, with Windows evidence under `artifacts/tests/windows/`.

The original outage fixture used Docker tmpfs, which erased roles/data on restart. The corrected fixture uses a uniquely named per-run Docker volume, removed after the test. This is a fixture fix, not a production hosting decision.

## CI and completion gate

`.github/workflows/build.yml` invokes the exact pipeline above and uploads TRX results. It passed actionlint 1.7.12, downloaded from the official release and checksum-verified. No real CI secret is required. The repository has no initial commit yet; the first GitHub-hosted run is pending commit/push. Local execution validates the CI command, not a claim of an existing hosted green run.

- [x] Five-project foundation and dependency guards.
- [x] Clean source build and actual API/Linux Worker startup without committed credentials.
- [x] Fresh/repeat PostgreSQL migration workflow.
- [x] Separate operational identities and tested least privileges.
- [x] Required configuration fails safely at startup.
- [x] Private artifact-root configuration and real I/O/privacy validation.
- [x] Structured logs, dependency readiness and safe HTTP errors.
- [x] Domain/Application/architecture/real PostgreSQL/HTTP test foundation.
- [x] CI workflow validated; its exact Release pipeline executed locally.
- [x] M3/later workflows remain unimplemented.

## Limits and next milestone

Developer-only routes have no product accounts or ownership model. API startup requires available PostgreSQL, correct grants and applied migrations; later outages are reflected by readiness. Windows API full HTTP startup was not exercised against the Unix-socket fixture; its build/configuration/private ACL were tested, while full host/HTTP integration ran in WSL.

Storage is a host-local private-root foundation. Artifact keys, bounded uploads, staging/verification, quota/retention and Windows/WSL sharing are M4/M7 work. OS administrators and the developer are trusted; filesystem checks do not defend against a compromised host. The Worker is intentionally idle, with no submission/job/execution adapter in application code. Production hosting, distributed operation and Isolate adoption remain deferred.

M3 establishes accounts and resource authorization before user-facing authoring/submission workflows: normal self-service email/password registration, Google sign-in, sessions/account lifecycle and permission/ownership policies. Its open authentication details are resolved in that milestone; no pilot cohort or administrator registration approval is introduced. Begin only after approval.

## Follow-up: Infrastructure package/build repair, 2026-09-14

Reproduced Windows NETSDK1064 for Microsoft.CodeAnalysis.Analyzers 3.11.0: the package was installed, but shared project-local restore assets pointed to WSL's absolute NuGet-cache paths. A Windows restore repaired the immediate error. Directory.Build.props now separates default generated outputs into artifacts/windows and artifacts/linux; explicit CI artifact roots remain supported and old bin/obj outputs are excluded by the SDK. Package versions and lock files required no changes.

Two executable MSBuild regression cases check evaluated restore/build paths for the current host and an explicit override. Windows build with --no-restore succeeded after WSL restore/build, with both NuGet-cache paths remaining separate. Fourteen focused Windows tests passed; EF migration discovery succeeded on both hosts with --no-connect. The full PostgreSQL/HTTP/host pipeline passed again: 57 tests passed, with one Windows-only ACL test skipped.
