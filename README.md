# VillagerAI

VillagerAI is a Unity Quest project for voice-driven interactions with NPCs in a virtual village. Players can talk to characters, build relationships, and take part in quests. Characters respond using their authored personalities, remembered conversations, and events they have observed in the game.

The project combines generated dialogue with authored gameplay. Meta models interpret speech and generate character responses; Unity Behavior Graphs and game code control when characters interact and what actions they can perform. This repository contains the hackathon submission's server and selected Unity integration files.

## Features

- **Voice conversations:** push-to-talk speech recognition, generated NPC dialogue, spoken replies, and captions.
- **Character-specific behavior:** authored personalities, knowledge, restrictions, voices, and relationship criteria shape each NPC's responses.
- **Persistent memory:** conversation history and confirmed action outcomes carry across interactions and application restarts within a campaign.
- **Evolving relationships:** authored rules apply relationship changes based on dialogue evidence and completed gameplay events.
- **Gameplay interaction driven behavior and memory:** NPCs remember confirmed item handoffs, action outcomes, and quest events, including the village’s wasp-nest and house-fire scenarios, and use that history in later conversations. Unity controls gameplay and reports what happened; the AI can propose available actions for Unity to validate and execute.
- **Proximity-driven interactions:** approaching a character starts an interaction; greetings and reactions can occur without a new player utterance.
- **Separate player campaigns:** authenticated player and campaign scopes keep persistent state separate, with a campaign reset starting a fresh history.
- **Local and hosted operation:** develop with a local Node/PostgreSQL service and Quest USB forwarding, or connect to a hosted service over HTTPS.

Meta's models run in the cloud. Unity captures microphone audio, controls NPC behavior, executes gameplay actions, and plays speech. A separate Node service calls Muse Spark and stores persistent state in PostgreSQL.

## Where each model runs

| Stage | Model or service | Caller | Where processing happens |
| --- | --- | --- | --- |
| Speech recognition | Muse Voice Transcribe, `muse-voice-transcribe-1.0` | Unity on the Quest or in the Editor | Meta cloud; microphone capture and transcript handling happen in Unity |
| Dialogue and proposals | Muse Spark, `muse-spark-1.3` | Node NPC service | Meta cloud; context assembly, validation, and persistence happen on the server |
| Speech synthesis | Wit.ai TTS through Meta Voice SDK | Unity | Wit.ai service; audio playback happens in Unity |

The TTS integration selects voice IDs through `TTSSpeaker`; it does not specify a Muse model ID. Meta Platform user-proof verification is also part of the connection, but it is authentication rather than model inference.

```mermaid
flowchart TD
    Speech[Player speech] --> ASR[Meta cloud: Muse Voice Transcribe]
    ASR -->|Transcript| Unity[Unity: NPC Behavior Graph]
    PlayerActions[Player world-affecting actions] --> Events[Observed world events and item handoffs]
    Events --> Unity
    Triggers[Proximity and NPC triggers] --> Unity

    Unity -->|Context and dialogue request| Server[Node NPC service]
    Server <-->|Generate speech and proposals| Spark[Meta cloud: Muse Spark]
    Server <-->|Load and persist state| DB[(PostgreSQL memory and game state)]
    Server -->|Validated response| Response[Unity: handle NPC response]

    Response -->|Speech| SDK[Meta Voice SDK]
    SDK --> TTS[Wit.ai speech synthesis]
    TTS --> Playback[Unity: play NPC voice]
    Response -->|Action proposal| Actions[Unity: validate and execute NPC actions]
    Actions -->|World changes| Events

    Unity -.->|Commit accepted player input| Server
    Actions -.->|Record confirmed outcome| Server
    Playback -.->|Record speech delivery| Server
```




World events and authored NPC triggers can start a turn without player speech.
The Behavior Graph chooses when to react and supplies the event's intent and
observations to the model. NPC action proposals pass through Unity's validation
and execution; their observed effects can trigger further NPC reactions.
Memory commits record accepted input and confirmed outcomes without themselves
requesting another model response. Dotted arrows show persistence requests. Player input is committed before its
dialogue request; the Behavior Graph controls action and speech ordering. Authored greetings can use
configured text without a Muse Spark request.

## A conversation turn

