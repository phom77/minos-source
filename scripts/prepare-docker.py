"""Create local Docker credentials without printing or overwriting passwords."""

import os
from pathlib import Path
import secrets


def main():
    repository = Path(__file__).resolve().parent.parent
    destination = repository / ".env"
    if destination.exists():
        print(".env already exists; it was left unchanged.")
        return
    socket = Path("/var/run/docker.sock")
    group = socket.stat().st_gid if os.name != "nt" and socket.exists() else 0
    values = ["MINOS_PROJECT_NAME=minos", "MINOS_HTTPS_PORT=8443", "MINOS_POSTGRES_PORT=5433", f"MINOS_DOCKER_GID={group}"]
    for role in ("BOOTSTRAP", "MIGRATOR", "API", "WORKER"):
        values.append(f"MINOS_{role}_PASSWORD={secrets.token_hex(32)}")
    descriptor = os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
        stream.write("# Private local Docker configuration. Keep with the existing database volume.\n")
        stream.write("\n".join(values) + "\n")
    print("Created private .env. Run: docker compose up --build -d --wait")


if __name__ == "__main__":
    main()
