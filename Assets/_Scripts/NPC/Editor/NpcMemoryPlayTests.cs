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

public class NpcMemoryPlayTests
{
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
        Assert.That(condition(), Is.True, reason + "; memory error: " + Get(Memory, "Error"));
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
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Assert.That(scene.isDirty, Is.False);
        SessionState.SetString(Prefix + "Scene", scene.path);
        SessionState.SetString(Prefix + "Campaign", PlayerPrefs.GetString(CampaignKey, ""));
        SessionState.SetFloat(Prefix + "Volume", AudioListener.volume);
        SessionState.SetBool(Prefix + "Background", Application.runInBackground);
        SessionState.SetBool(Prefix + "PlayerBackground", PlayerSettings.runInBackground);
        PlayerSettings.runInBackground = true;
        PlayerPrefs.SetString(CampaignKey, Guid.NewGuid().ToString()); PlayerPrefs.Save();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var player = new GameObject("MemoryPlayer", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera"; player.transform.position = new Vector3(20, 1.7f, 0);
        new GameObject("Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
        var npc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NpcInteractionTests.PrefabPath));
        npc.name = "MemoryNpc";
        npc.GetComponent("NpcProximityTrigger").GetType().GetField("playerOverride").SetValue(npc.GetComponent("NpcProximityTrigger"), player.transform);
        foreach (var component in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (component && component.GetType().Name == "PlayerAwareness") component.GetType().GetField("checkTargetOverride").SetValue(component, player.transform);
        npc.GetComponent<BehaviorGraphAgent>().SetVariableValue("SpeechText", "Hello, traveler.");
        yield return new EnterPlayMode();
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

    [UnityTest, Category("LiveMemory"), Timeout(600000)]
    public IEnumerator ConversationSurvivesPlayRestartAndResetRestoresFirstGreeting()
    {
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "first greeting completes");
        Assert.That((bool)Get(Memory, "HasSpoken"), Is.False);
        Assert.That(Greeting(), Is.EqualTo("Hello, traveler."));
        Assert.That(Call(Director, "SubmitTranscript", "My travel motto is copper lantern. What is my travel motto?"), Is.EqualTo(true));
        yield return Wait(() => (int)Get(Memory, "Revision") >= 2 && Ready, 90, "input and actual speech delivery committed");
        Assert.That(History.Length, Is.EqualTo(2));
        Assert.That((string)Get(History.GetValue(0), "text"), Does.Contain("copper lantern"));
        Assert.That((string)Get(History.GetValue(1), "delivery"), Is.EqualTo("completed"));
        Assert.That((string)Get(History.GetValue(1), "text"), Does.Contain("copper"));
        SessionState.SetString(Prefix + "TestCampaign", (string)Get(Director, "campaignId"));
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(20, 1.7f, 0);
        yield return Wait(() => !(bool)Get(Director, "InteractionOpen"), 5, "departure completes");
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "return greeting completes");
        Assert.That(Greeting(), Is.EqualTo((string)Get(Get(Director, "profile"), "returningGreeting")));
        Assert.That(History.Length, Is.EqualTo(2), "greetings do not manufacture player conversations");
        if (Npc) Npc.SetActive(false);
        yield return new WaitForSeconds(0.5f);
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();
        AudioListener.volume = 0; Application.runInBackground = true;
        Assert.That(Get(Director, "campaignId"), Is.EqualTo(SessionState.GetString(Prefix + "TestCampaign", "")));
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "restart loads prior conversation before greeting");
        Assert.That(Greeting(), Is.EqualTo((string)Get(Get(Director, "profile"), "returningGreeting")));
        Assert.That(History.Length, Is.EqualTo(2));
        Assert.That(Call(Director, "SubmitTranscript", "What travel motto did I tell you before?"), Is.EqualTo(true));
        yield return Wait(() => (int)Get(Memory, "Revision") >= 4 && Ready, 90, "model recalls persisted conversation");
        Assert.That((string)Get(History.GetValue(3), "text"), Does.Contain("copper"));
        // Invoke the same handler as the visible Reset campaign button.
        Call(Npc.GetComponent("NpcCampaignControls"), "ResetCampaign");
        yield return Wait(() => Ready && (bool)Get(Memory, "Loaded"), 60, "reset reloads a fresh history");
        Assert.That(Get(Director, "campaignId"), Is.Not.EqualTo(SessionState.GetString(Prefix + "TestCampaign", "")));
        Assert.That((bool)Get(Memory, "HasSpoken"), Is.False);
        Assert.That(History.Length, Is.Zero);
        Assert.That(Greeting(), Is.EqualTo("Hello, traveler."));
        SessionState.SetString(Prefix + "ResetCampaign", (string)Get(Director, "campaignId"));
        if (Npc) Npc.SetActive(false);
        yield return new WaitForSeconds(0.5f);
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();
        Assert.That(Get(Director, "campaignId"), Is.EqualTo(SessionState.GetString(Prefix + "ResetCampaign", "")), "reset ID persists too");
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
        Assert.That(Get(Memory, "Revision"), Is.EqualTo(2));
        Assert.That(History.Length, Is.EqualTo(2));
        Call(Memory, "Load");
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "repeat restore replaces history");
        Assert.That(History.Length, Is.EqualTo(2));

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
        Assert.That(History.Length, Is.Zero);
        Assert.That((bool)Get(Memory, "HasSpoken"), Is.False);

        var speaker = Npc.GetComponentInChildren<TTSSpeaker>(true);
        bool responseStarted = false;
        speaker.Events.OnPlaybackStart.AddListener((s, clip) => responseStarted = true);
        Assert.That(Call(Director, "SubmitTranscript", "Tell me where I can find lodging, in three sentences."), Is.EqualTo(true));
        yield return Wait(() => responseStarted && speaker.IsActive, 60, "real response begins playing");
        Call(Director, "CancelTurn");
        yield return Wait(() => Ready && (int)Get(Memory, "Revision") == 2, 20, "graph records interrupted speech");
        Assert.That(History.Length, Is.EqualTo(2));
        Assert.That(Get(History.GetValue(1), "delivery"), Is.EqualTo("interrupted"));
        Assert.That(Get(History.GetValue(1), "text"), Is.EqualTo(""));
        Call(Memory, "Load");
        yield return Wait(() => !(bool)Get(Memory, "IsBusy"), 10, "interrupted outcome restores");
        var delivered = (IList)Get(Get(Memory, "Buffer"), "conversationList");
        Assert.That(delivered.Count, Is.EqualTo(1), "undelivered reply never enters restored dialogue");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (Application.isPlaying)
        {
            if (Npc) Npc.SetActive(false);
            yield return new WaitForSeconds(0.5f);
            yield return new ExitPlayMode();
        }
        string original = SessionState.GetString(Prefix + "Campaign", "");
        if (original == "") PlayerPrefs.DeleteKey(CampaignKey); else PlayerPrefs.SetString(CampaignKey, original);
        PlayerPrefs.Save();
        AudioListener.volume = SessionState.GetFloat(Prefix + "Volume", 1);
        Application.runInBackground = SessionState.GetBool(Prefix + "Background", false);
        PlayerSettings.runInBackground = SessionState.GetBool(Prefix + "PlayerBackground", false);
        string scene = SessionState.GetString(Prefix + "Scene", "");
        if (scene != "") EditorSceneManager.OpenScene(scene);
    }
}
