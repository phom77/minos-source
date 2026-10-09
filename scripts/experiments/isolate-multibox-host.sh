#!/usr/bin/env bash
# Explicit temporary WSL fixture setup, never production provisioning or a startup hook.
set -euo pipefail
umask 022
[[ $(id -u) = 0 ]]
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
prefix=/opt/minos-isolate-multibox-20261009-8f185bb
commit=8f185bb37f3f23e29b33b0c7727c91c13429abe3
unit=minos-isolate-multibox-experiment.service
cg=/sys/fs/cgroup/system.slice/$unit
[[ $(realpath /opt) = /opt ]]
case "${1:-}" in
setup)
    count="${MINOS_ISOLATE_BOXES:-2}"
    [[ "$count" =~ ^([2-9]|1[0-6])$ && "$count" -lt $(nproc) ]]
    source_dir=$(realpath "${2:?Pinned source required}")
    git -c safe.directory="$source_dir" -c core.autocrlf=true -C "$source_dir" diff --quiet --no-ext-diff --no-textconv HEAD --
    [[ $(git -c safe.directory="$source_dir" -C "$source_dir" rev-parse HEAD) = "$commit" ]]
    built="$repo/artifacts/judging-multibox-20261009/helpers"
    [[ ! -L "$built" && $(realpath "$built") = "$built" && $(stat -c %u "$built") = 1000 ]]
    [[ ! -L "$built/.owner" && $(head -1 "$built/.owner") = "$commit" && $(tail -1 "$built/.owner") = "$count" ]]
    [[ ! -e "$prefix" && ! -L "$prefix" && $(stat -fc %T /sys/fs/cgroup) = cgroup2fs ]]
    [[ -z $(docker ps -q --filter label=com.docker.compose.service=worker) && -z $(docker ps -q --filter label=minos.judge=m6) ]]
    for ((id=0; id<count; id++)); do
        [[ -z $(getent passwd "$((61000+id))") && -z $(getent group "$((61000+id))") ]]
        [[ ! -L "$built/$id/isolate" && $(stat -c %u "$built/$id/isolate") = 1000 ]]
    done
    mkdir -m 755 "$prefix" "$prefix/helpers" "$prefix/boxes" "$prefix/locks"
    printf 'minos-multibox-%s\n%s\n' "$commit" "$count" > "$prefix/.owner"
    mkdir -m 700 "$prefix/work"
    chown 1000:1000 "$prefix/work"
    cat > "$prefix/isolate.cf" <<EOF
box_root = $prefix/boxes
lock_root = $prefix/locks
cg_root = auto:$prefix/cgroup
first_uid = 61000
first_gid = 61000
num_boxes = $count
EOF
    [[ ! -L "$built/isolate-cg-keeper" && $(stat -c %u "$built/isolate-cg-keeper") = 1000 ]]
    install -m 755 "$built/isolate-cg-keeper" "$prefix/isolate-cg-keeper"
    for ((id=0; id<count; id++)); do
        mkdir -m 755 "$prefix/helpers/$id" "$prefix/boxes/slot-$id"
        mount -t tmpfs -o size=8m,mode=755,nosuid,nodev,noexec tmpfs "$prefix/boxes/slot-$id"
        install -m 4755 "$built/$id/isolate" "$prefix/helpers/$id/isolate"
        sed "s|^box_root = .*|box_root = $prefix/boxes/slot-$id|" "$prefix/isolate.cf" > "$prefix/helpers/$id/isolate.cf"
        printf 'box%s.cpus = %s\n' "$id" "$id" >> "$prefix/helpers/$id/isolate.cf"
        "$prefix/helpers/$id/isolate" --check-config
    done
    systemd-run --unit="$unit" --collect --service-type=notify -p Delegate=yes "$prefix/isolate-cg-keeper"
    [[ $(cat "$prefix/cgroup") = "$cg" ]]
    printf '0\n' > "$cg/memory.swap.max"
    ;;
cleanup)
    # Literal resolved path and root-owned marker authorize only this experiment's resources.
    [[ -d "$prefix" && ! -L "$prefix" && $(realpath "$prefix") = "$prefix" && $(stat -c '%u:%g' "$prefix") = 0:0 ]]
    [[ ! -L "$prefix/.owner" && $(stat -c '%u:%g' "$prefix/.owner") = 0:0 && $(head -1 "$prefix/.owner") = "minos-multibox-$commit" ]]
    count=$(tail -1 "$prefix/.owner")
    [[ "$count" =~ ^([2-9]|1[0-6])$ ]]
    [[ -z $(find "$cg" -mindepth 1 -maxdepth 1 -type d ! -name daemon -print 2>/dev/null || true) ]]
    for ((id=0; id<count; id++)); do [[ -z $(ps -u "$((61000+id))" -o pid= || true) ]]; done
    systemctl stop "$unit" || [[ ! -d "$cg" ]]
    [[ ! -d "$cg" ]]
    for ((id=0; id<count; id++)); do
        target="$prefix/boxes/slot-$id"
        [[ ! -L "$target" && $(realpath "$target") = "$target" ]]
        if mountpoint -q "$target"; then umount "$target"; fi
    done
    [[ -z $(findmnt -rn -o TARGET | awk -v p="$prefix/" 'index($0,p)==1') ]]
    rm -rf -- "$prefix"
    [[ ! -e "$prefix" ]]
    ;;
*) printf 'Usage: %s setup PINNED_SOURCE | cleanup\n' "$0" >&2; exit 2 ;;
esac
