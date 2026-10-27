# Architecture

Minos uses five production projects, one PostgreSQL database and a separately hosted Linux judge. The API and Worker share application contracts and persistence, without calling each other's hosts. There is no broker, distributed worker topology or network execution service.

## Dependency boundaries

| Project | Project dependencies | Responsibility |
| --- | --- | --- |
| Domain | None | Entities, invariants and legal state transitions |
| Application | Domain | Commands/queries, resource authorization and external ports |
| Infrastructure | Application, Domain | EF Core, repositories, Identity, private storage and execution |
| Api | Application; Infrastructure at composition/hosting boundaries | Transport contracts, cookies, CSRF, safe errors and health |
| JudgeWorker | Application; Infrastructure at composition/hosting boundaries | Delivery, job scopes, cancellation and lease renewal |

Domain and Application have no EF Core, ASP.NET Core or external SDK dependencies. Repositories are focused on capabilities; they expose neither `DbContext` nor `IQueryable`. Architecture tests enforce the project graph and hosting-only Infrastructure references.

## Accounts and authorization

Domain accounts own display name, active/disabled state and Solver/Setter/Administrator role. Infrastructure owns Identity password hashes and stable provider/subject mappings. The API owns cookie and OAuth transport.

Application rechecks active-account status and resource ownership for each protected use case, including callers outside HTTP. Setter/Admin roles allow authoring, but do not bypass another author's ownership. Only active Administrators assign roles or moderate comments. Role changes and security operations revoke sessions. Initial administrator provisioning is a trusted maintenance command, not a registration option.

Authentication uses protected, Secure, HttpOnly cookies, an eight-hour absolute lifetime and per-request security-stamp validation. Mutations require CSRF protection. Google mappings never merge accounts solely because email matches; explicit linking requires proof of the current account and provider. Live Google consent and browser-compatible linking remain incomplete.

## Data ownership

| Area | Model and consistency rules |
| --- | --- |
| Problems | `Problem` is stable editable state; published versions are immutable snapshots. Ownership cannot change. Archive preserves historical versions and referenced tests. |
| Classification | Mutable difficulty/topics belong to the stable problem. Source evidence, policy and basis version are retained. A later publication marks reassessment without clearing labels. |
| Library progress | Derived from the active account's submissions across versions. Any completed Accepted keeps the problem Solved; later failures do not undo it. |
| Contests | Drafts select published versions. Publication freezes schedule and selection. Registration is unique and idempotent. |
| Submissions | Permanently owned by the active submitting account and pinned to a published version, language and toolchain. Source is available only in owner detail. |
| Jobs/results | PostgreSQL owns claims, lease tokens, attempts and the one authoritative result. An expired attempt cannot complete work. |
| Comments | Independent aggregates attached to stable problems, with immutable author, viewed version and root association. They are not loaded as a `Problem` collection. |

## Transaction boundaries

**Submission admission.** A short PostgreSQL transaction advisory lock serializes capacity checks with creation of Submission, JudgeJob and optional request identity. Defaults are five active submissions per account and 32 globally. Rejected admission creates no work and returns 429 with a retry hint.

**Request replay.** Optional account-scoped UUID keys bind request kind, target, language and exact source through a bounded fingerprint. Exact replay returns the original submission and version without consuming capacity; changed payloads conflict. The key/fingerprint is committed with admission and retained for the submission lifetime. First-party clients always send keys. Account and resource authorization still apply.

**Contest admission.** Registration, pinned problem selection and a start-inclusive/end-exclusive server window are verified before atomically persisting the contest association, submission and job. The window is rechecked inside admission. Authorized replay can return previously admitted work after the end; it creates no new submission. Organizers cannot enter their own contests. Problems remain publicly readable outside the contest.

**Job claim/completion.** Short `SKIP LOCKED` claims create unique attempts and expiring leases. Completion locks the job, checks attempt/token/Worker ownership and lease validity, verifies complete test evidence, then writes result, tests and terminal states together. Repeated completion of the accepted attempt is idempotent.

**Comments.** Writes lock the problem row, recheck public availability and parent state, and save with revision concurrency. Database constraints/triggers protect same-problem associations, immutable identity and two-level depth. Delete/hide erases body and spoiler while retaining replies; terminal content is not recoverable through the application.

## Reads and privacy

Public projections omit private tests, paths, classification rationale and source evidence. The problem library excludes drafts/archived problems; topic totals follow the same visibility rule. Search is ANDed with a bounded All/Any filter group. Any personal-status criterion requires an active account. Personalized library, comment capabilities, submission and operational responses are not shared-cacheable.

Submission history omits source. Result detail contains bounded compiler diagnostics and per-test measurements, never hidden input, expected answers or runtime stdout/stderr. API errors use safe ProblemDetails with a trace ID; raw stack traces and sensitive exception details are withheld.

Performance comparisons require completed Accepted results with identical problem version, language, toolchain and measurement profile. Each other account contributes its latest eligible result; the owner's whole history is excluded. At least 20 other accounts are required. Beats counts strictly greater resource values; ties are separate. Historical profiles remain null. Aggregation stays inside PostgreSQL and returns bounded histogram output without peer identities.

The measurement profile conservatively groups Linux boot ID, kernel/CPU/architecture, available CPU count, host memory, runner, toolchain, execution budget and native box count. Same-boot restarts with the same configuration retain a cohort; a host/VM reboot or configuration change starts another. Missing optional profile capture does not invalidate real verdict evidence, but prevents comparison eligibility. Profiles do not eliminate host-load noise or millisecond quantization.

## Deployment and design choices

The local stack serves Angular and API under one HTTPS origin. API and Worker have separate database identities; a third migrator identity owns schema changes. Runtime privileges are explicit, with no default access to future tables. Worker has no credential, classification, request-key or comment-table privileges.

PostgreSQL polling keeps admission and result authority in the existing transaction model. Execution cost includes a fresh sandbox for each test; adding a service transport would not remove that cost. A service extraction, broker, custom execution or distributed Worker requires a separate design decision and evidence of need.

See [judging](judging.md) for execution semantics and [operations](operations.md) for deployment and recovery.
