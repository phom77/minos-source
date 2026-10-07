#!/usr/bin/env bash
# Sourced only inside check-foundation.sh's disposable fixture; never an existing database.
[[ -n "${workspace:-}" && "$workspace" = /tmp/minos-foundation.* && -n "${draft_id:-}" ]] || {
    printf 'Run bash scripts/check-foundation.sh for M7 acceptance.\n' >&2; exit 1;
}
m7_start_worker() {
    ConnectionStrings__Minos="$MINOS_TEST_WORKER_CONNECTION" Minos__Artifacts__Root="$workspace/private-artifacts" \
        "$dotnet_command" "$worker_dll" >> "$workspace/m6-worker.log" 2>&1 &
    worker_pid=$!
}
m7_stop_worker() {
    kill -TERM "$worker_pid"
    wait "$worker_pid"
    worker_pid=''
    if sh "$repository/docker/worker-healthcheck.sh" > "$workspace/stopped-healthcheck.log" 2>&1; then
        printf 'Stopped Worker passed its health probe.\n' >&2; return 1
    fi
}
m7_submit() {
    local name="$1" payload="$2" cookies="${3:-solver-cookies}" request_csrf="${4:-$solver_csrf}"
    curl -kfsS -b "$workspace/$cookies" -H "X-CSRF-TOKEN: $request_csrf" -H 'Content-Type: application/json' \
        --data-binary "@$workspace/$payload-request.json" "$api_url/api/problems/$draft_id/submissions" > "$workspace/$name-created.json"
    python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$workspace/$name-created.json"
}
m7_poll() {
    local id="$1" output="$2" cookies="${3:-solver-cookies}" state=''
    for attempt in {1..480}; do
        kill -0 "$worker_pid"
        curl -kfsS -b "$workspace/$cookies" "$api_url/api/submissions/$id" > "$workspace/$output-result.json"
        state="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["status"])' "$workspace/$output-result.json")"
        if [[ "$state" = Completed || "$state" = SystemFailed ]]; then return; fi
        sleep 0.25
    done
    return 1
}
m7_assert_accepted() {
    python3 - "$workspace/$1-result.json" <<'PY'
import json, sys
r = json.load(open(sys.argv[1]))
assert r['status'] == 'Completed' and r['result']['verdict'] == 'Accepted', (r['status'], r['attempts'])
assert all(t['status'] == 'Passed' for t in r['result']['tests'])
PY
}
m7_wait_runtime() {
    for attempt in {1..240}; do
        kill -0 "$worker_pid"
        local sandbox
        sandbox="$(docker ps --filter label=minos.judge=m6 --format '{{.Names}}' | head -n 1)"
        if [[ -n "$sandbox" ]] && docker top "$sandbox" -eo pid,args > "$workspace/process-observation.txt" 2>/dev/null &&
            grep -E '(^|[[:space:]])/input/main([[:space:]]|$)' "$workspace/process-observation.txt" >/dev/null; then return; fi
        sleep 0.25
    done
    if [[ -f "$workspace/process-observation.txt" ]]; then cat "$workspace/process-observation.txt" >&2; fi
    return 1
}
# Queue eight real official jobs while the worker is stopped; one owner hits its bound.
burst_ids=()
for index in {1..5}; do burst_ids+=("$(m7_submit "burst-$index" accepted)"); done
status="$(curl -ksS -D "$workspace/overload-headers" -o "$workspace/overload.json" -w '%{http_code}' \
    -b "$workspace/solver-cookies" -H "X-CSRF-TOKEN: $solver_csrf" -H 'Content-Type: application/json' \
    --data-binary "@$workspace/accepted-request.json" "$api_url/api/problems/$draft_id/submissions")"
