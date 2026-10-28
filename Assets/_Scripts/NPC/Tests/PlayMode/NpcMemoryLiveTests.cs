#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Behavior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Meta.WitAi.TTS.Utilities;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[Explicit("Requires local service, PostgreSQL, Meta and Wit"), Category("NpcLive")]
public class NpcMemoryLiveTests
{
    NpcPlayFixture fixture;
    const string Prefix = "NpcMemoryPlayTests.";
    const string CampaignKey = "NpcCampaign.local-world";
    static object Get(object o, string p) => o.GetType().GetProperty(p)?.GetValue(o) ?? o.GetType().GetField(p)?.GetValue(o);
    static object Call(object o, string m, params object[] args) => o.GetType().GetMethod(m).Invoke(o, args);
    static GameObject Npc => GameObject.Find("MemoryNpc");
    static Component Director => Npc.GetComponent("NpcCharacterDirector");
    static Component Memory => Npc.GetComponent("NpcMemory");
    static bool Ready => (bool)Get(Director, "AcceptingTranscript") && !(bool)Get(Memory, "IsBusy");
    static IEnumerator Wait(Func<bool> condition, float seconds, string reason)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, reason + "; memory error: " + Get(Memory, "Error") + "; loaded=" + Get(Memory, "Loaded") + "; busy=" + Get(Memory, "IsBusy") + "; interaction=" + Get(Director, "InteractionOpen") + "; proxy=" + Get(Npc.GetComponent("NpcProxyClient"), "Error"));
    }
    static string Greeting()
    {
        Assert.That(Npc.GetComponent<BehaviorGraphAgent>().GetVariable<string>("Greeting", out var value), Is.True, "Greeting must be exposed on the root Blackboard");
        return value.Value;
    }
    static Array History => ((IEnumerable)Get(Get(Memory, "Buffer"), "acknowledgedHistory")).Cast<object>().ToArray();

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture = new NpcPlayFixture(useLiveProviders: true);
        SessionState.SetString(Prefix + "Campaign", PlayerPrefs.GetString(CampaignKey, ""));
        PlayerPrefs.SetString(CampaignKey, Guid.NewGuid().ToString()); PlayerPrefs.Save();
        var player = new GameObject("MemoryPlayer", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera"; player.transform.position = new Vector3(20, 1.7f, 0);
        new GameObject("Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
        var npc = (GameObject)fixture.InstantiateNpc();
        npc.name = "MemoryNpc";
        npc.GetComponent("NpcProximityTrigger").GetType().GetField("playerOverride").SetValue(npc.GetComponent("NpcProximityTrigger"), player.transform);
        foreach (var component in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (component && component.GetType().Name == "PlayerAwareness") component.GetType().GetField("checkTargetOverride").SetValue(component, player.transform);
        fixture.Activate(npc);
        yield return null;
        AudioListener.volume = 0; Application.runInBackground = true;
    }

    [UnityTest, Category("CampaignControls")]
    public IEnumerator HeadsetResetHoldCancelsEarlyAndResetsOncePerPress()
    {
        var controls = Npc.GetComponent("NpcCampaignControls");
        var view = Npc.GetComponentInChildren(Type.GetType("NpcCampaignStatusView, Assembly-CSharp", true), true);
        Assert.That(view, Is.Not.Null, "The shipped prefab must include a headset reset view");
        var inputObject = new GameObject("Reset input test");
        var anchor = new GameObject("Left controller anchor");
        anchor.transform.position = new Vector3(19.7f, 1.3f, 0.5f);
        var pad = InputSystem.AddDevice<Gamepad>();
        var actions = ScriptableObject.CreateInstance<InputActionAsset>();
        var action = actions.AddActionMap("Campaign").AddAction("Reset", InputActionType.Button, "<Gamepad>/buttonWest");
        var reference = InputActionReference.Create(action);
        try
        {
            var anchorField = view.GetType().GetField("controllerAnchor");
            Assert.That(anchorField, Is.Not.Null, "Reset hint must follow a controller anchor");
            anchorField.SetValue(view, anchor.transform);
            var input = inputObject.AddComponent(Type.GetType("Core3lb.InputXR, Assembly-CSharp", true));
            var field = input.GetType().GetField("altInteract");
            var references = Activator.CreateInstance(field.FieldType);
            field.SetValue(input, references);
            references.GetType().GetField("leftHand").SetValue(references, reference);
            action.Enable();
            controls.GetType().GetField("holdSeconds").SetValue(controls, 0.5f);
            yield return null;
            Assert.That(view.transform.position.x, Is.EqualTo(anchor.transform.position.x).Within(0.001f));
            anchor.transform.position += Vector3.left * 0.2f;
            yield return null;
            Assert.That(view.transform.position.x, Is.EqualTo(anchor.transform.position.x).Within(0.001f), "hint follows controller movement rather than the head");
            var label = (Component)Get(view, "label");
            Assert.That(((GameObject)Get(view, "panel")).activeInHierarchy, Is.True);
            Assert.That((string)Get(label, "text"), Does.Contain("X"));
            string original = (string)Get(Director, "campaignId");
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.West));
            yield return new WaitForSecondsRealtime(0.15f);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            Assert.That(Get(Director, "campaignId"), Is.EqualTo(original), "short press cancels");
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.West));
            yield return new WaitForSecondsRealtime(0.7f);
            string reset = (string)Get(Director, "campaignId");
            Assert.That(reset, Is.Not.EqualTo(original));
            Assert.That((string)Get(label, "text"), Does.Contain("Campaign reset"));
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.That(Get(Director, "campaignId"), Is.EqualTo(reset), "holding cannot repeatedly reset");
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            Assert.That(PlayerPrefs.GetString(CampaignKey), Is.EqualTo(reset));
        }
        finally
        {
            action.Disable();
            InputSystem.RemoveDevice(pad);
            UnityEngine.Object.Destroy(reference);
            UnityEngine.Object.Destroy(actions);
            UnityEngine.Object.Destroy(inputObject);
            UnityEngine.Object.Destroy(anchor);
        }
    }

    [UnityTest, Category("LiveMemory")]
    public IEnumerator RetryQueueDisabledResetAndInterruptedSpeechPreserveHistory()
    {
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "initial memory loads");
        var proxy = Npc.GetComponent("NpcProxyClient");
        var urlField = proxy.GetType().GetField("serviceUrl");
        string url = (string)urlField.GetValue(proxy);
        urlField.SetValue(proxy, "http://127.0.0.1:1");
        var turn = Call(Director, "CreateRequest", "Remember this input despite the failed first commit.");
        Call(Memory, "BeginTurn", turn);
        Call(Memory, "RecordSpeech", turn, "", "Cancelled");
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "failed commit remains pending");
        Assert.That(Get(Memory, "Error"), Is.Not.Null);
        urlField.SetValue(proxy, url);
        Call(Memory, "Load");
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "queued input and speech retry");
        Assert.That(Get(Memory, "Error"), Is.Null);
        Assert.That(Get(Memory, "Revision"), Is.EqualTo(3));
        Assert.That(History.Length, Is.EqualTo(3));
        Call(Memory, "Load");
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "repeat restore replaces history");
        Assert.That(History.Length, Is.EqualTo(3));

        var npc = Npc;
        var director = Director;
        string oldCampaign = (string)Get(director, "campaignId");
        npc.SetActive(false);
        var campaignType = Type.GetType("NpcCampaign, Assembly-CSharp", true);
        string newCampaign = (string)campaignType.GetMethod("Reset").Invoke(null, new object[] { "local-world" });
        Assert.That(newCampaign, Is.Not.EqualTo(oldCampaign));
        npc.SetActive(true);
        yield return Wait(() => Ready, 60, "disabled NPC adopts reset campaign");
        Assert.That(Get(Director, "campaignId"), Is.EqualTo(newCampaign));
        Assert.That(History.Length, Is.EqualTo(1));
        Assert.That((bool)Get(Memory, "HasSpoken"), Is.False);

        var speaker = Npc.GetComponentInChildren<TTSSpeaker>(true);
        bool responseStarted = false;
        speaker.Events.OnPlaybackStart.AddListener((s, clip) => responseStarted = true);
        Assert.That(Call(Director, "SubmitTranscript", "Tell me where I can find lodging, in three sentences."), Is.EqualTo(true));
        yield return Wait(() => responseStarted && speaker.IsActive, 60, "real response begins playing");
        Call(Director, "CancelTurn");
        yield return Wait(() => Ready && (int)Get(Memory, "Revision") == 3, 20, "graph records interrupted speech");
        Assert.That(History.Length, Is.EqualTo(3));
        Assert.That(Get(History.GetValue(2), "delivery"), Is.EqualTo("interrupted"));
        Assert.That(Get(History.GetValue(2), "text"), Is.EqualTo(""));
        Call(Memory, "Load");
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "interrupted outcome restores");
        var delivered = (IList)Get(Get(Memory, "Buffer"), "conversationList");
        Assert.That(delivered.Count, Is.EqualTo(2), "undelivered reply never enters restored dialogue");
    }

    [UnityTest, Category("LiveMemory")]
    public IEnumerator GraphNodesInitiateDialogueAndRememberActionsWithoutSelection()
    {
        ((Behaviour)Npc.GetComponent("NpcProximityTrigger")).enabled = false;
        Assert.That((bool)Get(Director, "InteractionOpen"), Is.False);
        Call(Memory, "Load");
        yield return Wait(() => (bool)Get(Memory, "Loaded") && !(bool)Get(Memory, "IsBusy"), 10, "load action history");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var actionType = Type.GetType("RememberNpcAction, Assembly-CSharp", true);
        var action = Activator.CreateInstance(actionType);
        actionType.GetField("Character").SetValue(action, new BlackboardVariable<GameObject> { Value = Npc });
        actionType.GetField("Description").SetValue(action, new BlackboardVariable<string> { Value = "Tried to open the copper gate, but it was stuck." });
        actionType.GetField("Outcome").SetValue(action, new BlackboardVariable<string> { Value = "failed" });
        Assert.That(actionType.GetMethod("OnStart", flags).Invoke(action, null).ToString(), Is.EqualTo("Running"));
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "persist observed action");
        Assert.That(actionType.GetMethod("OnUpdate", flags).Invoke(action, null).ToString(), Is.EqualTo("Success"));
        Assert.That(History.Length, Is.EqualTo(1));
        Assert.That(Get(History.GetValue(0), "kind"), Is.EqualTo("action_outcome"));
        Assert.That(Get(History.GetValue(0), "delivery"), Is.EqualTo("failed"));
        Assert.That(((IList)Get(Get(Memory, "Buffer"), "conversationList")).Count, Is.Zero);
        Call(Director, "BeginInteraction");
        var requestType = Type.GetType("RequestNpcResponse, Assembly-CSharp", true);
        var request = Activator.CreateInstance(requestType);
        requestType.GetField("Character").SetValue(request, new BlackboardVariable<GameObject> { Value = Npc });
        requestType.GetField("NpcInitiated").SetValue(request, new BlackboardVariable<bool> { Value = true });
        requestType.GetField("Intent").SetValue(request, new BlackboardVariable<string> { Value = "Tell the nearby player about your last recorded action and its result." });
        requestType.GetField("Situation").SetValue(request, new BlackboardVariable<string> { Value = "You notice the player. They have not spoken." });
        var response = new BlackboardVariable<string>();
        requestType.GetField("Response").SetValue(request, response);
        requestType.GetField("RequestError").SetValue(request, new BlackboardVariable<string>());
        Assert.That(Director.GetType().GetProperty("Selected").GetValue(null), Is.Null);
        Assert.That(requestType.GetMethod("OnStart", flags).Invoke(request, null).ToString(), Is.EqualTo("Running"), ((BlackboardVariable<string>)requestType.GetField("RequestError").GetValue(request)).Value);
        yield return Wait(() => !(bool)Get(Npc.GetComponent("NpcProxyClient"), "IsPending"), 60, "NPC-initiated generation");
        Assert.That(requestType.GetMethod("OnUpdate", flags).Invoke(request, null).ToString(), Is.EqualTo("Success"));
        Assert.That(response.Value.ToLowerInvariant(), Does.Contain("gate"));
        Assert.That(History.Length, Is.EqualTo(1), "a request never invents player input or spoken output");
        requestType.GetMethod("OnEnd", flags).Invoke(request, null);
        var turn = Get(Memory, "CurrentTurn");
        Call(Memory, "RecordSpeech", turn, response.Value, "Completed");
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "record autonomous speech outcome");
        Assert.That(History.Length, Is.EqualTo(2));
        Assert.That(Get(History.GetValue(1), "kind"), Is.EqualTo("npc_speech"));
        Assert.That((bool)Get(Memory, "HasSpoken"), Is.False, "no player utterance was fabricated");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        string original = SessionState.GetString(Prefix + "Campaign", "");
        if (original == "") PlayerPrefs.DeleteKey(CampaignKey); else PlayerPrefs.SetString(CampaignKey, original);
        PlayerPrefs.Save();
        yield return fixture.Dispose();
    }
}
#endif
