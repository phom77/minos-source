# Minos web

Angular 22 application using standalone components, lazy routes, strict TypeScript and colocated tests. See the root [README](../../README.md) for the complete Docker stack.

## Commands

Use Node.js 24 at a version accepted by `package.json`.

```sh
npm ci
npm start -- --ssl --host localhost
npm test
npm run build
npx playwright install chromium
npm run test:browser
```

The development proxy sends `/api`, `/accounts`, `/signin-google` and `/health` to `https://localhost:49674`. Browser cookie authentication requires trusted HTTPS. Production uses the same-origin Nginx proxy; changing `apiBaseUrl` alone does not configure cross-origin authentication.

## Structure

| Directory | Responsibility |
| --- | --- |
| `src/app/core` | Session, theme, configuration and HTTP protection |
| `src/app/layouts` | Application shell and route layout |
| `src/app/features` | Accounts, problems, submissions, contests and comments |
| `src/app/shared` | Feature-independent presentation/utilities |
| `src/styles` | Semantic tokens, base controls and syntax colors |
| `public` | Static brand assets, favicon and pre-bootstrap theme script |
| `testing` | Production-SPA fixture journeys and live browser checks |

Feature services own API calls; pages compose state and interactions. Use signals for local synchronous state, typed reactive forms for input and RxJS for HTTP/cancellation. Shared UI accepts data/events without importing feature services. Avoid speculative global stores or generic API/base classes.

## Sessions and source

Cookies remain browser-owned; CSRF headers apply only to marked Minos mutations. Network/server failures must not silently log the user out. Clear identity-scoped state and ignore stale responses after account changes. Never store credentials/tokens or submission source in browser storage.

Source/retry drafts are bounded and held in memory. Submission polls are serial, pause hidden tabs and stop at terminal states or errors requiring refresh. Keyed retry preserves the original admitted submission; reload or source edits after an uncertain response require checking history.

## Verification

`npm test` runs Vitest component/service/route tests. `npm run test:browser` requires a production build and Chromium; it serves the SPA on an ephemeral loopback port and uses isolated API fixtures. Screenshots and logs go to ignored `artifacts/`. Live scripts are invoked through `python scripts/checks/http-smoke.py` from the repository root.

Follow [UI conventions](../../docs/ui.md) for themes, navigation, safe rendering and validation. [Development](../../docs/development.md) covers real API/Worker checks and the distinction between fixtures and live verification.
