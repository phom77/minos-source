#!/bin/sh
set -eu
# Startup writes its PID only after database, storage, Docker and recovery checks pass.
# Do not run --check here: a second host must fail the M7 exclusive lock.
pid="$(cat /tmp/minos-judge-execution/.worker-ready 2>/dev/null)"
case "$pid" in ''|*[!0-9]*) exit 1 ;; esac
kill -0 "$pid"
tr '\000' '\n' < "/proc/$pid/cmdline" | grep -Eq '(^|/)Minos[.]JudgeWorker[.]dll$'
docker info >/dev/null 2>&1
