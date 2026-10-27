"""Run backend non-execution regression tests against disposable Linux PostgreSQL.

Builds existing API/checks targets, starts only checks dependencies, publishes no ports,
does not execute contestant programs, and removes its own Compose resources in finally.
"""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import secrets
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--filter", help="Optional dotnet test filter; default runs all non-execution tests, isolating global-capacity fixtures.")
    args = parser.parse_args()
    project = "minos-integration-" + dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d-%H%M%S") + "-" + secrets.token_hex(3)
    evidence = REPO / "artifacts" / project
    evidence.mkdir(parents=True)
    env = os.environ.copy()
    env.update(MINOS_PROJECT_NAME=project, MINOS_DOCKER_GID="0")
    for role in ("BOOTSTRAP", "MIGRATOR", "API", "WORKER"):
        env[f"MINOS_{role}_PASSWORD"] = secrets.token_hex(32)

    def run(command, log, check=True):
        with (evidence / log).open("a", encoding="utf-8") as output:
            result = subprocess.run(command, cwd=REPO, env=env, stdout=output, stderr=subprocess.STDOUT)
        if check and result.returncode:
            raise RuntimeError(f"Check failed; inspect {evidence / log}")
        return result.returncode

    if not args.skip_build:
        for target in ("api", "checks"):
            print(f"Building {target}...", flush=True)
            run(["docker", "build", "--target", target, "-t", f"minos-integration-{target}:check", "-f", "docker/backend.Dockerfile", "."], "build.log")
    images = {target: subprocess.check_output(["docker", "image", "inspect", "--format", "{{.Id}}", f"minos-integration-{target}:check"], text=True).strip() for target in ("api", "checks")}
    # Existing lease/admission fixtures share synthetic queue state. Give the global-capacity
    # class a clean database rather than changing production admission or deleting test evidence.
    groups = [("targeted", args.filter)] if args.filter else [
        ("regression", "FullyQualifiedName!~ReliabilityTests"), ("capacity", "FullyQualifiedName~ReliabilityTests")]
    summaries = []
    for name, test_filter in groups:
        group = evidence / name
        group.mkdir()
        fixture_project = project + "-" + name
        env["MINOS_PROJECT_NAME"] = fixture_project
        empty_env = group / "empty.env"
        empty_env.write_text("", encoding="utf-8")
        override = group / "override.yaml"
        # This feature requires PostgreSQL only; remove the Docker control socket and workspace.
        override.write_text("services:\n  init:\n    image: " + images["api"] + "\n  checks-migrate:\n    image: " + images["api"] +
            "\n  checks:\n    image: " + images["checks"] + "\n    volumes: !reset []\n    group_add: !reset []\n", encoding="utf-8")
        compose = ["docker", "compose", "--env-file", str(empty_env), "-p", fixture_project, "-f", "compose.yaml", "-f", "docker/compose.test.yaml", "-f", str(override), "--profile", "checks"]
        try:
            print(f"Migrating disposable PostgreSQL and running Linux {name} tests...", flush=True)
            run(compose + ["run", "--rm", "--env", "MINOS_TEST_SANDBOX=0", "--volume", str(group) + ":/results", "checks",
                "--filter", test_filter, "--logger", "trx;LogFilePrefix=integration", "--results-directory", "/results"], f"{name}/tests.log")
            # A repeated application of the same migration set must be harmless.
            run(compose + ["run", "--rm", "--no-deps", "checks-migrate"], f"{name}/repeat-migration.log")
            counts = [node.attrib for file in group.glob("*.trx") for node in ET.parse(file).getroot().iter() if node.tag.endswith("}Counters")]
            summaries.append({"project": fixture_project, "filter": test_filter, "testCounters": counts, "repeatMigrationPassed": True})
        finally:
            run(compose + ["down", "--volumes", "--remove-orphans"], f"{name}/cleanup.log")
    (evidence / "summary.json").write_text(json.dumps({"project": project, "images": images, "groups": summaries,
        "sandboxExecution": False, "testsPassed": True}, indent=2), encoding="utf-8")
    print(f"Integration checks passed. Evidence: {evidence}", flush=True)


if __name__ == "__main__":
    main()
