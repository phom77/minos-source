# Minos web

The Minos Angular application uses standalone components, strict TypeScript, and feature-first organization. Phases 1–3 provide the approved responsive shell, persistent light/dark themes, HTTP protection, popup authentication, Google sign-in integration, and the real public problem catalog/reader. Backend code and milestone scope are unchanged by this frontend work; current backend approvals in `AGENTS.md` remain authoritative.

## Commands

```bash
npm ci
npm start
npm test
npm run build
```

Use Node.js 24.15+ on the 24.x line, or another version allowed by `package.json`. `package-lock.json` pins the complete dependency graph; use `npm ci` for a clean checkout and CI. Use `npm install` when deliberately changing dependencies and commit the updated lockfile. Direct dependencies retain the versions verified for this foundation; upgrades are explicit maintenance work.

`npm start` and `ng serve` both use the development proxy configured in `angular.json`. `/accounts/**`, `/api/**`, `/signin-google`, and `/health/**` forward to the existing HTTPS API launch profile at `https://localhost:49674`; `secure: false` applies only to the local upstream certificate. Restart the dev server after changing the proxy. For browser HTTPS during cookie-authentication development, use `npm start -- --ssl --host localhost` and trust the local development certificate as appropriate. A plain HTTP preview is suitable for UI checks, not for verifying the API's Secure cookie lifecycle.

Production requests are same-origin by default. The environment files select build-time configuration; `app.config.ts` provides it through `API_CONFIGURATION`. Feature services inject that token indirectly through HTTP infrastructure rather than importing environment files. Use a same-origin API or reverse proxy for the cookie session. A separate API origin requires matching backend CORS, credentials, cookie, and CSRF support; setting `apiBaseUrl` alone does not implement that authentication topology.

## Structure

```text
src/
├── app/
│   ├── core/
│   │   ├── auth/ (current session model/state, guard, safe returns, tests)
│   │   ├── config/api.config.ts
│   │   ├── http/ (API routing/errors, CSRF, session failures, tests)
│   │   └── theme/ (theme preference and tests)
│   ├── features/
│   │   ├── accounts/
│   │   │   ├── accounts.routes.ts
│   │   │   ├── auth-dialog/ (lazy modal, keyboard/focus behavior, tests)
│   │   │   ├── auth-form/ (reused sign-in/registration forms, validation, tests)
│   │   │   ├── data-access/ (account API, Google flow, tests)
│   │   │   └── account-entry-page.{ts,html,css}
│   │   └── problems/
│   │       ├── problems.routes.ts
│   │       ├── data-access/ (public contracts, response validation, tests)
│   │       ├── problem-detail/ (latest/immutable published reader)
│   │       ├── testing/ (test-only fixtures; never production fallbacks)
│   │       ├── catalog-query.{ts,spec.ts}
│   │       └── problem-catalog-page.{ts,html,css}
│   ├── layouts/app-shell.{ts,html,css}
│   ├── shared/ui/ (headings, icons, feedback, badges, field messages)
│   ├── app.component.ts
│   ├── app.config.ts
│   ├── app.routes.ts
│   └── app.routes.spec.ts
├── environments/
├── styles/
├── styles.css
└── main.ts
```

- `core/` owns application-wide configuration, session/theme state, and HTTP transport concerns. It does not import feature code or accumulate business operations. The frontend `.gitignore` explicitly includes this source directory despite the repository's crash-dump ignore rule.
- `shared/` owns presentation or utilities independent of a business feature. It depends on neither `core/` nor `features/`; reusable components accept inputs and emit interactions.
- `layouts/` owns application chrome and route outlets, not feature operations. It composes the Accounts dialog through a lazy `@defer` block; account HTTP/form behavior stays in the Accounts feature.
- `features/accounts/` owns account routes, forms, validation, and login/register/logout data access. Only the session state consumed across the application belongs in `core/auth/`. Profile and security mutations remain Phase 4.
- `features/problems/` owns the public catalog, latest/immutable detail, URL pagination and public API contracts. Authoring remains a later phase in this feature.
- `app.routes.ts` mounts lazy feature route groups; each feature owns its child URLs and page loading. `app.config.ts` is the composition point for router, HTTP, and singleton providers.
- `styles/` owns global tokens and base rules. Component HTML/CSS stays beside its TypeScript, and tests stay beside the behavior they verify. `public/` holds files served unchanged, not TypeScript or private data.

