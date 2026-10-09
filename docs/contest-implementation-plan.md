# Contest creation and participation

Owner authorization: 2026-10-09, including the clarification that only active Setter/Admin accounts may organize contests. Ordinary active users may join and submit. Organizer-role applications, custom Run (M8), ratings and distributed execution remain deferred.

## Product and contracts

This increment supports public practice contests built from immutable published problems. Published problems remain public in the existing library; hiding the contest's problem list before its start does not make those problems secret. A future exclusive/unpublished contest problem policy needs its own publication and disclosure design.

- An organizer creates a private draft with title, description, UTC start/end and an ordered selection of 1–26 distinct problems. Duration is 1 minute to 7 days. Only its active Setter/Admin owner edits or publishes it. Revision checks reject stale edits.
- Publication freezes schedule and selected version IDs, requires a future start and at least one problem. Lifecycle is Draft → Scheduled → Live → Ended, calculated from server time. No background timer/job is required.
- Active users register before the end, including during the contest. Registration is idempotent and unique per contest/account. Organizers cannot enter their own contests. No membership cancellation in this increment.
- Participants submit only while start <= server time < end. Admission rechecks time after acquiring PostgreSQL's existing global admission lock. Submission, job and contest association commit atomically, using the same global/per-account limits as practice submissions. Worker/executor contracts are unchanged.
- Contest problem routes resolve the exact pinned version, never the latest edition. Standard owner-only submission detail/history provides judging evidence. No source or private artifacts appear in contest projections.
- New problem authoring reuses the existing Setter/Admin APIs, private ZIP upload and publication checklist; it does not duplicate or relax the publication gate. Newly published problems may then be selected for a contest.

## Backend slice

Domain: contest lifecycle, immutable ownership, bounds, ordered version selection, registration/submission eligibility. Application: active/role/owner/resource checks and focused contest persistence port. Infrastructure: four contest tables, PostgreSQL constraints and transaction boundaries. API: thin `/api/contests` transport contracts with existing cookie/CSRF/ProblemDetails handling. Keep all five existing production projects.

Endpoints: public paginated list/detail; authenticated owned list/create/update/publish; idempotent registration; gated pinned problem detail; participant submission; owner-only contest submission history. Drafts are never returned by public listing. Guessed draft IDs cannot reveal content.

## Frontend slice and design contract

Use the existing compact header, forest/alabaster semantic tokens and compact bordered working panels. The original brand board was inspected; its global sidebar remains superseded by approved top navigation. The generic skill search's marketing hero, blue palette and touched-only validation do not fit this product and are not adopted.

- Contest list: discover scheduled/live/past contests; primary action is open contest; organizers also see Create contest and My contests. Paginated real data, loading/empty/error states.
- Editor: create/edit a draft, select ordered problems, configure local date/time with explicit timezone, publish after review. Preserve fields on failed requests; focus accessible validation feedback; unchanged blur never reveals errors.
- Detail: timing, registration, problem access and organizer actions; pre-start problem list hidden from participants. State refresh handles time boundaries and membership updates.
- Solve: pinned statement/samples, C++20 source input, submission feedback and link to existing result evidence; server remains authoritative at end time.
- Problem authoring: contextual create form with execution limits, samples, private test ZIP packages and explicit publication checklist. Explain that publishing makes the problem public.

Responsive verification: 320/768/1024/1440 widths, keyboard navigation and focus, light/dark tokens, long titles, network failures and busy states. No fake standings or decorative metrics.

## Validation and operating notes

Domain tests cover lifecycle boundaries, uniqueness, immutable publication and validation. Application tests cover disabled/solver/cross-owner access. Real PostgreSQL/HTTP tests cover migration, grants, draft disclosure, CSRF, duplicate registration, stale edits, pinned versions, time boundaries, admission and atomic association. Frontend regression tests cover role visibility, navigation, form feedback and payloads. Run production build and existing relevant regression suites.

Apply the contest migration and updated `scripts/postgres-runtime-grants.sql` before starting updated hosts. Only API receives contest-table privileges; Worker needs none. Contest records and memberships follow existing lifetime retention and paired backups. No production deployment is included.

### Verification evidence (2026-10-09)

Later cross-feature verification passed all 260 frontend tests and the real HTTPS browser contest/problem-upload/submission journey through the Docker Worker. See [current changes verification](changes-verification-20261009.md) for the complete results and remaining profile/advisory limits. The implementation-time observations below remain historical evidence.

- Windows solution build: passed with zero warnings/errors. Linux Release build in the isolated foundation fixture: passed. Fresh migration, repeated migration and runtime grants: passed against real PostgreSQL.
- Worker `--check` passed using the restricted Worker database identity after the new migration/grants. Validation stopped at this startup check; it is not a claim that the remainder of the full judging foundation script ran.
- Selected backend regression run: 8 Domain tests and 75 integration/architecture/publication/privilege tests passed, zero skipped. Includes concurrent registration, shared practice/contest admission capacity, rollback on rejected admission, and a request held behind the PostgreSQL admission lock until the contest ends.
- Angular production build passed. The initial 10 contest regression tests passed during implementation. Final full and focused reruns encountered a Vitest worker-start timeout before any tests ran (both forks and threads); the final 11-test contest file therefore still requires a clean rerun. These runner failures are not counted as passing tests. Logs are in ignored `artifacts/contest-frontend-tests.log` and `artifacts/contest-focused-tests.log`. The isolated thread-pool configuration used for diagnosis stays under ignored `artifacts/`; project test defaults were not changed.
- Browser check: `web/minos-web/testing/contest-browser.cjs` passed organizer creation/publication and participant registration/pinned-problem submission, keyboard focus, light/dark themes and widths 320/768/1024/1440. No page errors or horizontal overflow. Screenshots in ignored `artifacts/contest-browser/` were reviewed. This browser run uses isolated API fixtures; the backend tests above use real HTTP/PostgreSQL. It does not establish end-to-end contestant sandbox execution or a real-browser private ZIP upload.
- The PostgreSQL validation used `MINOS_TEST_SANDBOX=0`; judging runtime acceptance remains separate from this contest increment. No live application database was migrated by these checks.

## Follow-on decisions

Leaderboard scoring/penalties, frozen standings, ratings, private invitations, exclusive unpublished contest problems, organizer-role applications and cancellation/rescheduling after publication are separate future slices.
