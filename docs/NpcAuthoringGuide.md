# Authoring an NPC

Living guide • Updated 4 November 2026 • Current implementation: /main cs:138 (documentation through cs:140)

Use this guide to build and tune a character with the capabilities currently available. The Villager AI architecture tab defines the system boundary; historical design and event notes supply experience and content direction. This guide translates those ideas into authoring steps. It should grow as each capability becomes usable.

These recipes target the full Unity project managed in Plastic SCM. The Git submission contains selected hack-created code and assets, not a standalone Unity project. Some referenced base prefabs, BaseScene, GroKit and GenAI dependencies, and historical docs are intentionally excluded. The matching repository guide is docs/NpcAuthoringGuide.md; the NPC and server READMEs describe runtime behavior and service setup.

## What you can author now

The established path is a Unity character profile, a configured NPC prefab and Unity Behavior graphs. It supports authored first greetings, generated returning greetings with authored fallback, model replies, persistent dialogue/outcome history, configurable relationships, confirmed handoffs and validated model-proposed actions. VillageScene’s Mira, Osric, Flynn and Sorrel use shared runtime components.

Personality, facts, relationship criteria, action descriptions and reaction wording belong in profiles, relationship assets, graphs and quest configuration. Shared code supplies validation, persistence and provider behavior. Broader context/history inspection and author test tools remain US-09 work.

Current limits: model replies are non-streaming; actions are restricted to configured catalog entries and implemented effects; arbitrary held-object context, typed beliefs/suspicions, summary retrieval and durable offline journaling are not implemented. Generic repeated-handoff reactions still need explicit need-completion behavior; the delivery quest separately restores its own cup state.

## 1. Write a small character brief

Decide who the NPC is, how they speak, what they know, what they want, and what they must never claim or do. Include a first greeting, a returning greeting, a safe service-unavailable line, and one concrete interaction the player can discover. Keep the first version small enough to test in one short conversation.

Example brief: an innkeeper who speaks warmly in short sentences, knows the inn’s location, and needs a drinking cup. Asking how to help reveals the need. A confirmed cup handoff satisfies it; saying “I gave you a cup” does not. A contrasting second character might value a different topic or respond more reservedly. These are content examples, not new built-in character types.

## 2. Create the character profile

In Unity’s Project window, use Create → NPC → Character Profile. Create a separate NpcProfile asset for each distinct character and assign it to NpcCharacterDirector.profile. Assets/_Behavior/BarbarianProfile.asset and MiraProfile.asset are the current examples: a village protector and a hopeful delivery-business founder.

npcId: choose a stable, unique identity. Reusing another character’s ID in the same player/world/campaign scope risks sharing their history. Keep the ID when revising the same character; use a new ID for a different character. Renaming the asset alone does not change this identity.

personality: write role, temperament, motivations, and speaking instructions in this text field. There are not yet separate fields for all of these concepts. Prefer concrete instructions such as “Speak warmly and plainly in one or two short sentences.”

voice: supply a voice identifier supported by the configured speech adapter. Barbarian uses WIT$WIZARD; Mira uses WIT$COCKNEY ACCENT. The director applies the profile voice at startup; test changes after restarting Play Mode. Assigning a string does not install a new voice provider.

facts: include only authored knowledge this character is allowed to use. State needs and their satisfaction conditions clearly. The current cup example explicitly says that a completed gift in memory meets the need and a promise or conversational claim does not.

restrictions: set lore and conversational boundaries, including what the character does not know and how to answer unknown questions. Do not place forbidden secrets in facts and expect a restriction to hide them. These instructions guide dialogue; gameplay permissions still require game-side validation.

relationship: assign an optional NpcRelationshipDefinition asset to configure category scales, scoring rules and greetings. BarbarianRelationship.asset and MiraRelationship.asset are examples in Assets/_Behavior. Learned history and relationship progress belong to runtime persistence, not hand-edited profile facts.

### Unity recipe: Mira’s Character Profile

Select Assets/_Behavior/MiraProfile.asset in the Project window. To recreate the asset, use Create → NPC → Character Profile. Enter each value in its own Inspector field; the paths below use serialized field names, which Unity may display with spaces. These are the current Mira values.

