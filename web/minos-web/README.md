# Minos web

The Minos Angular application is a standalone, feature-first frontend. It is intentionally small: this foundation establishes the shell, routing, HTTP boundary, error representation, shared visual language, and initial Accounts and Problems feature entry points without inventing M4 contracts.

## Commands

```bash
npm install
npm start
npm test
npm run build
```

`npm start` proxies `/accounts` and `/health` to the existing HTTPS API launch profile at `https://localhost:49674`. Production requests are same-origin by default. Override `apiBaseUrl` in the environment configuration only when deployment topology requires a separate API origin.

## Structure

- `core/` owns application-wide configuration and HTTP transport concerns.
- `shared/` owns UI that is already reused across business features.
- `layouts/` owns application chrome and page composition.
- `features/accounts/` is the future home of account pages, account data access, and account-specific presentation.
- `features/problems/` is the future home of public problem discovery. Authoring remains out of scope until M4 contracts are approved.

Feature folders grow by responsibility, not by template. Add `pages/`, `components/`, `data-access/`, or `state/` only when real code makes the distinction useful.
