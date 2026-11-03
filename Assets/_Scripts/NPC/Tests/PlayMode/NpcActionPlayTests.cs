#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class NpcActionPlayTests
{
    NpcPlayFixture fixture;
    string worldId;
    GameObject npc, player;
    Component director, effect, runner, speech, memory;
    static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    static object Get(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static object Field(object o, string name) => o.GetType().GetField(name).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
    bool Accepting() => (bool)Get(director, "AcceptingTranscript");
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture = new NpcPlayFixture();
        player = new GameObject("Action test player", typeof(Camera), typeof(AudioListener)); player.tag = "MainCamera";
        player.transform.position = new Vector3(2, 1.7f, 0);
        npc = fixture.InstantiateNpc();
        Set(npc.GetComponent("NpcProximityTrigger"), "playerOverride", player.transform);
        foreach (var c in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (c && c.GetType().Name == "PlayerAwareness") Set(c, "checkTargetOverride", player.transform);
        director = npc.GetComponent("NpcCharacterDirector"); memory = npc.GetComponent("NpcMemory");
        worldId = "action-test-" + Guid.NewGuid().ToString("N");
        Set(director, "worldId", worldId);
        effect = npc.AddComponent(Runtime("NpcTestActionEffect"));
        var catalog = npc.GetComponent("NpcActionCatalog") ?? npc.AddComponent(Runtime("NpcActionCatalog"));
        runner = npc.GetComponent("NpcActionRunner") ?? npc.AddComponent(Runtime("NpcActionRunner"));
        if (!npc.GetComponent("NpcActionMemory")) npc.AddComponent(Runtime("NpcActionMemory"));
        var binding = Activator.CreateInstance(Runtime("NpcActionBinding"));
        Set(binding, "id", "gesture"); Set(binding, "targetId", "self"); Set(binding, "description", "React with a gesture."); Set(binding, "effect", effect);
        var actions = Array.CreateInstance(binding.GetType(), 1); actions.SetValue(binding, 0); Set(catalog, "actions", actions);
        fixture.Service.ActionId = "gesture"; fixture.Service.ActionTarget = "self";
        fixture.Activate(npc);
        speech = npc.GetComponentInChildren(Runtime("NpcTestSpeech"), true);
        yield return Wait(() => Accepting(), "greeting");
    }
    void RequireTrust(int threshold)
    {
        var profile = UnityEngine.Object.Instantiate((ScriptableObject)Field(director, "profile"));
        Set(director, "profile", profile);
        var relationship = ScriptableObject.CreateInstance(Runtime("NpcRelationshipDefinition"));
        var config = JsonUtility.FromJson("{\"categories\":[{\"id\":\"trust\",\"initial\":0,\"min\":-100,\"max\":100}]}", Runtime("NpcRelationshipConfig"));
        Set(relationship, "rules", config); Set(profile, "relationship", relationship);
        ownedProfile = profile; ownedRelationship = relationship;
        var binding = ((Array)Field(npc.GetComponent("NpcActionCatalog"), "actions")).GetValue(0);
        var condition = JsonUtility.FromJson("{\"category\":\"trust\",\"comparison\":\"gte\",\"value\":" + threshold + "}", Runtime("NpcRelationshipCondition"));
        var conditions = Array.CreateInstance(condition.GetType(), 1); conditions.SetValue(condition, 0);
        Set(binding, "conditions", conditions);
        fixture.Service.ProposeOnlyOfferedActions = true;
        fixture.Service.TrustAfterPlayerResponse = 15;
    }
    ScriptableObject ownedProfile, ownedRelationship;
    [Serializable] class ResponseRequest { public string turnId, input; public int expectedRevision; public OfferedAction[] actions; }
    [Serializable] class OfferedAction { public string id; }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator NewlyUnlockedActionRunsInTheSameTurnWithoutSpeakingTheDraft()
    {
        RequireTrust(15);
        fixture.Service.HoldFollowUp = true;
        fixture.Service.PlayerReply = "Undelivered draft.";
        int starts = (int)Field(speech, "Starts"); Submit();
        yield return Wait(() => fixture.Service.PlayerResponseRequests == 2, "newly unlocked action reconsidered");
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts), "first draft is not spoken");
        Assert.That((int)Field(effect, "Starts"), Is.Zero);
        ResponseRequest first, second;
        lock (fixture.Service.PlayerRequests)
        {
            first = JsonUtility.FromJson<ResponseRequest>(fixture.Service.PlayerRequests[0]);
            second = JsonUtility.FromJson<ResponseRequest>(fixture.Service.PlayerRequests[1]);
        }
        Assert.That(first.actions, Is.Empty);
        Assert.That(second.actions.Single().id, Is.EqualTo("gesture"));
        Assert.That(second.turnId, Is.EqualTo(first.turnId));
        Assert.That(first.input, Is.EqualTo("That is wonderful news!"));
        Assert.That(second.input, Is.EqualTo(first.input));
        Assert.That(History().events.Count(e => e.kind == "player_input"), Is.EqualTo(1));
        // Release the held HTTP response with the final reply and offered action.
        fixture.Service.ReleaseHeldResponse("I'll do that.", "gesture", "self");
        yield return Wait(() => (int)Field(effect, "Completions") == 1 && Accepting() && !(bool)Get(memory, "IsBusy"), "one action and final reply completed");
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts + 1));
        Assert.That(History().events.Count(e => e.kind == "npc_speech" && e.text == "I'll do that."), Is.EqualTo(1));
        Assert.That(History().events.Any(e => e.text == "Undelivered draft."), Is.False);
        Assert.That(fixture.Service.PlayerResponseRequests, Is.EqualTo(2));
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator RelationshipChangeWithoutUnlockUsesOneCall()
    {
        RequireTrust(30); Submit();
        yield return Wait(() => History().events.Any(e => e.kind == "player_input") && Accepting(), "reply settled");
        Assert.That(fixture.Service.PlayerResponseRequests, Is.EqualTo(1));
        Assert.That((int)Field(effect, "Starts"), Is.Zero);
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator AlreadyAvailableActionUsesOneCall()
    {
        RequireTrust(0); Submit();
        yield return Wait(() => (int)Field(effect, "Completions") == 1 && Accepting(), "available action settled");
        Assert.That(fixture.Service.PlayerResponseRequests, Is.EqualTo(1));
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator FailedReconsiderationUsesDraftAndKeepsActionAvailableNextTurn()
    {
        RequireTrust(15); fixture.Service.FailFollowUp = true;
        fixture.Service.PlayerReply = "Let me fetch those.";
        int starts = (int)Field(speech, "Starts"); Submit();
        yield return Wait(() => fixture.Service.PlayerResponseRequests == 2 && Accepting() && !(bool)Get(memory, "IsBusy"), "draft fallback settled");
        Assert.That((int)Field(effect, "Starts"), Is.Zero);
        Assert.That(History().events.Count(e => e.kind == "npc_speech" && e.text == "Let me fetch those."), Is.EqualTo(1));
        Assert.That((string)Field(speech, "LastText"), Is.EqualTo("Let me fetch those."));
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts + 1));
        Assert.That(History().events.Count(e => e.kind == "player_input"), Is.EqualTo(1));

        fixture.Service.FailFollowUp = false;
        fixture.Service.PlayerReply = "I'll get them now."; Submit();
        yield return Wait(() => (int)Field(effect, "Completions") == 1 && Accepting() && !(bool)Get(memory, "IsBusy"), "unlocked action available on next turn");
        Assert.That(fixture.Service.PlayerResponseRequests, Is.EqualTo(3), "next turn needs only one call");
        Assert.That((int)Field(effect, "Starts"), Is.EqualTo(1));
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts + 2));
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator DepartureCancelsReconsideration()
    {
        RequireTrust(15); fixture.Service.HoldFollowUp = true;
        int starts = (int)Field(speech, "Starts"); Submit();
        yield return Wait(() => fixture.Service.PlayerResponseRequests == 2, "follow-up pending");
        Set(npc.GetComponent("NpcProximityTrigger"), "departureGraceSeconds", 0f);
        player.transform.position = new Vector3(100, 0, 0);
        yield return Wait(() => !(bool)Get(director, "InteractionOpen"), "departed");
        yield return null;
        Assert.That((bool)Get(npc.GetComponent("NpcProxyClient"), "IsPending"), Is.False);
        Assert.That((int)Field(effect, "Starts"), Is.Zero);
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts));
    }
    void Submit() { Call(director, "SelectForConversation"); Assert.That(Call(director, "SubmitTranscript", "That is wonderful news!"), Is.True); }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator SpeechOnlyTurnDoesNotRecordAnInventedActionFailure()
    {
        fixture.Service.ActionId = null;
        fixture.Service.PlayerReply = "Good to see you.";
        Submit();
        yield return Wait(() => History().events.Any(e => e.kind == "npc_speech" && e.text == "Good to see you.") && Accepting(), "speech-only turn settled");
        Assert.That((int)Field(effect, "Starts"), Is.Zero);
        Assert.That(History().events.Any(e => e.kind == "action_outcome"), Is.False);
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator SilentActionRunsOnceAndRecordsNoInventedSpeech()
    {
        fixture.Service.PlayerReply = "";
        int starts = (int)Field(speech, "Starts"); Submit();
        yield return Wait(() => (int)Field(effect, "Starts") == 1, "silent effect starts");
        var turn = Get(memory, "CurrentTurn");
        var proposal = JsonUtility.FromJson("{\"actionId\":\"gesture\",\"targetId\":\"self\"}", Runtime("NpcActionProposal"));
        Assert.That(Call(runner, "StartProposal", turn, proposal), Is.False, "same turn cannot execute twice");
        yield return Wait(() => Accepting() && !(bool)Get(memory, "IsBusy"), "silent effect settled");
        Assert.That((int)Field(effect, "Completions"), Is.EqualTo(1));
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts));
        Assert.That(History().events.Count(e => e.kind == "action_outcome" && e.delivery == "completed"), Is.EqualTo(1));
        Assert.That(History().events.Any(e => e.kind == "npc_speech" && string.IsNullOrWhiteSpace(e.text)), Is.False);
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator ActionAndSpeechOverlapAndDepartureDoesNotStopAction()
    {
        Set(effect, "Duration", 2f); Set(speech, "Duration", 3f);
        fixture.Service.PlayerReply = "I'll do that."; Submit();
        yield return Wait(() => (int)Field(effect, "Starts") == 1 && (string)Field(speech, "LastText") == "I'll do that.", "speech and action started");
        Assert.That((bool)Get(runner, "IsBusy"), Is.True);
        Assert.That((bool)Get(speech, "IsSpeechActive"), Is.True);
        ((Behaviour)npc.GetComponent("NpcProximityTrigger")).enabled = false;
        yield return Wait(() => !(bool)Get(director, "InteractionOpen"), "conversation ended");
        yield return Wait(() => (int)Field(effect, "Completions") == 1 && !(bool)Get(memory, "IsBusy"), "unstoppable action settled");
        Assert.That(History().events.Count(e => e.kind == "action_outcome" && e.delivery == "completed"), Is.EqualTo(1));
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator CampaignResetRetiresAnActionWithoutRecordingItInTheNewCampaign()
    {
        Set(effect, "Duration", 2f); Submit();
        yield return Wait(() => (int)Field(effect, "Starts") == 1, "effect started");
        string previousCampaign = (string)Field(director, "campaignId");
        Call(director, "ResetCampaign");
        Assert.That((string)Field(director, "campaignId"), Is.Not.EqualTo(previousCampaign));
        yield return new WaitForSeconds(2.2f);
        yield return Wait(() => Accepting() && !(bool)Get(memory, "IsBusy"), "new campaign ready");
        Assert.That((int)Field(effect, "Completions"), Is.Zero);
        Assert.That((bool)Get(runner, "IsBusy"), Is.False);
        Assert.That(History().events.Any(e => e.kind == "action_outcome"), Is.False);
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator FailureIsRecordedWithoutFollowUpSpeech()
    {
        Set(effect, "Fail", true); fixture.Service.PlayerReply = "Let me try.";
        int starts = (int)Field(speech, "Starts"); Submit();
        yield return Wait(() => (int)Field(effect, "Completions") == 1 && Accepting() && !(bool)Get(memory, "IsBusy"), "failure recorded");
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts + 1));
        Assert.That(History().events.Single(e => e.kind == "action_outcome").delivery, Is.EqualTo("failed"));
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator UnofferedTargetCannotExecute()
    {
        fixture.Service.ActionTarget = "invented-target"; Submit();
        yield return Wait(() => History().events.Any(e => e.kind == "action_outcome") && Accepting(), "rejected proposal recorded");
        Assert.That((int)Field(effect, "Starts"), Is.Zero);
        Assert.That(History().events.Single(e => e.kind == "action_outcome").delivery, Is.EqualTo("failed"));
    }
    [Serializable] class Event { public string kind, text, delivery; }
    [Serializable] class Events { public Event[] events; }
    Events History() => new Events { events = ((IEnumerable)Field(Get(memory, "Buffer"), "acknowledgedHistory")).Cast<object>()
        .Select(e => new Event { kind = (string)Field(e, "kind"), text = (string)Field(e, "text"), delivery = (string)Field(e, "delivery") }).ToArray() };
    static IEnumerator Wait(Func<bool> condition, string reason)
    {
        float until = Time.realtimeSinceStartup + 10;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Assert.That(condition(), Is.True, reason);
    }
    [UnityTearDown] public IEnumerator TearDown()
    {
        if (fixture != null) yield return fixture.Dispose();
        if (ownedProfile) UnityEngine.Object.Destroy(ownedProfile);
        if (ownedRelationship) UnityEngine.Object.Destroy(ownedRelationship);
        if (worldId != null) { PlayerPrefs.DeleteKey("NpcCampaign." + worldId); PlayerPrefs.Save(); }
    }
}
#endif
