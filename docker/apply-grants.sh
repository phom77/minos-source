#!/bin/sh
set -eu
export PGPASSWORD="$MINOS_MIGRATOR_PASSWORD"
exec psql -X --set ON_ERROR_STOP=1 --host "${MINOS_DATABASE_HOST:-postgres}" --username minos_migrator \
    --dbname minos --file /minos/postgres-runtime-grants.sql
