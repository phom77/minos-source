# Minos frontend implementation plan

Status: planning only; implementation has not started under this plan.

Scope: complete the Angular frontend for the approved M1–M5 capabilities. Do not change backend code or implement M6 without owner approval.

## Evidence and design contract

- [Frontend structure and engineering rules](../web/minos-web/README.md)
- [Approved visual reference](design/minos-visual-reference.md) and original images in `design/references/`
- [Account endpoints](../src/Minos.Api/Accounts/AccountEndpoints.cs) and [contracts](../src/Minos.Api/Accounts/AccountContracts.cs)
- [Google endpoints](../src/Minos.Api/Accounts/GoogleEndpoints.cs)
- [Problem endpoints](../src/Minos.Api/Problems/ProblemEndpoints.cs) and [contracts](../src/Minos.Api/Problems/ProblemContracts.cs)
- [Submission endpoints](../src/Minos.Api/Submissions/SubmissionEndpoints.cs) and [contracts](../src/Minos.Api/Submissions/SubmissionContracts.cs)

Preserve the approved forest palette, folded M branding, dark navigation, warm light workspace, compact controls, fine borders, restrained elevation, and functional typography. Use editorial and technical accents selectively. The reference board is visual evidence, not authorization for contests, ratings, discussions, fabricated metrics, or real contestant verdicts.

## Current gaps

The existing feature-first Angular structure is suitable for incremental development. Keep its standalone components, lazy feature routes, strict typing, and colocated tests.

| Area | Remaining work |
| --- | --- |
| Application shell | Replace placeholder branding and top navigation with the approved composition; add responsive navigation and route feedback |
| Problems | Replace explanatory placeholder with catalog, pagination, published detail, and authoring workflows |
| Accounts | Replace explanatory placeholder with authentication, profile, and security forms |
| Submissions | Add source submission, owner history, detail, and lifecycle tracking |
| Shared interactions | Add reusable field feedback, dialogs, pagination, loading, empty, error, and permission patterns |
| Integration | Add feature API clients, session state, CSRF handling, and protected navigation |

There is placeholder content, not a working mock-data application. Existing scaffold copy that says M4 is unapproved must be replaced as the actual workflows arrive.

## Phase 1 — Approved visual foundation and navigation

### Implement

- Reconcile semantic color, typography, spacing, border, focus, and status tokens with the approved reference.
- Replace placeholder branding with suitable approved-logo assets. Preserve the original reference files; review any production derivatives for silhouette and proportions.
- Build the dark forest sidebar, warm light workspace, and compact header/account controls.
- Add accessible mobile navigation, active-route indicators, breadcrumbs where useful, and a real not-found page.
- Establish focused patterns for buttons, labeled fields, validation feedback, status badges, loading placeholders, empty/error panels, confirmation dialogs, and pagination. Extract components as real consumers appear rather than creating a speculative component library.
- Keep `/problems` as the landing route. Add navigation entries only when their workflows are available.

### Dependencies

Existing shell, routes, and approved design reference.

### Expected result

A consistent, responsive application frame ready to host real workflows. Do not add decorative search, notifications, theme switches, or dashboard statistics without working behavior and supporting contracts.

## Phase 2 — HTTP protection and authentication

### Implement

- Extend frontend development proxy coverage to `/api/**`; verify HTTPS and same-origin cookie behavior.
- Fetch `/accounts/csrf` and send `X-CSRF-TOKEN` on Minos mutations, including problem and submission operations. Refresh protection after identity changes and never attach it to unrelated requests.
- Introduce one small application-wide session state: checking, authenticated, guest, and unavailable.
- Build sign-in, registration, and logout using the real account endpoints.
- Use typed reactive forms with email, display-name, password, and confirmation validation aligned with server rules.
- Handle invalid credentials, registration failure, rate limiting, pending requests, and session expiration.
- Add protected navigation and safe internal return URLs.
- Distinguish unauthenticated responses from network/server failures; an outage must not silently become logout.

### Dependencies

