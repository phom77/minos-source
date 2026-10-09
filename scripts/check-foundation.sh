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
finish() {
    local status=$?
    trap - EXIT
    if [[ "$status" != 0 && -f "$workspace/m6-worker.log" ]]; then
        printf '%s\n' '--- M6 Worker diagnostics ---' >&2
        python3 - "$workspace/m6-worker.log" <<'PY' >&2
import json, sys
events = []
for line in open(sys.argv[1]):
    try: event = json.loads(line)
    except ValueError: continue
    if event.get('Category', '').startswith('Minos.'):
        events.append(event.get('Message', ''))
for event in events[-30:]: print(event)
PY
    fi
    cleanup || status=1
    exit "$status"
}
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
export MINOS_TEST_SANDBOX="${MINOS_TEST_SANDBOX:-1}"
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
# Prove the Worker remains hosted until cancellation, then shuts down cleanly.
ConnectionStrings__Minos="$MINOS_TEST_WORKER_CONNECTION" "$dotnet_command" "$worker_dll" > "$workspace/worker-host.log" 2>&1 &
worker_pid=$!
for attempt in {1..240}; do
    kill -0 "$worker_pid"
    if grep -q 'Application started' "$workspace/worker-host.log"; then break; fi
    sleep 0.25
done
grep -q 'Application started' "$workspace/worker-host.log"
kill -0 "$worker_pid"
kill -TERM "$worker_pid"
wait "$worker_pid"
printf 'worker_host_start_and_cancellation=passed\n'
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$workspace/tls.key" -out "$workspace/tls.crt" \
    -days 1 -subj /CN=localhost > "$workspace/tls.log" 2>&1
openssl pkcs12 -export -out "$workspace/tls.pfx" -inkey "$workspace/tls.key" -in "$workspace/tls.crt" \
    -passout pass: > "$workspace/tls-export.log" 2>&1
(
umask 022
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=https://127.0.0.1:0 Minos__EnableDevelopmentEndpoints=true \
    Minos__Artifacts__Root="$workspace/private-artifacts" \
    ASPNETCORE_Kestrel__Certificates__Default__Path="$workspace/tls.pfx" \
    exec "$dotnet_command" "$api_dll" > "$workspace/api.log" 2>&1
) &
api_pid=$!
api_url=''
for attempt in {1..240}; do
    kill -0 "$api_pid"
    api_url="$(python3 -c 'import re,sys; m=re.search(r"Now listening on: (https://127\.0\.0\.1:[0-9]+)",open(sys.argv[1]).read()); print(m[1] if m else "")' "$workspace/api.log")"
    if [[ -n "$api_url" ]] && curl -kfsS "$api_url/health/ready" >/dev/null 2>&1; then break; fi
    sleep 0.25
done
curl -kfsS "$api_url/health/ready" >/dev/null
csrf="$(curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" "$api_url/accounts/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])')"
curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"email":"developer@example.test","password":"correct horse battery staple","displayName":"Developer"}' "$api_url/accounts/register" > "$workspace/account.json"
account_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$workspace/account.json")"
ConnectionStrings__Minos="$MINOS_TEST_MIGRATION_CONNECTION" "$dotnet_command" "$api_dll" \
    --provision-admin "$account_id" > "$workspace/provision-admin.log" 2>&1
csrf="$(curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" "$api_url/accounts/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])')"
curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"email":"developer@example.test","password":"correct horse battery staple"}' "$api_url/accounts/login" > "$workspace/login.json"
csrf="$(curl -kfsS -c "$workspace/cookies" -b "$workspace/cookies" "$api_url/accounts/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])')"
curl -kfsS -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' -d '{"title":"A + B","statement":"Print the sum."}' \
    "$api_url/api/authoring/problems/" > "$workspace/created.json"
draft_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$workspace/created.json")"
curl -kfsS -X PUT -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"title":"A + B","statement":"Print the sum.","timeLimitMilliseconds":1000,"memoryLimitMegabytes":64,"checker":"Exact","samples":[{"input":"1 2\n","output":"3\n"}]}' \
    "$api_url/api/authoring/problems/$draft_id/draft" > "$workspace/configured.json"
python3 - "$workspace" <<'PY'
import pathlib, sys, zipfile
for order, (input_text, output_text) in enumerate((('2 3\n','5\n'), ('19 23\n','42\n'), ('-7 10\n','3\n')), 1):
    with zipfile.ZipFile(pathlib.Path(sys.argv[1]) / f'testcase-{order}.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('input.txt', input_text)
        archive.writestr('output.txt', output_text)
PY
for order in 1 2 3; do
    curl -kfsS -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/zip' \
        --data-binary "@$workspace/testcase-$order.zip" "$api_url/api/authoring/problems/$draft_id/draft/tests" > "$workspace/uploaded.json"
done
curl -kfsS -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"statementReviewed":true,"samplesVerified":true,"referenceSolutionChecked":true}' \
    "$api_url/api/authoring/problems/$draft_id/publish" > "$workspace/published.json"
