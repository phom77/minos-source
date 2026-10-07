#!/usr/bin/env bash
set -euo pipefail
umask 077
# Quiesce both hosts first: PostgreSQL and filesystem snapshots must describe one state.
: "${MINOS_BACKUP_QUIESCED:?Set to 1 after stopping API and Worker}"
[[ "$MINOS_BACKUP_QUIESCED" = 1 ]]
: "${MINOS_POSTGRES_CONTAINER:?Required}"
: "${MINOS_MIGRATOR_PASSWORD:?Required}"
: "${MINOS_ARTIFACT_ROOT:?Required}"
: "${MINOS_BACKUP_ROOT:?Required}"
repository="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
artifact_root="$(realpath -- "$MINOS_ARTIFACT_ROOT")"
backup_root="$(realpath -- "$MINOS_BACKUP_ROOT")"
[[ "$artifact_root" != "$repository" && "$artifact_root" != "$repository/"* ]]
[[ "$backup_root" != "$repository" && "$backup_root" != "$repository/"* && "$backup_root" != "$artifact_root" && "$backup_root" != "$artifact_root/"* ]]
[[ "$(stat -c %a "$artifact_root")" = 700 && "$(stat -c %a "$backup_root")" = 700 ]]
[[ -z "$(find "$artifact_root" -type l -print -quit)" ]]
backup="$(mktemp -d "$backup_root/minos-backup.XXXXXXXX")"
touch "$backup/.incomplete"
docker exec --env "PGPASSWORD=$MINOS_MIGRATOR_PASSWORD" "$MINOS_POSTGRES_CONTAINER" \
    pg_dump -U minos_migrator -d "${MINOS_DATABASE:-minos}" --format=custom --no-owner --no-acl > "$backup/database.dump"
tar -C "$artifact_root" -cpf "$backup/artifacts.tar" .
(cd "$backup" && sha256sum database.dump artifacts.tar > manifest.sha256)
rm -- "$backup/.incomplete"
printf '%s\n' "$backup"
