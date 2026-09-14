#!/usr/bin/env bash
set -euo pipefail
readonly image='gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15'
readonly fixtures="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/fixtures" && pwd -P)"
workspace="$(mktemp -d /tmp/minos-m1-compile.XXXXXXXX)"
containers=()
cleanup() {
    local failed=0
    for container in "${containers[@]}"; do docker rm -f "$container" >/dev/null || failed=1; done
    if [[ "$failed" = 0 && "$workspace" = /tmp/minos-m1-compile.* ]]; then
        rm -f -- "$workspace/compile/main.cpp" "$workspace/run/main" "$workspace/run/stdin.txt" "$workspace/compiler.txt" "$workspace/stdout.txt"
        rmdir -- "$workspace/compile" "$workspace/run" "$workspace" || failed=1
    fi
    return "$failed"
}
finish() { local status=$?; trap - EXIT; cleanup || status=1; exit "$status"; }
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
test "$(stat -f -c %T "$workspace")" = ext2/ext3
mkdir -m 700 "$workspace/compile" "$workspace/run"
cp -- "$fixtures/sum.cpp" "$workspace/compile/main.cpp"
chmod 400 "$workspace/compile/main.cpp"
base=(--network none --user 1000:1000 --cap-drop ALL --security-opt no-new-privileges --read-only
      --memory 256m --memory-swap 256m --cpus 1 --pids-limit 32 --shm-size 1m
      --tmpfs /scratch:size=64m,mode=700,uid=1000,gid=1000,nosuid,nodev
      --log-driver none --ulimit nofile=64:64 --ulimit core=0 --ulimit fsize=8388608:8388608
      --label minos.spike=m1-compile)
compiler="$(docker create "${base[@]}" --mount "type=bind,src=$workspace/compile,dst=/input,readonly" "$image" sleep 120)"
containers+=("$compiler")
docker start "$compiler" >/dev/null
timeout --kill-after=2s 15s docker exec --env TMPDIR=/scratch "$compiler" g++ -std=c++20 -O2 -pipe /input/main.cpp -o /scratch/main
timeout --kill-after=2s 5s docker exec "$compiler" /bin/sh -c 'test -f /scratch/main && test ! -L /scratch/main && cat /scratch/main' > "$workspace/run/main"
test -f "$workspace/run/main" && test ! -L "$workspace/run/main"
chmod 500 "$workspace/run/main"
printf '19 23\n' > "$workspace/run/stdin.txt"
runner="$(docker create "${base[@]}" --mount "type=bind,src=$workspace/run,dst=/input,readonly" "$image" /bin/sh -c 'exec /input/main < /input/stdin.txt')"
containers+=("$runner")
timeout --kill-after=2s 8s docker start --attach "$runner" > "$workspace/stdout.txt"
test "$(docker inspect --format '{{.State.ExitCode}}' "$runner")" = 0
test "$(cat "$workspace/stdout.txt")" = 42
printf 'compile=passed run=passed trusted_output_check=passed\n'
# Reuse only the compiler input directory, after the successful compilation finished.
chmod 600 "$workspace/compile/main.cpp"
cp -- "$fixtures/compile-error.cpp" "$workspace/compile/main.cpp"
chmod 400 "$workspace/compile/main.cpp"
set +e
timeout --kill-after=2s 15s docker exec --env TMPDIR=/scratch "$compiler" g++ -std=c++20 -O2 -pipe /input/main.cpp -o /scratch/invalid > "$workspace/compiler.txt" 2>&1
compile_status=$?
set -e
test "$compile_status" = 1
test -s "$workspace/compiler.txt"
printf 'compile_failure_exit=%s diagnostics_bytes=%s\n' "$compile_status" "$(wc -c < "$workspace/compiler.txt")"
printf 'compile_run_probe=passed\n'
