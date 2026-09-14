#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0
readonly directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly dotnet_command="${MINOS_DOTNET:-dotnet}"
readonly build_directory=/tmp/minos-m1-build
mkdir -p "$directory/results"
{
    bash "$directory/probe-container-boundary.sh"
    bash "$directory/probe-compile-run.sh"
    "$dotnet_command" build "$directory/ExecutionSpike/ExecutionSpike.csproj" --artifacts-path "$build_directory" --nologo >&2
    "$dotnet_command" "$build_directory/bin/ExecutionSpike/debug/ExecutionSpike.dll" --all "$directory/fixtures"
} | tee "$directory/results/m1-results.log"
