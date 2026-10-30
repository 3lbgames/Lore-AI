#!/usr/bin/env python3
"""Install or update the NPC service on a dedicated Ubuntu 24.04 EC2 instance."""

import argparse
import getpass
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys
import time


INSTALL = Path("/opt/villagerai/npc-service")
CADDY = Path("/etc/caddy/Caddyfile")
MARKER = "# Managed by VillagerAI deploy-aws.py\n"
COMPOSE = ["docker", "compose", "--project-name", "villagerai", "--profile", "hosted",
           "-f", "compose.yaml", "-f", "compose.aws.yaml"]


def hostname(value):
    value = value.lower()
    label = r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?"
    if (len(value) > 253 or not re.fullmatch(rf"{label}(?:\.{label})+", value)
            or value.rsplit(".", 1)[1].isdigit()):
        raise ValueError("Use a DNS hostname such as npc.example.com, without https:// or a path.")
    return value


def env_value(value):
    if not value or any(c in value for c in "\r\n\x00"):
        raise ValueError("Configuration values must be nonempty and single-line.")
    return json.dumps(value.replace("$", "$$"))


def write(path, content, mode=0o600):
    temporary = path.with_name(path.name + ".new")
    temporary.write_text(content)
    temporary.chmod(mode)
    temporary.replace(path)


def ask(prompt, pattern=None):
    value = input(prompt + ": ").strip()
    if not value or len(value) > 128 or (pattern and not re.fullmatch(pattern, value)):
        raise ValueError(f"Invalid {prompt.lower()}.")
    return value


def configure(root):
    env = root / ".env"
    grants = root / "scopes.local.json"
    if env.exists() and grants.exists():
        env.chmod(0o600)
        return
    if env.exists() or grants.exists():
        raise ValueError(f"Incomplete configuration in {root}; restore the missing .env or scopes.local.json.")
    print("First installation: enter the server credentials and player access.")
    app_id = ask("Meta Platform app ID", r"\d{1,30}")
    app_secret = getpass.getpass("Meta Platform app secret: ")
    model_key = getpass.getpass("Meta Model API key: ")
    players = ask("Meta player IDs (comma-separated)", r"\d{1,30}(?:\s*,\s*\d{1,30})*")
    world = ask("World ID (match Unity)")
    campaign = ask("Initial campaign ID (match Unity)")
    npcs = [value.strip() for value in ask("NPC IDs (comma-separated, match Unity)").split(",")]
    if not all(npcs):
        raise ValueError("NPC IDs must not be empty.")
    values = {
        "AUTH_MODE": "meta", "HOST": "0.0.0.0", "PORT": "8787",
        "NPC_MODEL_REASONING": "minimal", "META_APP_ID": app_id,
        "META_APP_SECRET": app_secret, "MODEL_API_KEY": model_key,
        "POSTGRES_PASSWORD": secrets.token_hex(24),
    }
    content = "".join(f"{key}={env_value(value)}\n" for key, value in values.items())
    scopes = [{"playerId": player.strip(), "worldId": world, "campaignId": campaign,
               "allowNewCampaigns": True, "npcIds": npcs} for player in players.split(",")]
    write(env, content)
    # Bind-mounted into a container running as the unprivileged node user.
    write(grants, json.dumps(scopes, indent=2) + "\n", 0o644)


