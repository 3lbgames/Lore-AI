# NPC service

Node 22 or later. No third-party runtime dependencies. The service assembles character context and calls Meta's Responses API; Unity owns selection, permitted knowledge, voice, and gameplay. This first conversation has no persistent history or gameplay actions.

## Local Editor

1. Copy `.env.example` to `.env` and `scopes.example.json` to `scopes.local.json` if they do not exist.
2. Put the Meta Model API key in `.env` as `MODEL_API_KEY=your-key`. This key stays on Node. Do not put it in an Inspector, ScriptableObject, APK, or chat.
3. Set a random `LOCAL_ACCESS_TOKEN` in `.env`. Put that same token in the ignored project file `UserSettings/NpcService.local.json`: `{"url":"http://127.0.0.1:8787","token":"your-local-token"}`. The implementation setup creates these local files once.
4. From this directory, run `npm ci`, then `npm start`.
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

Set `AUTH_MODE=meta`, `HOST=0.0.0.0`, and `META_APP_ID` / `META_APP_SECRET` in the server `.env`. These are the Meta Platform app credentials, distinct from `MODEL_API_KEY`. Configure the same app in Unity's Meta Platform settings and use an entitled test account. Add its app-scoped user ID to `scopes.local.json`, with exact world, campaign, and NPC grants. The client obtains a fresh user proof for every request; Node validates it with Meta before checking scope.

Set `NpcProxyClient.serviceUrl` to the development computer's LAN address on port 8787. A headset's `127.0.0.1` refers to the headset. Allow that port through the computer firewall only on your trusted development network. Local bearer authentication is deliberately unavailable over LAN and is not compiled into a release player build. For an HTTP development endpoint, Unity's Player Settings must permit HTTP downloads in development builds. Use HTTPS before traffic crosses an untrusted network.

## Version 1 contract

`POST /v1/responses`, JSON, at most 64 KiB:

- `version: 1`
- `sessionId`, `interactionId`, `turnId`: client correlation strings
- `worldId`, `campaignId`, `npcId`: exact granted scope
- `input`: unchanged finalized transcript
- `profile`: `npcId`, `personality`, `voice`, `facts` and `restrictions` string arrays
- `situation`: currently permitted observation text
- `actions: []`: dialogue only
- `identity: {userId, nonce}`: required for Meta authentication; local mode uses its fixed development identity

A successful response echoes the three correlation IDs with `version: 1`, `status: "completed"` and `text`. Errors have `status: "failed"` and an `error` code: invalid input (400), authentication (401), scope (403), body size (413), busy/rate limit (429), provider failure (502), missing model key (503), or deadline (504). Provider response bodies and credentials are never echoed in errors. The deadline covers identity validation and generation. Closing the client connection aborts upstream work where supported.

Unity must reject obsolete correlation IDs and cancel playback on departure. Generated text is not a receipt of delivered speech, and this service does not store it as history.

## Provider reference

The adapter uses `muse-spark-1.3`, nonstreaming complete replies, and `store: false`. It sends no previous response ID, tool definitions, or speculative generation settings. See Meta's [Responses endpoint](https://dev.meta.ai/docs/api-reference/responses/create-response), [schemas](https://dev.meta.ai/docs/api-reference/responses/schemas), and [user proof verification](https://developers.meta.com/vr/documentation/native/ps-ownership/).