These choices follow the [Angular style guide](https://angular.dev/style-guide) and [lazy route-loading guidance](https://angular.dev/guide/routing/define-routes). Features stay flat while small. As a page gains several related files, group them under a page-named directory; add feature-local `data-access/` or `state/` when real responsibilities justify it. Do not create empty folders, extra application layers, a facade per feature, or a generic API/base service.

## Dependency and responsibility rules

- Features may use shared UI and core infrastructure. Features do not import another feature's internal pages, state, or data access. Compose capabilities through routes or move a genuinely shared contract to a focused common location.
- Pages coordinate route data, feature services, and navigation. Presentational components handle rendering and local interactions. Keep direct `HttpClient` requests in feature data access, not in a page/template component.
- Use signals for synchronous/local UI state, typed reactive forms for important forms, and RxJS for HTTP/cancellation/stream composition. Add feature-provided state only when it has a clear owner; do not default every service to a global store.
- Introduce a service or component when it owns a meaningful responsibility. Template/CSS size alone is not a reason to fragment a cohesive page into many tiny components.
- Guards and hidden actions improve navigation/UX. Account status, roles, ownership, and all authorization remain authoritative in backend use cases.

## HTTP boundary

Feature data access uses root-relative API paths. Explicitly mark Minos API calls with `MINOS_API_REQUEST` so the URL interceptor can prefix the configured base without rewriting local assets or other services:

```ts
import { HttpContext } from '@angular/common/http';
import { MINOS_API_REQUEST } from '../../core/http/api-url.interceptor';

// Inside a feature API service; `http` is its injected HttpClient.
return http.get<AccountResponse>('/accounts/me', {
  context: new HttpContext().set(MINOS_API_REQUEST, true),
});
```

The composition point registers CSRF protection before API URL routing. Marked, root-relative `/accounts` and `/api` mutations receive `X-CSRF-TOKEN`; GET/HEAD/OPTIONS, unmarked calls, unrelated paths, and foreign origins do not. Token requests are coalesced and cached only in memory, invalidated after identity changes, and refreshed on the next manual attempt after a 400. A stale in-flight token is rejected. Mutations are never automatically retried. Same-origin cookies remain browser-owned; no JWT or credential storage is needed.

Mark authenticated operations additionally with `SESSION_REQUIRED` from `core/http/session-error.interceptor.ts`. A protected 401 clears the current session and CSRF state; login's invalid-credential 401 does not. 403 and server/network errors do not silently become logout. An old identity's response cannot expire a newer session. `/accounts/me` is handled by the session service so guest checks remain normal behavior.

Use `toApiError(error)` from `core/http/api-error.ts` at the feature's error boundary rather than parsing raw transport errors in every component. It extracts known ProblemDetails fields without copying arbitrary server payloads; feature-specific wording and recovery stay with the feature. API response models belong with their consuming feature, and UI-only models are separate only when their shapes differ.

## Verification

`npm test` uses Angular CLI's Vitest integration. Routing tests load the real feature route groups and verify the home redirect, not-found recovery, page titles, shell reuse, navigation, and mobile drawer controls. Shared feedback tests cover projected actions and error/loading semantics. HTTP tests verify explicit API routing, asset/third-party isolation, request preservation, and safe error normalization. Add behavior tests for future forms and use cases instead of tests that merely assert a component exists.

`npm run build` produces the production bundle with size budgets. Run tests and build from a clean `npm ci` install before handing off structural/dependency changes. Keep generated `node_modules/`, `.angular/`, and `dist/` out of version control.

## Approved visual direction

Read the [Minos visual reference](../../docs/design/minos-visual-reference.md) and inspect its three original images before implementing or changing the UI. It records the owner-approved forest palette, folded M mark, typography roles, layout patterns, and design principles. Phase 1 reconciles the earlier scaffold's tokens, placeholder pages, and branding with that reference.

### Phase 1 visual foundation

Phase 1 introduced the approved palette through semantic tokens, a dark sidebar/light workspace, compact headings, shared native-button styles, outline icons, badges, and reusable empty/error/loading feedback. At that stage, Problems and Account were explicitly labeled placeholders. Phases 2–3 connect these public/account workflows; no fabricated statistics or unsupported navigation destinations are added.

At widths below 1024px, navigation uses a native modal dialog with labeled controls, Escape dismissal, explicit Tab cycling, focus restoration, and route-change dismissal. Opening a desktop viewport closes a mobile dialog. Same-page links preserve the current route; unknown URLs render a lazy not-found page instead of silently redirecting to the catalog.

Shared components are added for actual consumers. Field feedback, confirmation dialogs, and pagination will be extracted when the later form/catalog phases need them; Phase 1 does not introduce a speculative component library.

Production branding uses unchanged PNG originals with a clipped SVG presentation viewport; see [asset notes](public/brand/README.md). The shared decorative landscape uses irregular layered ridgelines and varied conifer silhouettes, not extracted reference photography. Its theme-specific tones, opacity, and masks keep artwork behind the reading flow; forms remain undecorated. System sans-serif, serif, and monospace stacks require no third-party font requests. The original design-reference images remain untouched.

### Phase 2 authentication and themes

The guarded `/account` remains a session overview. In Phase 3, `/account/login` and `/account/register` become compatibility redirects into a popup over public browsing. Registration signs in immediately, matching the server contract. The overview displays only returned display name, role, creation date, and account ID, and explains that logout revokes sessions across devices. Invalid credentials, rate limiting, pending requests, expired sessions, and unavailable service states have explicit recovery. Guards are UX only; server authorization is final. An unavailable service renders a retry panel instead of redirecting to sign-in.

Forms use typed reactive controls, trimmed email/display names, unmodified 12–256-character passwords, name bounds/control-character validation, and password confirmation. Password managers and paste work. Errors retain inline descriptions and focus a linked summary after submission. Duplicate submits are suppressed. Identity-changing requests finish (with a 15-second timeout) even if the originating view is destroyed, keeping browser cookie responses and in-memory session state consistent; destroyed views do not redirect. Passwords never enter browser storage.

Light and complementary dark workspaces use paired semantic tokens for canvas, panels, text, inputs, borders, actions, navigation, selection, focus, feedback, and modal scrims; approved brand primitives stay unchanged. Following the owner's full-interface theme refinement, light navigation is warm Alabaster with the supplied dark mark, and dark navigation is forest-toned with the supplied pale mark. This intentionally adapts the reference board's always-dark rail to the requested theme-specific logos without changing navigation layout. Branded hero/editorial panels remain forest-toned in both modes, with separate paired tokens rather than inheriting navigation colors. `public/theme-init.js` applies the stored preference or system theme and matching favicon before Angular/styles render. `ThemeService` keeps the toggle, system changes (until an explicit choice), cross-tab changes, and favicon consistent. Only `minos.theme` (`light`/`dark`) is persisted; blocked storage still permits an in-memory toggle. Native controls receive the matching `color-scheme`.

The password reveal uses a labeled native eye/eye-off button with pressed state and controlled field IDs. The helper contains only the 12–256-character requirement; password-manager/autofill and paste behavior remain unchanged.

Verification: frontend HTTP/form/session/theme/routing tests and the production build pass. Real-browser checks cover validation focus/links, reveal controls, responsive layout, light/dark rendering, and theme persistence. Chrome DevTools MCP is not configured; checks use the in-app browser. During implementation the development API proxy returned 502/ECONNREFUSED, so real HTTPS cookie registration → reload → logout is not yet verified. No mock authentication or backend startup/change was used to hide that gap. Profile/security editing remains Phase 4.

### Phase 3 catalog and authentication popup

`/problems?page=1&pageSize=20` requests `GET /api/problems`; changing pagination cancels stale reads, and unrelated authentication query changes do not refetch the catalog. Sizes 1–100 are accepted for deep links; the picker offers 10/20/50/100. Invalid/duplicate/overflowing parameters are rejected before HTTP. The table shows only title, published version and UTC date, with loading, genuine-empty, beyond-last-page, failure and manual retry states. Its labeled horizontal-scroll region contains overflow on small screens. Previous/next are real links; router scroll restoration supports browser back/forward.

`/problems/:problemId` reads the latest published version; `/problems/versions/:versionId` reads an immutable snapshot. Both expose statement, whitespace-preserving samples, limits, exact-checker behavior and public version metadata. Statements use plain-text interpolation, never raw HTML. Sample regions are keyboard-scrollable. Invalid IDs make no HTTP request; 404 and retryable failures are distinct. Submission UI remains frontend Phase 6, not a fabricated action.

The shell's sign-in button opens `?auth=login` over the current page; registration switches to `?auth=register` with a fresh form. `authReturn` is a validated internal protected destination, and `authNotice` carries expiration/sign-out feedback. Closing removes only modal parameters, preserving browsing query/fragment state. The lazy native dialog provides labeled context, an explicit close button, Escape/backdrop dismissal, focus cycling/restoration, scroll containment, and theme-specific supplied Minos marks. The LeetCode reference informs popup behavior, not branding or extra providers. No unsupported password-reset, CAPTCHA or legal links are shown.

#### Google browser flow and acceptance

The backend owns challenge, correlation/state/PKCE, external identity mapping and cookie issuance. The frontend uses a native `Continue with Google` link to `/accounts/google/login`, opening a separate tab with `noopener noreferrer`. This is deliberate: the existing completion redirects to JSON `/accounts/me`, so the parent confirms the resulting cookie session through that endpoint. While waiting, it checks every 2.5 seconds only when visible, coalesces overlapping/manual checks, stops after five minutes or cancellation/destruction, and offers explicit failure/retry feedback. It never reads provider credentials, OAuth codes/tokens or child-window contents. Email submission is disabled while a Google flow is pending. No frontend Google SDK or client secret is needed.

Use a trusted HTTPS same-origin app/API. On the existing development proxy, use `https://localhost:4200`, not `https://127.0.0.1:4200`: `changeOrigin` means the existing server can generate `https://localhost:49674/signin-google` as its callback. Register the actual generated URI in the owner's existing Google configuration; both callback and app must share the cookie host. Production reverse proxy routing must cover `/accounts/**` and `/signin-google` and preserve the configured public HTTPS host. Provider-disabled/failed consent cannot be inspected from the parent tab; cancellation and bounded/manual session checks remain the recovery path. Verify with the owner: Google consent → parent confirmed account → popup closes/returns safely → refresh retains session → logout. Do not claim that acceptance check has passed until performed with a running API and enabled provider.

Provider controls intentionally use Google's neutral light/dark surface colors and unmodified official G paths, rather than recoloring the logo into Minos green. The rest of the popup retains Minos tokens and typography. See [Google branding guidance](https://developers.google.com/identity/branding-guidelines) and [asset provenance](public/brand/README.md).

Phase 3 verification (2026-10-03): 136 frontend tests pass across 16 files. The production build passes without budget warnings; authentication remains a lazy chunk. The in-app browser verifies popup focus cycling/Escape/opener restoration, login↔registration switching, back/forward modal navigation, validation-summary links, the HTTP Google guard, supplied logos/semantic surfaces in both themes, invalid-link/retry recovery, and 320/768/1024/1440px layouts plus a 360px-high landscape viewport. Registration scrolls within its dialog; the page/dialog have no horizontal overflow. Ready catalog/detail responses, empty/out-of-range lists, safe plaintext/sample rendering and Google confirmation/failure/timeout/cancellation are covered with test-only HTTP fixtures. Live `/api/problems` and `/accounts/me` return 502 through the current proxy, so successful live problem browsing and HTTPS/Google consent remain explicitly unverified. No backend host was started and no backend file or data was changed by this slice.
