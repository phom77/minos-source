#!/usr/bin/env bash
# Explicit, temporary root setup for the approved WSL experiment; never an app startup hook.
set -euo pipefail
umask 022
[[ $(id -u) = 0 ]]
prefix=/opt/minos-isolate-phase2-20261008-8f185bb
commit=8f185bb37f3f23e29b33b0c7727c91c13429abe3
unit=minos-isolate-phase2-experiment.service
cg=/sys/fs/cgroup/system.slice/$unit
[[ $(realpath /opt) = /opt ]]
case "${1:-}" in
setup)
    source=$(realpath "${2:?Provide the built, pinned Isolate source directory}")
    [[ $(git -c safe.directory="$source" --no-pager -C "$source" rev-parse HEAD) = "$commit" ]]
    # Windows checkouts can use CRLF; compare canonical Git contents without dumping upstream source.
    git -c safe.directory="$source" -c core.autocrlf=true --no-pager -C "$source" diff --quiet --no-ext-diff --no-textconv HEAD --
    [[ ! -e "$prefix" && ! -L "$prefix" ]]
    [[ -z $(getent passwd 61000) && -z $(getent group 61000) ]]
    [[ $(stat -fc %T /sys/fs/cgroup) = cgroup2fs ]]
    # Both binaries must have been built with CONFIG=$prefix/isolate.cf (see performance report).
    mkdir -m 755 "$prefix"
    printf 'minos-phase2-%s\n' "$commit" > "$prefix/.owner"
    install -m 4755 "$source/isolate" "$prefix/isolate"
    install -m 755 "$source/isolate-cg-keeper" "$prefix/isolate-cg-keeper"
    mkdir -m 755 "$prefix/boxes" "$prefix/locks"
    # Native adapter scratch stays inside the helper-owned box, so root cleanup handles hostile permissions.
    mount -t tmpfs -o size=8m,mode=755,nosuid,nodev,noexec tmpfs "$prefix/boxes"
    mkdir -m 700 "$prefix/work" "$prefix/scratch"
    chown 1000:1000 "$prefix/work"
    mount -t tmpfs -o size=8m,mode=700,uid=1000,gid=1000,nosuid,nodev,noexec tmpfs "$prefix/scratch"
    cat > "$prefix/isolate.cf" <<EOF
box_root = $prefix/boxes
lock_root = $prefix/locks
cg_root = auto:$prefix/cgroup
first_uid = 61000
first_gid = 61000
num_boxes = 1
box0.cpus = 0
EOF
    systemd-run --unit="$unit" --collect --service-type=notify -p Delegate=yes "$prefix/isolate-cg-keeper"
    [[ $(cat "$prefix/cgroup") = "$cg" ]]
    printf '0\n' > "$cg/memory.swap.max"
    "$prefix/isolate" --check-config
    "$prefix/isolate" --version
    ;;
cleanup)
    # Verify the literal target, ownership and marker before any recursive deletion.
    [[ -d "$prefix" && ! -L "$prefix" && $(realpath "$prefix") = "$prefix" ]]
    [[ $(stat -c '%u:%g' "$prefix") = 0:0 ]]
    [[ ! -L "$prefix/.owner" && $(stat -c '%u:%g' "$prefix/.owner") = 0:0 ]]
    [[ $(cat "$prefix/.owner") = "minos-phase2-$commit" ]]
    [[ ! -d "$cg/box-0" ]]
    [[ -z $(ps -u 61000 -o pid= || true) ]]
    systemctl stop "$unit"
    [[ ! -d "$cg" ]]
    if mountpoint -q "$prefix/scratch"; then umount "$prefix/scratch"; fi
    if mountpoint -q "$prefix/boxes"; then umount "$prefix/boxes"; fi
    [[ -z $(findmnt -rn -o TARGET | awk -v p="$prefix/" 'index($0,p)==1') ]]
    rm -rf -- "$prefix"
    [[ ! -e "$prefix" ]]
    ;;
*) printf 'Usage: %s setup PINNED_BUILT_SOURCE | cleanup\n' "$0" >&2; exit 2 ;;
esac
