#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Meta.WitAi.TTS.Utilities;

public class NpcHandoffPlayTests
{
    NpcPlayFixture fixture;
    GameObject npc, player, hand, item;
    Component source, director, speech;
    bool live;
    string originalCampaign;
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        live = TestContext.CurrentContext.Test.Properties["Category"].Contains("NpcLive");
        if (live)
        {
            originalCampaign = PlayerPrefs.GetString("NpcCampaign.local-world", "");
            PlayerPrefs.SetString("NpcCampaign.local-world", Guid.NewGuid().ToString()); PlayerPrefs.Save();
        }
        fixture = new NpcPlayFixture(live);
        player = new GameObject("Player", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera";
        player.transform.position = new Vector3(2, 1.7f, 0);
        CreateNpc();
        yield return WaitFor(() => Accepting(), live ? 60 : 8, "opening completes");
        var receiver = new GameObject("Item receiver"); receiver.transform.SetParent(npc.transform);
        source = receiver.AddComponent(Type.GetType("Core3lb.ItemHandoffTrigger, Assembly-CSharp", true));
        Set(source, "enterEvent", new UnityEngine.Events.UnityEvent());
        Set(source, "triggerIncorrectEnter", new UnityEngine.Events.UnityEvent());
        Set(source, "recipient", npc);
        var field = source.GetType().GetField("itemID"); field.SetValue(source, Enum.ToObject(field.FieldType, 10));
        field = source.GetType().GetField("groupID"); field.SetValue(source, Enum.ToObject(field.FieldType, 2));
        var inbox = (Behaviour)npc.GetComponent("NpcHandoffInbox");
        Assert.That(inbox, Is.Not.Null, "The NPC prefab supplies the handoff inbox.");
        inbox.enabled = false;
        var sources = Array.CreateInstance(source.GetType(), 1); sources.SetValue(source, 0); Set(inbox, "sources", sources);
        inbox.enabled = true;
        item = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Items/GroKitGrabObject ItemID Variant.prefab"));
        item.transform.position = Vector3.one * 30;
        var id = item.GetComponent("ItemID"); Set(id, "DisplayName", "Mug");
        field = id.GetType().GetField("itemIDEnum"); field.SetValue(id, Enum.ToObject(field.FieldType, 10));
        hand = new GameObject("Player hand");
        var handComponent = (Behaviour)hand.AddComponent(Type.GetType("Core3lb.BaseGrabHand, Assembly-CSharp", true));
        handComponent.enabled = false;
        var input = Field(handComponent, "GrabProcessor");
        input.GetType().GetField("getState").SetValue(input, true);
        var grab = item.GetComponent("GroKitGrabObject");
        Set(grab, "overrideMovement", true);
        Set(grab, "currentHand", handComponent); Set(grab, "isGrabbed", true); Set(handComponent, "currentHeldObject", grab);
    }
    void CreateNpc()
    {
        npc = fixture.InstantiateNpc();
        Set(npc.GetComponent("NpcProximityTrigger"), "playerOverride", player.transform);
        foreach (var component in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (component && component.GetType().Name == "PlayerAwareness") Set(component, "checkTargetOverride", player.transform);
        director = npc.GetComponent("NpcCharacterDirector");
        speech = live ? (Component)Field(director, "speech") : npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp", true), true);
        fixture.Activate(npc);
    }
    static int Trust(object state) => ((IEnumerable)Field(state, "values")).Cast<object>().Where(v => (string)Field(v, "category") == "trust").Select(v => (int)Field(v, "value")).Single();
    void Give() => Invoke(source, "OnTriggerEnter", item.GetComponent<Collider>());
    [UnityTest, Explicit("Requires local service, PostgreSQL, Meta generation and Wit playback"), Category("NpcLive")]
    public IEnumerator RealCupNeedHandoffAndRecall()
    {
        string lastSpoken = "";
        npc.GetComponentInChildren<TTSSpeaker>(true).Events.OnPlaybackStart.AddListener((s, clip) => lastSpoken = clip.textToSpeak);
        Assert.That(Invoke(director, "SubmitTranscript", "Is there something you need that I could bring you?"), Is.True);
        yield return WaitFor(() => lastSpoken.Length > 0 && Accepting(), 90, "real model explains the authored need through Wit");
        Assert.That(lastSpoken.ToLowerInvariant(), Does.Contain("cup").Or.Contain("mug"));
        lastSpoken = "";
        Give(); Give();
        yield return WaitFor(() => lastSpoken == "Thank you. That's just what I needed." && Accepting(), 60, "real Wit thank-you completes");
        Assert.That(History().events.Count(e => e.kind == "action_outcome"), Is.EqualTo(1));
        var relationship = Get(npc.GetComponent("NpcMemory"), "Relationship");
        Assert.That(Trust(relationship), Is.EqualTo(20), "the confirmed handoff earns its configured relationship effect");
        string receipt = History().events.Single(e => e.kind == "action_outcome").eventId;
        RetireNpc(); yield return null;
        CreateNpc();
        yield return WaitFor(() => Accepting(), 60, "fresh NPC restores PostgreSQL memory");
        Assert.That(History().events.Count(e => e.eventId == receipt), Is.EqualTo(1));
        Assert.That(Trust(Get(npc.GetComponent("NpcMemory"), "Relationship")), Is.EqualTo(20), "the gift effect survives a fresh NPC instance");
        lastSpoken = "";
        npc.GetComponentInChildren<TTSSpeaker>(true).Events.OnPlaybackStart.AddListener((s, clip) => lastSpoken = clip.textToSpeak);
        Assert.That(Invoke(director, "SubmitTranscript", "What did I give you earlier? Do you still need it?"), Is.True);
        yield return WaitFor(() => lastSpoken.Length > 0 && Accepting(), 90, "real model recalls the physical gift through Wit");
        Assert.That(lastSpoken.ToLowerInvariant(), Does.Contain("cup").Or.Contain("mug"));
        TestContext.WriteLine("Gift recall: " + lastSpoken);
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator HandoffCancelsGenerationThanksOnceAndSurvivesNpcRestart()
    {
        fixture.Service.HoldPlayerResponses = true;
        Assert.That(Invoke(director, "SubmitTranscript", "How can I help?"), Is.True);
        var proxy = npc.GetComponent("NpcProxyClient");
        yield return WaitFor(() => (bool)Get(proxy, "IsPending"), 5, "generation pending");
        int starts = (int)Field(speech, "Starts");
        Give(); Give();
        Assert.That(item.activeSelf, Is.False);
        yield return WaitFor(() => (string)Field(speech, "LastText") == "Thank you. That's just what I needed." && Accepting(), 8, "authored thanks then listening");
        Assert.That((bool)Get(proxy, "IsPending"), Is.False);
        Assert.That((int)Field(speech, "Starts"), Is.EqualTo(starts + 1), "no duplicate thanks or late fallback");
        var history = History();
        Assert.That(history.events.Count(e => e.kind == "action_outcome" && e.text == "The player gave me Mug." && e.delivery == "completed"), Is.EqualTo(1));
        Assert.That(history.events.Any(e => e.text == "The inn is east of here."), Is.False, "cancelled generation is not remembered as spoken");
        string eventId = history.events.Single(e => e.kind == "action_outcome").eventId;
        // Replay a confirmed receipt with the same identity, including after a new component session.
        Invoke(npc.GetComponent("NpcMemory"), "RecordObservedOutcome", eventId.Replace(":action", ""), "The player gave me Mug.", "completed", "need-met");
        yield return WaitFor(() => !(bool)Get(npc.GetComponent("NpcMemory"), "IsBusy"), 5, "duplicate receipt settled");
        RetireNpc(); yield return null;
        CreateNpc();
        yield return WaitFor(() => Accepting(), 8, "new NPC restores persisted history");
        Assert.That(History().events.Count(e => e.eventId == eventId), Is.EqualTo(1));
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator DisabledInboxDropsOldReceiptAfterCampaignReset()
    {
        string original = PlayerPrefs.GetString("NpcCampaign.local-world", "");
        try
        {
            Give();
            var inbox = npc.GetComponent("NpcHandoffInbox");
            Assert.That(Invoke(inbox, "Peek"), Is.Not.Null);
            npc.SetActive(false);
            Type.GetType("NpcCampaign, Assembly-CSharp", true).GetMethod("Reset").Invoke(null, new object[] { "local-world" });
            npc.SetActive(true);
            Assert.That(Invoke(inbox, "Peek"), Is.Null, "a new campaign cannot inherit a pending gift");
            yield return WaitFor(() => Accepting(), 8, "new campaign opening");
            Assert.That(History().events.Any(e => e.kind == "action_outcome"), Is.False);
        }
        finally
        {
            PlayerPrefs.SetString("NpcCampaign.local-world", original); PlayerPrefs.Save();
        }
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator HandoffInterruptsSpeechButRemembersTheCompletedTransfer()
    {
        Set(speech, "Duration", 60f);
        Assert.That(Invoke(director, "SubmitTranscript", "Where is the inn?"), Is.True);
        yield return WaitFor(() => (string)Field(speech, "LastText") == "The inn is east of here." && (bool)Get(speech, "IsSpeechActive"), 8, "reply speaking");
        int cancellations = (int)Field(speech, "Cancellations");
        Set(speech, "Duration", .05f);
        Give();
        yield return WaitFor(() => (string)Field(speech, "LastText") == "Thank you. That's just what I needed." && Accepting(), 8, "handoff replaces speech");
        Assert.That((int)Field(speech, "Cancellations"), Is.EqualTo(cancellations + 1));
        Assert.That(History().events.Any(e => e.kind == "npc_speech" && e.delivery == "interrupted" && string.IsNullOrEmpty(e.text)), Is.True);
        Assert.That(History().events.Count(e => e.kind == "action_outcome" && e.delivery == "completed"), Is.EqualTo(1));
    }
    HistoryData History()
    {
        var buffer = npc.GetComponent("BaseAI_Conversation");
        var data = JsonUtility.ToJson(buffer);
        return JsonUtility.FromJson<HistoryData>(data);
    }
    void RetireNpc()
    {
        npc.SetActive(false);
        var kill = Type.GetType("DG.Tweening.DOTween, DOTween", true).GetMethod("Kill", new[] { typeof(object), typeof(bool) });
        foreach (var target in npc.GetComponentsInChildren<Transform>(true)) kill.Invoke(null, new object[] { target, false });
        UnityEngine.Object.Destroy(npc);
    }
    [Serializable] class HistoryData { public Event[] acknowledgedHistory; public Event[] events => acknowledgedHistory; }
    [Serializable] class Event { public string eventId, kind, text, delivery; }
    bool Accepting() => (bool)Get(director, "AcceptingTranscript");
    static object Get(Component c, string name) => c.GetType().GetProperty(name).GetValue(c);
    static object Field(object c, string name) => c.GetType().GetField(name).GetValue(c);
    static void Set(Component c, string name, object value) => c.GetType().GetField(name).SetValue(c, value);
    static object Invoke(Component c, string name, params object[] args) => c.GetType().GetMethod(name).Invoke(c, args);
    static IEnumerator WaitFor(Func<bool> predicate, float seconds, string label)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Assert.That(predicate(), Is.True, label);
    }
    [UnityTearDown] public IEnumerator TearDown()
    {
        if (npc) RetireNpc();
        yield return fixture.Dispose();
        if (live)
        {
            if (originalCampaign.Length > 0) PlayerPrefs.SetString("NpcCampaign.local-world", originalCampaign);
            else PlayerPrefs.DeleteKey("NpcCampaign.local-world");
            PlayerPrefs.Save();
        }
    }
}
#endif
