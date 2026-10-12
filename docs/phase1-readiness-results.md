# Phase 1 — Submission reliability and candidate verification

Date: 2026-10-12
Status: implementation complete; candidate verification in progress. Development-MVP owner acceptance remains pending.

Scope: P1.1–P1.5 of the [three-phase plan](microservice-readiness-plan.md), authorized by the owner on this date. Five production projects and both existing test projects remain unchanged. No M8, broker, microservice or distributed-host work is included.

## Baseline and implementation

Baseline commit: `53698cb450f0724fad07ce90538e01f931923716`. Its [hosted CI run](https://github.com/phom77/Minos/actions/runs/37936328239) failed. Authenticated read-only inspection confirmed the outcome; logs show Docker HTTP 400 during recovery requests and multiple sandbox failures. The workflow previously relied on the runner's unpinned engine despite the adapter using API 1.51. This is an environment-compatibility hypothesis, not a claim that those logs identified the exact engine version. The updated job installs verified Docker 28.5.2, routes CLI and the adapter to the same socket, and checks API 1.51 before testing.

Existing unrelated working-tree changes in `.dockerignore`, `.gitignore` and `docs/problem-bank-import.md` are preserved outside this implementation. Candidate code is tracked on a separate `codex/` branch; hosted run artifacts identify their exact revision.

| Item | Implemented result |
| --- | --- |
| P1.1 | Baseline SHA and actual failing hosted run inspected; candidate evidence distinguishes local execution from hosted CI. |
| P1.2 | Frontend CI now runs locked install, audit, production build, regressions and production-SPA browser fixtures. Existing backend foundation pipeline remains; browser evidence is uploaded separately. |
| P1.3 | Optional UUID request keys for practice/contest; lifetime bounded fingerprints committed with admission; account ownership constraints; exact replay, safe conflict and post-contest replay with active membership. First-party forms reuse keys after lost responses and use a new key for new work. See [ADR 0017](adr/0017-submission-request-idempotency.md). |
| P1.4 | Angular framework/build/CLI/compiler packages pinned together to 22.2.2; Playwright 1.64.0 is a development-only browser harness dependency. Registry resolution used a clean manifest directory rather than force/legacy-peer-deps. |
| P1.5 | Explicit migration/grant/replay/rollback instructions added to the runbook. HTTPS smoke now uses keyed submissions for every verdict and can run a real browser with disposable practice/contest identities. Owner acceptance is a separate final decision. |

Docker regression verification exposed a repeatable PID-exhaustion case: orphan zombies under the old `sleep` supervisor prevented final cgroup telemetry. The sandbox now uses Docker's init reaper under the same existing PID/resource/isolation limits. The original complete-evidence assertion is retained; no partial verdict or relaxed sandbox limit is accepted.

## Verification ledger

Only completed checks may be recorded as passed. Update the candidate/hosted links and final outcomes after execution.

| Check | Current evidence |
| --- | --- |
| Windows Release build | Passed with zero warnings/errors before the final sandbox-reaper change; final candidate build still required. |
| Angular production build | Passed with no budget warnings. |
| Frontend regressions | 261 passed across 23 files. |
| Production-SPA browser fixtures | Passed author contest creation/publication, registration, lost-response retry across contest end, practice replay/new-submit, result/history, keyboard focus and 320/768/1024/1440 widths. API fixtures only. Screenshots reviewed. |
| npm audit | Before: five affected package entries (3 high, 2 critical). After: zero reported vulnerabilities, including development dependencies, on 2026-10-12. |
| Linux Docker suite, initial | 30 Domain passed; 218 integration passed, 1 failed, 11 skipped. PID-exhaustion final-evidence failure reproduced on a focused rerun before the reaper correction. These runs are not passing acceptance evidence. |
| Linux Docker after reaper correction | Pending. |
| Real HTTPS API/Worker + browser | Pending. |
| New candidate hosted CI / recovery / backup-restore | Pending. Earlier M7 evidence remains historical until the new foundation run completes. |

Local logs/screenshots/TRX are under ignored `artifacts/phase1-*`, `artifacts/contest-browser` and `artifacts/submission-browser`. Temporary credentials remain private and are never written to this report. The fixture uses a separate `minos-phase1` Compose project, private database/storage/workspace volumes and HTTPS port 9443. Sandbox checks run while the normal Worker is stopped, then it is resumed directly without re-running its old migration/grant containers.

## Dependency findings

The prior advisories were [Angular SSR router DoS](https://github.com/advisories/GHSA-ff3f-86qr-9cv3), [MCP SDK OAuth credentials](https://github.com/advisories/GHSA-6qxp-vccf-f47h) and [Piscina prototype-pollution gadget](https://github.com/advisories/GHSA-67c8-pqhq-4rmx). The application remains an nginx-served browser SPA; it adds no SSR or MCP flow. The updated lockfile eliminates all five previously reported affected package entries. This is an audit result at the recorded date, not a guarantee about future advisories.

## Acceptance and limits

- [ ] Final candidate backend/frontend/required browser checks and hosted CI pass.
- [ ] Keyed admission/replay/migration/grants/authorization checks pass on real PostgreSQL.
- [ ] Recovery/backup/restore evidence is current for the candidate, including retained key metadata.
- [ ] Owner reviews the evidence and explicitly accepts the development MVP.

Native Isolate/multiple-box acceptance is separate; the current WSL distro has Docker integration disabled, so local Linux checks use the existing non-root checks container. Google remains disabled without a real OAuth client. Public hosting, password-recovery/email-verification delivery and capacity/retention policy are still separate public-release decisions.
