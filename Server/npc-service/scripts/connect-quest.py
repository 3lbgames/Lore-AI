#!/usr/bin/env python3
"""Provision USB-only NPC access into an installed Android development build."""
import json
from pathlib import Path
import shlex
import shutil
import subprocess

project = Path(__file__).resolve().parents[3]
adb = shutil.which("adb") or str(Path.home() / "Library/Android/sdk/platform-tools/adb")
package = "com.ThreePoundGames.VillagerAI"
settings = json.loads((project / "UserSettings/NpcService.local.json").read_text())
if settings.get("url") != "http://127.0.0.1:8787" or not isinstance(settings.get("token"), str) or not settings["token"].strip():
    raise SystemExit("Configure the local Editor service URL and token first.")

def run(*args, **kwargs):
    return subprocess.run([adb, *args], check=True, **kwargs)

# run-as fails for release builds. Never copy credentials into shared storage.
run("shell", "run-as", package, "mkdir", "-p", "files")
command = shlex.join(["run-as", package, "sh", "-c", "umask 077; cat > files/NpcService.local.json"])
run("shell", command, input=json.dumps({"url": settings["url"], "token": settings["token"]}).encode())
run("reverse", "tcp:8787", "tcp:8787")
run("shell", "am", "force-stop", package)
run("shell", "am", "start", "-n", package + "/com.unity3d.player.UnityPlayerGameActivity")
print("Quest connected over USB. Keep the cable connected and the local node server running.")
