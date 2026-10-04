#!/bin/sh
set -eu
export PGPASSWORD="$POSTGRES_PASSWORD"
psql -X --set ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --set database="$POSTGRES_DB" \
    --set migrator_password="$MINOS_MIGRATOR_PASSWORD" \
    --set api_password="$MINOS_API_PASSWORD" \
    --set worker_password="$MINOS_WORKER_PASSWORD" \
    --file /minos/postgres-bootstrap.sql
