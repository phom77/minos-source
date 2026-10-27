# Development

Run commands from the repository root unless a section says otherwise. Local credentials, generated outputs and problem datasets are ignored by Git.

## Complete Docker stack

Use Linux Docker with cgroup v2 and Compose v2. The runner's reference engine is Docker 28.5.2 / API 1.51; CI pins it. Windows uses Docker Desktop with WSL2. The Worker needs at least two CPUs and reserved management memory; see [judging configuration](judging.md#scheduling).

```sh
python scripts/prepare-docker.py
docker compose up --build -d --wait
```

Preparation creates `.env` only if missing and never replaces existing passwords. Do not discard it while keeping the database volume. Setup services `init`, `migrate` and `grants` exit successfully; PostgreSQL, API, Worker and web remain running. No .NET/Node installation is needed for the Docker path.

| Setting in `.env` | Purpose |
| --- | --- |
| `MINOS_PROJECT_NAME` | Compose project and image/volume prefix |
| `MINOS_HTTPS_PORT` | Browser port, default 8443 |
| `MINOS_POSTGRES_PORT` | Loopback database port, default 5433 |
| `MINOS_DOCKER_GID` | Docker socket group; 0 on Docker Desktop, socket's actual group on native Linux |
| `MINOS_{BOOTSTRAP,MIGRATOR,API,WORKER}_PASSWORD` | Four distinct local database credentials |

Nginx serves the SPA and proxies `/api`, `/accounts`, `/signin-google` and `/health` to the API over validated internal HTTPS. Only web and loopback PostgreSQL publish ports. API/Worker and private storage are not exposed directly.

### Local certificate

Create `artifacts/` and export the **public** CA certificate:

```sh
docker compose cp web:/certs/ca.crt artifacts/minos-local-ca.crt
```

On Windows, open it, choose Install Certificate → Current User → Trusted Root Certification Authorities, then restart the browser. Startup never changes the system trust store. Private CA/server keys remain in the certificate volume.

### Start, stop and update

Use `docker compose stop` followed by `docker compose start --wait` to resume existing containers. `docker compose down` removes containers but preserves data; `--volumes` removes data. For source/schema upgrades, follow the stopped-host backup/migration procedure in [operations](operations.md), rather than starting old migration images against a newer working tree.

## Native development tools

- .NET SDK selected by `global.json`; locked central NuGet dependencies.
- Node.js 24 at a version accepted by `web/minos-web/package.json`.
- Python 3 for verification/import tools; PyArrow is needed only for dataset extraction.
- Linux/WSL for Worker execution and full backend acceptance.

```sh
dotnet restore Minos.sln --locked-mode
dotnet build Minos.sln -c Release --no-restore
dotnet test tests/Minos.Domain.Tests -c Release --no-build

cd web/minos-web
npm ci
npm start -- --ssl --host localhost
```

The Angular proxy targets `https://localhost:49674`. Trust the development certificate for browser authentication. The API launch profile also exposes HTTP on 49675, but account operations require HTTPS. The API fallback without a launch profile is `http://127.0.0.1:5080`.

Windows and Linux restore/build outputs automatically use `artifacts/windows` and `artifacts/linux`. Never share `project.assets.json` across hosts. CI and private workspaces may use an explicit `--artifacts-path`.

### Database and private storage

For direct hosting, provision `minos_migrator`, `minos_api` and `minos_worker` using [bootstrap.sql](../scripts/database/bootstrap.sql) under a trusted bootstrap connection. Supply its psql variables `database`, `migrator_password`, `api_password` and `worker_password` privately. Never use migrator credentials for a runtime host.

Run API `--migrate` as migrator, then apply [runtime-grants.sql](../scripts/database/runtime-grants.sql) as migrator. Runtime hosts need `ConnectionStrings__Minos` with their own role and `Minos__Artifacts__Root` pointing to the same private storage. Linux roots must be mode 700, native filesystem paths outside the repository, without unsafe writable/symlinked ancestors. Windows API roots need private ACLs. The Worker is always Linux-hosted.

