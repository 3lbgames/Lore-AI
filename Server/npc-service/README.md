# NPC service

Node 22 or newer and PostgreSQL 17. Runtime dependencies are locked in package-lock.json. The service assembles character context and calls Meta's Responses API; Unity owns selection, permitted knowledge, voice, and gameplay execution. PostgreSQL stores conversation history, relationship state, and campaign game state. The model can propose an action from Unity's supplied options; a proposal is not proof that the action happened.

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

The server can run from this submission. Unity Editor and Quest instructions
assume the full Unity project, including the excluded GroKit, voice adapters,
SDKs, and project settings.

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

Before each headset build, confirm the local service is healthy, establish `adb reverse tcp:8787 tcp:8787`, and verify an actual HTTP request from the headset to `http://127.0.0.1:8787/health` succeeds. Repeat after build/install and after reopening Unity. A forwarding-table entry or a successful computer-side health check alone does not verify headset connectivity. The USB script provisions the connection and restarts the app; it does not perform this health check. If `run-as` reports that the package is not debuggable, install a Development Build and rerun the script. If local authentication fails, confirm the token in `UserSettings/NpcService.local.json` matches `.env`, then rerun the script to refresh the app's copy.

## Quest controls / LAN authentication

VillageScene is the first enabled build scene; BaseScene is also enabled. Approach an NPC and wait for the greeting to finish. Hold the right controller's index trigger while speaking, then release it to finish recording. `NpcSpeechControls` defaults to the rig's right-hand `InputXR` Interact binding and drives `NpcSpeechInput`; its controller and button are configurable in the Inspector. Accept the headset's microphone permission prompt. The Editor and standalone player also provide a **Talk to NPC** button followed by **Finish speaking** when an NPC is selected.

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
- `profile`: `npcId`, `personality`, and `voice` strings; `facts` and `restrictions` string arrays; optional `relationship` configuration described below
- `situation`: currently permitted observation text
- `actions`: up to 32 options, each with exactly `id`, `targetId`, and `description` strings. IDs must be unique; IDs and target IDs are at most 128 characters and descriptions at most 2000. Use `[]` for dialogue only.
- `identity: {userId, nonce}`: required for Meta authentication; local mode uses its fixed development identity

A successful response echoes the three correlation IDs with `version: 1`, `status: "completed"`, `text`, `actionProposal`, and `revision`, plus `relationship` when available. `actionProposal` is null or `{actionId,targetId}` matching one supplied option. Unity validates and executes the proposal and records the observed outcome separately. Use the returned revision for subsequent requests: relationship assessment can advance it even though generation does not commit speech.

Errors have `status: "failed"` and an `error` code: invalid input (400), authentication (401), scope (403), revision/event/writer/state conflicts or uncommitted input (409), body size (413), context limit (422), busy/rate limit (429), provider failure or invalid model output (502), missing model key or unavailable storage (503), or deadline (504). Provider response bodies and credentials are never echoed in errors. The default 30-second deadline covers identity validation and request processing. Closing the client connection aborts upstream work where supported. Concurrent work on the same NPC stream (or campaign game-state stream) returns `request_in_progress`; the default service-wide limit is 16 active requests (`service_busy`).

Unity must reject obsolete correlation IDs and cancel playback on departure. The response-generation endpoint does not store returned text as speech history. Unity records the speech attempt separately, with its audio-delivery status; generation alone is not a receipt of delivered speech.

## Provider reference

The adapter uses `muse-spark-1.3`, strict JSON Schema, `reasoning.effort: minimal`, `stream: false`, and `store: false`. One call returns speech, a relationship proposal, and an optional action proposal. `NPC_MODEL_REASONING` accepts `minimal`, `low`, `medium`, `high`, `xhigh`, or `max`; restart Node after changing it. There is no second interpretation call. The server validates relationship proposals and commits accepted category effects before returning speech. Generic instructions live in [src/prompts/interaction.txt](src/prompts/interaction.txt); character criteria and category expression guidance arrive in authored profile configuration. See Meta's [Responses endpoint](https://dev.meta.ai/docs/api-reference/responses/create-response), [schemas](https://dev.meta.ai/docs/api-reference/responses/schemas), and [user proof verification](https://developers.meta.com/vr/documentation/native/ps-ownership/).


## Persistent memory contract (US-03)

`POST /v1/memory/load` takes version, all three correlation IDs, worldId,
campaignId, npcId, writerId, and the same authentication as responses. It returns
`{version:1,status:"completed",sessionId,interactionId,turnId,revision,hasSpoken,history}`,
plus `relationship` when stored.
History is ordered by commit revision. `hasSpoken` means an acknowledged finalized
player input or completed NPC speech exists. A completed greeting or reaction counts
even if the player never speaks; interrupted/failed NPC speech and action outcomes
alone do not count. The field name is retained for existing clients.