version_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["versionId"])' "$workspace/published.json")"
curl -kfsS "$api_url/api/problems/versions/$version_id" | python3 -c \
    'import json,sys; r=json.load(sys.stdin); assert r["title"] == "A + B" and r["privateTestCount"] == 3 and "artifact" not in str(r).lower()'
test "$(curl -ksS -o /dev/null -w '%{http_code}' "$api_url/private-artifacts/any-file")" = 404
printf 'real_http_problem_publication_and_private_storage=passed\n'
# Exercise every official contestant verdict through the real API, durable queue, Worker,
# pinned Docker toolchain, private artifacts, exact checker and committed result projection.
python3 - "$workspace" <<'PY'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
sources = {
    'accepted': '#include <iostream>\nint main(){long long a,b;if(std::cin>>a>>b)std::cout<<a+b<<"\\n";}\n',
    'wrong': '#include <iostream>\nint main(){long long a,b;std::cin>>a>>b;std::cout<<(a==19?5:a+b)<<"\\n";}\n',
    'compile': 'int main(){ this is not valid C++; }\n',
    'runtime': 'int main(){return 7;}\n',
    'time': 'int main(){volatile unsigned long long n=0;for(;;)++n;}\n',
    'memory': '#include <cstddef>\nint main(){volatile char* p=new char[128*1024*1024];for(std::size_t i=0;i<128*1024*1024;i+=4096)p[i]=1;}\n',
    'output': '#include <iostream>\nint main(){for(;;)std::cout<<"xxxxxxxxxxxxxxxx";}\n',
    'renewal': '#include <iostream>\n#include <unistd.h>\nint main(){long long a,b;std::cin>>a>>b;sleep(11);std::cout<<a+b<<"\\n";}\n',
}
for name, source in sources.items():
    (root / f'{name}-request.json').write_text(json.dumps({'language':'cpp20','source':source}))
PY
ConnectionStrings__Minos="$MINOS_TEST_WORKER_CONNECTION" Minos__Artifacts__Root="$workspace/private-artifacts" \
    "$dotnet_command" "$worker_dll" > "$workspace/m6-worker.log" 2>&1 &
worker_pid=$!
for attempt in {1..240}; do
    kill -0 "$worker_pid"
    if grep -q 'Application started' "$workspace/m6-worker.log"; then break; fi
    sleep 0.25
done
grep -q 'Application started' "$workspace/m6-worker.log"
# Demonstrate the normal solver flow, independently of the trusted author account.
solver_csrf="$(curl -kfsS -c "$workspace/solver-cookies" -b "$workspace/solver-cookies" "$api_url/accounts/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])')"
curl -kfsS -c "$workspace/solver-cookies" -b "$workspace/solver-cookies" -H "X-CSRF-TOKEN: $solver_csrf" -H 'Content-Type: application/json' \
    -d '{"email":"solver@example.test","password":"correct horse battery staple","displayName":"Solver"}' "$api_url/accounts/register" > "$workspace/solver.json"
solver_csrf="$(curl -kfsS -c "$workspace/solver-cookies" -b "$workspace/solver-cookies" "$api_url/accounts/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])')"
read -r -a cases <<< "${MINOS_M6_CASES:-accepted wrong compile runtime time memory output}"
declare -A verdicts=(
    [accepted]=Accepted [wrong]=WrongAnswer [compile]=CompilationError [runtime]=RuntimeError
    [time]=TimeLimitExceeded [memory]=MemoryLimitExceeded [output]=OutputLimitExceeded)
declare -A statuses=(
    [accepted]=Passed [wrong]=WrongAnswer [compile]=NotExecuted [runtime]=RuntimeError
    [time]=TimeLimitExceeded [memory]=MemoryLimitExceeded [output]=OutputLimitExceeded)
for case_name in "${cases[@]}"; do
    curl -kfsS -b "$workspace/solver-cookies" -H "X-CSRF-TOKEN: $solver_csrf" -H 'Content-Type: application/json' \
        --data-binary "@$workspace/$case_name-request.json" "$api_url/api/problems/$draft_id/submissions" \
        > "$workspace/$case_name-created.json"
    submission_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$workspace/$case_name-created.json")"
    for attempt in {1..240}; do
        kill -0 "$worker_pid"
        curl -kfsS -b "$workspace/solver-cookies" "$api_url/api/submissions/$submission_id" > "$workspace/$case_name-result.json"
        state="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["status"])' "$workspace/$case_name-result.json")"
        if [[ "$state" = Completed || "$state" = SystemFailed ]]; then break; fi
        sleep 0.25
    done
    python3 - "$workspace/$case_name-result.json" "${verdicts[$case_name]}" "${statuses[$case_name]}" "$version_id" <<'PY'
