# ADR 0007: Versioned problems and private test artifacts

- Status: Accepted and implemented for M4
- Date: 2026-09-19
- Decisions: D05, D12, D13, M4 portion of D17

## Context

Problem authoring must remain editable while every definition used by future judging remains stable. Hidden input and expected-output data must survive later edits without becoming web-visible. M4 also needs a practical reference-validation gate, but trusted reference execution is not available until the M6 sandbox workflow.

## Decision

`Problem` is the aggregate root and stable identity across revisions. It owns the current editable authoring state: immutable owner, title, statement, execution limits, exact-checker selection, public samples, ordered private-test references, latest published version number, archive state and an optimistic-concurrency revision. The former foundation `ProblemDraft` evolves into this authoring state rather than remaining a separate entity.

Publishing validates configured limits, at least one sample, at least one private test, verified artifact existence/checksums and a completed publication checklist. It atomically increments the aggregate version and inserts a new `PublishedProblemVersion` snapshot. A version has its own GUID and a unique positive `(ProblemId, VersionNumber)` identity. It copies all judged configuration and private artifact references. Published classes expose no mutation behavior, and the API runtime role has insert/read but no update/delete grants on published tables. Later author edits change only the current `Problem`; the next publication creates another snapshot. Archiving blocks authoring and removes the problem from current catalog/latest-detail discovery while exact version IDs remain readable for history and future pinned submissions.

Solver is the registration default. Setter and Administrator may author only problems they own. Administrator may assign account roles through an Application-authorized operation; a migrator-only maintenance command provisions the first administrator. Role changes rotate the target account's security stamp. There is no pilot approval or enrollment workflow.

Private tests arrive as a ZIP containing exactly root-level `input.txt` and `output.txt`. Packages are limited to 16 MiB and each extracted artifact to 8 MiB. Infrastructure generates opaque keys under the configured private root, rejects unsafe names, duplicate entries, links and root escapes, stages extraction, hashes content, and promotes files before committing metadata. A database failure triggers best-effort immediate deletion; startup removes unreferenced uploads older than 24 hours. Published versions retain references, so editing a draft cannot clean up historical test data. Broader retention and quotas remain M7.

M4 uses a setter-attested checklist: statement reviewed, samples verified and reference solution checked. This is trusted problem-author evidence, not contestant execution and not an automated correctness guarantee. Automatic reference-solution execution through the Linux sandbox remains M6 work.

## Consequences and alternatives

Mutating one published row would be simpler but would make queued and historical results irreproducible. A separate persistent draft plus problem entity would add a lifecycle and joins without protecting another current invariant. Snapshot rows duplicate small public metadata and artifact references, which is the intended cost of stable judging specifications.

PostgreSQL and private filesystem updates cannot share one atomic transaction. Staging, verification, database commit, immediate compensation and age-based cleanup provide a bounded local failure model without an outbox or distributed transaction. The host and its administrators remain trusted; this design prevents client-controlled paths and accidental HTTP exposure rather than defending against a compromised host.

## Evidence

[M4 results](../milestones/m4-problems.md) cover Domain snapshots, authorization, real PostgreSQL concurrency/constraints/migrations, runtime grant immutability, unsafe and oversized packages, cleanup, restart persistence, public projections and a real HTTPS publication flow.
