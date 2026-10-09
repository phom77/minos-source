# ADR 0016: Public practice contests with pinned problems

Date: 2026-10-09. Status: accepted scope requested by the owner; implementation policies below are the bounded defaults for this increment.

## Context

The owner requested backend/frontend contest creation, problem composition and participation, and clarified that only Setter/Admin accounts may organize contests. Problem-import and judging improvements are being implemented independently. Existing publication already produces public immutable versions; the Worker already judges a pinned version.

## Decision

Keep contests in the existing five-project architecture. A contest's active Setter/Admin owner manages a private draft. Publication freezes its ordered problem-version selection and UTC schedule. State is derived from server time. Active non-owner accounts may register until the end, including late entry. Submissions require registration and the start-inclusive/end-exclusive window. Registration is unique/idempotent; admission uses the same PostgreSQL advisory lock and global/per-account capacity as practice. Submission, job and contest association are committed together, with eligibility rechecked after waiting for the admission lock. The Worker does not need contest-table access or a new judging contract.

This is a public practice contest. Existing published versions remain readable from the problem library, including newly authored contest problems. Hiding the contest's list before the start only organizes the user flow; it is not a secrecy guarantee. Do not advertise exclusive or secret contest problems without a separate publication/access design.

## Consequences

- Republish/archive of the original problem never changes the pinned contest version.
- Contest ownership and roles are enforced in Application, including non-HTTP calls. An Administrator can organize their own contest but does not acquire another organizer's edit rights.
- Only the API receives contest persistence grants. No new production project, service, broker or scheduler is introduced.
- Accepted submissions remain in the ordinary owner-only result/history flows. Contest history includes only that viewer's entries and omits source.
- Contests, registrations and associations follow existing developer-project lifetime retention. Backups include the new schema. Apply the additive migration and explicit runtime grants before updated hosts start. Restoring pre-contest schema loses contest records; use the paired backup procedure when rollback requires preserving data.
- Ratings, leaderboard scoring, organizer-role applications, private invitations, post-publication rescheduling and custom Run are deferred. Creation/participation approval does not authorize M8 or development-MVP acceptance.

Implementation and verification: [contest plan](../contest-implementation-plan.md).
