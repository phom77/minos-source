#!/bin/sh
set -eu
umask 077
mkdir -p "$DOCKER_CONFIG"
docker version --format '{{.Server.Os}}' | grep -qx linux
image='gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15'
# Pull before accepting jobs: download time is not a compilation deadline.
if ! docker image inspect "$image" >/dev/null 2>&1; then docker pull "$image"; fi
exec dotnet /app/Minos.JudgeWorker.dll "$@"
