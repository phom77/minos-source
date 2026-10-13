# Judging ownership and extraction candidate

Date: 2026-10-13. Phase 2 design, **not an approved service or transport implementation**. The five production projects, local PostgreSQL polling, one execution host and existing grants remain authoritative. Phase 3 requires the owner's decision; M8 remains outside scope.

## Current data authority

“Core” below means the current API/Application use cases and PostgreSQL, not a new project. Infrastructure owns persistence and filesystem adapters; Domain owns invariants. A role in the table describes the current runtime writer, not ownership of business rules.

| Data / source of truth | Current writer and readers | Consistency / authorization | Retention and migration implications | Candidate ownership |
| --- | --- | --- | --- | --- |
| `accounts.accounts` profile, role, disabled state | API writes; authenticated account use cases read; administrators alone change roles | Active-account checks in Application; role changes revoke target sessions; no Worker grant | Retained project data; stable account IDs must survive any future credential migration | Core |
| Identity credentials/provider-subject mappings; protected cookies/keys | Infrastructure credential store; API web authentication; no Worker reader | Platform hashing; unique stable provider identities; explicit proven linking, no email merge | Credentials and cookie keys remain private; paired offline restore includes keys; no transfer to executor | Core authentication boundary |
| Editable `problems.problems`, samples/tests | Active Setter/Admin owner via API; Worker reads allowed metadata only | Immutable owner; revision/publication use cases; Domain protects changes | Archive preserves versions; editable authoring is not a submitted version | Core |
| Published versions, samples, test order/metadata | API inserts snapshots; public projection excludes private artifacts; trusted executor reads private pinned metadata | Immutable distinct version ID; submission/contest permanently pin it; PostgreSQL constraints protect test count/order | Lifetime retention; capacity schema supports 1–1500 tests; never roll back to 100 while larger versions exist | Core version authority; executor receives only authorized immutable snapshot |
| Private input/answers under opaque artifact keys | Infrastructure upload store writes; trusted executor verifies/reads; sandbox gets only current input | Publication verifies prerequisites; execution preflight verifies all tests, then verified per-test reads; no paths/contents in public DTOs | Keep referenced artifacts; only unreferenced staging older than 24h removed; backup with DB while both hosts stopped | Core artifact authority; controlled private transfer if a future remote host is approved |
| `contests.contests`, problem selection, registrations | API via organizer/participant use cases; no Worker grant | Frozen published schedule/versions; unique registration; server start-inclusive/end-exclusive admission window | Retain links to immutable versions; cannot replace distributed authorization with a stale membership cache | Core |
| `evaluation.submissions`, `submission_requests`, `contests.submissions` | API inserts admitted source/pin/key/link; Worker updates submission lifecycle only; API owner reads | Shared capacity advisory lock; submission + job + key + contest link in one transaction; exact request replay precedes new capacity/window checks after active-account/resource authorization | Source and exact-key fingerprint retained for submission lifetime; no executor key-table access | Keep one Core admission authority |
| `evaluation.judge_jobs`, attempts | API inserts job atomically; Worker short `SKIP LOCKED` claim/renew/fail; API reads attempts | Attempt UUID + lease token + authenticated Worker identity; 30s persisted lease, renewal every 10s; three attempts, retry delays 1s/5s | Persist failures/expired attempts; a delivery ID is not authority; no dual writer during cutover | Core lease authority behind a future trusted interface |
| Result + ordered test evidence | Worker currently commits guarded result through Infrastructure; owner HTTP reads via repeatable-read snapshot | Live accepted attempt only; result/submission/job/attempt transaction; duplicate accepted completion idempotent; full pinned test set required | Immutable lifetime history; retain historical pre-complete-test evidence without rewriting/fabricating | Core official-result authority; executor proposes bounded evidence |
| Stage workspaces/compiled executable/sandbox lifecycle | Trusted single Linux execution host; no API execution; current test sandbox receives read-only own subdirectory | Host lock, bounded slots/memory and class FIFO; fresh test process/state; cleanup before permit release; uncertainty blocks claims | Scratch cleaned after stage/recovered at startup; ephemeral executable need not become durable source of truth | Execution host |
| Operational aggregates/metrics/logs | Admin use case queries DB; host meters/logs emit fixed labels and restricted opaque IDs | Active administrator inside Application; no source, private artifacts, credentials or lease tokens in diagnostics | Evidence reports contain approved fixture summaries; access to runtime logs is trusted maintenance access | Core aggregates + each host's process metrics |

