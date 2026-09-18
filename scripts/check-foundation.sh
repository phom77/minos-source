#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0
umask 077
trap 'printf "Foundation checks failed at line %s.\n" "$LINENO" >&2' ERR
readonly repository="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
readonly dotnet_command="${MINOS_DOTNET:-dotnet}"
readonly postgres_image='postgres@sha256:1c59e2c3c818eaa0f0628f695b36e7c9e362d6b219b36a54a32df645cbd7e1af'
workspace="$(mktemp -d /tmp/minos-foundation.XXXXXXXX)"
container=''
database_volume=''
api_pid=''
worker_pid=''
cleanup() {
    local failed=0
    if [[ -n "$api_pid" ]] && kill -0 "$api_pid" 2>/dev/null; then kill "$api_pid"; wait "$api_pid" || true; fi
    if [[ -n "$worker_pid" ]] && kill -0 "$worker_pid" 2>/dev/null; then kill "$worker_pid"; wait "$worker_pid" || true; fi
    if [[ -n "$container" ]]; then
        if [[ "$(docker inspect --format '{{.State.Running}}' "$container")" != true ]]; then
            docker start "$container" >/dev/null || failed=1
        fi
        docker exec --user 0 "$container" chown "$(id -u):$(id -g)" /var/run/postgresql >/dev/null || failed=1
        docker rm -f "$container" >/dev/null || failed=1
    fi
    if [[ -n "$database_volume" ]]; then docker volume rm "$database_volume" >/dev/null || failed=1; fi
    # Only this freshly created Linux-native fixture is removed; never a repository/mount path.
    if [[ "$failed" = 0 && "$workspace" = /tmp/minos-foundation.* && "$(realpath -- "$workspace")" = "$workspace" ]]; then
        rm -rf -- "$workspace"
    else failed=1; fi
    return "$failed"
}
finish() { local status=$?; trap - EXIT; cleanup || status=1; exit "$status"; }
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -m 777 "$workspace/socket"
mkdir -m 700 "$workspace/private-artifacts" "$workspace/source"
password="$(cat /proc/sys/kernel/random/uuid)"
migrator_password="$(cat /proc/sys/kernel/random/uuid)"
api_password="$(cat /proc/sys/kernel/random/uuid)"
worker_password="$(cat /proc/sys/kernel/random/uuid)"
database_volume="minos-foundation-$(cat /proc/sys/kernel/random/uuid)"
docker volume create --label minos.check=foundation "$database_volume" >/dev/null
database_arguments=(run -d --network none --label minos.check=foundation --memory 512m --pids-limit 64
    --mount "type=volume,src=$database_volume,dst=/var/lib/postgresql" --mount "type=bind,src=$workspace/socket,dst=/var/run/postgresql"
    --env POSTGRES_DB=minos --env POSTGRES_USER=minos_bootstrap --env "POSTGRES_PASSWORD=$password"
    --env 'POSTGRES_INITDB_ARGS=--auth-local=scram-sha-256 --auth-host=scram-sha-256'
    "$postgres_image" -c 'listen_addresses=')
container="$(docker "${database_arguments[@]}")"
wait_for_postgres() {
    for attempt in {1..60}; do
        if docker exec "$container" /bin/sh -c 'test "$(cat /proc/1/comm)" = postgres && pg_isready -U minos_bootstrap -d minos' >/dev/null 2>&1; then return; fi
        sleep 0.5
    done
    return 1
}
wait_for_postgres
docker exec -i --env "PGPASSWORD=$password" "$container" psql -X --set ON_ERROR_STOP=1 -U minos_bootstrap -d minos \
    --set database=minos --set "migrator_password=$migrator_password" --set "api_password=$api_password" \
    --set "worker_password=$worker_password" < "$repository/scripts/postgres-bootstrap.sql" > "$workspace/bootstrap.log" 2>&1