Phase 1 controls and feedback patterns.

### Expected result

Users can register, sign in, refresh the application, and sign out through real protected browser sessions. Cookies remain browser-owned. Do not store credentials or invented JWTs in browser storage. Guards improve UX; server authorization remains authoritative.

## Phase 3 — Public problem browsing and reading

### Implement

- Replace the catalog placeholder with paginated data from `/api/problems`.
- Keep page and page-size state in the URL and preserve browser back/forward behavior.
- Build latest-problem and immutable-version detail routes using the existing detail endpoints.
- Present statement, samples, limits, checker information, and published-version metadata.
- Render supplied content safely and preserve whitespace in sample blocks.
- Provide navigation between catalog, detail, and the later submission workflow.
- Handle loading, an empty catalog, unavailable problems, invalid URLs, failures, and retry.
- Make tables usable on narrow screens and sample blocks scroll without breaking page layout.

### Dependencies

Phase 1; Phase 2 transport/session infrastructure for authenticated actions.

### Expected result

Guests and authenticated users can browse and read actual published problems. Display only available API fields: difficulty, tags, acceptance rates, and server-side search are not supported by the current catalog contract.

## Phase 4 — Profile and account security

### Implement

- Replace the account placeholder with profile and security sections.
- Show available account fields and allow display-name updates.
- Add change-password and disable-account forms.
- Include current-password confirmation, new-password confirmation, visible validation, pending/success/error feedback, and destructive-action confirmation.
- Explain session revocation and return users to sign-in after operations that clear their session.
- Show role information without implying that users can promote themselves.
- Add Google sign-in/linking only when enabled and its browser flow has been verified.

### Dependencies

Phases 1–2. This phase can proceed alongside public browsing after authentication is stable.

### Expected result

Users can manage the account operations already supported by the backend. Do not introduce email editing, password reset, avatars, or linked-provider management where contracts are absent. `/accounts/me` does not return an email address.

Google completion currently redirects to `/accounts/me`, a JSON endpoint. Agree and verify an FE-compatible browser flow before presenting Google authentication as finished. Include provider callback proxy coverage if needed; do not silently change backend behavior.

## Phase 5 — Problem authoring and publication

### Implement

- Add Setter/Admin routes for creating a problem and opening an authorized draft by problem ID.
- Build an editor for title, statement, time/memory limits, samples, and the supported `Exact` checker.
- Add sample creation/removal, draft saving, unsaved-change protection, and a preview that reuses the public reading presentation.
- Add private-test ZIP upload/removal with file-size feedback and server-error handling. Use the existing raw `application/zip` request contract, not an invented multipart format.
- Display only returned test metadata; never expose private contents or filesystem paths.
- Add publication readiness feedback and the three existing reference-validation confirmations. These record manual evidence, not actual reference execution.
- Explain immutable publication: later draft edits do not change an already published version.
- Add archive confirmation and archived/read-only states.
- Handle authorization failures, concurrent-update conflicts, and failed saves while preserving unsaved input.
- Match server validation for title length, sample count, supported limits, and upload bounds. Keep server validation final.

### Dependencies

Phases 1–3; reuse Phase 4 form and confirmation patterns.

### Expected result

An authorized owner can create, edit, prepare, publish, and archive problems through real M4 endpoints.

There is no author-owned draft-list endpoint. Initially support creation and opening a known problem ID; record draft discovery as a separate contract gap rather than promising a complete “My drafts” page. Do not invent client-supplied revision preconditions absent from the update contract.

## Phase 6 — Submission workflow and history

### Implement

- Add a submission form from published problem detail.
- Start with an accessible source textarea and the supported `cpp20` language. A heavyweight editor is unnecessary initially.
- Validate nonempty source, null characters, and the 65,536-byte UTF-8 limit; show a byte count.
- Prevent duplicate clicks and handle admission-limit, authorization, validation, and network failures. Do not blindly retry an ambiguous submission POST.
- Navigate successful submissions to owner-only detail.
- Build paginated history from `/api/me/submissions`; do not request or expose source unnecessarily in history.
- Show the actual returned pinned problem-version ID, language/toolchain, timestamps, attempts, status, and failure information. Source belongs on owner detail.
- Use the version returned by the submission response as authoritative; the creation request does not let the client select a historical version.
- Poll active submissions conservatively, stop at terminal states, and cancel on navigation. Provide refresh/retry after connection failures.
- Clear account-scoped state when identity changes.

