# LORE / AI demo

A small browser client for the real NPC service. No Unity runtime, Unity asset files,
art packs, browser framework, or additional npm dependencies are required.

## Run locally

Start the NPC service and PostgreSQL using the parent README. Use `AUTH_MODE=local`
and a configured model key. Then, from `Server/npc-service`, in another terminal:

```sh
node --env-file=.env demo/server.js
```

Open **http://127.0.0.1:8788** (use this address, not `localhost`). The demo reads
the local access token on Node; neither that token nor the model key is sent to
the page. The proxy only listens on loopback and accepts same-origin JSON API
requests. It forwards to the existing service at `http://127.0.0.1:8787`.
Optional environment settings: `DEMO_PORT` and `DEMO_SERVICE_URL` (loopback HTTP only).
No restart or changes to the existing service are needed.

## Try it

1. Encourage Mira’s woodworking and offer to help with her first delivery. At trust 15, ask her to put the cup out. Her eligible proposal executes automatically; collect the cup, switch to Osric, and deliver it. A cup that is not available or held cannot be delivered.
2. Switch to Sorrel. Promise to use smoke without harming the wasps and ask for
   smokebombs. The real model can select an authored relationship rule and propose
   `give_smokebombs`. When trust is at least 15, the proposal automatically places them on the table. Then pick one up and use it at the tree.
3. Raising Flynn’s ladder, burning his house, and smoking or burning Sorrel’s nest automatically trigger LLM reactions after confirmed outcomes are saved. Osric automatically gives the Unity-authored thank-you on cup delivery. Compare the relationship evidence and history.
4. In a new campaign, pick up and light a torch, then burn the nest instead, or enable **Simulate action failure**.
   Failed actions record failed outcomes without changing campaign state or
   applying successful-action relationship effects.
5. Reload the page or switch NPCs: history persists and campaign state is shared.
   Greet again to exercise a generated returning greeting. A first greeting is authored.
6. Expand the request log to inspect the actual request/response sequence and revisions.

Eligible NPC proposals start before speech on the same turn, matching Unity’s action/speech ordering. Player action buttons execute immediately. The browser reloads accepted state, checks the exact proposed action and target plus current authored conditions, then performs a text-only simulation and stores an observed outcome. An ineligible proposal makes no world change. Game-state and memory commits are separate transactions; an unfinished
operation remains pending so its second write can be resumed. A lost acknowledgement
retries the exact saved event ID and payload. Local storage retains the campaign,
installation writer ID, and pending operation. Use one tab at a time. Clearing local
storage starts a fresh installation/campaign; it does not delete database history.

## Coverage and substitutions

Real paths: memory load/commit, input-before-generation, Meta model context and
structured replies, relationship assessment/evidence, validated action proposals,
observed outcomes, campaign state, writer leases, revisions, persistent history,
and NPC-initiated dialogue. All five `/v1/` endpoints are exercised.

The page replaces Behavior Graph orchestration and physical gameplay with a small
authored JavaScript scenario. Profiles and relationship definitions are snapshots of the Unity-authored Osric, Sorrel, Mira, and Flynn configuration, with browser-osric, browser-sorrel, browser-mira, and browser-flynn IDs. All four characters use Unity-authored relationship greeting tables and relationship rules. First greetings select the highest-priority matching band, using the graph/prefab greeting when the selected text is empty. Returning greetings use the Unity-authored generation instructions. Object transitions are browser simulations. Flynn’s ladder must be raised before he can propose descending. The snapshot is intentionally standalone; Unity edits must be copied over explicitly. They live in
`profiles.js`; generic client orchestration lives in `client.js`.

Dictation uses microphone capture and Muse Voice Transcribe through the local
demo server, with the same 24 kHz PCM protocol as the Unity integration. Press
**Dictate**, speak, then **Finish dictation** (30-second maximum). The server sends
the clip to Meta using `MODEL_API_KEY`; review and send the resulting transcript
yourself. Audio is held in memory and is not written to disk. The server replays
the clip at real-time speed, so transcription can take roughly the clip's duration.
Read-aloud uses browser speech synthesis, not Wit TTS.
Text mode records completed delivery after displaying the text. Voice mode records
completed only after playback ends, or interrupted/failed on cancellation/error.
It does not prove that the player heard the speech. Reload during voice playback
leaves an interrupted receipt to resume.

Meta Platform identity, entitlement, Quest input, navigation, animation, and spatial
physics are not exercised. Local authentication uses the service's `local-player`;
the separate `browser-demo` world, `browser-*` NPC IDs, and fresh campaign UUID isolate headset progress. The v2 browser storage namespace starts a fresh campaign and leaves prior demo history intact.
Health means the service is reachable, not that model credentials are valid.
Model interactions use the configured real API and its normal quota.

## Verify

```sh
node --test test/demo*.test.js
npm test
```

The demo is local-only. Do not expose this credential-bearing companion proxy over
a tunnel or deploy it as a public web app. A hosted demo needs its own identity flow.

## Unity interaction mapping

Fresh campaigns start with Flynn; restoring an existing campaign keeps its selected character. Switching characters represents approaching them and triggers their greeting. First arrival also greets automatically. Returning greetings never execute model-proposed actions, matching Unity’s greeting subtree. Flynn reacts to a raised ladder without automatically agreeing to descend; that still needs a valid model proposal. Fire makes him jump and react. Sorrel reacts to both smoke and fire. Cup delivery notifies Mira’s memory and gives Osric’s authored handoff thanks. House-fire news reaches Osric and Mira; nest news reaches Osric, Mira, and Flynn, matching VillageScene’s witness lists. Witnesses remember these events but do not all speak at once.

Reaction intents and witness routing are scenario data; the generic client persists outcome, witness receipts, and then a resumable reaction turn. Failed actions do not trigger successful-event reactions or witness news. Extra reactions are available only under debug controls.

The browser still substitutes buttons for grabbing, walking, and trigger collisions, and compresses movement, smoke timers, and the house’s burn animation. It is a text simulation, not execution of Unity’s Behavior Graph. Browser speech synthesis remains a substitute for Unity’s voice provider.

## Additional parity checks

The effective Unity action catalogs are mirrored, including wave/cheer, Osric’s quiet-spot/home walks, Mira’s pickup-table/quiet-spot/home walks, and the exact quest action IDs, targets, and persuasion descriptions. The browser represents animations with completed action receipts and walking with named positions. It assumes the authored animator and navigation configuration can execute; it does not run NavMesh or animation-layer validation.

Only currently permitted actions are offered. If accepted relationship evidence unlocks an action during a player turn and no action was initially selected, the same input/turn is reconsidered once; only the final reply is delivered. A failed reconsideration uses the first draft. Ordinary generation failures use Unity’s authored fallback; returning-greeting failures use the selected relationship greeting. World-event generation retries once automatically before leaving a resumable operation. Memory-write failures remain resumable.

NPC actions start before speech, silent actions produce no empty speech receipt, and rejected proposals record a failed action receipt. Tool pickups update simulated physical state without inventing NPC memories. Burning requires a held, lit torch; using smoke requires a collected smokebomb. Cup delivery and quest outcomes use the authored memory descriptions. Flynn’s roof wording retains the user correction that he chose to be there.

Remaining substitutions are explicit: buttons replace physical grabbing/collisions/proximity, timed movement and burning are compressed, gestures are textual, speech uses browser voices rather than Wit voices, and local authentication replaces headset identity. Snapshots must be refreshed when Unity authoring changes.