npcId = mira

personality = Mira is a hopeful young adult who dreams of starting a village delivery business. Speak brightly and conversationally in one or two short sentences. Welcome encouragement, listen to practical caution, and show hurt when your ambition is belittled.

voice = WIT$COCKNEY ACCENT

facts → Size = 5

facts → Element 0 = You want to start a small delivery business in the village. You have not started it yet; it is an aspiration.

facts → Element 1 = You would like to begin with one safe local route and work with residents. Encouragement and useful ideas matter to you. Reveal this naturally when asked about yourself or your plans.

facts → Element 2 = Today is your birthday in this authored demonstration occasion. Mention it naturally if asked about your day or celebrations.

facts → Element 3 = The Oak Inn is beside the east gate and offers lodging for travelers.

facts → Element 4 = You have one cup to send to the Barbarian as a first delivery trial. It becomes available on the pickup table beside you only when the first_delivery quest is accepted. Accepted means the player can pick it up; completed means the Barbarian actually received it. An appeal that connects this short trial route to starting your business can persuade you when the action is available.

restrictions → Size = 4

restrictions → Element 0 = If first_delivery is absent from accepted campaign state, the delivery is not accepted and the cup is unavailable. Do not say it is on the table or ready to collect. When proposing grant_first_delivery, express intent, such as "I will put the cup out for you", rather than claiming it is already there. Only direct the player to collect it when accepted campaign state explicitly says first_delivery is accepted. If completed, the Barbarian has received it.

restrictions → Element 1 = Do not invent world facts. Say when you do not know.

restrictions → Element 2 = Conversation alone does not create a business, route, quest, gift, or completed delivery.

restrictions → Element 3 = You have no private knowledge of the other villager or their relationship with the player.

relationship = drag Assets/_Behavior/MiraRelationship.asset into this object-reference slot.

In BaseScene, select Mira_NPC. On NpcCharacterDirector, drag MiraProfile.asset into profile. Keep its existing speech reference. Restart Play Mode to apply the configured voice. If you duplicate Mira to make a different NPC, assign a different npcId rather than sharing mira.

## 3. Connect the character in the scene

VillageScene is the current demo; BaseScene remains a smaller authoring/test scene. Barbarian_NPC Variant.prefab and Mira_NPC.prefab share NPC_base.prefab under Assets/_Prefabs/NPCs. Duplicate or create a character variant deliberately, rebinding its references and unique identity while preserving speech, selection, caption and interaction wiring.

Check NpcCharacterDirector (profile and speech), NpcProxyClient (service connection), NpcMemory, and BehaviorGraphAgent. Bind graph Character references to the new NPC, and check Player, Interaction, and InteractionActive wiring. NpcProximityTrigger supplies presence and its interaction channel; it does not choose the character’s dialogue.

Keep the NavMeshObstacle disabled while the NavMeshAgent is enabled. The shared prefab now uses that configuration: enabling both made the avatars relocate to avoid their own carved holes. Use one campaign-reset control and one shared microphone input for the scene, rather than duplicating those controls for each NPC.

Use the same world and campaign for characters sharing a playthrough, with distinct npcId values. The runtime obtains the current campaign from NpcCampaign; editing a director’s campaign field is not a substitute for the campaign reset flow. A campaign reset starts fresh history across that world’s participating NPCs, so use it intentionally during testing.

Configure Node and PostgreSQL using Server/npc-service/README.md before testing generation or persistence. Dialogue credentials remain in the Node environment. The direct Unity Meta ASR prototype separately uses an ignored local credential resource bundled in local builds; it is not a public-release secret-distribution mechanism. Adding a personality or fact set requires authored data, not new shared Node code.

### Unity recipe: caption visibility

On NpcSpeechCaptionView, assign output to the character’s NpcSpeechOutput and display to its UI_TextDisplay. Keep the view outside the hidden text holder. The speech reference is resolved from the output’s GameObject when left empty.