export ConnectionStrings__Minos="Host=$workspace/socket;Database=minos;Username=minos_api;Password=$api_password;Timeout=3"
export MINOS_TEST_MIGRATION_CONNECTION="Host=$workspace/socket;Database=minos;Username=minos_migrator;Password=$migrator_password;Timeout=3"
export MINOS_TEST_WORKER_CONNECTION="Host=$workspace/socket;Database=minos;Username=minos_worker;Password=$worker_password;Timeout=3"
export MINOS_TEST_BOOTSTRAP_CONNECTION="Host=$workspace/socket;Database=minos;Username=minos_bootstrap;Password=$password;Timeout=3"
export Minos__Artifacts__Root="$workspace/private-artifacts"
export Authentication__Google__Enabled=false
# Provider tests explicitly enable a fake backchannel; local secrets never enable real Google here.
unset Authentication__Google__ClientId Authentication__Google__ClientSecret
# Build a clean source export, excluding prior outputs and execution spikes. CI uses this exact pipeline.
tar -C "$repository" --exclude=bin --exclude=obj --exclude='*.user' -cf - \
    src tests scripts docs .config .github AGENTS.md README.md Directory.Build.props Directory.Packages.props global.json Minos.sln | tar -C "$workspace/source" -xf -
export MINOS_TEST_REPOSITORY="$workspace/source"
readonly build_directory="$workspace/build"
"$dotnet_command" restore "$workspace/source/Minos.sln" --locked-mode --artifacts-path "$build_directory" --nologo
"$dotnet_command" build "$workspace/source/Minos.sln" --no-restore -c Release --artifacts-path "$build_directory" --nologo
readonly api_dll="$build_directory/bin/Minos.Api/release/Minos.Api.dll"
readonly worker_dll="$build_directory/bin/Minos.JudgeWorker/release/Minos.JudgeWorker.dll"
ConnectionStrings__Minos="$MINOS_TEST_MIGRATION_CONNECTION" "$dotnet_command" "$api_dll" --migrate > "$workspace/migrate.log" 2>&1
ConnectionStrings__Minos="$MINOS_TEST_MIGRATION_CONNECTION" "$dotnet_command" "$api_dll" --migrate > "$workspace/migrate-repeat.log" 2>&1
docker exec -i --env "PGPASSWORD=$migrator_password" "$container" psql -X --set ON_ERROR_STOP=1 -U minos_migrator -d minos \
    < "$repository/scripts/postgres-runtime-grants.sql" > "$workspace/grants.log" 2>&1
printf 'fresh_migration_and_repeat=passed\n'
test_filter=()
if [[ -n "${MINOS_TEST_FILTER:-}" ]]; then test_filter=(--filter "$MINOS_TEST_FILTER"); fi
"$dotnet_command" test "$workspace/source/Minos.sln" "${test_filter[@]}" --artifacts-path "$build_directory" --no-build -c Release --nologo \
    --logger trx --results-directory "$repository/artifacts/tests"
ConnectionStrings__Minos="$MINOS_TEST_WORKER_CONNECTION" "$dotnet_command" "$worker_dll" --check > "$workspace/worker-check.log" 2>&1
grep -q 'worker_foundation=passed' "$workspace/worker-check.log"
printf 'worker_foundation=passed\n'
# Prove the idle Worker actually remains hosted until cancellation, then shuts down cleanly.
ConnectionStrings__Minos="$MINOS_TEST_WORKER_CONNECTION" "$dotnet_command" "$worker_dll" > "$workspace/worker-host.log" 2>&1 &
worker_pid=$!
for attempt in {1..60}; do
    kill -0 "$worker_pid"
    if grep -q 'Application started' "$workspace/worker-host.log"; then break; fi
    sleep 0.25
done
grep -q 'Application started' "$workspace/worker-host.log"
kill -0 "$worker_pid"
kill -TERM "$worker_pid"
wait "$worker_pid"
printf 'worker_host_start_and_cancellation=passed\n'
mkdir -m 700 "$workspace/api-private-artifacts"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$workspace/tls.key" -out "$workspace/tls.crt" \
    -days 1 -subj /CN=localhost > "$workspace/tls.log" 2>&1