Evidence: [runtime grants](../scripts/postgres-runtime-grants.sql), [practice admission](../src/Minos.Infrastructure/Persistence/SubmissionRepository.cs), [contest admission](../src/Minos.Infrastructure/Persistence/ContestRepository.cs), [job authority](../src/Minos.Infrastructure/Persistence/JudgeJobRepository.cs), [official execution](../src/Minos.Infrastructure/Evaluation/OfficialSubmissionExecutor.cs), [owner authorization](../src/Minos.Application/Evaluation/SubmissionUseCases.cs), [admin diagnostics](../src/Minos.Application/Evaluation/JudgingOperationsUseCases.cs).

## Transactions that must survive a boundary

1. Admission serializes the bounded active counts and inserts submission/job/request metadata/optional contest association atomically. An accepted HTTP response can be lost; replay resolves the original submission and version. Do not move job creation behind an unreliable “publish after commit” call.
2. Claim locks only eligible job rows, expires the previous attempt when necessary, starts the new attempt and submission state in one commit. Delivery remains a repeatable ID hint. Claims occur only when a processing slot is available.
3. Renewal/failure/completion re-read the locked persisted job in an independent scope. Stale credentials never finish work; three infrastructure failures or exhausted expired leases yield SystemFailed without a contestant verdict.
4. Complete validates contiguous ordered evidence for the pinned count and Domain verdict rules, then commits result/test rows, attempt success, job completion and submission completion together. Owner reads never combine pre-commit status with post-commit evidence.
5. Filesystem writes and PostgreSQL are not one ACID transaction. Opaque verified artifacts and referenced-data retention handle this existing boundary; publication checks and execution verification remain mandatory. Offline paired backup is the current consistency policy.

## Candidate contract v1 (design only)

The candidate moves trusted judging orchestration/execution behind a narrow interface while keeping admission, leases, immutable version authority and official results in Core. Compile/runtime/checking stay together on one execution host. The initial candidate remains single-host; it does not approve a remote Worker fleet, broker, outbox or new production project.

If extraction is approved later, an adapter must translate these **new wire contracts** into existing Application use cases. `ExecutionRequest` and `ExecutionEvidence` currently contain in-process byte arrays, including stdout/expected checking inputs; serializing those ports wholesale is not the design.

### Identity, versions and bounds

- Private mutually authenticated channel between Core and a trusted executor; service identity comes from the channel and an allowlist, not a caller-controlled `workerId` alone. User cookies, provider tokens, request-key metadata and credential database access never cross it. Exact credential provisioning/rotation must be accepted before a transport implementation.
- Explicit major `contractVersion=1` and a bounded supported capability set: `cpp20`, the exact admitted GCC image/toolchain digest, complete-test judging policy, exact checker and evidence schema version. Reject unsupported major/capabilities **before claim**. Additive optional fields only inside the same major; required-field/schema changes require a new major and a tested compatibility window.
- Opaque job/submission/attempt/version IDs may be trusted correlation fields. Lease tokens are authentication secrets: redact them from logs, metrics and reports. No high-cardinality metric labels. UTC timestamps are informational on the executor; Core's clock decides leases/windows.
- Claim source ≤65,536 UTF-8 bytes, language ≤32 chars and toolchain identity ≤160 chars. Proposed wire claim uses bounded `sourceUtf8Base64` (≤87,384 ASCII bytes) inside a ≤128-KiB envelope, decoded/validated strictly; arbitrary JSON-escaped source could exceed that envelope and is not the proposed encoding. This does not change the existing public source DTO. Manifest ≤1,500 ordered test descriptors; a proposed 2-MiB descriptor envelope is a new transport bound, to be tested rather than assumed from an in-process object size.
- Each private input/answer ≤8 MiB; stream and verify exact stored digest/length with a bounded reader; no all-tests preload. Answer stays in trusted executor/checker memory and never enters sandbox. No caller-supplied filesystem path, arbitrary URL or archive extraction destination.
- Compiler diagnostics ≤32 KiB UTF-8; completion ≤1 MiB proposed, ≤1,500 contiguous test records with bounded numeric values and typed enums. Runtime stdout/stderr do not cross completion or enter durable public evidence. Transport timeouts/body limits and schema validation are mandatory; proposed envelopes require maximum-size serialization tests before acceptance.