`POST /v1/memory/commit` additionally takes `expectedRevision` and
`event: {eventId,turnId,kind,text,delivery,evidenceKey?}` and optional top-level
`relationship` configuration. `kind` is `player_input`,
`npc_speech`, or `action_outcome`; `delivery` is `completed`, `interrupted`, or `failed`.
Player input must be completed (up to 8000 characters). NPC speech text is up
to 12000 characters. The Unity client retains the full NPC line for completed,
interrupted, and failed speech attempts. The delivery status describes audio
playback; stored text does not prove that the player heard the line or identify
the spoken portion. The API also accepts empty text for interrupted/failed speech.
Action outcomes retain a nonempty observed description (up to 12000 characters)
for all three statuses. An optional `evidenceKey` (up to 128 characters) identifies
an authored action rule; a nonempty key is permitted only on action outcomes.
Only completed outcomes with a matching key can apply action relationship effects.
Record what actually happened, not a requested action or
a model prediction. Queued/generated speech must not be committed as completed. A successful commit
returns the immutable receipt revision and relationship state when available. Retry the identical event ID and payload
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
before replying to it, and commit speech when its playback attempt ends. All responses require
the current revision. Player replies additionally require matching committed turn
input (`input_not_committed`, 409); NPC-initiated requests require an intent and
empty input, and never manufacture a player utterance. Node sends committed
player input and nonempty recorded NPC speech with user/assistant roles, including
each input once. NPC speech text is included even when its audio delivery was
interrupted or failed; those speech delivery statuses are stored but are not
attached to the assistant-role text sent to the model. Action outcomes are
separate context records with their actual status, including failed and
interrupted actions. Generation never commits speech. Provider context overflow returns `context_limit` (422); history
is never silently trimmed. A failed generation leaves committed events intact.
Player statements remain dialogue, not witnessed facts.

Reset campaign generates a new UUID and loads that separate history. Prior
campaign history stays stored. Full crash/offline journaling and retention are
not part of US-03. Both local and Meta authentication create campaigns on first
use under the authenticated player identity; no local grant or allowlist is required.

## Relationship configuration and state

Supply optional `profile.relationship` on response requests and top-level
`relationship` on memory commits. Both use `{categories,rules}`:

- `categories`: 1–50 entries with unique `id`, `description`, `expression`, and integer `initial`, `min`, `max`. Bounds must contain the initial value; each number has absolute value at most 10000.
- `rules`: up to 50 entries with unique `id`, `source` (`dialogue` or `action`), `condition`, `meaning`, and nonempty `effects: [{category,delta}]`. Effects reference distinct configured categories and integer deltas with absolute value at most 10000. Action rules also require `evidenceKey`.
- Optional rule `repeat: {maxApplications,cooldownSeconds}` defaults to one application with no cooldown. Zero `maxApplications` means unlimited; the maximum is 10000. Cooldowns range from zero to 31536000 seconds.

IDs and action evidence keys are nonempty strings up to 128 characters;
descriptions, expressions, conditions, and meanings are nonempty strings up to
2000 characters. The implementation is in [src/relationships.js](src/relationships.js).

For committed player input, the model may select one eligible dialogue rule
with a nonempty verbatim evidence substring from that input, or propose no
change. The server validates the proposal, applies bounded effects, and records
that the input was evaluated. NPC-initiated speech does not reevaluate prior
player input. Completed action outcomes use their evidence key to select the
first matching action rule; dialogue alone cannot establish a completed action.
Repeat limits and cooldowns apply to both sources.

Relationship state contains `values` (`{category,value}` entries), `ruleUsage`,
`evaluatedEventIds`, and `experiences` recording accepted evidence and effects.
Dialogue assessment advances the NPC stream revision, including assessments
that apply no effect. Retrying an already evaluated input does not apply its
effects again. Behavior Graphs and authored configuration choose reactions;
the persistence layer records accepted events and relationship changes.

## Campaign game-state contract

`POST /v1/game/load` uses the same version, correlation IDs, scope, writer ID,
and authentication as memory load. `npcId` is required by request validation,
but storage is shared across NPCs within the authenticated player's world and
campaign. The response includes `gameState: {revision,values}`, where `values`
is an array of `{key,value}` strings. A new campaign starts at revision zero
with an empty array. Game-state revisions are independent of NPC memory revisions.

`POST /v1/game/commit` additionally requires the game-state `expectedRevision`
and exactly `mutation: {eventId,key,expected,value}`. Event IDs and keys are
nonempty strings up to 128 characters; expected and replacement values may be
empty and are at most 2000 characters. A missing key compares as an empty string.
The server checks both revision and expected value, writes the replacement,
and returns the new `gameState` with revision incremented and values sorted by key.
Mismatches return `revision_conflict` or `state_conflict` (409).

Retrying the same event ID and identical mutation returns the current game state,
even with a stale expected revision. Reusing the ID with a changed mutation
returns `event_conflict` (409). The mutation and receipt are transactional.
Game-state operations renew the same 120-second campaign writer lease used by
NPC memory; another installation receives `writer_conflict` until it expires.
Unity owns the meaning and transitions of these values; the server stores them
without choosing gameplay behavior.

## Tests

`npm test` runs service/provider tests. Set `TEST_DATABASE_URL` to a PostgreSQL 17
test database to also run real transaction/restart/isolation tests. Test rows use
unique player identities. `docker compose --profile hosted up --build` runs the
same migrations and PostgreSQL major with Meta authentication; supply platform
credentials externally. Local bearer mode runs Node on the host loopback only.

Deployment-script tests run separately with
`python3 -m unittest discover -s scripts -p 'test_*.py'` (Windows:
`py -3 -m unittest discover -s scripts -p 'test_*.py'`).
