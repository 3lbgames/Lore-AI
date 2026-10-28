# NPC interaction

The Barbarian prefab starts an interaction when the player enters `NpcProximityTrigger.interactionRadius`. Set `SpeechText` on the Behavior Graph Agent’s Blackboard to choose what it says. Staying outside range for `departureGraceSeconds` (one second by default) cancels the interaction; entering again after that starts it again. Brief boundary crossings preserve the current speech. After speech finishes, the NPC keeps facing the player while they remain nearby, without repeating the line.

`Assets/_Behavior/NPC.asset` receives the interaction event and runs `Conversation.asset`. That subgraph wraps `FaceInteractingPlayer`, `SpeakText`, and `WaitForInteractionEnd` in `FacePlayerDuringInteraction`, which restores the previous facing settings and smoothly turns back to the saved pose when the interaction ends. The wait node keeps that scope open after speech until departure is confirmed. The return uses the facing component’s turn duration and can be interrupted by a new interaction. `SpeakText` stops its own playback when it ends. The proximity trigger only publishes the player and interaction state; it contains no facing or speech behavior.

`SpeakText` accepts a speaker and arbitrary text. It uses `BaseAI_TextToSpeech` for playback, activity, and cancellation. The current prefab uses Wit; the graph node has no Wit dependency or greeting-specific behavior. Wit service access is required; a Node server and language model are not.

When the speaker has a `UI_TextDisplay`, `SpeakText` shows the spoken text in that display and hides it when speech completes or is cancelled.

The graphs use Unity Behavior 1.0.16 and can be edited in Unity's Behavior window.

Run the `NpcInteraction.Tests` EditMode assembly in Unity Test Runner. Its `LiveWit` test enters Play Mode with the actual prefab and checks approach, facing, departure cancellation, repeat interaction, and return to idle. It requires working Wit configuration and service access. Test playback is muted and the previous listener volume is restored afterward.