hideAfterSpeaking = true and afterSpeakingDelay = 4 keep the caption visible for four seconds after observed speech stops. hideDelay = 1.5 keeps it visible for 1.5 seconds after the interaction ends. These are component defaults; check the scene or prefab’s serialized values when tuning a character. A new line replaces the caption and resets the after-speaking timer. Disabling NpcSpeechOutput clears it.

Player-visible check: hear a full line and remain nearby until the caption hides, then repeat while walking away. Also test interrupted and failed audio: captions appear when speech is requested and are not proof of successful playback.

## 4. Author the conversation behavior

Reuse Assets/_Behavior/NPC.asset and its Conversation subgraph for the same interaction pattern. Use per-character exposed values where available. If the flow itself must differ, create a separate graph/subgraph and assign it deliberately; changing a shared graph changes every character that uses it.

Tune FirstGreeting, ReturningGreeting, MemoryUnavailableGreeting, ReplyIntent, ReplySituation and FallbackText. The graph selects an authored default and optional relationship greeting after loading memory. If HasSpoken is false, the opening stays authored. If true, it attempts an NPC-initiated returning greeting; success replaces the authored text, while failure retains it. HasSpoken includes committed player input or completed NPC speech, including a previous greeting.

Keep dialogue inside NPC Interaction Scope. Load NPC Memory before generation and commit the exact finalized player input before Request NPC Response. Intent describes the desired response; Situation supplies permitted context. For returning greetings, edit the intent and situation on the greeting Request NPC Response node in Conversation.asset: it asks for a fresh brief greeting reflecting history and relationship, without treating an old player line as newly spoken.

### Unity recipe: player-reply Blackboard values and node bindings

Open Assets/_Behavior/Conversation.asset. The current Blackboard uses these values; edit the text values in the Blackboard, then bind the node fields to variables rather than typing variable names as literal text.

ReplyIntent (String) = Respond to the player's latest utterance in character.

ReplySituation (String) = The player is nearby and speaking to this character.

ReplyNpcInitiated (Boolean) = false

On Request NPC Response, bind:

Character → Character (GameObject)

Transcript → Transcript (String)

Response → Response (String)

RequestError → RequestError (String)

Intent → ReplyIntent (String)

Situation → ReplySituation (String)

NpcInitiated → ReplyNpcInitiated (Boolean)

Keep the player-reply sequence: Wait For NPC Transcript → Commit NPC Player Input → Request NPC Response. Run this within NPC Interaction Scope after Load NPC Memory. The Transcript value comes from the microphone/input path; do not replace it with your Intent text. Leave the existing failure and speech-delivery branches connected.

For NPC-initiated generation, enable NpcInitiated and supply an Intent with empty input. Wrap playback in Remember NPC Speech Outcome. The queued-world-event path takes priority over greeting, consumes its event only after success, and retries memory load, game-state load, generation and speech once each after 0.5 seconds. Speech retries reuse the generated text without repeating the action or model request. A failed/interrupted event remains available for a later encounter; the inbox is not durable.

A normal player reply uses one Meta Responses call for speech, relationship interpretation and an optional action, with configurable reasoning (minimal by default) and no token streaming. Node validates and commits authored relationship effects before replying. If that update unlocks a previously unoffered action and the reply selected none, RequestNpcResponse makes at most one extra call with the same committed input and updated actions. The first draft is held; second-call failure uses it without an action, while cancellation discards it. The input is not scored twice.

Record actual action results with the action-memory path. A proposed action or generated sentence is not proof of completion. NPC speech stores its caption text even when audio fails or is interrupted; delivery status does not identify a reliable spoken prefix. Pending commits are in-process and do not survive application termination.

## 5. Add a discoverable need and confirmed handoff

The US-04 cup interaction is the current reusable example. Put the NPC’s need and its completion meaning in the profile. Configure the receiver’s GroKit item/group filter, physical transfer UnityEvents, and recipient; add that receiver to the receiving NPC’s NpcHandoffInbox sources.

