# NPC service

Node 22 and PostgreSQL 17. Runtime dependencies are locked in package-lock.json. The service assembles character context and calls Meta's Responses API; Unity owns selection, permitted knowledge, voice, and gameplay. Conversation history persists in PostgreSQL; gameplay actions remain out of scope.

## AWS: one EC2 instance

Prerequisites: a registered Quest app in the Meta Horizon Developer Dashboard,
its **App ID** and **App Secret** from **your app → API**, and a separate
**Meta Model API key**. Use the same registered app in Unity's Meta Platform
settings. The App Secret lets the server verify player identity; the Model API
key authenticates model requests. See [Meta's server-to-server documentation](https://developers.meta.com/vr/documentation/native/ps-s2s-basics/).

Create a dedicated **Ubuntu 24.04 x86-64** EC2 instance (start with `t3.small`
and 30 GB encrypted storage). Choose a region near the players. Assign an
Elastic IP and point a DNS hostname at it. Allow inbound TCP 80/443 publicly
and SSH 22 from your IP only. Keep ports 8787 and 5432/54329 closed to the internet.

Copy `Server/npc-service` to the instance, excluding `node_modules`, `.env`
and legacy `*.local.json` files. From the repository root on macOS/Linux:

```sh
tar --exclude=node_modules --exclude='.env*' --exclude='*.local.json' \
  --exclude=__pycache__ -czf /tmp/npc-service.tar.gz -C Server npc-service
scp -i /path/to/key.pem /tmp/npc-service.tar.gz ubuntu@SERVER_IP:~/
ssh -i /path/to/key.pem ubuntu@SERVER_IP
```

On the instance:

```sh
tar -xzf npc-service.tar.gz
sudo python3 npc-service/scripts/deploy-aws.py npc.your-domain.com
```

The installer prompts once for the Meta app ID, app secret and Model API key.
It installs Ubuntu's Docker/Compose and Caddy packages, builds
the app, enables automatic restarts, configures HTTPS, and creates nightly
database backups. It runs on an existing instance; it does not create AWS
resources or DNS records. An existing unmanaged Caddy installation is rejected.

The deployment lives at `/opt/villagerai/npc-service`. The root-only `.env`
is preserved on reruns. To change model/app credentials, edit it with
`sudoedit` and rerun the installer. Keep `POSTGRES_PASSWORD` unchanged;
changing it in `.env` alone does not rotate the password in an existing database.
No model key goes into Unity.

Set `NpcProxyClient.serviceUrl` to the HTTPS hostname and test with an entitled
Meta account for the configured app. Remove the USB development override or use a
release build. Verify a real conversation, memory after a server reboot and
response time with three players. The installer checks HTTPS reachability;
it does not verify Meta credentials or model responses.

### Updates and backups

Upload a fresh source archive, remove only the previous **upload directory**
(`~/npc-service`), extract the new archive, and rerun the same command. Keep
`/opt/villagerai/npc-service` and its Docker volume intact. Updates take a
database backup before applying migrations and briefly interrupt service.

Nightly dumps run at 03:15 in the server's timezone and are retained locally
for approximately seven days in `/opt/villagerai/npc-service/backups`. Copy
them off the instance; local backups cannot recover from instance/disk loss.
Test restoring a dump into a separate PostgreSQL 17 database with `pg_restore`
before relying on it. Never run `docker compose down -v` on this deployment.

Inspect the service:

```sh
sudo sh -c 'cd /opt/villagerai/npc-service && docker compose --project-name villagerai --profile hosted -f compose.yaml -f compose.aws.yaml ps'
sudo journalctl -u caddy --no-pager -n 50
```

The server stays on between sessions and uses minimal model reasoning. Measure
transcription, model-request and speech-startup times before increasing instance
size; current model responses are non-streaming.

## Local Editor

### Prerequisites: macOS

- Install Node.js 22 or newer (including npm).
- Install and start [OrbStack](https://docs.orbstack.dev/docker/) or Docker Desktop. OrbStack provides the Docker engine and supports the same `docker compose` commands below. PostgreSQL runs in a container; Node runs directly on your Mac.
- Open Terminal in the project's `Server/npc-service` directory.

### Prerequisites: Windows

- Install Node.js 22 or newer (including npm) for Windows.
- Install and start [Docker Desktop with the WSL 2 backend](https://docs.docker.com/desktop/setup/install/windows-install/), using Linux containers.
- Open PowerShell in the project's `Server/npc-service` directory. Run Node and the USB setup from Windows, rather than inside WSL, for this workflow. PostgreSQL runs in Docker Desktop.
- If PowerShell blocks `npm.ps1`, use `npm.cmd` in place of `npm` in the commands below.

On either platform, check that `node --version`, `npm --version`, and `docker compose version` work before continuing.

### First-time configuration (both platforms)

1. Copy `.env.example` to `.env` if it does not exist.
2. Put the Meta Model API key in `.env` as `MODEL_API_KEY=your-key`. This key stays on Node. Do not put it in an Inspector, ScriptableObject, APK, or chat.
3. Keep `AUTH_MODE=local`, `HOST=127.0.0.1`, and `PORT=8787`. Set a random `LOCAL_ACCESS_TOKEN` in `.env`.
4. Create the ignored file `UserSettings/NpcService.local.json` under the Unity project root if missing, with the same token: `{"url":"http://127.0.0.1:8787","token":"your-local-token"}`.
5. Set a URL-safe `POSTGRES_PASSWORD` and the matching password in `DATABASE_URL`, following `.env.example`. The local database address is `127.0.0.1:54329`.

Unity reads the project settings file in the Editor. Android Development Builds can read a copy provisioned into the app's private storage by the USB script below. Local authentication requires the local token and a loopback connection, using `local-player` as the development identity. Unity supplies world, campaign and NPC IDs; no scope allowlist or server registration is required. Existing `scopes.local.json` files are ignored. Release builds require Meta Platform authentication.

### Start the service (both platforms)

With OrbStack or Docker Desktop running, execute these commands from `Server/npc-service`:

```sh
docker compose up -d --wait postgres
npm ci
npm start
```

`--wait` waits for PostgreSQL to become healthy before Node starts. Run `npm ci` on first setup and after dependency changes; subsequent starts only need the Docker command and `npm start`. Startup applies versioned migrations transactionally. The named volume preserves history across container restarts.

Keep the Node terminal open. Open [the health endpoint](http://127.0.0.1:8787/health) to confirm the service is reachable. Stop Node with Ctrl+C; `docker compose stop postgres` stops the database without removing its stored history.

Run `npm test` for service and adapter tests. `GET /health` reports service availability, not provider readiness.

## Quest / USB development

For local headset testing without a registered Meta Platform app:

Install Python 3 and [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools) on the development computer. Add the SDK's `platform-tools` directory to your `PATH`, then open a new terminal and confirm `adb version` works. Unity's installed Android SDK also contains these tools. The script searches `PATH` first, then falls back to `~/Library/Android/sdk/platform-tools/adb` on macOS; Windows requires `adb.exe` on `PATH`.

1. Build and install an Android **Development Build**. Permit HTTP downloads for development in Player Settings.
   Keep **Development Build** checked in Unity when using Build And Run again. A release rebuild excludes the local connection code and attempts Meta Platform authentication, even if Node is running and USB forwarding is active.
2. Keep Node running with the same local configuration used by the Editor.
3. Enable Developer Mode on the Quest, connect it with a USB data cable, and accept the headset's USB debugging prompt. Run `adb devices`; the headset should show as `device`, not `unauthorized`. Connect only the intended device for this script. If Windows does not detect it, install the Meta Quest ADB driver.
4. In a second terminal, open `Server/npc-service` and run the command for your platform:

   **macOS:**

   ```sh
   python3 scripts/connect-quest.py
   ```

   **Windows PowerShell:**

   ```powershell
   py -3 scripts/connect-quest.py
   ```

   If your Windows Python installation provides `python` instead of `py`, use `python scripts/connect-quest.py`.

The script copies the local service token into the development app's private files using `adb run-as`, forwards headset port 8787 to port 8787 on the development computer, and restarts the app. It never reads or copies the model API key. Keep USB connected; rerun the script after reconnecting. Release builds do not read this file and still require Meta authentication. No local-token access is enabled over LAN.

After setup, run `adb reverse --list` and confirm a `tcp:8787 tcp:8787` entry, and check the computer's health endpoint again. If `run-as` reports that the package is not debuggable, install a Development Build and rerun the script. If local authentication fails, confirm the token in `UserSettings/NpcService.local.json` matches `.env`, then rerun the script to refresh the app's copy.

## Quest controls / LAN authentication

In BaseScene, approach the NPC and wait for the greeting to finish. Hold the right controller's index trigger while speaking, then release it to finish recording. `NpcSpeechInput` uses the rig's `InputXR` Interact binding; its controller and button are configurable in the Inspector. Accept the headset's microphone permission prompt. The Editor also provides a **Talk to NPC** button followed by **Finish speaking**.

Set `AUTH_MODE=meta`, `HOST=0.0.0.0`, and `META_APP_ID` / `META_APP_SECRET` in the server `.env`. These are the Meta Platform app credentials, distinct from `MODEL_API_KEY`. Configure the same app in Unity's Meta Platform settings and use an entitled test account. The client obtains a fresh user proof for every request; Node validates it with Meta. Unity supplies world, campaign and NPC IDs, and the server creates storage on first use under the verified player identity. No scope configuration file is required. Players cannot read or write another player’s state.

Set `NpcProxyClient.serviceUrl` to the development computer's LAN address on port 8787. A headset's `127.0.0.1` refers to the headset. Allow that port through the computer firewall only on your trusted development network. Local bearer authentication is deliberately unavailable over LAN and is not compiled into a release player build. For an HTTP development endpoint, Unity's Player Settings must permit HTTP downloads in development builds. Use HTTPS before traffic crosses an untrusted network.

## Version 1 contract

`POST /v1/responses`, JSON, at most 64 KiB:

- `version: 1`
- `sessionId`, `interactionId`, `turnId`: client correlation strings
- `worldId`, `campaignId`, `npcId`: Unity-provided storage scope within the authenticated player
- `writerId`: persistent installation ID, unchanged on application restart
- `expectedRevision`: acknowledged stream revision
- `npcInitiated`: `false` (default) for a player reply; `true` for graph-initiated speech
- `input`: unchanged committed transcript for a reply; empty string for NPC-initiated speech
- `intent`: graph-authored dialogue instruction (required for NPC-initiated speech, up to 8000 characters)
- `profile`: `npcId`, `personality`, `voice`, `facts` and `restrictions` string arrays
- `situation`: currently permitted observation text
- `actions: []`: dialogue only
- `identity: {userId, nonce}`: required for Meta authentication; local mode uses its fixed development identity

A successful response echoes the three correlation IDs with `version: 1`, `status: "completed"` and `text`, plus `revision`. Errors have `status: "failed"` and an `error` code: invalid input (400), authentication (401), scope (403), body size (413), busy/rate limit (429), provider failure (502), missing model key (503), or deadline (504). Provider response bodies and credentials are never echoed in errors. The deadline covers identity validation and generation. Closing the client connection aborts upstream work where supported.

Unity must reject obsolete correlation IDs and cancel playback on departure. Generated text is not a receipt of delivered speech, and this service does not store it as history.

## Provider reference

The adapter uses `muse-spark-1.3`, strict JSON Schema, `reasoning.effort: minimal`, `stream: false`, and `store: false`. One call returns speech, a relationship proposal and a null action proposal. `NPC_MODEL_REASONING` overrides effort; there is no second interpretation call. Game logic validates and commits category effects before returning speech. Generic instructions live in `src/prompts/interaction.txt`; character criteria and category expression guidance arrive in authored profile configuration. See [US-05 configuration and boundaries](../../docs/US05Implementation.md), Meta's [Responses endpoint](https://dev.meta.ai/docs/api-reference/responses/create-response), [schemas](https://dev.meta.ai/docs/api-reference/responses/schemas), and [user proof verification](https://developers.meta.com/vr/documentation/native/ps-ownership/).


## Persistent memory contract (US-03)

`POST /v1/memory/load` takes version, all three correlation IDs, worldId,
campaignId, npcId, writerId, and the same authentication as responses. It returns
`{version:1,status:"completed",sessionId,interactionId,turnId,revision,hasSpoken,history}`.
History is ordered by commit revision. `hasSpoken` means an acknowledged finalized
player input exists; greetings alone do not count.

`POST /v1/memory/commit` additionally takes `expectedRevision` and
`event: {eventId,turnId,kind,text,delivery}`. `kind` is `player_input`,
`npc_speech`, or `action_outcome`; `delivery` is `completed`, `interrupted`, or `failed`.
Player input must be completed (up to 8000 characters). Completed NPC speech is
up to 12000 characters. Interrupted/failed speech records metadata with empty
text, because the current speech adapter cannot confirm the spoken portion.
Action outcomes retain a nonempty observed description (up to 12000 characters)
for all three statuses. Record what actually happened, not a requested action or
a model prediction. Queued/generated speech must not be committed as completed. A successful commit
returns the immutable receipt revision. Retry the identical event ID and payload
following a lost acknowledgement: it returns its original receipt even if the
expected revision is stale. Changed payloads return `event_conflict` (409).

Load and commit implement the server-side `IVillagerMemoryStore` contract in
`src/memory.js`. They atomically acquire/renew a 120-second writer lease on the
player/world/campaign branch; the same installation can resume immediately.
Another installation receives `writer_conflict` (409) until lease expiry.
Per-character revisions reject stale new commits (`revision_conflict`, 409).
Both the event and deduplication receipt commit in one transaction. Missing or
unavailable storage fails explicitly; there is no in-memory runtime fallback.

Unity graphs load memory before generating dialogue, commit finalized player input
before replying to it, and commit speech after delivery. All responses require
the current revision. Player replies additionally require matching committed turn
input (`input_not_committed`, 409); NPC-initiated requests require an intent and
empty input, and never manufacture a player utterance. Node sends completed
dialogue with user/assistant roles, including each input once. Action outcomes
are separate context records with their actual status, including failed and
interrupted actions. Undelivered speech text is excluded. Generation never
commits speech. Provider context overflow returns `context_limit` (422); history
is never silently trimmed. A failed generation leaves committed events intact.
Player statements remain dialogue, not witnessed facts.

Reset campaign generates a new UUID and loads that separate history. Prior
campaign history stays stored. Full crash/offline journaling and retention are
not part of US-03. Local authentication requires `allowNewCampaigns: true` in the matching
local grant for campaign reset. Meta authentication creates campaigns on first use.

`npm test` runs service/provider tests. Set `TEST_DATABASE_URL` to a PostgreSQL 17
test database to also run real transaction/restart/isolation tests. Test rows use
unique player identities. `docker compose --profile hosted up --build` runs the
same migrations and PostgreSQL major with Meta authentication; supply platform
credentials externally. Local bearer mode runs Node on the host loopback only.
