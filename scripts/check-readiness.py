"""Disposable single-host Docker workload; refuses active Workers, never uses the dev database."""

import argparse
import datetime as dt
import json
import math
import os
from pathlib import Path
import secrets
import subprocess
import sys


REPO = Path(__file__).resolve().parent.parent


def command(args, **kwargs):
    return subprocess.run(args, check=True, text=True, cwd=REPO, **kwargs)


def read(args):
    return command(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20).stdout.strip()


def inside_fixture():
    if os.name != "posix" or os.geteuid() == 0 or os.environ.get("MINOS_TEST_SANDBOX") != "1":
        raise SystemExit("Requires the non-root disposable checks fixture.")
    with Path("/results/resources-stream.jsonl").open("w", encoding="utf-8") as resources:
        sampler = subprocess.Popen([sys.executable, str(REPO / "scripts/collect-readiness-resources.py")], stdout=resources)
        try:
            result = subprocess.run(["dotnet", "test", "tests/Minos.IntegrationTests", "--no-build", "-c", "Release", "--artifacts-path", "/build",
                "--filter", "FullyQualifiedName~ReadinessLoadIntegrationTests|FullyQualifiedName~JudgingObservabilityIntegrationTests",
                "--logger", "trx;LogFilePrefix=readiness", "--logger", "console;verbosity=detailed", "--results-directory", "/results"])
        finally:
            sampler.terminate()
            try: sampler.wait(timeout=10)
            except subprocess.TimeoutExpired:
                sampler.kill(); sampler.wait()
    raise SystemExit(result.returncode)


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
    parser.add_argument("--inside-fixture", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.inside_fixture: inside_fixture()
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
    exit_code = 1
    try:
        # Only the checks dependencies start: no development API, Worker, database or published port.
        # `run` waits for each declared healthy/completed dependency. `up --wait` would
        # incorrectly require the one-shot grants/init services to remain running.
        with (evidence / "tests.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(compose + ["run", "--rm", "--name", runner,
                "--volume", str(evidence) + ":/results", "--env", "MINOS_TEST_READINESS=1",
                "--env", "MINOS_READINESS_PROJECT=" + project, "--entrypoint", "python3", "checks",
                "/src/scripts/check-readiness.py", "--inside-fixture"],
                cwd=REPO, text=True, stdout=log, stderr=subprocess.STDOUT)
        exit_code = result.returncode
    finally:
        try:
            command(compose + ["down", "--volumes", "--remove-orphans"], stdout=subprocess.DEVNULL)
        finally:
            env_file.unlink(missing_ok=True)
        stream = evidence / "resources-stream.jsonl"
        resource_rows = [json.loads(line) for line in stream.read_text(encoding="utf-8").splitlines()] if stream.exists() else []
        samples = [r for r in resource_rows if r["kind"] == "sample"]
        sample_errors = [r for r in resource_rows if r["kind"] == "gap"]
        (evidence / "resources.json").write_text(json.dumps({"samples": samples, "samplingGaps": sample_errors}, indent=2), encoding="utf-8")
    if exit_code:
        raise SystemExit(f"Correctness check failed; inspect {evidence / 'tests.log'} (fixture cleaned).")
    records = [json.loads(line.split("READINESS_JSON=", 1)[1]) for line in (evidence / "tests.log").read_text(encoding="utf-8").splitlines() if "READINESS_JSON=" in line]
    checks = summarize(records, manifest)
    summary = {"schemaVersion": 1, "comparisons": checks, "measurements": records,
               "resourceSamples": len(samples), "samplingGaps": len(sample_errors),
               "sampledPeakCpuPercentOneCoreUnits": max((s["cpuPercentOneCoreUnits"] for s in samples if s["cpuPercentOneCoreUnits"] is not None), default=None),
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