### Dependencies

Phases 1–3. Phase 5 supplies realistic published content but is not a technical prerequisite.

### Expected result

Users can submit source and follow the actual M5 lifecycle: `Pending`, `Judging`, `Completed`, and `SystemFailed`.

`Completed` does not mean `Accepted`. Do not add Run, compiler output, contestant verdicts, rejudge, or per-test results before M6 approval.

## Phase 7 — Administration and completion verification

### Implement

- If administration belongs in the FE deliverable, add a narrow Admin-only role-assignment form using a known account ID and the existing role endpoint.
- Include confirmation and session-revocation messaging; do not invent an account directory.
- Finish deep-link navigation, route titles, focus restoration, and unsupported-route handling.
- Verify real-API journeys:
  - Register → sign in → update profile.
  - Browse → read → submit → view history/detail.
  - Create draft → upload tests → publish → read published version.
- Verify guest, Solver, Setter, Admin, expired-session, and denied-ownership behavior.
- Exercise loading, empty, validation, permission, conflict, rate-limit, network, and server-error states.
- Review at 320, 768, 1024, and 1440px, including long statements and Vietnamese text.
- Check keyboard navigation, dialog focus, labels, announcements, contrast, reduced motion, and touch interaction.
- Run frontend tests and production build; inspect browser console/network behavior.

### Dependencies

Previous phases and any resolved conditional integration gaps.

### Expected result

A verified M1–M5 frontend with honest capability boundaries and no production mock behavior.

## Implementation order and architecture rules

Recommended order: **1 → 2 → 3 → 4 → 5 → 6 → 7**. Profile work can proceed alongside public browsing after authentication is stable. Submissions depend on public problem detail and authentication, not on the authoring UI itself.

- Keep feature APIs and business-specific state inside their owning feature.
- Keep only shared session, configuration, and transport concerns in `core`.
- Keep shared presentation independent of feature services and core infrastructure.
- Preserve lazy routes and colocated behavior tests.
- Use signals for synchronous/local state, typed reactive forms for forms, and RxJS for HTTP/cancellation. Do not introduce a global state library or generic API abstraction without evidence.

## Mock-to-API handoff

| Slice | Real integration boundary |
| --- | --- |
| Authentication and security | `/accounts/csrf`, `/accounts/register`, `/accounts/login`, `/accounts/logout`, `/accounts/me`, `/accounts/password`, `/accounts/disable` |
| Published problems | `/api/problems`, `/api/problems/{id}`, `/api/problems/versions/{versionId}` |
| Authoring | `/api/authoring/problems` creation, draft, tests, publish, and archive endpoints |
| Submissions | `/api/problems/{problemId}/submissions`, `/api/me/submissions`, `/api/submissions/{id}` |
| Conditional administration/Google | Existing role assignment and enabled Google endpoints only |

Use realistic fixtures for component tests and temporary visual development only. Replace fixtures with real endpoints within each feature phase rather than postponing integration to the end. Keep test doubles outside production behavior.

## Completion criteria for every phase

- Implement applicable loading, empty, validation, success, error, and permission states.
- Include responsive behavior and accessible interaction from the start.
- Add meaningful form, route, HTTP, or state tests for the new behavior.
- Run relevant frontend tests and build; verify real browser behavior for implemented journeys.
- Review the diff for unintended backend changes, extra dependencies, unsupported capabilities, and visual drift.

Contests, leaderboards, discussions, ratings, fabricated metrics, and real judging remain outside this plan. Conditional contract gaps are recorded explicitly, not implemented by inventing endpoints or changing backend code.
