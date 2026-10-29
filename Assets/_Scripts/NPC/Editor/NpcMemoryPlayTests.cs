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

[Explicit("Live providers and intentional domain reloads; run separately"), Category("NpcRestart")]
public class NpcMemoryRestartTests
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
        Assert.That(condition(), Is.True, reason + "; memory error: " + Get(Memory, "Error") + "; loaded=" + Get(Memory, "Loaded") + "; busy=" + Get(Memory, "IsBusy") + "; interaction=" + Get(Director, "InteractionOpen") + "; proxy=" + Get(Npc.GetComponent("NpcProxyClient"), "Error"));
    }
    static string Greeting()
    {
        Assert.That(Npc.GetComponent<BehaviorGraphAgent>().GetVariable<string>("Greeting", out var value), Is.True, "Greeting must be exposed on the root Blackboard");
        return value.Value;
    }
    static int Trust(object state) => ((IEnumerable)Get(state, "values")).Cast<object>().Where(v => (string)Get(v, "category") == "trust").Select(v => (int)Get(v, "value")).Single();
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
        yield return new EnterPlayMode();
        AudioListener.volume = 0; Application.runInBackground = true;
    }

    [UnityTest, Category("LiveMemory"), Timeout(600000)]
    public IEnumerator ConversationSurvivesPlayRestartAndResetRestoresIndependentOpening()
    {
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "first greeting completes");
        Assert.That((bool)Get(Memory, "HasSpoken"), Is.False);
        Assert.That(Greeting(), Is.Not.Null.And.Not.Empty);
        Assert.That(Npc.GetComponent<BehaviorGraphAgent>().GetVariable<bool>("MemoryLoaded", out var loaded), Is.True);
        Assert.That(loaded.Value, Is.True, "the memory subgraph must expose its output to the root graph");
        Assert.That(Call(Director, "SubmitTranscript", "My travel motto is copper lantern. What is my travel motto?"), Is.EqualTo(true));
        yield return Wait(() => (int)Get(Memory, "Revision") >= 3 && Ready, 90, "input and actual speech delivery committed");
        Assert.That(History.Length, Is.EqualTo(3));
        Assert.That((string)Get(History.GetValue(1), "text"), Does.Contain("copper lantern"));
        Assert.That((string)Get(History.GetValue(2), "delivery"), Is.EqualTo("completed"));
        Assert.That((string)Get(History.GetValue(2), "text"), Does.Contain("copper"));
        SessionState.SetString(Prefix + "TestCampaign", (string)Get(Director, "campaignId"));
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(20, 1.7f, 0);
        yield return Wait(() => !(bool)Get(Director, "InteractionOpen"), 5, "departure completes");
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "return greeting completes");
        Assert.That(Greeting(), Is.Not.Null.And.Not.Empty);
        Assert.That(History.Length, Is.EqualTo(4), "the opening is remembered without manufacturing player input");
        if (Npc) Npc.SetActive(false);
        yield return new WaitForSeconds(0.5f);
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();
        AudioListener.volume = 0; Application.runInBackground = true;
        Assert.That(Get(Director, "campaignId"), Is.EqualTo(SessionState.GetString(Prefix + "TestCampaign", "")));
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "restart loads prior conversation before greeting");
        Assert.That(Greeting(), Is.Not.Null.And.Not.Empty);
        Assert.That(History.Length, Is.EqualTo(5));
        Assert.That(Call(Director, "SubmitTranscript", "What travel motto did I tell you before?"), Is.EqualTo(true));
        yield return Wait(() => (int)Get(Memory, "Revision") >= 7 && Ready, 90, "model recalls persisted conversation");
        Assert.That((string)Get(History.GetValue(6), "text"), Does.Contain("copper"));
        // Invoke the same handler as the visible Reset campaign button.
        Call(Npc.GetComponent("NpcCampaignControls"), "ResetCampaign");
        yield return Wait(() => Ready && (bool)Get(Memory, "Loaded"), 60, "reset reloads a fresh history");
        Assert.That(Get(Director, "campaignId"), Is.Not.EqualTo(SessionState.GetString(Prefix + "TestCampaign", "")));
        Assert.That((bool)Get(Memory, "HasSpoken"), Is.False);
        Assert.That(History.Length, Is.EqualTo(1));
        Assert.That(Greeting(), Is.Not.Null.And.Not.Empty);
        SessionState.SetString(Prefix + "ResetCampaign", (string)Get(Director, "campaignId"));
        if (Npc) Npc.SetActive(false);
        yield return new WaitForSeconds(0.5f);
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();
        Assert.That(Get(Director, "campaignId"), Is.EqualTo(SessionState.GetString(Prefix + "ResetCampaign", "")), "reset ID persists too");
    }

    [UnityTest, Category("US05"), Timeout(300000)]
    public IEnumerator AcceptedRelationshipRestoresFriendlyGreetingAcrossDomainReload()
    {
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "initial greeting completes");
        // The physical transfer is covered by RealCupNeedHandoffAndRecall; this
        // fixture isolates restoration of its accepted outcome across a domain reload.
        Call(Memory, "RecordObservedOutcome", "restart-help", "The player fulfilled my mug need.", "completed", "need-met");
        yield return Wait(() => Ready && Get(Memory, "Relationship") != null && Trust(Get(Memory, "Relationship")) == 20, 30, "confirmed help is persisted");
        SessionState.SetString(Prefix + "TestCampaign", (string)Get(Director, "campaignId"));
        Npc.SetActive(false);
        yield return new WaitForSeconds(0.5f);
        yield return new ExitPlayMode();
        yield return new EnterPlayMode();
        AudioListener.volume = 0; Application.runInBackground = true;
        Assert.That(Get(Director, "campaignId"), Is.EqualTo(SessionState.GetString(Prefix + "TestCampaign", "")));
        GameObject.Find("MemoryPlayer").transform.position = new Vector3(2, 1.7f, 0);
        yield return Wait(() => Ready, 60, "relationship restores before the greeting");
        var state = Get(Memory, "Relationship");
        Assert.That(Trust(state), Is.EqualTo(20));
        Assert.That(((Array)Get(state, "experiences")).Length, Is.EqualTo(1));
        object[] args = { state, "unexpected fallback", null };
        string expected = (string)Call(Get(Get(Director, "profile"), "relationship"), "SelectGreeting", args);
        Assert.That(Greeting(), Is.EqualTo(expected));
        Assert.That(History.Cast<object>().Count(e => (string)Get(e, "kind") == "action_outcome"), Is.EqualTo(1));
        Assert.That(History.Cast<object>().Count(e => (string)Get(e, "kind") == "player_input"), Is.Zero);
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
