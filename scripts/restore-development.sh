#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${MINOS_POSTGRES_CONTAINER:?Required}"
: "${MINOS_MIGRATOR_PASSWORD:?Required}"
: "${MINOS_RESTORE_DATABASE:?Required fresh provisioned database}"
: "${MINOS_RESTORE_ARTIFACT_ROOT:?Required empty private directory}"
backup="$(realpath -- "${1:?Pass a completed backup directory}")"
root="$(realpath -- "$MINOS_RESTORE_ARTIFACT_ROOT")"
repository="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
[[ "$root" != "$repository" && "$root" != "$repository/"* && "$(stat -c %a "$root")" = 700 ]]
[[ ! -e "$backup/.incomplete" && -z "$(find "$root" -mindepth 1 -print -quit)" ]]
[[ "$(stat -c %a "$backup")" = 700 ]]
(cd "$backup" && sha256sum -c manifest.sha256 >/dev/null)
# Validate every member before either database or filesystem mutation.
python3 - "$backup/artifacts.tar" <<'PY'
import pathlib, sys, tarfile
with tarfile.open(sys.argv[1]) as archive:
    for member in archive:
        path = pathlib.PurePosixPath(member.name)
        if path.is_absolute() or '..' in path.parts or not (member.isfile() or member.isdir()):
            raise SystemExit('Invalid backup archive member')
PY
empty="$(docker exec --env "PGPASSWORD=$MINOS_MIGRATOR_PASSWORD" "$MINOS_POSTGRES_CONTAINER" \
    psql -X -At -U minos_migrator -d "$MINOS_RESTORE_DATABASE" -v ON_ERROR_STOP=1 \
    -c "SELECT NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema' AND c.relkind IN ('r','p','v','m','S'))")"
[[ "$empty" = t ]]
docker exec -i --env "PGPASSWORD=$MINOS_MIGRATOR_PASSWORD" "$MINOS_POSTGRES_CONTAINER" \
    pg_restore -U minos_migrator -d "$MINOS_RESTORE_DATABASE" --single-transaction --exit-on-error --no-owner --no-acl < "$backup/database.dump"
tar -C "$root" --no-same-owner -xpf "$backup/artifacts.tar"
printf 'restore_completed=passed\n'