def run(*args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def install_packages():
    run("apt-get", "update")
    run("apt-get", "install", "-y", "docker.io", "docker-compose-v2", "caddy", "curl", "cron",
        env={**os.environ, "DEBIAN_FRONTEND": "noninteractive"})
    run("systemctl", "enable", "--now", "docker", "caddy", "cron")


OVERRIDE = """services:
  postgres:
    restart: unless-stopped
    logging: &logs
      driver: json-file
      options:
        max-size: '10m'
        max-file: '3'
  service:
    restart: unless-stopped
    stop_grace_period: 45s
    logging: *logs
    healthcheck:
      test: ['CMD', 'node', '-e', "fetch('http://127.0.0.1:8787/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
      interval: 5s
      timeout: 3s
      retries: 12
      start_period: 30s
"""

BACKUP = """#!/bin/sh
set -eu
umask 077
cd /opt/villagerai/npc-service
mkdir -p backups
file="backups/$(date -u +%Y%m%dT%H%M%SZ).dump"
trap 'rm -f "$file.tmp"' EXIT
docker compose --project-name villagerai --profile hosted -f compose.yaml -f compose.aws.yaml exec -T postgres pg_dump -U villagerai -d villagerai -Fc > "$file.tmp"
mv "$file.tmp" "$file"
find backups -name '*.dump' -mtime +7 -delete
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("hostname", type=hostname, help="DNS name pointing to this EC2 instance")
    args = parser.parse_args()
    if os.geteuid() != 0:
        raise ValueError("Run with sudo on the EC2 instance.")
    release = Path("/etc/os-release").read_text()
    if not re.search(r'^ID=ubuntu$', release, re.M) or 'VERSION_ID="24.04"' not in release:
        raise ValueError("This installer requires Ubuntu 24.04 on a dedicated EC2 instance.")
    if CADDY.exists() and not CADDY.read_text().startswith(MARKER):
        raise ValueError("An unmanaged Caddyfile exists. Use a fresh instance; this script will not replace it.")
    source = Path(__file__).resolve().parent.parent
    for name in ("package.json", "package-lock.json", "Dockerfile", "compose.yaml", ".dockerignore", "src", "migrations"):
        if not (source / name).exists():
            raise ValueError(f"Missing {source / name}. Copy the complete Server/npc-service directory.")
    os.umask(0o077)
    INSTALL.mkdir(parents=True, exist_ok=True)
    INSTALL.chmod(0o700)
    configure(INSTALL)
    install_packages()
    if not CADDY.read_text().startswith(MARKER):
        write(CADDY, MARKER + CADDY.read_text(), 0o644)
    # Preserve the old stack's configuration until its database backup completes.
    if (INSTALL / "compose.aws.yaml").exists():
        run(*COMPOSE, "up", "-d", "--wait", "postgres", cwd=INSTALL)
        run("sh", str(INSTALL / "backup.sh"))
    if source != INSTALL:
        for name in ("package.json", "package-lock.json", "Dockerfile", "compose.yaml", ".dockerignore"):
            shutil.copyfile(source / name, INSTALL / name)
            (INSTALL / name).chmod(0o644)
        for name in ("src", "migrations"):
            if (INSTALL / name).exists():
                shutil.rmtree(INSTALL / name)
            shutil.copytree(source / name, INSTALL / name)
    # Docker COPY preserves permissions; source and migrations must be readable by node.
    for name in ("src", "migrations"):
        for path in (INSTALL / name).rglob("*"):
            path.chmod(0o755 if path.is_dir() else 0o644)
        (INSTALL / name).chmod(0o755)
    write(INSTALL / "compose.aws.yaml", OVERRIDE)
    write(INSTALL / "backup.sh", BACKUP, 0o700)
    run(*COMPOSE, "up", "-d", "--build", "--wait", "--wait-timeout", "180", cwd=INSTALL)
    candidate = CADDY.with_name("Caddyfile.new")
    write(candidate, MARKER + f"{args.hostname} {{\n    reverse_proxy 127.0.0.1:8787\n}}\n", 0o644)
    run("caddy", "validate", "--config", str(candidate), "--adapter", "caddyfile")
    candidate.replace(CADDY)
    run("systemctl", "reload", "caddy")
    write(Path("/etc/cron.d/villagerai-backup"),
          "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin\n"
          "15 3 * * * root /opt/villagerai/npc-service/backup.sh\n", 0o644)
    run("sh", str(INSTALL / "backup.sh"))
    url = f"https://{args.hostname}/health"
    for _ in range(12):
        result = subprocess.run(["curl", "--fail", "--silent", "--max-time", "5", url],
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if result.returncode == 0:
            print(f"Service reachable: https://{args.hostname}\n"
                  "Next: set the Unity service URL and test a real Quest conversation.\n"
                  "Database backups: /opt/villagerai/npc-service/backups (copy off the instance).")
            return
        time.sleep(5)
    raise ValueError("Containers started, but public HTTPS failed. Check DNS, ports 80/443 and journalctl -u caddy. Rerun after fixing them.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"Deployment failed: {error}", file=sys.stderr)
        sys.exit(1)
