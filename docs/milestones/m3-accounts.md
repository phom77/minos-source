# M3 account implementation and validation

Date: 2026-09-14. Owner: project developer.

**M3 is complete and approved for the development scope.** The owner confirmed that no Google OAuth client exists, so live Google consent remains a deferred external smoke check rather than a blocker for M4.

## Implemented foundation

| Layer / class | Responsibility |
| --- | --- |
| Domain `Account` | Identity independent of providers; display-name/UTC invariants; disabling |
| Domain `ProblemDraft` (M3 foundation model) | Required immutable account ownership; evolved into M4 `Problem` |
| Application `AccountUseCases` | Self-registration/login/profile/logout/password/disable orchestration; active-account authorization |
| Application `ExternalLoginUseCases` | Resolve stable identities; signup and explicit account-bound linking |
| Application `ProblemDraftAccess` / use cases (M3 foundation) | Active identity and owner checks even without HTTP; replaced by M4 authoring use cases |
| Infrastructure `AccountStore` / `ExternalLoginStore` | Focused persistence, conditional profile/state updates, explicit transactions, Identity integration |
| Infrastructure `AccountUserManager` | Framework-supported cancellation override for Identity EF operations |
| API `AuthenticationComposition` | Middleware/cookie/CSRF/rate-limit/key configuration and framework principal composition |
| API account / Google endpoints | Explicit transport contracts and thin calls to Application |

The five production projects and two test projects remain unchanged. Domain/Application have no framework packages. JudgeWorker registers no web authentication and cannot access account/credential/provider tables.

PostgreSQL adds `accounts.accounts`, `credentials`, `external_logins`, `claims` and `tokens`. Identity remains a separate role-free persistence model. Unique normalized emails, stable login keys, one login per provider/account, profile constraints and owner foreign keys protect persisted states. API receives only required grants; Worker receives none in the account schema. The development database has all six migrations.

## Request flows

- Obtain `/accounts/csrf`, retain its cookie, and send `requestToken` as `X-CSRF-TOKEN` on POST/PATCH. Fetch a fresh token after registration/login changes the identity.
- `POST /accounts/register`: validate bounds → Domain account → atomic account/hashed credential creation → protected cookie → explicit account response. No approval.
- `POST /accounts/login`: framework password verification/lockout → active account → cookie. Credential failures are generic.
- `GET /accounts/me`, `PATCH /accounts/me`: active account → explicit profile projection / Domain rename. Input cannot grant privileges.
- `POST /accounts/logout`, `/password`, `/disable`: Application authorization → revocation/change → clear browser cookies. Disable/password operations require the current password.
- `GET /accounts/google/login` → standard `/signin-google` middleware → `/accounts/google/complete` → stable identity lookup/signup → cookie → fixed `/accounts/me` redirect.
- Matching Google/local email returns a controlled conflict. Sign in locally, then `POST /accounts/google/link` with password/CSRF; provider proof is bound to that account. Existing email confirmation is not silently changed.
- At M3 completion, opt-in development draft endpoints required a real active account and immutable ownership. M4 subsequently replaced them with real Setter/Admin authoring and publication routes.

Account HTTP endpoints require HTTPS. See [ADR 0006](../adr/0006-account-authentication-and-linking.md) for session/linking and deferred recovery policies.

## Automated evidence

`bash scripts/check-foundation.sh` runs the clean locked Release pipeline with isolated PostgreSQL and real HTTPS requests. Google is disabled by default; provider tests explicitly enable a fake backchannel, independent of private Google configuration.

- Windows build: zero warnings/errors. Full PostgreSQL/HTTP suite: 11 Domain and 78 integration/Application/architecture tests passed; one Linux-only permission test skipped. Secure sessions persist across restart with DPAPI-protected keys. Its temporary database/private directory were removed afterward.
- WSL Release: 11 Domain and 78 integration/Application/architecture tests passed; one Windows-only ACL test skipped and validated separately on Windows.
- Fresh/repeated migrations, explicit API/Worker privilege denials and drift detection passed. A separate database proves legacy ownership upgrade fails while preserving draft content.
- Local registration races preserve one account; credentials are hashed; login/lockout, streaming-body/CSRF/rate limits, profile safety, restart persistence, absolute expiration and replay/all-session revocation passed.
- Google middleware challenge/correlation/state/PKCE/backchannel and deterministic signup, subject stability across restart/email changes, verified-email requirement, collision/no-merge and explicit account-bound/conflicting links passed without Google calls.
- Direct Application tests reject anonymous/disabled identities and cross-owner reads. Concurrent disabling blocks an in-flight profile edit.
- Real HTTPS account/draft roundtrip under a normal Linux umask, private storage/readiness, database outage/recovery and independent Worker startup/cancellation passed. Key directories are created/validated before DataProtection initialization.
- Windows API user secrets now contain the existing development API connection, private `%LOCALAPPDATA%\Minos\artifacts` root and development-route opt-in. Real configured HTTPS startup returned readiness 200 and anonymous `/accounts/me` 401. No account was created in the persistent database by this smoke check.

TRX files and local logs are under ignored `artifacts/`. GitHub Actions invokes the same script; a hosted run follows commit/push and has not been claimed here.

## Remaining live Google check

Create a Google OAuth client of type **Web application** and register exactly `https://localhost:49674/signin-google` as its authorized redirect URI. Use your own Google account in the provider's development/testing configuration. Store client settings on the host running API, outside source control.

PowerShell 7, from the repository:

```powershell
$minosGoogleId = Read-Host 'Google client ID'
$minosGoogleSecret = Read-Host 'Google client secret' -MaskInput
@{
    'Authentication:Google:Enabled' = 'true'
    'Authentication:Google:ClientId' = $minosGoogleId
    'Authentication:Google:ClientSecret' = $minosGoogleSecret
} | ConvertTo-Json | dotnet user-secrets set --project src/Minos.Api
Remove-Variable minosGoogleId, minosGoogleSecret
dotnet run --project src/Minos.Api --launch-profile Minos.Api
```

Keep the existing API database/private-root configuration. Open `https://localhost:49674/accounts/google/login`, complete consent, and confirm `/accounts/me`. Repeat sign-in to confirm the same account. Verify an email collision does not merge; explicit linking requires local login/password and Google proof. Record that result here before marking M3 complete. Client secret values should never be pasted into repository files or the conversation.

If the browser rejects the localhost development certificate, run `dotnet dev-certs https --trust` yourself and approve the Windows trust prompt. The certificate is present; this implementation did not add certificate trust.

## Completion and limits

- [x] Provider-independent account model and self-service local flows
- [x] Secure cookie/CSRF, bounded authentication, generic errors and configuration validation
- [x] Deterministic Google signup/login/linking; no email-based merge
- [x] Resource authorization in Application; immutable owner/FK
- [x] Revocation, account state and persistence/restart tests
- [x] PostgreSQL privileges, migration safety and architecture checks
- [ ] Deferred live developer Google consent/callback validation with an actual OAuth client
- [x] M3 approved before M4 began

Email confirmation/recovery delivery and provider-only passwordless management remain deferred and block public release. Authentication limiting is local to one API process. Linux development key encryption relies on private filesystem protection. This milestone provides backend endpoints, not a frontend. No Setter/Admin provisioning or later evaluation features are implemented.

M4 subsequently added the actual problem-authoring/testing/publication workflows and their permissions; see [M4 results](m4-problems.md).