Author HandoffDescriptionTemplate and HandoffThanks in the Conversation blackboard. The description template supports {item}, {itemId}, and {groupId}. The current receiver confirms a drop-and-deactivate transfer before issuing its receipt; a trigger overlap or spoken claim is insufficient. A different physical transfer convention needs its own confirmation implementation.

Test physical transfer, the authored thank-you and later recall. Match the graph’s evidence key to the relationship action rule; need-met can award the configured cup effect without a model call. Model-requested gestures, movement and quest grants use the separate NpcActionCatalog/NpcActionRunner path and implemented NpcActionEffect bindings.

To author an action, configure NpcActionCatalog.actions with a unique id, targetId, description, optional relationship conditions and an enabled NpcActionEffect reference. Reuse an implemented effect and its Inspector fields; new gameplay effects need code. The shared response graph captures ActionProposal and runs it through the action runner. For quest-aware dialogue, assign catalog.gameState to the relevant NpcGameState; an effect’s quest reference alone does not add accepted state to the prompt. Test both eligible and ineligible states, cancellation and observed success/failure.

Generic handoff limitation: HandoffThanks is still an authored response and does not itself check prior need completion; relationship caps prevent repeated credit independently. Do not generalize the earlier reusable-mug test to the delivery quest: NpcDeliveryQuest restores its own accepted/completed cup state and received visual. Durable coordination of quest progress with the actor’s and witnesses’ memories is still missing.

### Unity recipe: house-fire outcomes and burn-down

VillageScene’s NpcHouseFireQuest owns the ladder and fire routes for Flynn. Assign its shared NpcGameState and stateKey, ranger, ladderTop, ladderBottom, fireLanding, ladderTween, ladderInteractCollider, houseFlames and optional headSmoke. Configure witnesses for villagers who receive confirmed fire-outcome memories. Keep their campaign scope aligned with the ranger’s.

Wire the physical ladder interaction to RaiseLadder and the confirmed lit-torch trigger to IgniteHouse. The conversation action uses NpcHouseFireDescendEffect to attempt DescendByLadder through the validated action path. Raising the ladder alone does not make Flynn climb; the accepted quest state and action eligibility still apply.

Under House burn-down, assign houseRoot explicitly to the house mesh hierarchy. If left empty, the component searches using houseObjectName, default House_01, near the flames. burnSeconds defaults to 14; burnedHeightScale defaults to 0.16; charredColor controls the darkened remains. The effect chars the house, breaks it into falling pieces and leaves rubble. Verify the chosen hierarchy and materials in the scene before tuning the effect.

The fire route accepts rescued_fire campaign state, records the ranger and configured witness outcomes, and queues the ranger’s authored reaction. It starts the collapse only after Flynn reaches fireLanding; a missing landing or a ranger more than two metres from it prevents the live collapse. This sequence does not make campaign state, memories and animation a single durable transaction.

Tune ladderRaisedDescription, fireDescription and witnessFireDescription as confirmed outcome text, and ladderReactionIntent and fireReactionIntent as instructions for the graph’s generated response. The integration reports the event; the Conversation graph chooses and delivers the reaction. Do not write descriptions that claim gameplay effects the integration has not confirmed.

Player-visible check: raise the ladder and persuade Flynn to descend; in a fresh campaign, use a lit torch and verify his jump precedes the collapse. Restart in the same campaign and check that rescued_fire restores the burned end state. Reset the campaign and check that the house returns to its original state and configured torches return unlit. Test the ranger’s later recall and witness recall separately from the visuals.

## 6. Author relationship categories, rules and greetings

Use Create → NPC → Relationship Definition and assign the asset to the profile’s relationship field. The Inspector exposes rules.categories, rules.rules, and greetings. There is no single standing score: category IDs are configurable data, and the shared runtime does not hardcode trust or any other category name.

Each category defines id, description, expression, initial, min and max. Description explains its meaning; expression tells the model how its current value should influence speech. The fixtures start all categories at zero: trust, respect and affection range from −100 to 100; fear and annoyance range from 0 to 100. Effects clamp to these bounds. There is no passive decay.

### Unity recipe: trust and affection category fields