### Operations

| Operation | Input / response | Authority and retry semantics |
| --- | --- | --- |
| Discover capabilities | Major/schema/toolchain/checker policies; no source/artifacts | Fail closed before accepting deliveries if incompatible. Read-only retry is bounded by host readiness. |
| Receive delivery | Job ID only (current polling implementation) | Does not claim, acknowledge durable work or remove the job. Duplicates and old IDs are expected. No MQ implementation in Phase 2. |
| Claim(job ID) | Authenticated service identity, accepted capabilities → claimed job/submission/attempt, opaque lease token, expiry, immutable pin/source/limits | Core short transaction; no source/artifact access without a live owned claim. Null/not-claimable is a normal disposition, not an error/retry attempt. |
| Read pinned manifest / artifact | Live attempt credentials + pin + opaque reference and bounded stream | Core verifies that reference belongs to that exact published version and attempt. No generic artifact-key download endpoint; no path/address authority delegated to executor. Missing/corrupt data is infrastructure failure. |
| Renew | Job/attempt/lease credentials → accepted/stale, current expiry | Core clock decides; independent renewal scope. Proposed network deadline <10s interval, allowing cancellation before lease expiry. If uncertain, halt stages and clean up; never infer ownership from a local timeout. |
| Complete | Attempt credentials + complete ordered resource/exit/cleanup/check evidence | Core revalidates version count, verdict consistency and live lease; performs existing atomic commit. Duplicate accepted attempt returns accepted without overwriting historical evidence. Never trust a proposed summary over per-test evidence. |
| Fail | Attempt credentials + fixed infrastructure failure kind; safe flags only | Core owns three-attempt cap/backoff and persisted state. Executor never schedules an unbounded local retry or returns a contestant verdict for infrastructure uncertainty. |
| Inspect disposition | Attempt identity over trusted channel; bounded state/accepted attempt | Required to resolve lost completion/failure acknowledgement. Read does not create work or extend a lease. Exact transport contract/test is prerequisite to extraction. |

Compiled executable may stay local to the claimed attempt. The same pinned immutable configuration drives every test, complete judging continues after contestant failure, and result summaries exclude compilation. Retain stage permits and the processing scope until cleanup is complete. If the executor loses Core connectivity while cleaning up, cleanup still finishes; ownership is resolved through persisted disposition/reclaim afterward.

## Error matrix and regression evidence

The “future boundary test” column is a requirement for Phase 3, not a claim that network fault handling exists today. Existing real PostgreSQL/sandbox tests establish the current invariant.