test "$status" = 429
grep -qi '^retry-after: 5' "$workspace/overload-headers"
for index in {6..8}; do burst_ids+=("$(m7_submit "burst-$index" accepted cookies "$csrf")"); done
curl -kfsS -b "$workspace/cookies" "$api_url/api/operations/judging" > "$workspace/m7-operations.json"
python3 - "$workspace/m7-operations.json" <<'PY'
import json, sys
r = json.load(open(sys.argv[1])); assert r['pending'] == 8 and r['leased'] == 0 and r['maximumActive'] == 32, r
PY
test "$(curl -ksS -o /dev/null -w '%{http_code}' -b "$workspace/solver-cookies" "$api_url/api/operations/judging")" = 403
m7_start_worker
for index in {1..8}; do
    cookies=solver-cookies
    if ((index > 5)); then cookies=cookies; fi
    m7_poll "${burst_ids[$((index-1))]}" "burst-$index" "$cookies"
    m7_assert_accepted "burst-$index"
done
python3 - "$workspace" "$repository/artifacts/m7-workload.json" <<'PY'
import datetime, json, pathlib, re, sys
root = pathlib.Path(sys.argv[1]); waits=[]; elapsed=[]; cpu=[]; memory=[]
for index in range(1,9):
    r=json.load(open(root/f'burst-{index}-result.json')); assert len(r['attempts']) == 1
    t=lambda key:datetime.datetime.fromisoformat(re.sub(r'\.(\d+)([+-]\d\d:\d\d|Z)$', lambda m:'.'+(m[1]+'000000')[:6]+m[2], key))
    waits.append((t(r['attempts'][0]['startedAtUtc'])-t(r['createdAtUtc'])).total_seconds())
    elapsed.append((t(r['completedAtUtc'])-t(r['createdAtUtc'])).total_seconds())
    cpu.append(r['result']['maximumCpuTimeMilliseconds']);memory.append(r['result']['maximumMemoryBytes'])
assert max(waits)<120, waits
evidence={'submissions':8,'owners':2,'maximumQueueWaitSeconds':max(waits),'maximumEndToEndSeconds':max(elapsed),
          'maximumTestCpuMilliseconds':max(cpu),'maximumTestMemoryBytes':max(memory),'queueWaitTargetSeconds':120}
pathlib.Path(sys.argv[2]).write_text(json.dumps(evidence,indent=2)+'\n')
print('m7_workload_and_overload=passed '+json.dumps(evidence))
PY
# A live health probe must not acquire or release the running host's lock.
sh "$repository/docker/worker-healthcheck.sh"
if ConnectionStrings__Minos="$MINOS_TEST_WORKER_CONNECTION" "$dotnet_command" "$worker_dll" --check \
    > "$workspace/duplicate-worker-check.log" 2>&1; then
    printf 'A second Worker bypassed the exclusive host lock.\n' >&2; exit 1
fi
sh "$repository/docker/worker-healthcheck.sh"
printf 'm7_live_healthcheck_preserves_exclusive_worker=passed\n'
# A hard kill occurs with a live contestant process and private workspace.
crash_id="$(m7_submit crash renewal)"
m7_wait_runtime
kill -KILL "$worker_pid"
wait "$worker_pid" 2>/dev/null || true
worker_pid=''
test -n "$(docker ps -aq --filter label=minos.judge=m6)"
test -n "$(find /tmp/minos-judge-execution -mindepth 1 -maxdepth 1 -type d -name 'job-*' -print -quit)"
m7_start_worker
m7_poll "$crash_id" crash
m7_assert_accepted crash
python3 - "$workspace/crash-result.json" <<'PY'
import json, sys
r=json.load(open(sys.argv[1])); assert len(r['attempts'])==2 and r['attempts'][0]['status']=='LeaseExpired'
assert r['result']['attemptId']==r['attempts'][1]['id']
PY
test -z "$(docker ps -aq --filter label=minos.judge=m6)"
test -z "$(find /tmp/minos-judge-execution -mindepth 1 -maxdepth 1 -type d -name 'job-*' -print -quit)"
printf 'm7_worker_sigkill_and_orphan_recovery=passed\n'
# Losing the database during runtime cancels processing at renewal and recovers the lease.
outage_id="$(m7_submit outage renewal)"
m7_wait_runtime
docker stop --time 1 "$container" >/dev/null
status="$(curl -ksS -o "$workspace/outage-admission.json" -w '%{http_code}' -b "$workspace/solver-cookies" \
    -H "X-CSRF-TOKEN: $solver_csrf" -H 'Content-Type: application/json' \
    --data-binary "@$workspace/accepted-request.json" "$api_url/api/problems/$draft_id/submissions")"
