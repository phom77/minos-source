"""Disposable single-host Docker workload; refuses active Workers, never uses the dev database."""

import argparse
import datetime as dt
import json
import math
import os
from pathlib import Path
import re
import secrets
import subprocess
import threading
import time


REPO = Path(__file__).resolve().parent.parent


def command(args, **kwargs):
    return subprocess.run(args, check=True, text=True, cwd=REPO, **kwargs)


def read(args):
    return command(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20).stdout.strip()


def bytes_value(value):
    number, unit = re.fullmatch(r"([\d.]+)\s*([A-Za-z]*)", value.strip()).groups()
    units = {"B": 1, "kB": 1000, "MB": 1000**2, "GB": 1000**3, "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3}
    return int(float(number) * units[unit])


def transient_output(args):
    # Docker can emit valid partial JSON/stats and a nonzero exit when an ephemeral
    # sandbox disappears. Keep stable-container observations, never invent its peak.
    return subprocess.run(args, text=True, cwd=REPO, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE, timeout=20).stdout.strip()


def summarize(records, manifest):
    comparisons = []
    for record in records:
        targets = manifest[record["workload"]]["targets"]
        observed = {
            "queueP95Seconds": record["queueSeconds"]["p95"],
            "completionP95Seconds": record["completionSeconds"]["p95"],
            "apiP95Milliseconds": record["apiMilliseconds"]["p95"],
            "admissionP95Milliseconds": record["admissionMilliseconds"]["p95"],
            "systemFailed": record["systemFailed"], "retries": record["retries"],
            "expectedAccountRejections": record["accountRejections"]
        }
        checks = {key: value == targets[key] if key == "expectedAccountRejections" else value <= targets[key]
                  for key, value in observed.items()}
        comparisons.append({"workload": record["workload"], "activeJobs": record["activeJobs"],
                            "observed": observed, "targets": targets, "checks": checks})
    expected = {(name, jobs) for name in ("mixed", "many-short-tests") for jobs in manifest[name]["activeJobs"]}
    actual = {(r["workload"], r["activeJobs"]) for r in records}
    if actual != expected or len(records) != len(expected):
        raise ValueError("Missing/duplicate workload results; an incomplete run cannot establish readiness.")
    for record in records:
        spec = manifest[record["workload"]]
        if record["jobs"] != spec["jobs"] or record["testsPerJob"] != spec["testsPerJob"]:
            raise ValueError("Measured workload differs from predeclared manifest.")
        if any(not math.isfinite(value) for field in ("queueSeconds", "completionSeconds", "apiMilliseconds", "admissionMilliseconds")
               for value in (record[field]["p50"], record[field]["p95"], record[field]["max"])):
            raise ValueError("Invalid measurement.")
    return comparisons


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-build", action="store_true", help="Use prebuilt minos-readiness-{api,checks}:local images.")
    args = parser.parse_args()
    manifest = json.loads((REPO / "scripts/fixtures/readiness-workload.json").read_text(encoding="utf-8"))
    for label in ("com.docker.compose.service=worker", "minos.judge=m6"):
        if read(["docker", "ps", "-q", "--filter", "label=" + label]):
            raise SystemExit("Stop all Workers and finish sandbox checks first; restart the previous Worker after this check.")
    if not args.skip_build:
        for target in ("api", "checks"):
            command(["docker", "build", "--target", target, "-t", f"minos-readiness-{target}:local", "-f", "docker/backend.Dockerfile", "."])
    for target in ("api", "checks"):
        read(["docker", "image", "inspect", f"minos-readiness-{target}:local"])
    suffix = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d-%H%M%S") + "-" + secrets.token_hex(3)
    project = "minos-readiness-" + suffix
    evidence = REPO / "artifacts" / project
    evidence.mkdir(parents=True, exist_ok=False)
    env_file = evidence / "fixture.env"
    override = evidence / "compose.override.yaml"
    values = [f"MINOS_PROJECT_NAME={project}", "MINOS_DOCKER_GID=0"]
    for role in ("BOOTSTRAP", "MIGRATOR", "API", "WORKER"):
        values.append(f"MINOS_{role}_PASSWORD={secrets.token_hex(32)}")
    descriptor = os.open(env_file, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as output:
        output.write("\n".join(values) + "\n")
    override.write_text("services:\n  init:\n    image: minos-readiness-api:local\n  checks-migrate:\n    image: minos-readiness-api:local\n  checks:\n    image: minos-readiness-checks:local\n", encoding="utf-8")
    compose = ["docker", "compose", "--env-file", str(env_file), "-p", project, "-f", "compose.yaml", "-f", "compose.checks.yaml", "-f", str(override), "--profile", "checks"]
    runner = project + "-measurement"
    volume = project + "-judge-workspaces"
    metadata = {"schemaVersion": 1, "project": project, "manifest": manifest,
                "revision": read(["git", "rev-parse", "HEAD"]),
                "dirtyFiles": read(["git", "diff", "--name-only"]).splitlines(),
                "host": json.loads(read(["docker", "info", "--format", "{{json .}}"])),
                "images": {target: json.loads(read(["docker", "image", "inspect", f"minos-readiness-{target}:local"]))[0]["Id"] for target in ("api", "checks")}}
    # Host evidence contains engine configuration, not credentials; retain only resource/platform fields.
    metadata["host"] = {key: metadata["host"][key] for key in ("NCPU", "MemTotal", "ServerVersion", "KernelVersion", "OperatingSystem", "CgroupVersion")}
    (evidence / "manifest.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    stopped = threading.Event(); samples = []; sample_errors = []

    def sample():
        while not stopped.is_set():
            try:
                ids = read(["docker", "ps", "-q", "--filter", "label=com.docker.compose.project=" + project]).splitlines()
                # The run container also has this label. Sibling sandboxes are included only for our exact volume.
                candidates = read(["docker", "ps", "-q", "--filter", "label=minos.judge=m6"]).splitlines()
                if candidates:
                    for container in json.loads(transient_output(["docker", "inspect", *candidates]) or "[]"):
                        if any(mount.get("Name") == volume for mount in container["Mounts"]):
                            ids.append(container["Id"])
                if ids:
                    stats = [json.loads(line) for line in transient_output(["docker", "stats", "--no-stream", "--format", "{{json .}}", *ids]).splitlines()]
                    if not stats:
                        sample_errors.append("NoContainerStats")
                        stopped.wait(2)
                        continue
                    row = {"atUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "containers": len(stats),
                           "cpuPercentOneCoreUnits": sum(float(s["CPUPerc"].rstrip("%")) for s in stats),
                           "memoryBytes": sum(bytes_value(s["MemUsage"].split("/")[0]) for s in stats)}
                    if runner in read(["docker", "ps", "--format", "{{.Names}}", "--filter", "name=" + runner]).splitlines():
                        memory = read(["docker", "exec", runner, "cat", "/proc/meminfo"])
                        available = re.search(r"^MemAvailable:\s+(\d+) kB", memory, re.MULTILINE)
                        total = re.search(r"^MemTotal:\s+(\d+) kB", memory, re.MULTILINE)
                        if available and total:
                            row["hostMemoryAvailableFraction"] = int(available[1]) / int(total[1])
                        disk = transient_output(["docker", "exec", runner, "sh", "-c", "df -B1 /tmp/minos-judge-execution | tail -1; du -sb /tmp/minos-judge-execution"]).splitlines()
                        columns = disk[0].split()
                        row.update(workspaceFilesystemBytes=int(columns[1]), workspaceFilesystemFreeBytes=int(columns[3]), workspaceBytes=int(disk[1].split()[0]))
                    samples.append(row)
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired, ValueError, KeyError) as error:
                # A short-lived sandbox may disappear during inspection; explicitly report sampling gaps.
                sample_errors.append(type(error).__name__)
            stopped.wait(2)

    thread = threading.Thread(target=sample, daemon=True)
    exit_code = 1
    try:
        # Only the checks dependencies start: no development API, Worker, database or published port.
        # `run` waits for each declared healthy/completed dependency. `up --wait` would
        # incorrectly require the one-shot grants/init services to remain running.
        thread.start()
        with (evidence / "tests.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(compose + ["run", "--rm", "--name", runner,
                "--volume", str(evidence) + ":/results", "--env", "MINOS_TEST_READINESS=1", "checks",
                "--filter", "FullyQualifiedName~ReadinessLoadIntegrationTests|FullyQualifiedName~JudgingObservabilityIntegrationTests",
                "--logger", "trx;LogFilePrefix=readiness", "--logger", "console;verbosity=detailed", "--results-directory", "/results"],
                cwd=REPO, text=True, stdout=log, stderr=subprocess.STDOUT)
        exit_code = result.returncode
    finally:
        stopped.set()
        if thread.is_alive(): thread.join(timeout=15)
        command(compose + ["down", "--volumes", "--remove-orphans"], stdout=subprocess.DEVNULL)
        env_file.unlink(missing_ok=True)
        (evidence / "resources.json").write_text(json.dumps({"samples": samples, "samplingGaps": sample_errors}, indent=2), encoding="utf-8")
    if exit_code:
        raise SystemExit(f"Correctness check failed; inspect {evidence / 'tests.log'} (fixture cleaned).")
    records = [json.loads(line.split("READINESS_JSON=", 1)[1]) for line in (evidence / "tests.log").read_text(encoding="utf-8").splitlines() if "READINESS_JSON=" in line]
    checks = summarize(records, manifest)
    summary = {"schemaVersion": 1, "comparisons": checks, "measurements": records,
               "resourceSamples": len(samples), "samplingGaps": len(sample_errors),
               "sampledPeakCpuPercentOneCoreUnits": max((s["cpuPercentOneCoreUnits"] for s in samples), default=None),
               "sampledPeakMemoryBytes": max((s["memoryBytes"] for s in samples), default=None),
               "sampledPeakWorkspaceBytes": max((s.get("workspaceBytes", 0) for s in samples), default=None),
               "minimumSampledHostMemoryAvailableFraction": min((s["hostMemoryAvailableFraction"] for s in samples if "hostMemoryAvailableFraction" in s), default=None),
               "minimumWorkspaceDiskFreeFraction": min((s["workspaceFilesystemFreeBytes"] / s["workspaceFilesystemBytes"] for s in samples if "workspaceFilesystemBytes" in s), default=None)}
    summary["meetsLatencyAndCorrectnessTargets"] = all(all(c["checks"].values()) for c in checks)
    summary["meetsSampledHeadroomTargets"] = (
        summary["minimumSampledHostMemoryAvailableFraction"] is not None and
        summary["minimumSampledHostMemoryAvailableFraction"] >= manifest["headroom"]["minimumDockerMemoryHeadroomFraction"] and
        summary["minimumWorkspaceDiskFreeFraction"] is not None and
        summary["minimumWorkspaceDiskFreeFraction"] >= manifest["headroom"]["minimumWorkspaceDiskFreeFraction"])
    (evidence / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"Readiness report: {evidence / 'summary.json'}")
    print("Targets: " + ("met" if summary["meetsLatencyAndCorrectnessTargets"] else "missed; retain measurements and evaluate the bottleneck"))
    # A missed experimental latency target is a finding, not a failed correctness test.


if __name__ == "__main__":
    main()
