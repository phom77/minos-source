#!/usr/bin/env bash
# Build unmodified pinned helpers as the non-root controller, never in the setup script.
set -euo pipefail
umask 077
[[ $(id -u) = 1000 && $(id -g) = 1000 ]]
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
source_dir=$(realpath "${1:?Pinned source required}")
output="$repo/artifacts/judging-multibox-20261009/helpers"
prefix=/opt/minos-isolate-multibox-20261009-8f185bb
commit=8f185bb37f3f23e29b33b0c7727c91c13429abe3
count="${MINOS_ISOLATE_BOXES:-2}"
[[ "$count" =~ ^([2-9]|1[0-6])$ && "$count" -lt $(nproc) ]]
[[ $(git -C "$source_dir" rev-parse HEAD) = "$commit" ]]
git -c core.autocrlf=true -C "$source_dir" diff --quiet --no-ext-diff --no-textconv HEAD --
[[ ! -L "$output" ]]
mkdir -p "$output"
[[ $(realpath "$output") = "$output" ]]
for ((id=0; id<count; id++)); do
    [[ ! -L "$output/$id" ]]
    mkdir -p "$output/$id"
    make -C "$source_dir" -B isolate CONFIG="$prefix/helpers/$id/isolate.cf" > "$output/$id/build.log" 2>&1
    install -m 755 "$source_dir/isolate" "$output/$id/isolate"
done
make -C "$source_dir" -B isolate-cg-keeper CONFIG="$prefix/isolate.cf" > "$output/keeper-build.log" 2>&1
install -m 755 "$source_dir/isolate-cg-keeper" "$output/isolate-cg-keeper"
printf '%s\n%s\n' "$commit" "$count" > "$output/.owner"
printf 'Built %s pinned helpers in %s\n' "$count" "$output"