Select Assets/_Behavior/MiraRelationship.asset and expand rules → categories. The existing array has Size = 5; keep the other category entries when editing these examples. To create a separate definition, use Create → NPC → Relationship Definition.

rules → categories → Element 0 → id = trust

rules → categories → Element 0 → description = Confidence that this player is dependable and sincere.

rules → categories → Element 0 → expression = With more trust, speak openly and confidently about relying on the player; with less, remain cautious. Do not grant permissions through dialogue.

rules → categories → Element 0 → initial = 0

rules → categories → Element 0 → min = -100

rules → categories → Element 0 → max = 100

rules → categories → Element 2 → id = affection

rules → categories → Element 2 → description = Personal warmth and fondness toward this player.

rules → categories → Element 2 → expression = With more affection, be warmer and pleased to see them; with less, be more reserved.

rules → categories → Element 2 → initial = 0

rules → categories → Element 2 → min = -100

rules → categories → Element 2 → max = 100

The remaining existing entries are Element 1 = respect, Element 3 = fear, and Element 4 = annoyance. The encouragement recipe below references trust and affection exactly. Changing initial configures the starting value; it does not reset a relationship already stored for the current campaign.

Each rule defines a stable id, source (dialogue or action), condition, meaning, effects, and repeat policy. Each effects entry names a category and an integer delta, so one accepted rule can affect several categories. Action rules also require an evidenceKey matching a confirmed gameplay outcome. Author a discoverable preference in the profile and a corresponding rule; writing “likes kindness” in personality alone does not define a persistent score change.

Conditions are semantic prose and can describe broad social criteria; authors do not need to enumerate every wording. For example, Mira’s business-encouragement rule accepts sincere support while excluding questions alone, ambiguity, sarcasm, negation and claims of completed work. Its effects are trust +10 and affection +5. Her practical-start rule accepts concrete constructive advice and gives trust +10, respect +5. Belittled-ambition gives trust −10, annoyance +10. These are editable fixture rules, not special runtime behaviors.

### Unity recipe: Mira’s encouragement rule

On MiraRelationship.asset, expand rules → rules → Element 0. The outer rules field is the configuration object; the inner rules field is its rule array. The existing rule array has Size = 4. Keep its other three entries.

id = business-encouragement

source = dialogue

condition = The player sincerely supports my hopes, confidence, or efforts, including my aspiration to start a delivery business. A question alone, ambiguous remarks, sarcasm, negation, or a claim of completed work is not encouragement. Use practical-start instead when they offer a concrete constructive plan.

meaning = The player offered sincere encouragement for my hopes and efforts.

evidenceKey = leave empty

effects → Size = 2

effects → Element 0 → category = trust

effects → Element 0 → delta = 10

effects → Element 1 → category = affection

effects → Element 1 → delta = 5

repeat → maxApplications = 10

repeat → cooldownSeconds = 300

Enter source as the lowercase text dialogue; it is a string field, not an enum. Paste the full condition into its text field without surrounding quotation marks. With this rule eligible, “I believe you can make your delivery business work” is a positive test; “Do you want to start a business?” should not count as encouragement. The model interprets the utterance; game logic applies the configured numbers.

repeat.maxApplications limits applications of that rule per player/world/campaign/NPC relationship; zero means unlimited. repeat.cooldownSeconds sets the minimum server-clock interval between accepted applications. Defaults are one application and no cooldown. The broad encouragement, advice and disparagement fixture rules allow ten applications with a 300-second cooldown. Birthday wishes and the protector’s cup-need rule allow one application. Event-ID deduplication remains independent of caps and cooldowns; retrying an event never creates another scoring opportunity.

A dialogue proposal names at most one eligible rule and quotes exact supporting text from the current committed utterance. The model never chooses category deltas. Game-owned Node logic checks the proposal, repeat limits, bounds and revision before atomically persisting effects. A spoken claim of a deed cannot earn an action-rule effect. For the authored birthday occasion, sincere wishes can give trust +10 and affection +10 once; there is no automatic birthday calendar or occasion rollover.