Create the first administrator with the migrator connection and `dotnet run --project src/Minos.Api -- --provision-admin <account-guid>`. Later role assignments use the administrator-only API. A fresh stack does not create demo accounts or seed problems.

## Verification

### Frontend and offline tools

```sh
python scripts/problem-bank/test-problem-bank.py
cd web/minos-web
npm test
npm run build
npx playwright install chromium
npm run test:browser
```

The browser runner serves the production build on an ephemeral loopback port and uses isolated API fixtures. It covers contests, submissions, Settings, library, workspace, comparisons, comments and themes. Screenshots/logs belong under ignored `artifacts/`, not source directories. Live browser scripts are invoked by the HTTPS harness below.

### PostgreSQL integration without sandbox execution

```sh
python scripts/checks/integration.py
```

This builds the API/checks images and uses fresh, unexposed PostgreSQL fixtures. Regression and global-capacity tests get separate databases. It checks repeated migrations, removes only its own Compose project/volumes and does not mount the Docker socket into the test host. `--filter` selects a subset; `--skip-build` requires current matching images. This can run while the normal stack is active.

### Full Linux backend acceptance

Stop application Workers before sandbox/recovery checks; their recovery ownership uses the same engine. In a non-root Linux/WSL shell with .NET, Docker, Bash, Python 3, curl and OpenSSL:

```sh
docker pull gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15
bash scripts/checks/backend.sh
```

This performs a clean locked build, fresh/repeated/upgrade migrations, real PostgreSQL tests, HTTPS author/solver flows, all seven verdicts, long-job renewal, workload/overload, Worker SIGKILL, database interruption, missing artifacts and paired backup/restore. It uses disposable targets. Require `backend_checks=passed` and `reliability_checks=passed`. Restart the previous Worker after success or failure. CI runs this entry point.

The optional Compose overlay is `docker/compose.test.yaml`, always used **after** the root file so relative paths resolve from the repository:

```sh
docker compose -f compose.yaml -f docker/compose.test.yaml --profile checks build checks
docker compose stop worker
docker compose -f compose.yaml -f docker/compose.test.yaml --profile checks run --rm checks
docker start minos-worker-1
docker compose -f compose.yaml -f docker/compose.test.yaml --profile checks rm -s -f -v checks-grants checks-migrate checks-postgres
```

Use the actual existing Worker container name if the project name differs. Always restart it even when tests fail; direct `docker start` avoids rerunning migration dependencies. The test database is disposable; the execution workspace is shared, which is why the application Worker must stop.

### Running-stack HTTPS smoke

```sh
python scripts/checks/http-smoke.py --browser-smoke --comments-browser
```

This creates temporary author/solver accounts and problems, exercises the actual API/Worker and checks all verdicts, request replay, privacy, comments and browser journeys. Cleanup archives fixture problems and disables fixture accounts; durable evidence remains under normal retention. It intentionally writes test data, so use development only. `--comments-only` skips judging/contest creation. Pass `--ca` and `--base-url` when needed. Browser credentials stay in environment variables.

## Benchmarks

`scripts/benchmarks/docker.sh <checks-image>` measures compilation and 100 serial tests. `python scripts/benchmarks/workload.py` builds a disposable workload stack, pins inspected image IDs, records resource coverage and compares the declared targets in `workload.json`. Stop all Workers first; neither command deploys to the development database.

`scripts/checks/concurrency.sh <checks-image>` uses the Linux fixture harness with Docker. `scripts/benchmarks/native.sh` also supports a separately provisioned Isolate host; set `MINOS_TEST_RUNNER=isolate` and `MINOS_TEST_ISOLATE_ROOT` explicitly. That harness requires controller UID/GID 1000 and only known trusted fixtures. Its internal test-only core-policy override does not apply to the live Worker. Native provisioning requirements are in [judging](judging.md#optional-native-runtime).

Benchmark outputs are local artifacts. Sampled peaks may miss short processes; in-process API timings exclude network/TLS. Fixture targets and machine-specific runs are not public latency or throughput guarantees.
