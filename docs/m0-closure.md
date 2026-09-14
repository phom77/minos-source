# M0 closure summary

Closed: 2026-09-13\
Status: **M0 complete — planning baseline accepted**\
At closure: **M1 eligible to begin; not started**. Subsequent progress: [M1 work log](milestones/m1-execution-feasibility.md).

Related documents: [Decision register](decisions.md), [Project design](project-design.md), [Implementation roadmap](implementation-plan.md)

## Accepted decisions

| Decision | Accepted baseline |
| --- | --- |
| [D01 / ADR 0001](adr/0001-initial-audience-and-access-policy.md) | Developer-only development, no real users or enrollment/approval workflow. Finished-product self-service email/password registration and Google sign-in; no approval for normal users. |
| [D02 / ADR 0002](adr/0002-judge-development-environment.md) | Repository stays on Windows D:. Worker runs in WSL2 Ubuntu; Docker first for compile/run. Private Linux-native submission copies; no Windows drive/repository exposure. Backend-independent execution contract; future Isolate runs directly on Linux. No dedicated development VM; production hosting deferred. |
| [D03 / ADR 0003](adr/0003-official-judging-mvp.md) | Official judging is the first end-to-end MVP. Custom-input Run follows reliable official judging. |
| [D10 / ADR 0004](adr/0004-minimum-execution-safety-baseline.md) | No direct contestant execution in API/Worker hosts; network disabled by default; bounded wall time, CPU, memory, processes/threads, stdout/stderr, and scratch space; non-root execution with unnecessary capabilities removed; structured exit/timeout/resource/output/infrastructure evidence. |

D10 is an accepted minimum baseline. **Numeric tuning remains M1 work; final verdict precedence and resource-accounting semantics remain M6 work.** No policy beyond that baseline is accepted by this closure.

## Architecture assumptions

- One developer, no real users; validation uses developer-run workflows, test identities, and synthetic fixtures.
- C#, ASP.NET Core, PostgreSQL, EF Core; modular monolith with selective Clean Architecture/DDD. Detailed module and project organization remains proposed.
- API and Worker are distinct hosts. The Worker coordinates compile/run/check; contestant source is compiled/executed only through the isolated execution backend.
- WSL2 already exists; Ubuntu/Docker connectivity and actual resource support still need verification in M1.
- Repository location does not determine runner mounts. Source and required input are staged into private Linux-native workspaces; hidden answers stay outside contestant access.
- The execution adapter reports evidence; the Submission/Judging model and durable job lifecycle remain independent of Docker.
- Use milestone notes for ordinary setup choices. Add technologies or formal decisions only for demonstrated needs affecting correctness, security, or evolution.

## MVP boundary

The development MVP covers self-service email/password and Google authentication, minimal profiles/permissions, problem authoring and testcase management, problem browsing, C++ standard-input/output submissions, asynchronous official judging, basic results/resource reporting, submission history, and reliability/operational basics.

Its central demonstration is problem preparation/publication -> official submission -> isolated compilation/execution/checking -> durable result/history. Authentication mechanics, publication/version modeling, queue details, checking, and final outcome/accounting policies are resolved in their dependent milestones; they are not silently finalized here.

Custom Run, contests, ratings, discussions, additional languages, advanced checkers/scoring, interactive problems, and distributed workers are outside the first official-judging release. No pilot cohort or enrollment management is required.

## Deferred decisions

| When needed | Remaining decisions/work |
| --- | --- |
| M1 | D09 toolchain/image selection; D13 private staging mechanics; D10 numeric smoke limits and actual enforcement/evidence collection. |
| M2 | D04 module ownership and D15 project packaging. |
| M3 | D11 authentication/session/account lifecycle mechanics; relevant D17 access/visibility rules. |
| M4–M6 | D05 publication/version rules; D12 reference validation; D13 durable artifacts; D06/D07 jobs/recovery; D08 lifecycle/results; D09 checker; D16 test order; D10 final verdict/accounting policy in M6. |
| Before M7 | Remaining D17 retention/history and synthetic workload targets. |
| Beyond core MVP | Custom Run after reliable judging; D18 manual rejudge workflow remains a deferred proposal. Production hosting, Isolate adoption, scaling infrastructure, and advanced product features await evidence/need. |

Later recommendations remain proposals until resolved with their dependent work. Closing M0 does not approve all 18 register topics.

## Known risks

- The actual WSL2/Docker configuration may not enforce or report every required limit as expected; M1 must discover unsupported controls before judging integration.
- Incorrect mounts or unsafe staging could expose Windows/repository files or another submission; path and isolation probes are required.
- Unbounded compilation, output, scratch use, or surviving containers could exhaust the development machine; use conservative test bounds and verify cleanup.
- Docker control access is sensitive. The trusted Worker manages the backend; contestant programs receive no daemon access, host credentials, or privilege escalation configuration.
- Local resource contention can distort timing/memory observations. M1 findings do not finalize official accounting or production performance.
- Infrastructure errors and resource termination signals can be ambiguous. Preserve structured evidence; do not infer final contestant verdicts from a generic exit code.

## M1 dependencies

1. Verify WSL2 Ubuntu and access to a Linux Docker engine, including the actual daemon location and supported controls.
2. Select a pinned C++ toolchain/image during M1 and server-controlled compile/run configuration.
3. Establish a private Linux-native workspace that the selected engine can access without exposing Windows D: or the repository.
4. Implement the small backend-independent execution/evidence contract, then its Docker adapter; the main API/domain/database solution is not required for this spike.
5. Configure bounded compile/run, network and privilege isolation, bounded capture/scratch, and cleanup before untrusted-program experiments.
6. Use trusted smoke tests followed by controlled resource/failure fixtures; record configuration, outcomes, and limitations from the D02/D10 checks.

These are M1 setup/validation tasks, not outstanding M0 approvals. M1 does not require Google credentials, the production host, Isolate, an external broker, or a complete durable queue.

## Final completion checklist

- [x] D01 accepted and development access separated from product authentication.
- [x] D02 accepted, with private Linux workspaces and replaceable execution boundary.
- [x] D03 accepted: official judging first; custom Run deferred.
- [x] D10 minimum safety/evidence baseline accepted.
- [x] Numeric limits left to M1; final verdict/accounting semantics left to M6.
- [x] Later decisions assigned to dependent milestones; production hosting deferred.
- [x] Accepted assumptions, MVP scope, risks, and M1 dependencies recorded and synchronized.
- [x] Project owner explicitly accepted the remaining gates and requested M0 closure.

**M0 complete. M1 can begin as a controlled development feasibility spike.** Start with verification and configured containment; resource/adversarial tests follow those checks. This closes planning, not sandbox validation. At M0 closure, M1 had not started and no implementation code had been written during M0. Subsequent M1 work is recorded separately.
