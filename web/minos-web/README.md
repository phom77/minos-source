# Minos web

The Minos Angular application uses standalone components, strict TypeScript, and feature-first organization. The frontend currently contains a shell, shared presentation, configuration/HTTP infrastructure, and placeholder Accounts and Problems entry pages. Backend M1–M5 are approved; frontend workflows will be added as separate slices against their actual contracts. M6 still requires owner approval.

## Commands

```bash
npm ci
npm start
npm test
npm run build
```

Use Node.js 24.15+ on the 24.x line, or another version allowed by `package.json`. `package-lock.json` pins the complete dependency graph; use `npm ci` for a clean checkout and CI. Use `npm install` when deliberately changing dependencies and commit the updated lockfile. Direct dependencies retain the versions verified for this foundation; upgrades are explicit maintenance work.

`npm start` and `ng serve` both use the development proxy configured in `angular.json`. `/accounts/**` and `/health/**` forward to the existing HTTPS API launch profile at `https://localhost:49674`; `secure: false` applies only to the local upstream certificate. For browser HTTPS during cookie-authentication development, use `npm start -- --ssl` and trust the local development certificate as appropriate.

Production requests are same-origin by default. The environment files select build-time configuration; `app.config.ts` provides it through `API_CONFIGURATION`. Feature services inject that token indirectly through HTTP infrastructure rather than importing environment files. Use a same-origin API or reverse proxy for the cookie session. A separate API origin requires matching backend CORS, credentials, cookie, and CSRF support; setting `apiBaseUrl` alone does not implement that authentication topology.

## Structure

```text
src/
├── app/
│   ├── core/
│   │   ├── config/api.config.ts
│   │   └── http/
│   │       ├── api-error.ts
│   │       ├── api-error.spec.ts
│   │       ├── api-url.interceptor.ts
│   │       └── api-url.interceptor.spec.ts
│   ├── features/
│   │   ├── accounts/
│   │   │   ├── accounts.routes.ts
│   │   │   └── account-entry-page.{ts,html,css}
│   │   └── problems/
│   │       ├── problems.routes.ts
│   │       └── problem-catalog-page.{ts,html,css}
│   ├── layouts/app-shell.{ts,html,css}
│   ├── shared/ui/section-heading.{ts,html,css}
│   ├── app.component.ts
│   ├── app.config.ts
│   ├── app.routes.ts
│   └── app.routes.spec.ts
├── environments/
├── styles/
├── styles.css
└── main.ts
```

- `core/` owns application-wide configuration and HTTP transport concerns. It does not import feature code or accumulate business operations. The frontend `.gitignore` explicitly includes this source directory despite the repository's crash-dump ignore rule.
- `shared/` owns presentation or utilities independent of a business feature. It depends on neither `core/` nor `features/`; reusable components accept inputs and emit interactions.
- `layouts/` owns application chrome and route outlets, not feature operations.
- `features/accounts/` owns account routes and UI; future account data access, forms, and feature-specific state belong here. Only the session state truly consumed across the application belongs in `core/auth/` when implemented.
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

This context flag only controls URL routing. It does not authorize the request, obtain a CSRF token, or handle a login session. Preserve browser-owned cookies; future account data access must obtain `/accounts/csrf`, send `X-CSRF-TOKEN` for mutations, and refresh the token after identity changes. No JWT or credential storage is needed.

Use `toApiError(error)` from `core/http/api-error.ts` at the feature's error boundary rather than parsing raw transport errors in every component. It extracts known ProblemDetails fields without copying arbitrary server payloads; feature-specific wording and recovery stay with the feature. API response models belong with their consuming feature, and UI-only models are separate only when their shapes differ.

## Verification

`npm test` uses Angular CLI's Vitest integration. Routing tests load the real feature route groups and verify home/fallback redirects, page titles, shell reuse, and navigation. HTTP tests verify explicit API routing, asset/third-party isolation, request preservation, and safe error normalization. Add behavior tests for future forms and use cases instead of tests that merely assert a component exists.

`npm run build` produces the production bundle with size budgets. Run tests and build from a clean `npm ci` install before handing off structural/dependency changes. Keep generated `node_modules/`, `.angular/`, and `dist/` out of version control.

## Approved visual direction

Read the [Minos visual reference](../../docs/design/minos-visual-reference.md) and inspect its three original images before implementing or changing the UI. It records the owner-approved forest palette, folded M mark, typography roles, layout patterns, and design principles. The current scaffold predates this reference; its tokens, placeholder pages, and favicon have not yet been reconciled with the approved design.
