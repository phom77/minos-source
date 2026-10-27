#!/usr/bin/env bash
# Uses the same private disposable PostgreSQL/SDK harness; no application deployment.
set -euo pipefail
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
export MINOS_TEST_RUNNER="${MINOS_TEST_RUNNER:-docker}" MINOS_TEST_ALL="${MINOS_TEST_ALL:-1}" MINOS_TEST_BENCHMARK="${MINOS_TEST_BENCHMARK:-0}"
exec bash "$repo/scripts/benchmarks/native.sh" "${1:-minos-checks:local}"
