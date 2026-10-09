# Moving the development project to another machine

GitHub preserves source, migrations, lockfiles, tests and engineering documents. It does not preserve the local database, Docker volumes or ignored private files. Restore the database and its private artifacts as one consistent pair.

## Private backup inventory

The 2026-10-09 machine-move backup is outside the repository at `D:/MinosData/backups/machine-move-20261009-final`. It was captured with API and Worker stopped, then their existing containers were resumed directly. Its files are:

| File | Purpose |
| --- | --- |
| `database.dump` | PostgreSQL custom-format dump: accounts, credentials/provider mappings, problems/versions, submissions, jobs and retained result evidence. |
| `artifacts.tar` | Entire private-artifact volume, including private input/answer files and persisted cookie data-protection keys. |
| `certificates.tar` | Local development CA and HTTPS certificates/private keys. Trust the development CA again on the new machine if reusing these certificates. |
| `local-config.zip` | `.env`, project API user secrets, local `.agents` skills and `artifacts/codecontests-private/owner.json` importer credentials. These contain secrets. |
| `problem-bank.zip` | Complete ignored `data/codecontests-200` tree: raw Parquet shards, candidate/reference files, import payloads, receipts, identifiers and verification metadata. Raw data is reproducible, but copying it avoids downloading/extracting again. |
| `verification-evidence.zip` | Selected local test reports, browser screenshots and judging experiment evidence. Useful historical evidence, not required to run the app. |
| `manifest.json`, `manifest.sha256` | File sizes and SHA-256 checksums for transfer integrity. |

Copy the **entire completed directory off the old computer** before resetting or disposing of it. Keep it on trusted private/encrypted storage; do not add it to Git or publish it as a GitHub artifact. Its Windows ACL protects the current local copy, not an arbitrary destination. Keep the original until restoration is verified. An `.incomplete` marker means the backup is unusable.

## Restore order

1. Clone/pull this repository and install the Docker Desktop/WSL2 prerequisites documented in the [README](../../README.md). Docker builds provide the pinned application toolchains; native development additionally needs .NET 10 and the supported Node version. Check every backup checksum before extraction.
2. Restore `.env` and the portable project files from `local-config.zip` into the new checkout. Put `UserSecrets/minos-api-development/secrets.json` into the new user's platform user-secrets directory, not into tracked source. Move `.agents` with the project. Extract `problem-bank.zip` into the checkout if retaining the import/research tooling. Paths and optional provider settings may need adjustment for the new host. Do not print secret contents.
3. Build the current Docker images, then start **only PostgreSQL** on a fresh volume. Its initializer provisions the stable `minos_migrator`, `minos_api` and `minos_worker` roles using the restored private passwords. Keep API and Worker stopped. Do not run application migrations against the empty database before restoring the dump: restore requires an empty target.
4. Restore `database.dump` with `pg_restore --single-transaction --exit-on-error --no-owner --no-acl` as `minos_migrator`. Restore `artifacts.tar` into a fresh private-artifact volume and `certificates.tar` into a fresh local-certs volume. Validate archive members before extraction and use the documented container ownership/permissions. The [development runbook](development-runbook.md#backup-and-restore) and `scripts/restore-development.sh` describe guarded database/private-files restoration for a private Linux filesystem target; the Compose profile uses the corresponding named volumes instead of that filesystem target. Never restore over an active database or merge private roots.
5. Run current migrations, then current runtime grants, before starting matching API/Worker binaries. This backup predates the contest migration; the restored database must be upgraded. Build/init/migrate/grant dependencies in the Compose profile implement that order once restoration is complete. Keep the 1,500-test capacity migration; older 100-test binaries/schema cannot accept the existing larger versions.
6. Start the normal Compose profile, trust its development CA on the new host and require API/Worker/web readiness. Verify the existing 200-problem bank, an existing account/history and a new submission through the real Worker. Reapply runtime grants after restoration; `--no-acl` intentionally omits them. Retain the private backup until these checks pass.

Docker images, NuGet/npm caches, `node_modules`, `bin`, `obj`, `.angular`, `dist`, generated build assets and the judge execution-workspace volume can be recreated. Do not copy restore assets between Windows and Linux. This machine had no configured native Isolate installation to transfer; native helper/cgroup installation requires the runbook on a compatible new Linux host. GitHub/SSH authentication is machine-specific and must be set up separately.

The backup is a completed snapshot, not ongoing synchronization. Any later database, private-file or credential changes require a new paired backup before the final move.