| Failure | Expected durable state; retry owner | Current evidence / future boundary test |
| --- | --- | --- |
| Duplicate/stale delivery | One active attempt; completed work not reclaimed; Core claim returns not-claimable | `JudgingConcurrencyIntegrationTests.Delivery_is_only_an_id_and_duplicate_or_expired_ids_keep_database_attempt_authority`; duplicate delivery through proposed transport |
| Executor dies before claim | Pending unchanged; next polling can discover ID | Worker delivery/claim tests; disconnect before request arrival |
| Claim commits, reply lost / process dies after claim | Leased until expiry; executor does not blindly claim new local work; Core eventually expires/reclaims within attempt cap | Real M7 forced-kill/recovery and expired-lease tests; drop claim response after commit |
| Lease expires / renewal rejected | Cancel work, finish cleanup; stale evidence cannot commit; Core reclaim authoritative | Lease guard and Worker scheduling tests; drop renewal response and delayed stale completion |
| Completion commits, acknowledgement lost | Completed result/accepted attempt remains; same guarded completion/inspect is idempotent | Duplicate completion and paired restore evidence; drop complete response after commit, compare exact result/attempt |
| DB/Core unavailable during run | No fabricated verdict; stop on uncertain lease, clean up; durable lease eventually expires | M7 database-outage rehearsal; partition trusted interface, reconnect and inspect/reclaim |
| Manifest/artifact missing or corrupt | Fixed infrastructure failure → bounded retries → SystemFailed, no partial contestant verdict | Official judging missing/changed artifact tests; scoped download denial, corrupt stream/hash and mid-stream EOF |
| Runner/measurement fault or incomplete result | Reject contestant result; bounded infrastructure path; no Accepted without full evidence | Official/Domain evidence tests; omit test/measurement, duplicate order, inconsistent primary verdict or oversize completion |
| Cleanup uncertain | Stop all new stage reservations and job deliveries until successful startup recovery; preserve durable authority | `ExecutionBudgetTests`, native/Docker cleanup fixtures; remote peer disconnect during cleanup and host restart |
| Shutdown/cancellation while queued/running | No new claims, cancel stages, retain scopes/permits until cleanup; uncompleted lease expires | Scheduler/cancellation/M7 checks; interrupt each transport phase and assert zero leaked resources |
| Unsupported/mixed major/toolchain/policy | Reject before claim; no downgrade of complete judging; old accepted results readable | Current pinned-toolchain/startup tests; v1/v2 capability negotiation and maximum wire envelopes |
| Invalid service identity / artifact scope | Deny with bounded safe error, no source/keys/artifacts/claims | Current grants/owner tests; revoked certificate, forged worker string and cross-attempt artifact request |
| Core overload / contestant POST uncertainty | Safe 429 with retry hint, no durable row for rejected request; same-key replay returns original work | Contest capacity and request-key tests plus readiness mixed workload; HTTP retry under proposed deployment |

Proposed safe transport error codes: `not-claimable`, `stale-attempt`, `incompatible-contract`, `denied`, `invalid-evidence`, `dependency-unavailable`. Do not reuse contestant verdicts as transport errors or leak exception messages. 400/401/403/409/429/503 mappings are design candidates; internal leases and authorization are the authority, regardless of HTTP status.

## Migration and rollback prerequisites

No Phase 2 migration, new grant or service was introduced. For any later approved extraction:

1. Implement/test trusted capability/claim/renew/complete/artifact/disposition adapters with the existing invariant and authorization tests. Make Core use-case resource authorization effective for non-HTTP trusted calls too.
2. Keep admission/job/key/contest/result tables under one writer authority. Avoid a cross-database transaction by retaining them together. Any changed ownership/grants must ship in an explicit additive migration and operational check; remove executor direct DB privileges only after new access is verified.
3. Stop the old execution host and prove host lock/cleanup ownership before starting the new one; no simultaneous old/new executors. Drain or let persisted leases expire; preserve accepted results and key metadata.
4. Rehearse both peer versions, reply-loss faults, paired backup/restore and binary rollback. Rollback stops the new host and returns to the prior compatible binaries/configuration on the same authority; never discard results or idempotency keys to force a rollback.
5. Compare the same workload and correctness evidence before/after. Remote networking, public hosting or distributed execution needs its own accepted security/operations scope. A broker additionally requires durable handoff, acknowledgement, recovery and duplicate processing tests; a job ID port alone is insufficient.
