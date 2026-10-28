# NPC interaction

The Barbarian prefab starts an interaction when the player enters `NpcProximityTrigger.interactionRadius`. Set `SpeechText` on the Behavior Graph Agent’s Blackboard to choose what it says. Staying outside range for `departureGraceSeconds` (one second by default) cancels the interaction; entering again after that starts it again. Brief boundary crossings preserve the current speech. After speech finishes, the NPC keeps facing the player while they remain nearby, without repeating the line.

`Assets/_Behavior/NPC.asset` receives the interaction event and runs `Conversation.asset`. That subgraph keeps greeting and conversation inside `FacePlayerDuringInteraction`, which restores the previous facing settings and smoothly turns back to the saved pose when the interaction ends. The conversation loop keeps that scope open until departure is confirmed. The return uses the facing component’s turn duration and can be interrupted by a new interaction. `SpeakText` stops its own playback when it ends. The proximity trigger only publishes the player and interaction state; it contains no facing or speech behavior.

`SpeakText` accepts a speaker and arbitrary text. It uses `BaseAI_TextToSpeech` for playback, activity, and cancellation. The current prefab uses Wit; the graph node has no Wit dependency or greeting-specific behavior. Greeting playback requires Wit access. Answering a question additionally requires the Node service and model access.

When the speaker has a `UI_TextDisplay`, `SpeakText` shows the spoken text in that display and hides it when speech completes or is cancelled.

The graphs use Unity Behavior 1.0.16 and can be edited in Unity's Behavior window.

Run the `NpcInteraction.Tests` EditMode assembly in Unity Test Runner. Its `LiveWit` test enters Play Mode with the actual prefab and checks approach, facing, departure cancellation, repeat interaction, and return to idle. It requires working Wit configuration and service access. Test playback is muted and the previous listener volume is restored afterward.

## Profile conversation

The Barbarian now has an `NpcCharacterDirector`, `NpcProxyClient`, and a `BarbarianProfile` asset. Edit the profile's personality, permitted facts, restrictions, fallback and voice preset in `Assets/_Behavior/BarbarianProfile.asset`. `NpcWitVoiceBinding` applies that voice preset to the existing Wit speaker.

The Conversation graph greets once, then repeats **Wait For NPC Transcript → Request NPC Response → Speak Text**. Transcript and Response are transient Blackboard strings. The request node submits once and stays running until completion or failure. Departure cancels the request and playback through the existing graph scope. Generated text is kept out of conversation history; persistence is a later story.

The scene's `NPC Speech Input` uses the current Wit SDK through `NpcWitTranscription`. In the Editor, enter Play Mode, approach the Barbarian, wait for its greeting, click **Talk to NPC**, and ask “Where can I get lodging for the night?”. Click **Finish speaking** or let Wit finalize the utterance. On Quest, hold the right index trigger to record and release to finish; the controller and `InputXR` button are configurable on `NpcSpeechInput`. One NPC owns the active conversation until departure; gameplay may explicitly call `SelectForConversation` to choose another nearby NPC. A recording captures its recipient and interaction at its start, and late transcripts are discarded after departure or selection change.

Start the local service using [the service instructions](../../../../Server/npc-service/README.md). Provider credentials belong only in `Server/npc-service/.env`. Editor connection settings live in ignored `UserSettings/NpcService.local.json`; player builds use Meta Platform user proof instead.

`NpcConversationPlayTests` covers real-model Wit playback and departure cancellation, plus unavailable-server recovery and single-recipient routing. Real-model tests require the local service and Wit access. They mute the listener during automated playback and restore the previous scene, listener volume, and background-execution setting.

The headset displays recording and processing status below the center of view, with partial recognized words during capture. The Wit configuration disables unused intent-attribute dispatch to avoid an assembly scan during Quest startup. For USB testing without a Meta Platform app, use the development-only setup in `Server/npc-service/README.md`.
