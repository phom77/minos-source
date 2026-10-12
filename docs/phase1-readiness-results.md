# Phase 1 — Submission reliability and candidate verification

Implementation authorized: 2026-10-12
Verification updated: 2026-10-13
Status: Phase 1 implementation complete and verified; candidate ready for owner review. Development-MVP owner acceptance remains pending.

Scope: P1.1–P1.5 of the [three-phase plan](microservice-readiness-plan.md), authorized by the owner on 2026-10-12. Five production projects and both existing test projects remain unchanged. No M8, broker, microservice or distributed-host work is included.

## Baseline and implementation

Baseline commit: `53698cb450f0724fad07ce90538e01f931923716`. Its [hosted CI run](https://github.com/phom77/Minos/actions/runs/37936328239) failed. Authenticated read-only inspection confirmed the outcome; logs show Docker HTTP 400 during recovery requests and multiple sandbox failures. The workflow previously relied on the runner's unpinned engine despite the adapter using API 1.51. This is an environment-compatibility hypothesis, not a claim that those logs identified the exact engine version. The updated job installs verified Docker 28.5.2, routes CLI and the adapter to the same socket, and checks API 1.51 before testing.

Existing unrelated working-tree changes in `.dockerignore`, `.gitignore` and `docs/problem-bank-import.md` are preserved outside this implementation. Candidate code is tracked on a separate `codex/` branch; hosted run artifacts identify their exact revision.

Verified code candidate: `2a3cc2a0e354780d9621c431af97a2a3ce2b7a5e` on `codex/phase1-submission-reliability`; [final candidate CI](https://github.com/phom77/Minos/actions/runs/38285252130) passed both jobs. The subsequent evidence-only documentation commit skips another CI run; it changes no production code, tests, scripts, workflow or package lockfile. No merge or original-stack upgrade is included.

| Item | Implemented result |
| --- | --- |
| P1.1 | Baseline SHA and actual failing hosted run inspected; candidate evidence distinguishes local execution from hosted CI. |
| P1.2 | Frontend CI now runs locked install, audit, production build, regressions and production-SPA browser fixtures. Existing backend foundation pipeline remains; browser evidence is uploaded separately. |
| P1.3 | Optional UUID request keys for practice/contest; lifetime bounded fingerprints committed with admission; account ownership constraints; exact replay, safe conflict and post-contest replay with active membership. First-party forms reuse keys after lost responses and use a new key for new work. See [ADR 0017](adr/0017-submission-request-idempotency.md). |
| P1.4 | Angular framework/build/CLI/compiler packages pinned together to 22.2.2; Playwright 1.64.0 is a development-only browser harness dependency. Registry resolution used a clean manifest directory rather than force/legacy-peer-deps. |
| P1.5 | Explicit migration/grant/replay/rollback instructions added to the runbook. HTTPS smoke now uses keyed submissions for every verdict and can run a real browser with disposable practice/contest identities. Owner acceptance is a separate final decision. |

Docker regression verification exposed a repeatable PID-exhaustion case: orphan zombies under the old `sleep` supervisor prevented final cgroup telemetry. The sandbox now uses Docker's init reaper under the same existing PID/resource/isolation limits. The original complete-evidence assertion is retained; no partial verdict or relaxed sandbox limit is accepted.

Hosted CI additionally exposed a fixture that requested 2–4 stage slots on a two-CPU runner, violating the required management CPU reserve. Real concurrency fixtures now clamp stage/class quotas to available CPU headroom while retaining concurrent jobs, scopes and lease renewals; production host validation is unchanged. Multi-slot execution is covered by the local Docker profile and deterministic scheduling tests. The live browser found owner-result overflow at 320px with long source/table content; bounded grid tracks and component minimum widths fix it without changing branding, navigation or data exposure. Browser fixtures now verify this regression with long source at four widths.

The later [run on `57c0de3`](https://github.com/phom77/Minos/actions/runs/38282153192) passed frontend and all backend tests, but its rehearsal counted nine pending jobs instead of eight: synthetic integration jobs could remain in the shared database. The foundation harness now clones the migrated empty schema into `minos_tests` with explicit restricted database ACLs before tests, and keeps `minos` for the actual HTTP/workload/recovery rehearsal. Assertions and production behavior are unchanged; fixture order no longer determines the rehearsal queue.

The [next run](https://github.com/phom77/Minos/actions/runs/38283565416) passed isolated tests/workload/SIGKILL, but database recovery surfaced incomplete exec state twice. Docker's [attachment router](https://github.com/moby/moby/blob/v28.5.2/api/server/router/container/exec.go) sends headers before invoking start, and its [inspection implementation](https://github.com/moby/moby/blob/v28.5.2/daemon/inspect.go) exposes a nullable exit code. The adapter now waits within the existing three-second transport budget for not-running/null-exit transitional state. Persistent missing evidence remains an infrastructure fault; cancellation remains responsive. Three deterministic protocol regressions cover transition to running/completion, bounded missing evidence and cancellation. The final real recovery run passed without increasing attempts, inventing an exit code or relaxing evidence requirements.

## Verification ledger

The initial failing runs remain recorded; they are not counted as passing acceptance evidence.

| Check | Current evidence |
| --- | --- |
| Windows locked restore / Release build | Passed with zero warnings/errors after the sandbox-reaper correction. |
| Windows checks | 30 Domain tests, 10 selected architecture/build/authorization/private-ACL tests, and 3 exec-state protocol regressions passed. |
| Angular production build | Passed with no budget warnings. |
| Frontend regressions | 261 passed across 23 files. |
| Production-SPA browser fixtures | Passed author contest creation/publication, registration, lost-response retry across contest end, practice replay/new-submit, owner result/history with long source, keyboard focus and 320/768/1024/1440 widths. API fixtures only. Screenshots reviewed. |
| npm audit | Before: five affected package entries (3 high, 2 critical). After: zero reported vulnerabilities, including development dependencies, on 2026-10-12. |
| Linux Docker suite, initial | 30 Domain passed; 218 integration passed, 1 failed, 11 skipped. PID-exhaustion final-evidence failure reproduced on a focused rerun before the reaper correction. These runs are not passing acceptance evidence. |
| Local Linux Docker after reaper correction | 71 focused sandbox/idempotency/grant tests passed; one opt-in benchmark skipped. TRX retained. |
| Hosted Linux full suite | Final candidate: 30 Domain and 222 integration tests passed, 11 configuration/platform skips, zero failures; Docker sandbox tests enabled. Fresh/repeated/upgrade migration and exact grants covered. |
| Real HTTPS API/Worker | All seven verdicts passed with complete-test evidence; replay after completion retains the same submission/attempts; cross-account denial and private projections passed. |
| Real HTTPS browser | Passed sign-in from the practice screen, deliberately lost response after actual admission, exact retry, real Worker Accepted, owner evidence/source at 320/1440, Settings section navigation and contest Worker Accepted. No page errors. |
| Hosted recovery / backup-restore | Final candidate: workload/overload, renewed leases, SIGKILL/orphan cleanup, database interruption, missing-artifact bounded retries, corrupt/unsafe backup rejection, fresh-target restore and judging passed. Restored request key returns the exact original attempt/result; `phase1_restored_request_key_replays_original_attempt=passed`, `m7_checks=passed`, `foundation_checks=passed`. |

Final hosted workload: eight submissions/two owners, maximum queue wait 8.330 seconds against the fixture's 120-second target, maximum end-to-end 15.040 seconds. This is development fixture evidence, not public production capacity. Final hosted logs/artifacts are associated with run `38285252130`, attempt 1. The local real-browser run preceded the final exec-state correction; final-backend real HTTP/Worker/fault behavior is covered by hosted CI, and the frontend/contracts are unchanged.

Local logs/screenshots/TRX are under ignored `artifacts/phase1-*`, `artifacts/contest-browser` and `artifacts/submission-browser`. Hosted logs are stored per run ID to avoid mixing failed and successful runs. Temporary credentials are never written to this report; the fixture environment copy was removed after cleanup. The fixture used a separate `minos-phase1` Compose project, private database/storage/workspace volumes and HTTPS port 9443. Its containers/volumes were removed, and the normal API/Worker/web/PostgreSQL stack is healthy. Original persistent data and release images were not upgraded; applying this candidate there requires the stopped-host migration/grant/binary procedure.

The initial live harness runs exposed a too-short publication lead time, an incorrect legacy login route/ambiguous selector, and the actual mobile overflow. These are fixed and the final live run passed; earlier failed logs remain historical. API verdict evidence is in `artifacts/phase1-live-http-browser.log`; the final browser-only rerun is in `artifacts/phase1-live-browser.log` and `artifacts/phase1-live-browser/`. The optional `--browser-only` mode creates the same disposable fixture without repeating already-verified API verdict cases. A clean reproduction can use the complete `--browser-smoke` mode instead.

## Dependency findings

The prior advisories were [Angular SSR router DoS](https://github.com/advisories/GHSA-ff3f-86qr-9cv3), [MCP SDK OAuth credentials](https://github.com/advisories/GHSA-6qxp-vccf-f47h) and [Piscina prototype-pollution gadget](https://github.com/advisories/GHSA-67c8-pqhq-4rmx). The application remains an nginx-served browser SPA; it adds no SSR or MCP flow. The updated lockfile eliminates all five previously reported affected package entries. This is an audit result at the recorded date, not a guarantee about future advisories.

## Acceptance and limits

- [x] Final candidate backend/frontend/required browser checks and hosted CI pass.
- [x] Keyed admission/replay/migration/grants/authorization checks pass on real PostgreSQL.
- [x] Recovery/backup/restore evidence is current for the final backend, including retained key metadata.
- [ ] Owner reviews the evidence and explicitly accepts the development MVP.

Native Isolate/multiple-box acceptance is separate; the current WSL distro has Docker integration disabled, so local Linux checks use the existing non-root checks container. Google remains disabled without a real OAuth client. Public hosting, password-recovery/email-verification delivery and capacity/retention policy are still separate public-release decisions.