test "$status" = 500
for attempt in {1..120}; do
    if [[ -z "$(docker ps -aq --filter label=minos.judge=m6)" ]]; then break; fi
    sleep 0.25
done
test -z "$(docker ps -aq --filter label=minos.judge=m6)"
docker start "$container" >/dev/null
wait_for_postgres
m7_poll "$outage_id" outage
m7_assert_accepted outage
python3 - "$workspace/outage-result.json" <<'PY'
import json, sys
r=json.load(open(sys.argv[1])); assert len(r['attempts'])>=2 and r['attempts'][0]['status']=='LeaseExpired'
PY
printf 'm7_database_interruption_during_execution=passed\n'
# Missing published input is a platform failure with bounded attempts, never a verdict.
input_key="$(docker exec --env "PGPASSWORD=$migrator_password" "$container" psql -X -At -U minos_migrator -d minos \
    -c "SELECT input_artifact_key FROM problems.published_problem_test_cases WHERE problem_version_id=(SELECT id FROM problems.published_problem_versions WHERE problem_id='$draft_id' ORDER BY version_number DESC LIMIT 1) ORDER BY \"order\" LIMIT 1")"
[[ "$input_key" =~ ^tests/[0-9a-f]{32}/input.bin$ ]]
input_path="$workspace/private-artifacts/problem-artifacts/$input_key"
mv -- "$input_path" "$input_path.m7-hidden"
missing_id="$(m7_submit missing accepted)"
m7_poll "$missing_id" missing
mv -- "$input_path.m7-hidden" "$input_path"
python3 - "$workspace/missing-result.json" <<'PY'
import json,sys
r=json.load(open(sys.argv[1]));assert r['status']=='SystemFailed' and r['result'] is None and len(r['attempts'])==3
PY
printf 'm7_storage_fault_bounded_retry=passed\n'
# Quiesce writers and back up both stores, including protected-cookie keys.
m7_stop_worker
kill -TERM "$api_pid"; wait "$api_pid"; api_pid=''
mkdir -m 700 "$workspace/backups" "$workspace/restored-artifacts"
backup="$(MINOS_BACKUP_QUIESCED=1 MINOS_POSTGRES_CONTAINER="$container" MINOS_MIGRATOR_PASSWORD="$migrator_password" \
    MINOS_ARTIFACT_ROOT="$workspace/private-artifacts" MINOS_BACKUP_ROOT="$workspace/backups" bash "$repository/scripts/backup-development.sh")"
docker exec --env "PGPASSWORD=$password" "$container" psql -X -v ON_ERROR_STOP=1 -U minos_bootstrap -d postgres \
    -c 'CREATE DATABASE minos_restored' >/dev/null
docker exec -i --env "PGPASSWORD=$password" "$container" psql -X -v ON_ERROR_STOP=1 -U minos_bootstrap -d minos_restored \
    >/dev/null <<'SQL'
REVOKE ALL ON DATABASE minos_restored FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE minos_restored TO minos_migrator;
GRANT CONNECT ON DATABASE minos_restored TO minos_api, minos_worker;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO minos_migrator;
GRANT USAGE ON SCHEMA public TO minos_api, minos_worker;
SQL
mkdir -m 700 "$workspace/corrupt-backup" "$workspace/unsafe-backup"
cp -- "$backup/database.dump" "$backup/artifacts.tar" "$backup/manifest.sha256" "$workspace/corrupt-backup/"
printf 'corruption' >> "$workspace/corrupt-backup/artifacts.tar"
if MINOS_POSTGRES_CONTAINER="$container" MINOS_MIGRATOR_PASSWORD="$migrator_password" MINOS_RESTORE_DATABASE=minos_restored \
    MINOS_RESTORE_ARTIFACT_ROOT="$workspace/restored-artifacts" bash "$repository/scripts/restore-development.sh" "$workspace/corrupt-backup" > "$workspace/corrupt-restore.log" 2>&1; then
    printf 'Corrupt backup was accepted.\n' >&2; exit 1
