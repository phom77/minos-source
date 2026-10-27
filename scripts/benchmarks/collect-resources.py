"""Trusted fixture-only sampler: bounded one-shot Engine reads, never reads file contents."""
import datetime as dt
import http.client
import json
import os
from pathlib import Path
import signal
import socket
import stat
import time
import urllib.parse


class Engine(http.client.HTTPConnection):
    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(3)
        self.sock.connect("/var/run/docker.sock")


def get(path):
    connection = Engine("docker", timeout=3)
    try:
        connection.request("GET", "/v1.51/" + path)
        response = connection.getresponse()
        data = response.read(4 * 1024 * 1024 + 1)
        if response.status != 200 or len(data) > 4 * 1024 * 1024:
            raise ValueError("Unavailable or oversized Engine evidence")
        return json.loads(data)
    finally:
        connection.close()


def size(root):
    total = 0
    for base, _, files in os.walk(root, followlinks=False):
        for name in files:
            try:
                info = os.stat(os.path.join(base, name), follow_symlinks=False)
                if stat.S_ISREG(info.st_mode): total += info.st_size
            except FileNotFoundError:
                pass  # A stage completed during this sampled observation.
    return total


def main():
    if os.name != "posix" or os.geteuid() == 0 or os.environ.get("MINOS_TEST_SANDBOX") != "1":
        raise SystemExit("Only the non-root disposable checks fixture can run this sampler.")
    project = os.environ["MINOS_READINESS_PROJECT"]
    volume = os.environ["MINOS_TEST_WORKSPACE_VOLUME"]
    stopped = False
    prior_cpu = {}
    def stop(*_):
        nonlocal stopped
        stopped = True
    signal.signal(signal.SIGTERM, stop); signal.signal(signal.SIGINT, stop)
    while not stopped:
        try:
            filters = urllib.parse.quote(json.dumps({"label": ["com.docker.compose.project=" + project]}))
            containers = get("containers/json?filters=" + filters)
            filters = urllib.parse.quote(json.dumps({"label": ["minos.judge=m6"]}))
            containers += [c for c in get("containers/json?filters=" + filters)
                           if any(m.get("Name") == volume for m in c["Mounts"])]
            cpu = 0.0; memory = 0; captured = 0; missing = 0; cpu_observed = 0
            for container in containers:
                try:
                    evidence = get(f"containers/{container['Id']}/stats?stream=false&one-shot=true")
                    current = evidence["cpu_stats"]; previous = prior_cpu.get(container["Id"])
                    prior_cpu[container["Id"]] = current
                    if previous is not None:
                        delta = current["cpu_usage"]["total_usage"] - previous["cpu_usage"]["total_usage"]
                        system = current.get("system_cpu_usage", 0) - previous.get("system_cpu_usage", 0)
                        if system > 0 and delta >= 0:
                            cpu += delta / system * current.get("online_cpus", 1) * 100
                            cpu_observed += 1
                    memory += evidence["memory_stats"].get("usage", 0)
                    captured += 1
                except (OSError, ValueError, KeyError, http.client.HTTPException):
                    missing += 1
            mem = dict((line.split(":")[0], int(line.split()[1])) for line in Path("/proc/meminfo").read_text().splitlines() if line.startswith(("MemTotal:", "MemAvailable:")))
            disk = os.statvfs("/tmp/minos-judge-execution")
            print(json.dumps({"kind": "sample", "atUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
                "containers": captured, "missingContainers": missing, "cpuObservedContainers": cpu_observed,
                "cpuPercentOneCoreUnits": cpu if cpu_observed else None,
                "memoryBytes": memory, "hostMemoryAvailableFraction": mem["MemAvailable"] / mem["MemTotal"],
                "workspaceFilesystemBytes": disk.f_blocks * disk.f_frsize,
                "workspaceFilesystemFreeBytes": disk.f_bavail * disk.f_frsize,
                "workspaceBytes": size("/tmp/minos-judge-execution")}), flush=True)
        except (OSError, ValueError, KeyError, http.client.HTTPException) as error:
            print(json.dumps({"kind": "gap", "errorType": type(error).__name__}), flush=True)
        for _ in range(20):
            if stopped: break
            time.sleep(.1)


if __name__ == "__main__": main()
