# ADR 0005: Database identities and private artifact roots

- Status: Accepted M2 engineering decision
- Date: 2026-09-14
- Scope: Developer-only backend foundation; production hosting and product authentication remain deferred.

## Context

The original fixture and CI supplied a PostgreSQL superuser to both runtime hosts. Storage had no required private-root configuration. These gaps conflict with the accepted operational/security boundaries and would make migration and authorization tests misleading.

## Decision

Use separate PostgreSQL logins: bootstrap for one-time provisioning; `minos_migrator` owns application objects and applies migrations; `minos_api` reads/inserts drafts; `minos_worker` only reads during M2. None of the latter three is a superuser, database owner, role/database creator, replication principal or RLS-bypass principal, or inherits another role. Remove PUBLIC database/schema grants. Grant access only to named current tables, including read-only migration history; no blanket default grants to future tables. Runtime hosts fail startup on identity/grant mismatch and never migrate automatically.

Require a configured, existing private host-local artifact root outside repositories. Reject symlinks/junctions. Linux requires private permission bits, protected ancestors and a native filesystem; Windows requires an owner/private ACL limited to the host account, SYSTEM and Administrators. Startup/readiness exercise asynchronous, cancellable read/write/delete access using a private temporary probe. Do not expose storage as static HTTP content.

## Alternatives and consequences

One shared superuser is simpler but cannot prove or enforce runtime restrictions. Broad schema/default grants reduce setup work but silently expose future tables. Explicit object grants require a deliberate update when new workflows need permissions.

A public/repository directory risks accidental publication or source-control inclusion. Object storage and cross-host sharing are unnecessary for this checkpoint; M4 must establish artifact keys, bounded uploads, verification and transfer/access before real judging artifacts are consumed. No production storage topology is selected. OS administrators and the developer remain trusted; configuration checks do not defend against a compromised host. PostgreSQL identities are operational credentials, separate from future user accounts.

## Evidence

[M2 executable results](../milestones/m2-foundation.md): fresh/repeat migrations, actor-specific privilege denials and grant-drift detection, private storage/ACL probes, HTTP errors, host startup and database/storage readiness recovery.
