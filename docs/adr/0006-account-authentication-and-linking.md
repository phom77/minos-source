# ADR 0006: Provider-independent accounts and framework authentication

- Status: Selected and implemented for the approved M3 scope; live Google validation pending
- Date: 2026-09-14
- Decision: D11

## Context

M3 replaces the development-flag access policy with real self-service accounts and ownership. Domain must remain independent of credential stores and providers. The project owner explicitly prohibits silent email-based account merges and defers complete Setter/Admin policies to M4.

## Decision

Domain `Account` owns an immutable GUID, bounded display name, UTC creation time and disabled state. `ProblemDraft` requires an immutable account owner. Application owns active-account and owner checks, account use cases, and focused account/external-login persistence ports.

Infrastructure uses ASP.NET Core Identity's role-free user context for credentials, framework password hashing/validation, lockout, normalized unique email, security stamps and external-login mappings. Account and credential rows share an ID but remain separate models. Registration and provider linking use explicit PostgreSQL transactions. Conditional account updates reject edits after concurrent disabling.

API owns Identity cookie authentication, principal composition, antiforgery and Google OAuth middleware. The session cookie is HTTPS-only, HttpOnly, SameSite=Lax and expires absolutely after eight hours. Stamp validation occurs every request without renewing expiry. Logout revokes all account sessions; password changes, disabling and provider linking rotate the security stamp. Protected-cookie keys persist under the private artifact root's `data-protection` directory; Windows uses DPAPI. Linux development relies on private filesystem protection.

Authentication requests are bounded to 16 KiB, including streaming bodies. Passwords contain 12–256 characters; the framework handles hashing without mandatory character categories. Login/proof endpoints have a 20-request/minute/IP local limiter and framework five-failure/15-minute lockout. Generic credential errors and a framework dummy-hash verification reduce account enumeration. Request logging excludes OAuth callback query strings; credentials/provider tokens are never deliberately logged or persisted as plaintext.

Google is opt-in and requires private client configuration at startup. Standard middleware handles OAuth state, correlation and PKCE; five-minute flows/external cookies and a bounded backchannel are used. Tokens are not saved. Only middleware-authenticated provider/subject evidence reaches Application. A new Google account requires verified email. An existing mapping follows its stable subject even when email changes.

External mappings are unique by `(provider, subject)` and by `(account, provider)`. Matching an existing account's email never creates a link or merges accounts. Explicit linking requires an active session, password confirmation and successful Google authentication bound to that same account. It revokes old sessions and issues a fresh session to the completing browser. Additional providers require an adapter/middleware change, not a Domain change.

Email delivery, confirmation enforcement, forgotten-password recovery, MFA and provider-only reauthentication/account management remain deferred. Local unverified accounts may sign in during developer-only development. Google-only accounts support sign-in/logout; password-confirmed management/linking requires a local credential. Before public access, recovery/verification and provider-only management must be completed or explicitly scoped by the owner. No enrollment or administrator approval is introduced.

## Alternatives and consequences

A bearer-token/session framework would add lifecycle and storage work without a current same-origin web requirement. Using Identity entities as Domain accounts would couple business state and ownership to authentication mechanisms. Automatic email matching would weaken the explicitly required linking boundary.

Per-request stamp/state validation requires PostgreSQL availability and extra reads; it gives immediate revocation on subsequent requests without a separate session table. A private key directory must survive API restart; losing it invalidates cookies. Distributed rate limits and production key management are deferred. Unverified local registration can reserve an email and block Google signup; recovery/verification is therefore a public-release dependency.

Legacy development drafts have no trustworthy owner. The additive ownership migration rejects databases containing them and preserves their data, requiring an explicit data migration. The developer database was empty before upgrading. Existing M2 migrations remain unchanged.

## Evidence

[M3 results](../milestones/m3-accounts.md) cover local and deterministic Google flows, authorization outside HTTP, concurrent registration/disabling, absolute expiry and revocation, PostgreSQL privileges/constraints, migration safety and both hosts. Live Google consent remains pending because the owner has no OAuth client yet.