1. **Unity captures speech.** The player holds the talk control. `MetaStreamingSpeechToText` streams 24 kHz PCM audio directly to `wss://api.meta.ai/v1/asr/realtime`. `MetaAsrSession` requests `muse-voice-transcribe-1.0` in push-to-talk mode and receives partial and final transcripts. This audio does not pass through the NPC server.
2. **Unity commits the accepted input.** NPC code associates the final transcript with the current interaction and stores it through the server's memory API before requesting a reply. Obsolete interactions are rejected.
3. **The server requests dialogue.** Unity sends the character profile, permitted observations, authored intent, available action options, and current memory revision to `/v1/responses`. The server loads conversation and relationship context and calls `https://api.meta.ai/v1/responses` with `muse-spark-1.3`.
4. **The server validates the result.** A single model call returns speech, a relationship proposal, and an optional action proposal. The server checks proposed actions against Unity's supplied options and validates relationship evidence against committed input. It applies accepted relationship effects before returning the response. Requests use a strict JSON schema, non-streaming output, and minimal reasoning by default.
5. **Unity performs the response.** Behavior Graphs and authored data control the interaction. Unity executes accepted gameplay actions and sends speech text to Wit.ai TTS through the Meta Voice SDK. The headset plays the resulting audio; the Node service does not synthesize or play it.
6. **Unity records what happened.** Completed speech and observed action outcomes are committed separately. Generated dialogue is not treated as delivered speech, and an action proposal is not treated as a completed action. PostgreSQL retains the accepted events for future turns.

Graph-initiated greetings and reactions can start at the dialogue-request step without microphone input. They carry an authored intent and an empty player-input field.

## Code map

| Location | Responsibility |
| --- | --- |
| `Assets/_Scripts/NPC/NpcProxyClient.cs` | Unity HTTP transport to the NPC service, local bearer authentication, and Meta Platform user proofs |
| `Assets/_Scripts/NPC/NpcProfile.cs` | Character profile and shared server request/response types |
| `Assets/_Scripts/NPC/NpcMemory.cs` and `NpcGameState.cs` | Unity clients for persistent NPC memory and campaign state |
| `Assets/_Scripts/NPC/` | NPC selection, interaction flow, action execution, speech coordination, and Behavior Graph nodes |
| `Assets/GenAI/MetaASR/` | Direct Unity-to-Meta speech recognition: `MetaStreamingSpeechToText.cs` and `MetaAsrSession.cs` |
| `Assets/GenAI/MetaWitt.AI/WittTTS/Witt_TextToSpeech.cs` | Wit.ai speech synthesis and playback integration used by the NPC base prefab |
| [Server/npc-service/src/meta.js](Server/npc-service/src/meta.js) | Muse Spark requests and Meta Platform identity verification |
| [Server/npc-service/src/prompts/interaction.txt](Server/npc-service/src/prompts/interaction.txt) | Shared model instructions; character-specific context comes from Unity |
| [Server/npc-service/src/service.js](Server/npc-service/src/service.js) | API routing, validation, authentication, and response coordination |
| [Server/npc-service/src/memory.js](Server/npc-service/src/memory.js) | PostgreSQL persistence, revisions, writer leases, and event receipts |

## Running the service

See the [NPC service README](Server/npc-service/README.md) for Node/PostgreSQL setup, Quest USB development, Meta authentication, AWS deployment, and API contracts.

For local development, Node runs on the development computer and the Quest reaches it through ADB reverse forwarding. For hosted use, Unity connects to an HTTPS service endpoint. In both cases, Muse inference remains in Meta's cloud.

The server's `MODEL_API_KEY` stays on the server. Direct ASR uses a separate Unity-side credential resource (`MetaAsrKey.private`), and Wit.ai TTS uses its own configuration. Meta Platform app credentials and user proofs authenticate access to the NPC server; they are separate from model-request credentials. Credential values are not part of the submission.

## Submission scope

This Git repository is a hackathon submission view of a larger Unity project managed in Plastic SCM. Its ignore rules include the server and selected Unity asset folders, while excluding the main GroKit library, `Assets/GenAI`, and other project dependencies. The direct ASR and TTS implementation paths above describe the full working project; those files are currently excluded from this Git submission.

The submission is therefore not a standalone Unity project. The Unity integration depends on GroKit, Meta Platform and Voice SDKs, Unity Behavior, and the full project's scene and package configuration.
