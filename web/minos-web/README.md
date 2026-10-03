# Minos web

The Minos Angular application uses standalone components, strict TypeScript, and feature-first organization. Phases 1–2 provide the approved responsive shell, persistent light/dark themes, HTTP protection, and real email/password authentication/session workflows. Problems remain a clearly labeled Phase 3 placeholder. Backend code and milestone scope are unchanged by this frontend work; current backend approvals in `AGENTS.md` remain authoritative.

## Commands

```bash
npm ci
npm start
npm test
npm run build
```

Use Node.js 24.15+ on the 24.x line, or another version allowed by `package.json`. `package-lock.json` pins the complete dependency graph; use `npm ci` for a clean checkout and CI. Use `npm install` when deliberately changing dependencies and commit the updated lockfile. Direct dependencies retain the versions verified for this foundation; upgrades are explicit maintenance work.

`npm start` and `ng serve` both use the development proxy configured in `angular.json`. `/accounts/**`, `/api/**`, and `/health/**` forward to the existing HTTPS API launch profile at `https://localhost:49674`; `secure: false` applies only to the local upstream certificate. Restart the dev server after changing the proxy. For browser HTTPS during cookie-authentication development, use `npm start -- --ssl` and trust the local development certificate as appropriate. A plain HTTP preview is suitable for UI checks, not for verifying the API's Secure cookie lifecycle.

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
│   │   │   ├── auth-page/ (sign-in/registration forms, validation, tests)
│   │   │   ├── data-access/accounts-api.ts
│   │   │   └── account-entry-page.{ts,html,css}
│   │   └── problems/
│   │       ├── problems.routes.ts
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
- `layouts/` owns application chrome and route outlets, not feature operations.
- `features/accounts/` owns account routes, forms, validation, and login/register/logout data access. Only the session state consumed across the application belongs in `core/auth/`. Profile and security mutations remain Phase 4.
- `features/problems/` owns problem routes and UI; future discovery, detail, and authoring use the real M4 contracts within this feature.
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

Phase 1 introduced the approved palette through semantic tokens, a dark sidebar/light workspace, compact headings, shared native-button styles, outline icons, badges, and reusable empty/error/loading feedback. At that stage, Problems and Account were explicitly labeled placeholders. Phase 2 connects Accounts as described below; Problems remain planned and no fabricated statistics or unsupported navigation destinations are added.

At widths below 1024px, navigation uses a native modal dialog with labeled controls, Escape dismissal, explicit Tab cycling, focus restoration, and route-change dismissal. Opening a desktop viewport closes a mobile dialog. Same-page links preserve the current route; unknown URLs render a lazy not-found page instead of silently redirecting to the catalog.

Shared components are added for actual consumers. Field feedback, confirmation dialogs, and pagination will be extracted when the later form/catalog phases need them; Phase 1 does not introduce a speculative component library.

Production branding uses unchanged PNG originals with a clipped SVG presentation viewport; see [asset notes](public/brand/README.md). The shared decorative landscape uses irregular layered ridgelines and varied conifer silhouettes, not extracted reference photography. Its theme-specific tones, opacity, and masks keep artwork behind the reading flow; forms remain undecorated. System sans-serif, serif, and monospace stacks require no third-party font requests. The original design-reference images remain untouched.

### Phase 2 authentication and themes

Routes: `/account/login`, `/account/register`, and the guarded `/account` session overview. Registration signs in immediately, matching the server contract. The overview displays only returned display name, role, creation date, and account ID, and explains that logout revokes sessions across devices. Invalid credentials, rate limiting, pending requests, expired sessions, and unavailable service states have explicit recovery. Guards are UX only; server authorization is final. An unavailable service renders a retry panel instead of redirecting to sign-in.

Forms use typed reactive controls, trimmed email/display names, unmodified 12–256-character passwords, name bounds/control-character validation, and password confirmation. Password managers and paste work. Errors retain inline descriptions and focus a linked summary after submission. Duplicate submits are suppressed. Identity-changing requests finish (with a 15-second timeout) even if the originating view is destroyed, keeping browser cookie responses and in-memory session state consistent; destroyed views do not redirect. Passwords never enter browser storage.

Light and complementary dark workspaces use paired semantic tokens for canvas, panels, text, inputs, borders, actions, navigation, selection, focus, feedback, and modal scrims; approved brand primitives stay unchanged. Following the owner's full-interface theme refinement, light navigation is warm Alabaster with the supplied dark mark, and dark navigation is forest-toned with the supplied pale mark. This intentionally adapts the reference board's always-dark rail to the requested theme-specific logos without changing navigation layout. Branded hero/editorial panels remain forest-toned in both modes, with separate paired tokens rather than inheriting navigation colors. `public/theme-init.js` applies the stored preference or system theme and matching favicon before Angular/styles render. `ThemeService` keeps the toggle, system changes (until an explicit choice), cross-tab changes, and favicon consistent. Only `minos.theme` (`light`/`dark`) is persisted; blocked storage still permits an in-memory toggle. Native controls receive the matching `color-scheme`.

The password reveal uses a labeled native eye/eye-off button with pressed state and controlled field IDs. The helper contains only the 12–256-character requirement; password-manager/autofill and paste behavior remain unchanged.

Verification: frontend HTTP/form/session/theme/routing tests and the production build pass. Real-browser checks cover validation focus/links, reveal controls, responsive layout, light/dark rendering, and theme persistence. Chrome DevTools MCP is not configured; checks use the in-app browser. During implementation the development API proxy returned 502/ECONNREFUSED, so real HTTPS cookie registration → reload → logout is not yet verified. No mock authentication or backend startup/change was used to hide that gap. Google sign-in, profile/security editing, and public catalog integration remain later phases.