openssl pkcs12 -export -out "$workspace/tls.pfx" -inkey "$workspace/tls.key" -in "$workspace/tls.crt" \
    -passout pass: > "$workspace/tls-export.log" 2>&1
(
umask 022
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=https://127.0.0.1:0 Minos__EnableDevelopmentEndpoints=true \
    Minos__Artifacts__Root="$workspace/api-private-artifacts" \
    ASPNETCORE_Kestrel__Certificates__Default__Path="$workspace/tls.pfx" \
    exec "$dotnet_command" "$api_dll" > "$workspace/api.log" 2>&1
) &
api_pid=$!
api_url=''
for attempt in {1..60}; do
    kill -0 "$api_pid"
    api_url="$(python3 -c 'import re,sys; m=re.search(r"Now listening on: (https://127\.0\.0\.1:[0-9]+)",open(sys.argv[1]).read()); print(m[1] if m else "")' "$workspace/api.log")"
    if [[ -n "$api_url" ]] && curl -kfsS "$api_url/health/ready" >/dev/null 2>&1; then break; fi
    sleep 0.25
done
curl -kfsS "$api_url/health/ready" >/dev/null
csrf="$(curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" "$api_url/accounts/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])')"
curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"email":"developer@example.test","password":"correct horse battery staple","displayName":"Developer"}' "$api_url/accounts/register" > "$workspace/account.json"
csrf="$(curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" "$api_url/accounts/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])')"
curl -kfsS -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' -d '{"title":"A + B","statement":"Print the sum."}' \
    "$api_url/_development/problem-drafts/" > "$workspace/created.json"
draft_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$workspace/created.json")"
curl -kfsS -b "$workspace/cookies" "$api_url/_development/problem-drafts/$draft_id" | \
    python3 -c 'import json,sys; r=json.load(sys.stdin); assert r["title"] == "A + B" and r["statement"] == "Print the sum."'
test "$(curl -ksS -o /dev/null -w '%{http_code}' "$api_url/private-artifacts/any-file")" = 404
printf 'real_http_draft_roundtrip_and_private_storage=passed\n'
# Storage must affect readiness, without affecting liveness.
chmod 755 "$workspace/api-private-artifacts"
test "$(curl -ksS -o /dev/null -w '%{http_code}' "$api_url/health/ready")" = 503
curl -kfsS "$api_url/health/live" >/dev/null
chmod 700 "$workspace/api-private-artifacts"
curl -kfsS "$api_url/health/ready" >/dev/null
printf 'storage_privacy_readiness=passed\n'
docker stop --time 5 "$container" >/dev/null
test "$(curl -ksS -o /dev/null -w '%{http_code}' "$api_url/health/ready")" = 503
curl -kfsS "$api_url/health/live" >/dev/null
if ConnectionStrings__Minos="$MINOS_TEST_WORKER_CONNECTION" "$dotnet_command" "$worker_dll" --check > "$workspace/worker-check.log" 2>&1; then
    printf 'Worker unexpectedly started without PostgreSQL.\n' >&2
    exit 1
fi
grep -q 'cannot connect to PostgreSQL' "$workspace/worker-check.log"
docker start "$container" >/dev/null
wait_for_postgres
for attempt in {1..60}; do
    if curl -kfsS "$api_url/health/ready" >/dev/null 2>&1; then break; fi
    sleep 0.25
done
curl -kfsS "$api_url/health/ready" >/dev/null
curl -kfsS -b "$workspace/cookies" "$api_url/_development/problem-drafts/$draft_id" | \
    python3 -c 'import json,sys; assert json.load(sys.stdin)["title"] == "A + B"'
printf 'database_down_readiness_and_worker_startup=passed\nfoundation_checks=passed\n'
