# ADR 0017 — Account-scoped submission request idempotency

Date: 2026-10-12
Status: accepted for the owner-authorized Phase 1 implementation; validation evidence is recorded separately.

## Context

Admission already commits submission/job/contest association atomically. A client that loses the response after commit cannot know whether another POST creates duplicate work. The owner authorized the complete first phase of the [readiness plan](../microservice-readiness-plan.md), including this correctness change.

## Decision

- Both create endpoints accept an optional non-empty UUID `requestKey` in their JSON body. Existing keyless clients keep their previous behavior; updated first-party forms always send a key.
- Application derives a SHA-256 fingerprint from an unambiguously serialized request kind, contest ID when applicable, stable target ID, exact language and untrimmed source. The fingerprint format is a persistence contract; future changes must preserve historical replay compatibility. Request identity is not a Domain entity or HTTP type.
- PostgreSQL stores only account/key, fingerprint and submission ID in `evaluation.submission_requests`, in the same transaction as admission. The existing capacity advisory lock serializes lookup/replay and new admission. A primary key enforces account/key uniqueness; a composite submission/owner foreign key prevents cross-account associations. API has SELECT/INSERT only; Worker has no table privileges.
- An exact replay returns the original submission ID and current owner-visible evidence, including the original published version/toolchain. It does not consume another admission slot, create a job/attempt or resolve a new latest version. A changed valid payload/target with the same account/key returns safe 409. Malformed keys/source are validation errors.
- Account activation and normal authorization still apply on replay. Contest replay additionally requires current membership and visibility. Accepted contest work may be replayed after end time; a new key must pass the server-authoritative window again under the admission lock. Replay is checked before creating a new contest association, including the concurrent race path.
- Retain bounded key metadata for the lifetime of its referenced submission. No TTL silently turns an old retry into new work. Rejected/rolled-back admission retains no key. Legacy history is not backfilled.
- The browser holds one account-owned, bounded request draft in memory. Retry with unchanged source/target reuses its key; acknowledgement or an edit starts a new request identity. Identity changes clear it. Internal navigation may preserve it; reload/closing the browser loses it, so the UI directs users to check history before a new POST in that case. Source is never persisted in browser storage.

## Consequences and release

This adds one table and one migration within Infrastructure, without projects, dependencies, brokers or service topology changes. Apply migration and updated explicit grants with hosts stopped before updated binaries. Runtime startup validates the new API/Worker privilege baseline.

For rollback, stop matching hosts and retain the additive table and metadata while restoring prior binaries/grants as appropriate. Old clients/binaries cannot promise idempotent retry. Dropping the table discards replay protection; do not roll back the schema and later claim historical keys still work. Restore a consistent database/artifact backup only to a fresh target using the existing runbook.

Validation covers concurrent requests, lost responses/restart, publication/archive, capacity/rejection, payload/target conflicts, account isolation/disabled accounts, contest end-time/membership, PostgreSQL constraints/grants and first-party browser retry/new-submit states. Existing attempt authority, complete-test evidence and isolation remain unchanged.
