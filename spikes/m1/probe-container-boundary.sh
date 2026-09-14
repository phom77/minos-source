#!/usr/bin/env bash
# Trusted, fixed-output setup probe. This is not a general submission runner.
set -euo pipefail

readonly probe_image='alpine@sha256:14358309a308569c32bdc37e2e0e9694be33a9d99e68afb0f5ff33cc1f695dce'
readonly script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly marker_source="$script_directory/fixtures/workspace-marker.txt"

command -v docker >/dev/null
command -v timeout >/dev/null
docker image inspect "$probe_image" >/dev/null
[[ -f "$marker_source" && ! -L "$marker_source" ]]

probe_workspace="$(mktemp -d /tmp/minos-m1-boundary.XXXXXXXX)"
probe_container=''

cleanup() {
    local cleanup_failed=0
    if [[ -n "$probe_container" ]]; then
        if ! docker rm --force "$probe_container" >/dev/null; then
            printf 'Container cleanup failed: %s\n' "$probe_container" >&2
            cleanup_failed=1
        fi
    fi
    # Remove only the known fixture and its own empty directory; never recurse.
    case "$probe_workspace" in
        /tmp/minos-m1-boundary.*)
            if [[ "$cleanup_failed" -eq 0 ]]; then
                if ! rm --force -- "$probe_workspace/workspace-marker.txt"; then
                    cleanup_failed=1
                fi
                if ! rmdir -- "$probe_workspace"; then
                    cleanup_failed=1
                fi
            fi
            ;;
        *) printf 'Refusing unexpected workspace cleanup path.\n' >&2; return 1 ;;
    esac
    return "$cleanup_failed"
}

finish() {
    local probe_status=$?
    trap - EXIT
    if ! cleanup; then
        probe_status=1
    fi
    exit "$probe_status"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

workspace_filesystem="$(stat -f -c %T "$probe_workspace")"
case "$workspace_filesystem" in
    ext2/ext3|tmpfs|xfs|btrfs|overlayfs) ;;
    *) printf 'Refusing non-Linux/unknown workspace filesystem: %s\n' "$workspace_filesystem" >&2; exit 1 ;;
esac
cp -- "$marker_source" "$probe_workspace/workspace-marker.txt"
chmod 700 "$probe_workspace"
chmod 400 "$probe_workspace/workspace-marker.txt"

probe_command="$(cat <<'CONTAINER_PROBE'
set -eu
test "$(id -u)" = 1000
test "$(cat /input/workspace-marker.txt)" = minos-m1-private-workspace
for forbidden in /mnt/d /host_mnt/d /mnt/host/d /var/run/docker.sock; do
    test ! -e "$forbidden"
done
for device in /sys/class/net/*; do
    test "${device##*/}" = lo
done
test "$(awk '/^CapEff:/ {print $2}' /proc/self/status)" = 0000000000000000
test "$(awk '/^NoNewPrivs:/ {print $2}' /proc/self/status)" = 1
test "$(awk '/^Seccomp:/ {print $2}' /proc/self/status)" = 2
test "$(cat /sys/fs/cgroup/memory.max)" = 134217728
test "$(cat /sys/fs/cgroup/memory.swap.max)" = 0
test "$(cat /sys/fs/cgroup/pids.max)" = 16
test "$(cat /sys/fs/cgroup/cpu.max)" = '25000 100000'
if touch /rootfs-probe 2>/dev/null; then
    printf 'Unexpected writable root filesystem.\n' >&2
    exit 1
fi
if touch /input/unexpected-file 2>/dev/null; then
    printf 'Unexpected writable input mount.\n' >&2
    exit 1
fi
printf 'scratch check\n' > /scratch/probe.txt
test "$(df -Pk /scratch | awk 'NR == 2 {print $2}')" = 16384
printf 'uid=1000 network=loopback-only capabilities=none no_new_privileges=1 seccomp=filter\n'
printf 'workspace=private-linux-copy input=readonly rootfs=readonly scratch=16MiB\n'
printf 'memory_max_bytes=134217728 swap_max_bytes=0 pids_max=16 cpu_max=25000/100000\n'
CONTAINER_PROBE
)"

probe_container="$(docker create \
    --name "minos-m1-boundary-${probe_workspace##*.}" \
    --label minos.spike=m1-boundary \
    --network none \
    --user 1000:1000 \
    --cap-drop ALL \
    --security-opt no-new-privileges \
    --read-only \
    --memory 128m \
    --memory-swap 128m \
    --cpus 0.25 \
    --pids-limit 16 \
    --shm-size 1m \
    --tmpfs /scratch:size=16m,mode=700,uid=1000,gid=1000,noexec,nosuid,nodev \
    --log-driver none \
    --ulimit nofile=64:64 \
    --ulimit core=0 \
    --mount "type=bind,src=$probe_workspace,dst=/input,readonly" \
    "$probe_image" /usr/bin/env -i PATH=/usr/bin:/bin /bin/sh -c "$probe_command")"
[[ "$probe_container" =~ ^[0-9a-f]{64}$ ]]

printf 'image=%s workspace_filesystem=%s\n' "$probe_image" "$workspace_filesystem"
# Limits attachment time; the EXIT trap removes the container if this times out.
timeout --signal=TERM --kill-after=2s 8s docker start --attach "$probe_container"
docker inspect --format 'state={{json .State}} mounts={{json .Mounts}}' "$probe_container"
printf 'boundary_probe=passed\n'