The internal model object contains speech, relationshipProposal and actionProposal. To abstain from a relationship effect, both ruleId and evidence are null. actionProposal may be null or {"actionId":"offered-id","targetId":"offered-target"}. Node validates the pair, and Unity revalidates current eligibility before execution. A relationship proposal alone does not authorize an action or an unconditional offer; speech must not claim unconfirmed completion.

### Unity recipe: the protector’s confirmed-cup rule and graph binding

Select Assets/_Behavior/BarbarianRelationship.asset. Expand rules → rules → Element 2, the existing cup-need entry. Enter or verify these fields:

id = cup-need

source = action

condition = A confirmed handoff meets my configured cup need.

meaning = The player helped by meeting my need for a drinking cup.

evidenceKey = need-met

effects → Size = 2

effects → Element 0 → category = trust

effects → Element 0 → delta = 20

effects → Element 1 → category = respect

effects → Element 1 → delta = 5

repeat → maxApplications = 1

repeat → cooldownSeconds = 0

In Conversation.asset, use the existing confirmed-handoff branch. Set its Blackboard strings:

HandoffDescriptionTemplate = The player gave me {item}.

HandoffThanks = Thank you. That's just what I needed.

On Read Item Handoff, bind Character → Character, DescriptionTemplate → HandoffDescriptionTemplate, Description → HandoffDescription, EventId → HandoffEventId, and ItemName → HandoffItemName.

On Remember NPC Action Outcome, bind Character → Character, Description → HandoffDescription, EventId → HandoffEventId, and MemoryError → MemoryError. Set literal Outcome = completed, EvidenceKey = need-met, and WaitForAcknowledgement = false, matching the existing handoff branch. The pending commit queue handles acknowledgement; do not manufacture a new event ID for a retry.

The evidenceKey in the asset and EvidenceKey on the graph node must match exactly. Reuse the existing receiver filter, drop-and-deactivate events and inbox wiring; these fields do not implement physical transfer. A model response or the words “I gave you a cup” must never trigger this confirmed-handoff branch.

Each authored greeting has priority, name, conditions and text. Conditions combine with AND using category, comparison (eq, ne, lt, lte, gt or gte) and value. Select Relationship Greeting picks the highest-priority match; ties retain asset order, unknown categories fail, and empty text retains DefaultGreeting. This is the first-encounter text and returning-generation fallback, not a guarantee of the exact words spoken on return.

Mira’s authored annoyed and wary entries both say “Oh. What is it?” Annoyed has priority 100 and annoyance >= 10; wary has priority 50 and trust < 0. Trusted has priority 40, trust >= 40 and annoyance < 10; friendly has priority 20, trust >= 20 and annoyance < 10. The neutral entry keeps the graph default. Returning-generation success replaces this selected line using current relationship and history.

### Unity recipe: Mira’s friendly greeting and selector node

Select MiraRelationship.asset. Keep greetings Size = 5 and expand Element 3, the existing friendly entry:

priority = 20

name = friendly

greeting = You came back! Your encouragement means a lot.

conditions → Size = 2

conditions → Element 0 → category = trust

conditions → Element 0 → comparison = gte

conditions → Element 0 → value = 20

conditions → Element 1 → category = annoyance

conditions → Element 1 → comparison = lt

conditions → Element 1 → value = 10

Enter comparison as lowercase text gte or lt, not the symbols >= or <. Both conditions must pass. Keep the other greeting entries: annoyed, wary, trusted and neutral.

In Conversation.asset, after memory loading and the existing first/returning default selection, bind Select Relationship Greeting as follows:

Character → Character (GameObject)

DefaultGreeting → Greeting (String)

Greeting → Greeting (String)

Band → RelationshipBand (String)

RelationshipLoaded → RelationshipLoaded (Boolean)

The existing selector uses Greeting for both default input and selected output. At trust = 20 and annoyance = 0 it selects friendly; annoyance = 10 selects the higher-priority annoyed band. The returning-generation branch may then replace the text before Speak Text. Relationship values are persisted runtime state, not fields to enter in the greeting asset.

