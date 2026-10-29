# NPC service

Node 22 and PostgreSQL 17. Runtime dependencies are locked in package-lock.json. The service assembles character context and calls Meta's Responses API; Unity owns selection, permitted knowledge, voice, and gameplay. Conversation history persists in PostgreSQL; gameplay actions remain out of scope.

## Local Editor

1. Copy `.env.example` to `.env` and `scopes.example.json` to `scopes.local.json` if they do not exist.
2. Put the Meta Model API key in `.env` as `MODEL_API_KEY=your-key`. This key stays on Node. Do not put it in an Inspector, ScriptableObject, APK, or chat.
3. Set a random `LOCAL_ACCESS_TOKEN` in `.env`. Put that same token in the ignored project file `UserSettings/NpcService.local.json`: `{"url":"http://127.0.0.1:8787","token":"your-local-token"}`. The implementation setup creates these local files once.
4. Set `POSTGRES_PASSWORD` and matching `DATABASE_URL` using `.env.example`. Run `docker compose up -d postgres`, then `npm ci` and `npm start`. Startup applies versioned migrations transactionally. The named volume preserves history across container restarts.
5. Unity's proxy client reads those local settings in the Editor only. Local authentication accepts loopback connections and grants only `local-player` the scopes listed in `scopes.local.json`.

Run `npm test` for service and adapter tests. `GET /health` reports service availability, not provider readiness.

## Quest / USB development

For local headset testing without a registered Meta Platform app:

1. Build and install an Android **Development Build**. Permit HTTP downloads for development in Player Settings.
   Keep **Development Build** checked in Unity when using Build And Run again. A release rebuild excludes the local connection code and attempts Meta Platform authentication, even if Node is running and USB forwarding is active.
2. Keep Node running with the same local configuration used by the Editor.
3. Connect the headset by USB and allow USB debugging.
4. From this directory run `python3 scripts/connect-quest.py`.

The script copies the local service token into the development app's private files using `adb run-as`, forwards headset port 8787 to the Mac, and restarts the app. It never reads or copies the model API key. Keep USB connected; rerun the script after reconnecting. Release builds do not read this file and still require Meta authentication. No local-token access is enabled over LAN.

## Quest controls / LAN authentication

In BaseScene, approach the NPC and wait for the greeting to finish. Hold the right controller's index trigger while speaking, then release it to finish recording. `NpcSpeechInput` uses the rig's `InputXR` Interact binding; its controller and button are configurable in the Inspector. Accept the headset's microphone permission prompt. The Editor also provides a **Talk to NPC** button followed by **Finish speaking**.

Set `AUTH_MODE=meta`, `HOST=0.0.0.0`, and `META_APP_ID` / `META_APP_SECRET` in the server `.env`. These are the Meta Platform app credentials, distinct from `MODEL_API_KEY`. Configure the same app in Unity's Meta Platform settings and use an entitled test account. Add its app-scoped user ID to `scopes.local.json`, with exact world and NPC grants. `allowNewCampaigns: true` additionally grants UUID campaign IDs for that player/world/NPC set, enabling Reset campaign. Existing exact campaign grants remain supported. The client obtains a fresh user proof for every request; Node validates it with Meta before checking scope.

Set `NpcProxyClient.serviceUrl` to the development computer's LAN address on port 8787. A headset's `127.0.0.1` refers to the headset. Allow that port through the computer firewall only on your trusted development network. Local bearer authentication is deliberately unavailable over LAN and is not compiled into a release player build. For an HTTP development endpoint, Unity's Player Settings must permit HTTP downloads in development builds. Use HTTPS before traffic crosses an untrusted network.

## Version 1 contract

`POST /v1/responses`, JSON, at most 64 KiB:

- `version: 1`
- `sessionId`, `interactionId`, `turnId`: client correlation strings
- `worldId`, `campaignId`, `npcId`: exact granted scope
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
not part of US-03. Persistent campaigns require `allowNewCampaigns: true` in the
matching local grant; do not grant wildcard players, worlds or NPCs.

`npm test` runs service/provider tests. Set `TEST_DATABASE_URL` to a PostgreSQL 17
test database to also run real transaction/restart/isolation tests. Test rows use
unique player identities. `docker compose --profile hosted up --build` runs the
same migrations and PostgreSQL major with Meta authentication; supply platform
credentials externally. Local bearer mode runs Node on the host loopback only.
