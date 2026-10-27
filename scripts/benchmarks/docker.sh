#!/usr/bin/env bash
set -euo pipefail
umask 077

# Use a locally built, trusted checks image; no database or application deployment.
image="${1:-minos-checks:local}"
if [[ -n "$(docker ps --filter label=com.docker.compose.service=worker --format '{{.Names}}')" ||
      -n "$(docker ps --filter label=minos.judge=m6 --format '{{.Names}}')" ]]; then
    printf '%s\n' 'Stop the application Worker and finish active sandbox checks before benchmarking.' >&2
    exit 1
fi
docker image inspect "$image" >/dev/null
volume="minos-judging-benchmark-$(cat /proc/sys/kernel/random/uuid)"
runner="$volume-runner"
cleanup() {
    local status=$?
    trap - EXIT
    # The generated name and label identify this script's disposable resources.
    if [[ "$(docker inspect --format '{{index .Config.Labels "minos.check"}}' "$runner" 2>/dev/null || true)" = judging-benchmark ]]; then
        docker rm -f "$runner" >/dev/null || status=1
    fi
    # Interrupted execution can leave an exited sandbox holding our volume.
    while IFS= read -r sandbox; do
        [[ -z "$sandbox" ]] && continue
        if [[ "$(docker inspect --format '{{index .Config.Labels "minos.judge"}}' "$sandbox")" != m6 ]]; then
            printf '%s\n' 'Unexpected container attached to benchmark volume; leaving resources for inspection.' >&2
            status=1
            continue
        fi
        docker rm -f "$sandbox" >/dev/null || status=1
    done < <(docker ps -aq --filter "volume=$volume")
    if [[ "$(docker volume inspect --format '{{index .Labels "minos.check"}}' "$volume" 2>/dev/null || true)" = judging-benchmark ]]; then
        docker volume rm "$volume" >/dev/null || status=1
    fi
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
docker volume create --label minos.check=judging-benchmark "$volume" >/dev/null
docker run --rm --network none --user 0 --entrypoint /bin/sh \
    --mount "type=volume,src=$volume,dst=/workspace" "$image" \
    -c 'chown 1654:1654 /workspace; chmod 700 /workspace'
docker run --rm --network none --name "$runner" --label minos.check=judging-benchmark \
    --group-add "$(stat -c %g /var/run/docker.sock)" \
    -e MINOS_TEST_SANDBOX=1 -e MINOS_TEST_BENCHMARK=1 -e "MINOS_TEST_WORKSPACE_VOLUME=$volume" \
    --mount "type=volume,src=$volume,dst=/tmp/minos-judge-execution" \
    --mount type=bind,src=/var/run/docker.sock,dst=/var/run/docker.sock \
    --entrypoint dotnet "$image" test tests/Minos.IntegrationTests --no-build -c Release \
    --artifacts-path /build --filter FullyQualifiedName~Benchmark_compile_and_100_serial_tests \
    --logger 'console;verbosity=detailed'
