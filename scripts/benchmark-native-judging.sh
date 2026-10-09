#!/usr/bin/env bash
# Known-fixture validation only. Requires an explicitly configured temporary native helper.
set -euo pipefail
umask 077
ulimit -c 0
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
image="${1:-minos-checks:local}"
native_root="${MINOS_TEST_ISOLATE_ROOT:-/opt/minos-isolate-phase2-20261008-8f185bb}"
runner="${MINOS_TEST_RUNNER:-isolate}"
[[ "$runner" = isolate || "$runner" = docker ]]
[[ $(id -u) = 1000 && $(id -g) = 1000 ]]
if [[ "$runner" = isolate ]]; then
    boxes="${MINOS_TEST_ISOLATE_BOXES:-1}"
    [[ "$boxes" =~ ^([1-9]|1[0-6])$ ]]
    if [[ "$boxes" = 1 ]]; then [[ -x "$native_root/isolate" ]]; else [[ -x "$native_root/helpers/0/isolate" ]]; fi
fi
if [[ -n $(docker ps -q --filter label=com.docker.compose.service=worker) ||
      -n $(docker ps -q --filter label=minos.judge=m6) ]]; then
    printf 'Stop all application Workers and finish sandbox checks first.\n' >&2
    exit 1
fi
docker image inspect "$image" >/dev/null
base=$(mktemp -d /tmp/minos-native-checks.XXXXXXXX)
printf 'minos-native-checks\n' > "$base/.owner"
exporter=minos-native-export-$(cat /proc/sys/kernel/random/uuid)
container=minos-native-postgres-$(cat /proc/sys/kernel/random/uuid)
volume="$container-data"
cleanup() {
    local status=$?
    trap - EXIT
    if [[ $(docker inspect --format '{{index .Config.Labels "minos.check"}}' "$exporter" 2>/dev/null || true) = native-export ]]; then
        docker rm -f "$exporter" >/dev/null || status=1
    fi
    if [[ $(docker inspect --format '{{index .Config.Labels "minos.check"}}' "$container" 2>/dev/null || true) = native-postgres ]]; then
        docker exec --user 0 "$container" chown "$(id -u):$(id -g)" /var/run/postgresql >/dev/null || status=1
        docker rm -f "$container" >/dev/null || status=1
    fi
    if [[ $(docker volume inspect --format '{{index .Labels "minos.check"}}' "$volume" 2>/dev/null || true) = native-postgres ]]; then
        docker volume rm "$volume" >/dev/null || status=1
    fi
    if [[ "$status" = 0 && "$base" = /tmp/minos-native-checks.* && $(realpath "$base") = "$base" &&
          ! -L "$base" && $(cat "$base/.owner") = minos-native-checks ]]; then
        rm -rf -- "$base"
    else printf 'Retained disposable checks workspace for inspection: %s\n' "$base" >&2; status=1; fi
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -m 700 "$base/runtime" "$base/tests" "$base/api" "$base/domain" "$base/private-artifacts"
mkdir -m 777 "$base/socket"
chmod 777 "$base/socket"
docker create --name "$exporter" --label minos.check=native-export --network none --entrypoint /bin/true "$image" >/dev/null
docker cp "$exporter:/usr/share/dotnet/dotnet" "$base/runtime/dotnet"
for directory in host shared sdk sdk-manifests packs templates; do docker cp "$exporter:/usr/share/dotnet/$directory" "$base/runtime/$directory"; done
docker cp "$exporter:/build/bin/Minos.IntegrationTests/release/." "$base/tests/"
docker cp "$exporter:/build/bin/Minos.Api/release/." "$base/api/"
docker cp "$exporter:/build/bin/Minos.Domain.Tests/release/." "$base/domain/"
docker rm "$exporter" >/dev/null
python3 - "$base/tests/MvcTestingAppManifest.json" "$repo" <<'PY'
import json, pathlib, sys
manifest = pathlib.Path(sys.argv[1]); contents = json.loads(manifest.read_text())
for name in contents:
    if 'Minos.Api' in name: contents[name] = sys.argv[2] + '/src/Minos.Api'
manifest.write_text(json.dumps(contents))
PY
export DOTNET_ROOT="$base/runtime" MINOS_DOTNET="$base/runtime/dotnet" PATH="$base/runtime:$PATH"
export MINOS_TEST_REPOSITORY="$repo" MINOS_TEST_SANDBOX=1 MINOS_TEST_BENCHMARK="${MINOS_TEST_BENCHMARK:-1}"
if [[ "$runner" = isolate ]]; then
    export MINOS_TEST_ISOLATE_ROOT="$native_root" MINOS_TEST_TRUSTED_ISOLATE_FIXTURES=1
else unset MINOS_TEST_ISOLATE_ROOT MINOS_TEST_TRUSTED_ISOLATE_FIXTURES; fi
export Minos__Artifacts__Root="$base/private-artifacts" Authentication__Google__Enabled=false
export Logging__LogLevel__Default=Warning Logging__LogLevel__Microsoft=Error
password=$(cat /proc/sys/kernel/random/uuid)
migrator_password=$(cat /proc/sys/kernel/random/uuid)
api_password=$(cat /proc/sys/kernel/random/uuid)
worker_password=$(cat /proc/sys/kernel/random/uuid)
docker volume create --label minos.check=native-postgres "$volume" >/dev/null
docker run -d --name "$container" --label minos.check=native-postgres --network none --memory 512m --pids-limit 64 \
    --mount "type=volume,src=$volume,dst=/var/lib/postgresql" --mount "type=bind,src=$base/socket,dst=/var/run/postgresql" \
    --env POSTGRES_DB=minos --env POSTGRES_USER=minos_bootstrap --env "POSTGRES_PASSWORD=$password" \
    --env 'POSTGRES_INITDB_ARGS=--auth-local=scram-sha-256 --auth-host=scram-sha-256' \
    postgres@sha256:1c59e2c3c818eaa0f0628f695b36e7c9e362d6b219b36a54a32df645cbd7e1af -c listen_addresses= >/dev/null
ready=false
for attempt in {1..60}; do
    if docker exec "$container" /bin/sh -c 'test "$(cat /proc/1/comm)" = postgres && pg_isready -U minos_bootstrap -d minos' >/dev/null 2>&1; then ready=true; break; fi
    sleep .5
done
[[ "$ready" = true ]]
docker exec -i --env "PGPASSWORD=$password" "$container" psql -X --set ON_ERROR_STOP=1 -U minos_bootstrap -d minos \
    --set database=minos --set "migrator_password=$migrator_password" --set "api_password=$api_password" --set "worker_password=$worker_password" \
    < "$repo/scripts/postgres-bootstrap.sql" > "$base/bootstrap.log" 2>&1
export ConnectionStrings__Minos="Host=$base/socket;Database=minos;Username=minos_api;Password=$api_password;Timeout=3"
export MINOS_TEST_MIGRATION_CONNECTION="Host=$base/socket;Database=minos;Username=minos_migrator;Password=$migrator_password;Timeout=3"
export MINOS_TEST_WORKER_CONNECTION="Host=$base/socket;Database=minos;Username=minos_worker;Password=$worker_password;Timeout=3"
export MINOS_TEST_BOOTSTRAP_CONNECTION="Host=$base/socket;Database=minos;Username=minos_bootstrap;Password=$password;Timeout=3"
# Keep ASP.NET's configuration watcher away from the Windows repository/artifacts tree.
(cd "$base/api"; ConnectionStrings__Minos="$MINOS_TEST_MIGRATION_CONNECTION" "$DOTNET_ROOT/dotnet" "$base/api/Minos.Api.dll" --migrate) > "$base/migrate.log" 2>&1
docker exec -i --env "PGPASSWORD=$migrator_password" "$container" psql -X --set ON_ERROR_STOP=1 -U minos_migrator -d minos \
    < "$repo/scripts/postgres-runtime-grants.sql" > "$base/grants.log" 2>&1
"$DOTNET_ROOT/dotnet" "$base/domain/Minos.Domain.Tests.dll" -parallel none -nocolor
classes=()
if [[ -n "${MINOS_TEST_CLASS:-}" ]]; then
    [[ "$MINOS_TEST_CLASS" =~ ^[A-Za-z][A-Za-z0-9]*$ ]]
    classes+=(-class "Minos.IntegrationTests.$MINOS_TEST_CLASS")
elif [[ "${MINOS_TEST_ALL:-0}" != 1 ]]; then
    for name in DockerExecutionTests IsolateExecutionTests OfficialJudgingIntegrationTests ExecutionBudgetTests WorkerSchedulingTests JudgingConcurrencyIntegrationTests; do
        classes+=(-class "Minos.IntegrationTests.$name")
    done
fi
(cd "$base/tests"; "$DOTNET_ROOT/dotnet" "$base/tests/Minos.IntegrationTests.dll" -parallel none -nocolor -showliveoutput "${classes[@]}")