fi
cp -- "$backup/database.dump" "$workspace/unsafe-backup/"
python3 - "$workspace/unsafe-backup/artifacts.tar" <<'PY'
import sys, tarfile
with tarfile.open(sys.argv[1], 'w') as archive:
    link=tarfile.TarInfo('escape');link.type=tarfile.SYMTYPE;link.linkname='/tmp';archive.addfile(link)
PY
(cd "$workspace/unsafe-backup" && sha256sum database.dump artifacts.tar > manifest.sha256)
if MINOS_POSTGRES_CONTAINER="$container" MINOS_MIGRATOR_PASSWORD="$migrator_password" MINOS_RESTORE_DATABASE=minos_restored \
    MINOS_RESTORE_ARTIFACT_ROOT="$workspace/restored-artifacts" bash "$repository/scripts/restore-development.sh" "$workspace/unsafe-backup" > "$workspace/unsafe-restore.log" 2>&1; then
    printf 'Unsafe archive was accepted.\n' >&2; exit 1
fi
test -z "$(find "$workspace/restored-artifacts" -mindepth 1 -print -quit)"
MINOS_POSTGRES_CONTAINER="$container" MINOS_MIGRATOR_PASSWORD="$migrator_password" MINOS_RESTORE_DATABASE=minos_restored \
    MINOS_RESTORE_ARTIFACT_ROOT="$workspace/restored-artifacts" bash "$repository/scripts/restore-development.sh" "$backup"
docker exec --env "PGPASSWORD=$migrator_password" "$container" psql -X -v ON_ERROR_STOP=1 -U minos_migrator -d minos_restored \
    -c 'GRANT USAGE ON SCHEMA problems TO minos_api, minos_worker' >/dev/null
docker exec -i --env "PGPASSWORD=$migrator_password" "$container" psql -X -v ON_ERROR_STOP=1 -U minos_migrator -d minos_restored \
    < "$repository/scripts/postgres-runtime-grants.sql" >/dev/null
export ConnectionStrings__Minos="${ConnectionStrings__Minos/Database=minos;/Database=minos_restored;}"
export MINOS_TEST_WORKER_CONNECTION="${MINOS_TEST_WORKER_CONNECTION/Database=minos;/Database=minos_restored;}"
mv -- "$workspace/private-artifacts" "$workspace/original-artifacts"
mv -- "$workspace/restored-artifacts" "$workspace/private-artifacts"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$api_url" \
    ASPNETCORE_Kestrel__Certificates__Default__Path="$workspace/tls.pfx" "$dotnet_command" "$api_dll" > "$workspace/restored-api.log" 2>&1 &
api_pid=$!
for attempt in {1..240}; do
    kill -0 "$api_pid"
    if curl -kfsS "$api_url/health/ready" >/dev/null 2>&1; then break; fi
    sleep 0.25
done
curl -kfsS -b "$workspace/solver-cookies" "$api_url/api/submissions/$crash_id" > "$workspace/restored-history.json"
python3 - "$workspace/crash-result.json" "$workspace/restored-history.json" <<'PY'
import json,sys
assert json.load(open(sys.argv[1])) == json.load(open(sys.argv[2]))
PY
m7_start_worker
restored_id="$(m7_submit restored accepted)"
m7_poll "$restored_id" restored
m7_assert_accepted restored
m7_stop_worker
printf 'm7_backup_restore_history_keys_and_real_judging=passed\nm7_checks=passed\n'