import json, sys
result = json.load(open(sys.argv[1]))
expected_verdict, expected_status, version_id = sys.argv[2:]
assert result['status'] == 'Completed', result
assert result['problemVersionId'] == version_id
assert result['result']['verdict'] == expected_verdict
assert result['configuration'] == {'timeLimitMilliseconds':1000, 'memoryLimitMegabytes':64, 'checker':'Exact', 'testCount':3}
statuses = [test['status'] for test in result['result']['tests']]
if expected_verdict == 'Accepted': assert statuses == ['Passed'] * 3
elif expected_verdict == 'WrongAnswer': assert statuses == ['Passed','WrongAnswer','Passed']
elif expected_verdict == 'CompilationError': assert statuses == ['NotExecuted'] * 3
else: assert statuses == [expected_status] * 3
executed = sum(status != 'NotExecuted' for status in statuses)
passed = statuses.count('Passed')
assert result['result']['testSummary'] == {'totalTests':3, 'executedTests':executed, 'passedTests':passed, 'failedTests':executed-passed}
assert result['result']['attemptId'] == result['attempts'][-1]['id']
serialized = json.dumps(result['result']).lower()
assert all(secret not in serialized for secret in ('stdout', 'stderr', 'expectedoutput', 'inputartifact', 'outputartifact'))
if expected_verdict == 'CompilationError':
    assert result['result']['compilerDiagnostics']
    assert result['result']['maximumCpuTimeMilliseconds'] is None
else:
    assert result['result']['compilerDiagnostics'] == ''
    assert result['result']['maximumCpuTimeMilliseconds'] is not None
    assert result['result']['maximumMemoryBytes'] is not None
PY
done
printf 'real_api_official_verdict_matrix=passed cases=%s\n' "${cases[*]}"
# A real job outlasts the original 30-second lease. Three 11-second runs must finish
# in one attempt while independent scopes renew the lease during compilation/execution.
curl -kfsS -X PUT -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"title":"A + B","statement":"Print the sum.","timeLimitMilliseconds":10000,"memoryLimitMegabytes":64,"checker":"Exact","samples":[{"input":"1 2\n","output":"3\n"}]}' \
    "$api_url/api/authoring/problems/$draft_id/draft" > "$workspace/renewal-configured.json"
curl -kfsS -b "$workspace/cookies" -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"statementReviewed":true,"samplesVerified":true,"referenceSolutionChecked":true}' \
    "$api_url/api/authoring/problems/$draft_id/publish" > "$workspace/renewal-published.json"
curl -kfsS -b "$workspace/solver-cookies" -H "X-CSRF-TOKEN: $solver_csrf" -H 'Content-Type: application/json' \
    --data-binary "@$workspace/renewal-request.json" "$api_url/api/problems/$draft_id/submissions" > "$workspace/renewal-created.json"
submission_id="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$workspace/renewal-created.json")"
for attempt in {1..480}; do
    kill -0 "$worker_pid"
    curl -kfsS -b "$workspace/solver-cookies" "$api_url/api/submissions/$submission_id" > "$workspace/renewal-result.json"
    state="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["status"])' "$workspace/renewal-result.json")"
    if [[ "$state" = Completed || "$state" = SystemFailed ]]; then break; fi
    sleep 0.25
done
python3 - "$workspace/renewal-result.json" "$workspace/renewal-published.json" <<'PY'
import datetime, json, re, sys
r = json.load(open(sys.argv[1]))
assert r['status'] == 'Completed' and r['result']['verdict'] == 'Accepted'
assert len(r['attempts']) == 1 and all(t['status'] == 'Passed' for t in r['result']['tests']), r['attempts']
assert r['problemVersionId'] == json.load(open(sys.argv[2]))['versionId']
attempt = r['attempts'][0]
timestamp = lambda value: datetime.datetime.fromisoformat(re.sub(r'\.(\d+)([+-]\d\d:\d\d|Z)$', lambda m:'.'+(m[1]+'000000')[:6]+m[2], value))
elapsed = timestamp(attempt['finishedAtUtc']) - timestamp(attempt['startedAtUtc'])
assert elapsed.total_seconds() > 30
PY
renewed="$(docker exec -i --env "PGPASSWORD=$worker_password" "$container" psql -X -At --set ON_ERROR_STOP=1 -U minos_worker -d minos \
    --set "submission_id=$submission_id" <<'SQL'
SELECT lease_expires_at_utc > started_at_utc + interval '30 seconds'
FROM evaluation.judging_attempts WHERE submission_id = :'submission_id'::uuid;
SQL
)"
test "$renewed" = t
printf 'real_worker_long_running_lease_renewal=passed\n'
kill -TERM "$worker_pid"
wait "$worker_pid"
worker_pid=''
source "$repository/scripts/check-m7.sh"
# Storage must affect readiness, without affecting liveness.
chmod 755 "$workspace/private-artifacts"
test "$(curl -ksS -o /dev/null -w '%{http_code}' "$api_url/health/ready")" = 503
curl -kfsS "$api_url/health/live" >/dev/null
chmod 700 "$workspace/private-artifacts"
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
curl -kfsS "$api_url/api/problems/versions/$version_id" | \
    python3 -c 'import json,sys; assert json.load(sys.stdin)["title"] == "A + B"'
printf 'database_down_readiness_and_worker_startup=passed\nfoundation_checks=passed\n'
