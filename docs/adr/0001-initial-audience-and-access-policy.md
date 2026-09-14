# ADR 0001: Development access and future registration behavior

Decision register ID: D01\
Created: 2026-09-13\
Status: Accepted\
Decision owner: Project owner\
Accepted date: 2026-09-13

Related documents: [Decision register](../decisions.md), [Project design](../project-design.md), [Implementation roadmap](../implementation-plan.md)

## Context

Minos is a personal project with no real users during development. The initial proposal assumed a private-pilot cohort and controlled enrollment. The project owner explicitly rejected that assumption and separated development access from intended product authentication.

Developer-only development identifies who uses the environment today. It does not determine how normal users will register when the product is complete, nor does it remove the need to develop and test authentication and authorization.

## Options considered

| Option | Benefits | Trade-offs | Outcome |
| --- | --- | --- | --- |
| Developer-only development with self-service product authentication | Matches the current project; permits realistic account-flow tests without enrollment management | No external-user feedback or real-user capacity evidence during development | Selected |
| Private pilot with approved participants | Feedback from a controlled real-user group | Requires a cohort and enrollment operations that do not exist | Rejected for current scope |
| Public beta during development | Broad participation and feedback | Adds public operation, support, and abuse-management obligations | Not selected |

## Decision

### Development access

- The environment is developer-only.
- There are no real users, private-pilot cohort, or enrollment-management requirements.
- No administrator approval or invitation workflow is designed for normal-user registration.
- The developer and automated tests use test identities to exercise intended product roles and ownership rules.
- A private pilot may be planned only after an explicit later project-owner decision.

### Intended finished-product authentication

- Normal users can register using email and password.
- Google sign-in is the first third-party authentication provider.
- Normal users do not need administrator approval to register or sign in.
- These are intended product capabilities to implement and validate even while the environment remains developer-only.

Development access and future product authentication remain separate concerns in architecture, configuration, documentation, and acceptance criteria.

## Rationale and MVP fit

This reflects the actual project audience and avoids enrollment-management work without a product need. Test identities and automated checks can establish role/ownership behavior during development. The future product retains ordinary self-service authentication rather than inheriting a temporary development access restriction.

Google is the first external provider; this decision does not require additional providers or an extensible provider-management feature.

## Consequences

### Positive

- No pilot coordination, enrollment management, approval queues, or invitation system.
- Clear acceptance criteria for both developer-only access and the intended registration/sign-in behavior.
- The roadmap can validate a development MVP without inventing real-user milestones.

### Costs and limitations

- Development evidence comes from the developer, test identities, synthetic workloads, and automated tests rather than a real-user cohort.
- Email/password and Google integration still need correct account lifecycle, authorization, and provider configuration.
- Developer-only access does not make executed programs trusted; compile/run isolation and resource controls remain necessary.
- A future public deployment still needs its own capacity and operational review.

### Effects on later decisions

- D02 selects a practical development execution environment; no purchased or remote judge host is assumed.
- D10 defines the minimum safety contract; M1 smoke-test bounds and M7 synthetic workload targets replace cohort forecasts.
- D11 implements email/password and Google behavior and resolves identity/session integration, verification, recovery, external-account linking, and privileged-account provisioning. It does not add normal-user approval.
- D17 defines intended product visibility and retention independently of who currently accesses development.
- M3 includes self-service email/password and Google sign-in.
- M7 validates development MVP completion; a real-user pilot is not required.

## Deferred details

- Linux environment and host layout: D02.
- Session/cookie/token choice, verification/recovery, Google callback configuration, and safe account linking: D11.
- Exact smoke-test bounds: M1. Broader synthetic capacity targets and retention policies: relevant later milestones before M7.
- Public hosting, production capacity, or any private-pilot operation: explicit later decisions.

## Acceptance evidence

The project owner explicitly specified developer-only development, no real users or enrollment/approval workflow, and intended self-service email/password plus Google authentication. The owner requested this ADR revision and continuation to D02.

The earlier private-pilot recommendation was never accepted. This accepted record replaces that proposal.

## Follow-up

The documents are synchronized; D02, D03, and the D10 baseline are accepted in their separate ADRs. M0 is complete as recorded in the [closure summary](../m0-closure.md). This decision establishes product/development access. Subsequent implementation progress is recorded in the [M1 work log](../milestones/m1-execution-feasibility.md).
