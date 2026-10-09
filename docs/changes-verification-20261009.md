# Current changes verification — 2026-10-09

The default Docker development profile connects the current frontend, API, PostgreSQL migrations/grants and Worker. A real browser exercised contest problem creation and private ZIP uploads through to a committed Accepted result. This is development verification; it does not approve M8 or constitute production acceptance.

## Executed checks

| Check | Result |
| --- | --- |
| Locked Linux Release solution build/publish | Passed, zero errors/warnings; existing five production projects |
| Locked Windows Release solution build | Passed, zero errors/warnings; separate artifacts/windows restore/build assets |
| Angular production build, Node 24.15.0 Docker image | Passed without bundle-budget warnings |
| Frontend regression suite | 260 passed across 23 files |
| Domain suite | 30 passed |
| Real PostgreSQL/HTTP/architecture/sandbox integration suite | 209 passed, 11 skipped, zero failed; Docker execution enabled |
| Offline portable problem-bank regressions | 7 passed |
| Fresh Compose PostgreSQL migration, grants and API/Worker readiness | Passed in a separately named disposable stack |
| Contest browser fixtures | Passed creation/publication, registration/submission, keyboard focus, light/dark and 320/768/1024/1440px layouts |
| Real HTTPS browser journey | Passed UI login, contest draft, contextual problem authoring, three private ZIP uploads, problem publication and contest association, contest publication, participant registration, submission, real Worker Accepted, three per-test evidence rows and contest history |
| Real practice browser journey | Passed submission, inline Accepted and navigation to owner result with three per-test rows |
| Real HTTPS API/Worker smoke | All seven verdicts passed, complete-test status evidence, immutable version, accepted attempt, resource measurements, cross-account denial and private projection checks |
| NuGet advisory audit including transitives | No vulnerable packages reported in the five production and two test projects |
| Git whitespace check | Passed |

The integration suite includes concurrent shared contest/practice admission, unique registration, time revalidation after the admission lock, migration capacity/rollback protection, targeted claims, bounded stage allocation, Worker scope/renewal/cancellation and real Docker judging. Compilation failure leaves all tests NotExecuted; runtime failures retain evidence for every test.

`scripts/check-docker-http.py` still expected historical first-failure behavior. Its assertions now require all runtime tests to execute and validate the returned test summary. The constant-output WrongAnswer fixture fails tests two and three; its expected statuses are therefore Passed/WrongAnswer/WrongAnswer.

The live check used a separate `minos-verification` Compose project on HTTPS port 9443, with its own database, artifacts, cookie keys, certificates and workspace volume. The normal Worker was stopped while checks owned the shared Docker sandbox recovery label, then resumed directly. Existing application schema/records were not upgraded or changed. Browser fixture screens were served from the current production build, and the live browser used the actual HTTPS nginx/API/Worker stack without API interception. Local harness and screenshot evidence are under ignored `artifacts/commit-verification/` and `artifacts/contest-browser/`.

During restoration, `docker compose start worker` unexpectedly restarted the old migration container and current mounted grant script. The old migrator had no new migrations; existing grants were reapplied, then the contest grant failed because that database has no `contests` schema. The unchanged Worker was recovered with `docker start minos-worker-1`; API/Worker/web/PostgreSQL are healthy. README now documents direct container resume. This original stack still runs its prior images/schema; the new changes were verified in the disposable current-image stack. Updating the original stack requires the documented stopped-host migration/grant/binary upgrade, not merely restarting old containers.

## Explicit limits

- Eleven integration cases require a native Isolate host, opt-in benchmarks/external problem-bank data or Windows ACLs and were skipped in the Linux Docker suite. The current machine has no configured native helper at the documented prefixes; this run does not establish live Isolate/multiple-box acceptance or benchmark capacity.
- Google is disabled in Compose. Fake-provider regression coverage does not establish live Google consent acceptance.
- The paired backup/restore and SIGKILL/database-interruption foundation script was not rerun in this review; its earlier M7 evidence remains separate. No production deployment or real-user capacity claim is made.
- One existing account-settings lifecycle test emits Angular NG0953 after destroying its output owner; all assertions pass. The live browser journey reported no page errors or HTTP 5xx responses.

## npm advisory triage

Audit reports five affected package entries: three high and two critical, representing three underlying advisories. Direct Angular versions and their exact tooling dependencies are unchanged by this review. Review again by **2026-10-16**, and before enabling SSR/MCP or processing untrusted build configuration.

| Advisory | Current reachability and deferral |
| --- | --- |
| [Angular router SSR DoS](https://github.com/advisories/GHSA-ff3f-86qr-9cv3) | The shipped app is a browser SPA served by nginx; it has no Node SSR service. The advisory explicitly excludes client-only SPAs. Upgrade the Angular family together to a patched version in a separate dependency change. |
| [MCP SDK OAuth credentials](https://github.com/advisories/GHSA-6qxp-vccf-f47h) | SDK 1.30.0 is an exact Angular CLI development dependency. Minos has no SDK OAuth HTTP client/MCP integration; its build/test commands do not enable this flow. Reassess before using CLI MCP with remote OAuth servers. |
| [Piscina prototype-pollution gadget](https://github.com/advisories/GHSA-67c8-pqhq-4rmx) | Piscina 5.2.0 is an exact Angular build dependency and does run during builds. Exploitation additionally requires prior Object.prototype pollution. Current builds consume trusted repository configuration in the build stage; the served image contains nginx/static files, not Piscina. No untrusted-config build service is provided. Upgrade the Angular build toolchain before changing that boundary. |

These findings are not an audit-clean release claim. `npm audit fix --force` was not used: the suggested remediation changes the pinned Angular family outside its declared versions and needs its own build/browser validation.
