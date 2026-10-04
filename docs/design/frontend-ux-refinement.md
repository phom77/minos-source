# Frontend UX refinement — 2026-10-04

This implements the owner-approved navigation and Settings refinement in Angular. It does not approve M7 or add backend capabilities, dependencies, layers or projects. The original Minos and LeetCode reference images remain unchanged.

## Screen contract

| Screen | User task and primary action | Essential information | Relevant states |
| --- | --- | --- | --- |
| Shared header | Open Problems or reach Settings through the account menu | Minos identity, active destination, theme control, signed-in display name | Guest, checking, unavailable, authenticated, expanded menu; keyboard and mobile navigation |
| Problem library | Choose a problem to read | Title, version link, publication date, pagination and actual page size | Loading, empty, invalid pagination, beyond last page, failure and retry |
| Problem reader | Read the statement and examples; choose a published edition | Statement, whitespace-preserving samples, time/memory limits, exact checking, edition/date | Loading, invalid link, missing version, failure/retry; unchanged-edition explanation only on a version route |
| Settings / Profile | Edit the display name | Current name and contextual edit action | Closed editor, editing, validation, pending, failed save, saved, cancel |
| Settings / Security | Change a confirmed Minos password or sign out | Consequences for all sessions; actual availability of password actions | Capability unknown, contextual editing, linked validation, pending, safe failure, completion |
| Settings / Account | Review and confirm disabling | Loss of sign-in access, all sessions ended, data retained, no self-service reactivation | Capability unknown, password proof, explicit confirmation, cancellation, pending, safe failure, completion |

## Implementation and capability evidence

- `web/minos-web/src/app/layouts/app-shell.*` replaces the global left rail and decorative footer with a compact top header. Problems is the supported primary destination; Settings belongs to the user menu. The mobile native dialog keeps focus inside, restores its opener on Escape, and closes on navigation or a desktop resize. The user-menu disclosure supports Tab, Escape and focus-out dismissal.
- `app.routes.ts` and `features/accounts/accounts.routes.ts` add `/settings/profile`, `/settings/security` and `/settings/account`. `/settings` and the old `/account` URL lead to Profile. Existing login/registration links, protected return URLs and popup notices continue to work. Sections have deep links and explicit active states; changing sections discards private editor input.
- `account-settings/*` composes existing API-backed forms from grouped rows. Only one editor opens at a time. Opening focuses the first field; save/cancel restores the row action. Failed requests retain the input. Profile success updates the shared account/header. Account disabling remains a separate password-proof and native-confirmation workflow, never deletion.
- `AccountResponse` in `src/Minos.Api/Accounts/AccountContracts.cs` contains only ID, name, role and creation time. It cannot establish a password, email, connected-provider list or session inventory. Nothing in the UI invents those values. `AccountUseCases.LogoutAsync` revokes account sessions, so the supported sign-out action accurately says all devices.
- Successful `POST /accounts/login` or `POST /accounts/register` proves a local password exists. `AccountsApi` records only that evidence, scoped to the account in `SessionService` memory. Matching profile updates and session refreshes preserve it; logout, expiration and a different identity clear it. A full page reload or a Google/restored session starts with unknown capability, so password change and disabling controls are omitted. The affected sections explain the supported sign-out/password-sign-in path; they do not label an unknown account as passwordless or show a fake password mask. This evidence is presentation state, never an authorization check or a server capability replacement.
- `form-feedback.ts` separates edits, blurred feedback and previously revealed errors from field validity. Unchanged blur stays quiet; first typing stays quiet; edit/blur reveals an invalid field; correction updates immediately after an error. Submit validates everything and focuses a linked accessible summary. Reset, cancel and success clear feedback. Pending requests prevent duplicates; invalidity does not disable submit.
- Pointer submission defers the newly revealed blur feedback to submit, so an error cannot move the clicked button between pointer-down and click. Keyboard edit/blur still reveals errors. This fixes a real browser reproduction where the first empty profile submission missed its button.
- Catalog/reader cleanup removes motivational copy, repeated introductions, unsupported-action explanations and internal identifiers/test counts. Edition links and useful checker consequences remain. Asynchronously rendered page-size options explicitly select the returned size; the first rendered page no longer silently shows 10 when the response says 20.

