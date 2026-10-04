# ADR 0011: Containerized trusted Worker for local development

Date: 2026-10-03. Status: Accepted by the project owner; implementation validation recorded below.

The owner requested Docker hosting for the trusted Worker and a Compose startup for the complete local stack. This updates the development hosting choice in ADR 0002; it does not select production infrastructure or introduce M7 capabilities.

## Decision

The Worker runs as UID 1654 in a Linux container. Only this trusted service receives the host Docker control socket, with its group supplied through configuration. It creates sibling sandbox containers on the host engine; there is no nested Docker daemon. Docker control access remains powerful in both native and containerized hosting and is never given to submitted programs.

Compose mounts a private Linux-native named volume at `/tmp/minos-judge-execution` in the Worker. Infrastructure accepts the trusted `Minos:Execution:WorkspaceVolume` configuration and passes only the current generated job subdirectory to the sandbox using Docker `volume-subpath`, read-only. Sandboxes never receive the full volume, another stage's files, source repository, expected answers, credentials or Docker socket. With the setting omitted, the existing native Linux/WSL bind-mount profile remains available.

API and Worker share private artifact storage with the same non-root UID. Database migration, API and Worker identities remain distinct, and the Worker retains no credential-table grants. Compose orders database initialization, migration and explicit grants before runtime startup. A one-time initializer sets private volume permissions and creates persistent local HTTPS certificates.

Angular and API retain same-origin authentication. Nginx forwards backend routes over verified HTTPS and preserves the browser host, so existing HTTPS checks, Secure cookies and CSRF protection remain effective. Its upstream resolves the API name through Docker DNS as addresses change, following the [Nginx upstream documentation](https://nginx.org/en/docs/http/ngx_http_upstream_module.html#resolve). Local certificate trust is a manual developer operation. Secrets live in an ignored `.env`, generated with random distinct passwords.

## Consequences

Local startup needs Docker and a one-time credential preparation, rather than separate host .NET/Node runtimes. Worker execution limits, verdict policies, leases, retries and cleanup semantics are unchanged. Docker Engine/API, cgroup accounting, workspace isolation and cleanup must be verified in the containerized profile. Packaging the Worker does not make it suitable for every managed Azure container service; production hosting remains deferred.

## Validation

Verified on 2026-10-04 with Docker Desktop 4.51, Linux Engine 28.5.2/API 1.51, WSL2 kernel 6.6.87.2 and cgroup v2:

- Clean locked Linux Release build and Windows Release build: zero warnings/errors. Containerized backend checks: 149 passed, one Windows ACL test skipped on Linux. Real sandbox tests covered compilation, resource evidence, descendants, cancellation cleanup and inability to read another job's workspace, private keys or Docker socket.
- Angular build and 178 frontend tests passed with Node 24.15.0. One existing destroyed `OutputRef` warning appeared in an account-settings lifecycle test; no frontend source was changed in this slice.
- `python scripts/check-docker-http.py` passed real HTTPS/CSRF/Secure-cookie registration, profile changes, publication, all seven contestant verdicts, pinned versions/accepted attempts, per-test measurements, later NotExecuted tests and cross-account denial. Its problem was archived and fixture accounts disabled.
- Isolated real Chrome checks passed registration, profile update, reload, sign-out/sign-in and catalog rendering. Recreating API with a different Docker network address demonstrated proxy recovery and preserved authenticated access. The browser test accepted the development certificate only within its temporary context; no OS trust was changed.
- Worker ran as UID/GID 1654 with only the configured socket group added. Private artifact/workspace roots were mode 700, owned by 1654. Runtime services were healthy and no labeled M6 sandbox containers remained after checks. Disposable check services/database and DNS-probe containers were removed; the local application stack stays running.
- The owner's DBeaver request added PostgreSQL publishing on loopback `127.0.0.1:5433` (configurable with `MINOS_POSTGRES_PORT`). A real TCP login through that published port succeeded as `minos_migrator`. Recreating PostgreSQL kept the same development data counts (10 accounts, three problems, seven submissions); repeated migration reported the database already up to date. The stopped legacy `minos-development-postgres` container and its separate data volume remain untouched, with its existing port mapping `127.0.0.1:15432`.

The real catalog request exposed a pre-existing EF query translation failure: ordering a constructed `PublishedProblemSummary` returned HTTP 500. Infrastructure now orders/paginates the database projection before constructing the Application record. A PostgreSQL HTTP regression assertion failed before the fix and passed afterward, including latest-version visibility and archive removal. No domain rules, projects or execution policies changed.