Load NPC Memory restores history and accepted relationships without a model call. Read Relationship Category exposes a value, its presence and load status; fresh missing values use authored initials. Persisted relationship state includes category values, rule counts/times, evaluated event IDs and accepted experiences. NpcActionCatalog uses this accepted state to filter model-offered actions.

Mira and Osric demonstrate independent profiles and relationships; Flynn and Sorrel add authored quest interactions. Earlier verification records remain in docs/US05Verification.md and related test documents. This guide’s source update is not a fresh Editor or attended Quest playtest.

Memory scope remains limited: profile facts, confirmed outcomes, attributed player statements and accepted relationship experiences are distinguished. There are no separately typed beliefs or suspicions with confidence/revision rules, and no separate admission or expiry policy for temporary conversational observations. That broader checklist task remains open. Ordinary conversation must not silently become authoritative world truth.

## 7. Test the character as a player

Fresh encounter: approach, hear the intended authored greeting, select the NPC, and ask about an authored fact. Check audible voice, captions, speaking style, and return to listening.

Knowledge boundary: ask about an unknown fact or secret. The character should admit uncertainty rather than invent authority. Check that another NPC’s private knowledge is absent.

Continuity: converse or complete a configured handoff, leave and return, then restart in the same campaign. Verify relevant recall and a generated returning greeting; separately fail generation to verify the authored fallback. Do not require an exact friendly greeting string when generation succeeds.

Failure and interruption: leave during generation or speech, then return. Check that stale speech does not resume. With the service unavailable, check authored fallback without falsely claiming that prior history has been erased.

Relationship/action acceptance: compare NPC reactions, inspect accepted effects, and verify caps/cooldowns across restart. Cross an action’s relationship threshold and check same-turn reconsideration without duplicate scoring. Test second-call failure and cancellation. Repeated confirmed gifts must not bypass a rule cap. Validate physical quest restoration separately from generic handoff reactions.

### Example: a repeat-limit and restart test

- Start a deliberately fresh test campaign for Mira. Confirm trust = 0 and affection = 0. Say a clearly supportive line and inspect the accepted rule: if business-encouragement is selected, expect trust = 10, affection = 5 and applications = 1.

- Immediately repeat encouragement in a new interaction. While the 300-second cooldown is active, that rule should award no additional credit. A retry of the original event must also award none, regardless of elapsed time.

- After at least 300 seconds, offer new sincere encouragement. If accepted under the same rule, expect trust = 20, affection = 10 and applications = 2. Verify the authored selector yields friendly when annoyance is below 10; a successful generated returning greeting need not repeat that string. Restart in the same campaign and verify saved values and counts.

- Test the protector’s cup rule with a deliberately repeatable handoff fixture: the first accepted need-met event earns trust +20 and respect +5, while later distinct handoffs cannot exceed its one-application cap. Retry the same event to verify deduplication. For VillageScene’s delivery quest, verify completed state keeps the pickup cup unavailable after restart; do not expect that quest item to respawn for this repeat test.

Record whether a result came from an automated test, an Editor playthrough, or attended Quest use. Hearing the result on the target headset remains a separate check from code or asset inspection.

## Keep this guide current

Update the relevant authoring step whenever a story adds or changes an author-facing field, graph node, interaction, or inspection tool. Include where to edit it, one small example, its prerequisites, and a player-visible check. Replace provisional instructions when the capability is accepted, and update this tab’s date and status.

Available now: configured model-proposed actions and three quest implementations, direct Meta ASR, returning-greeting generation and bounded world-event retries. Remaining work includes held-object context (US-08), broader authoring/context inspection (US-09), typed memory/promise policy, retrieval/summarization, durable recovery, model streaming and emotion channels. Keep planned capabilities separate from current Inspector recipes.

Owning sources: NpcProfile.cs and NpcRelationshipDefinition.cs for profiles/rules; NpcActionCatalog.cs, NpcActionRunner.cs and implemented NpcActionEffect components for actions; RequestNpcResponse.cs for same-turn reconsideration; Conversation.asset for greetings and reactions; NpcMemory.cs/NpcGameState.cs for persistence. Historical verification documents and docs/UserStories.md provide evidence and remaining scope.