The intentional visual change follows the 2026-10-04 approval: top navigation supersedes the brand board's left rail, and working screens no longer carry its slogans. The existing supplied mark variants, forest palette, quiet panels, typography, semantic light/dark colors and compact forest library heading remain. Local Settings navigation becomes a compact three-column presentation below 768px; global navigation collapses below 1024px.

## Verification

Final checks use Node 24.19.0: `ng test --watch=false` passes **191 tests in 18 files**, and `ng build` passes with no warnings. Initial production assets total approximately **328 kB**; Settings and authentication remain lazy. The shell's default Node 20 is below this project's declared Angular requirement. Test temporary files were redirected into `artifacts/frontend-tests-temp` after the sandbox temporary-directory rename failed. No dependency or operating-system change was needed.

Regression coverage includes unchanged focus/blur, initially edited typing, edit/blur, empty submit and summary links, correction, confirmation timing, pointer-submit stability, cancellation/reset, restored/provider capability limits, credential cleanup, safe API failures and retry, pending serialization, explicit disabling confirmation, deep-linked Settings sections and user-menu navigation. Existing transport/CSRF, wrong-password versus expired-session, stale-identity and outstanding-write tests continue to pass.

Chrome DevTools MCP tools were not configured. Real browser verification used the in-app browser against an isolated localhost preview of the production build with disposable HTTP fixtures. The preview never forwarded requests to the real API and was not added to production source. Browser checks verified:

- Header/account menu and Settings access, native mobile navigation Tab cycling/Escape/opener restoration, section switching and active states.
- Contextual editor focus, profile empty submit, immediate correction, deliberate safe 503 failure/input retention, manual retry/save/header update, cancel and opener restoration.
- Password summary links and cancel; disable confirmation's initial Cancel focus, Tab/Shift+Tab cycling, Escape dismissal and password-proof reset.
- Restored-session password limitations and sign-out notices; successful local authentication revealing password operations.
- 320/768/1024/1440px Settings layouts in both themes, 46px-high inputs, long Vietnamese names, contained mobile catalog scrolling and mobile statement/sample layout. No page horizontal overflow was observed.
- Normal Settings copy/action contrast: minimum measured **5.86:1 light**, **7.48:1 dark**. Disabled controls are excluded from those measurements. Screenshots are kept locally in `artifacts/frontend-ux-verification/`.
- A fresh final-build browser tab has no console warnings or errors. The intentional profile 503 scenario produces its expected failed-request console entry; the UI displays safe recovery and the next manual save succeeds.

Live Secure-cookie acceptance, actual password/session revocation, disabled-account rejection and enabled Google consent remain unverified: no real API host was listening during this frontend verification. Their requests, safe failure handling and expected UI transitions are covered by HTTP tests; fixture results are not live backend acceptance.

## Future backend planning candidates — not implemented or approved

| User need | Missing API/data contract | Required security boundaries |
| --- | --- | --- |
| See which security actions work after reload | Authenticated capability projection for local-password availability and supported sign-in methods; safe email/provider summaries if displayed | Derive from credential persistence; enforce active owner access in Application; expose no password hash, provider subject, credential or fabricated connection |
| Explicitly connect Google to the current account | Browser-compatible protected initiation/completion plus an authenticated linked-provider projection; the existing JSON/CSRF challenge cannot supply this UI contract, and a successful /me read cannot prove a link | Proof of the current account and provider; CSRF, state/correlation/PKCE; stable mappings; never merge by matching email |
| Change a sign-in email | Current verified-email projection, verification workflow and explicit update command | Reauthentication and proof of the new address, uniqueness/transaction protection, enumeration-safe feedback and appropriate session revocation; provider email is not implicit linking proof |
| Recover a forgotten password or establish one for a provider account | Recovery/setup initiation, bounded completion proof and authoritative password capability | Rate limiting, enumeration resistance, single-use expiring proof, current-account/provider proof for setup, platform hashing and session revocation; no secrets in frontend storage |
| Disable a Google-only account | Explicit provider reauthentication proof accepted by a separately authorized disabling use case; today's endpoint requires a Minos password | Short-lived proof bound to the active account, explicit consequence confirmation, all-session revocation; preserve disabling versus deletion semantics |

MFA, phone management, billing/subscriptions, notifications and additional providers pictured in references have no approved implementation here. They are not placeholder sections or scheduled milestones.
