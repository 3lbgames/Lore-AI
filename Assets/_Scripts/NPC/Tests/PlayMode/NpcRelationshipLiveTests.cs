#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Meta.WitAi.TTS.Utilities;

[Explicit("Requires local Node, PostgreSQL, Meta and Wit"), Category("NpcLive")]
public class NpcRelationshipLiveTests
{
    const string CampaignKey = "NpcCampaign.local-world";
    const string ProtectorPath = "Assets/_Prefabs/NPCs/Barbarian_NPC Variant.prefab";
    const string MiraPath = "Assets/_Prefabs/NPCs/Mira_NPC.prefab";
    const string Encouragement = "I believe in you. Give your delivery business a try!";
    const string Safety = "The residents and their homes matter. Let us plan a safe route around them.";
    NpcPlayFixture fixture;
    GameObject player;
    Actor protector, mira;
    string originalCampaign, testCampaign;

    sealed class Actor
    {
        public GameObject Npc;
        public Component Director, Memory;
        public int PlaybackStarts;
        public string LastSpoken;
    }

    static object Get(object value, string name) => value.GetType().GetProperty(name)?.GetValue(value) ?? value.GetType().GetField(name)?.GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name).Invoke(value, args);
    static bool Ready(Actor actor) => (bool)Get(actor.Director, "AcceptingTranscript") && !(bool)Get(actor.Memory, "IsBusy");
    static int Trust(Actor actor)
    {
        var state = Get(actor.Memory, "Relationship");
        return state == null ? 0 : ((IEnumerable)Get(state, "values")).Cast<object>().Where(v => (string)Get(v, "category") == "trust").Select(v => (int)Get(v, "value")).Single();
    }
    static object[] History(Actor actor) => ((IEnumerable)Get(Get(actor.Memory, "Buffer"), "acknowledgedHistory")).Cast<object>().ToArray();

    static IEnumerator Wait(Actor actor, Func<bool> condition, string reason)
    {
        float deadline = Time.realtimeSinceStartup + 90;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, reason + "; npc=" + actor.Npc.name +
            "; memory=" + Get(actor.Memory, "Error") + "; loaded=" + Get(actor.Memory, "Loaded") +
            "; busy=" + Get(actor.Memory, "IsBusy") + "; interaction=" + Get(actor.Director, "InteractionOpen") +
            "; proxy=" + Get(actor.Npc.GetComponent("NpcProxyClient"), "Error") + "; last spoken=" + actor.LastSpoken);
    }

    Actor Create(string path, Vector3 position)
    {
        var npc = fixture.InstantiateNpc(path);
        npc.transform.position = position;
        var proximity = npc.GetComponent("NpcProximityTrigger");
        proximity.GetType().GetField("playerOverride").SetValue(proximity, player.transform);
        foreach (var component in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (component && component.GetType().Name == "PlayerAwareness")
                component.GetType().GetField("checkTargetOverride").SetValue(component, player.transform);
        var actor = new Actor { Npc = npc, Director = npc.GetComponent("NpcCharacterDirector"), Memory = npc.GetComponent("NpcMemory") };
        var profile = Get(actor.Director, "profile");
        Assert.That(Get(profile, "relationship"), Is.Not.Null, "The saved prefab must carry its authored relationship definition");
        var speaker = npc.GetComponentInChildren<TTSSpeaker>(true);
        Assert.That(speaker, Is.Not.Null);
        speaker.Events.OnPlaybackStart.AddListener((s, clip) => { actor.PlaybackStarts++; actor.LastSpoken = clip.textToSpeak; });
        fixture.Activate(npc);
        return actor;
    }

    IEnumerator Approach(Actor actor, Actor leaving = null)
    {
        int starts = actor.PlaybackStarts;
        player.transform.position = actor.Npc.transform.position + new Vector3(2, 1.7f, 0);
        if (leaving != null)
            yield return Wait(leaving, () => !(bool)Get(leaving.Director, "InteractionOpen"), "departure releases the previous conversation");
        yield return Wait(actor, () => actor.PlaybackStarts > starts && Ready(actor), "authored opening is actually played and finishes");
        Assert.That(Get(actor.Memory, "Loaded"), Is.EqualTo(true));
        TestContext.WriteLine(actor.Npc.name + " greeting: " + actor.LastSpoken);
    }

    IEnumerator Say(Actor actor, string text, int expectedTrust)
    {
        int starts = actor.PlaybackStarts;
        Assert.That(Call(actor.Director, "SubmitTranscript", text), Is.EqualTo(true), "selected NPC accepts the exact utterance");
        yield return Wait(actor, () => actor.PlaybackStarts > starts && Ready(actor), "generated reply is actually played and its memory settles");
        Assert.That(Get(actor.Memory, "Error"), Is.Null);
        Assert.That(Get(actor.Npc.GetComponent("NpcProxyClient"), "Error"), Is.Null,
            "Live provider must complete: " + text + "; spoken=" + actor.LastSpoken);
        Assert.That(Trust(actor), Is.EqualTo(expectedTrust), text + "; spoken=" + actor.LastSpoken);
        Assert.That(History(actor).Any(e => (string)Get(e, "kind") == "player_input" && (string)Get(e, "text") == text), Is.True);
        TestContext.WriteLine(actor.Npc.name + " heard: " + text + " | replied: " + actor.LastSpoken + " | trust=" + Trust(actor));
    }

    static void AssertGeneratedGreeting(Actor actor)
    {
        var proxy = actor.Npc.GetComponent("NpcProxyClient");
        Assert.That(Get(proxy, "Error"), Is.Null);
        Assert.That(actor.LastSpoken, Is.Not.Null.And.Not.Empty);
        Assert.That(actor.LastSpoken, Is.EqualTo((string)Get(proxy, "textOutput")), "returning greeting uses the live model response");
        Assert.That(History(actor).Any(e => (string)Get(e, "kind") == "npc_speech" && (string)Get(e, "text") == actor.LastSpoken), Is.True);
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalCampaign = PlayerPrefs.GetString(CampaignKey, "");
        testCampaign = Guid.NewGuid().ToString();
        PlayerPrefs.SetString(CampaignKey, testCampaign); PlayerPrefs.Save();
        fixture = new NpcPlayFixture(useLiveProviders: true);
        player = new GameObject("Relationship player", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera"; player.transform.position = new Vector3(30, 1.7f, 0);
        protector = Create(ProtectorPath, Vector3.zero);
        mira = Create(MiraPath, new Vector3(12, 0, 0));
        yield return null;
    }

    [UnityTest, Category("US05"), Timeout(1200000)]
    public IEnumerator TwoProfilesDiscoverPreferencesEarnIndependentRelationshipsAndRestoreSpokenGreetings()
    {
        yield return Approach(mira);
        yield return Say(mira, "What would you like to do with your life?", 0);
        string discovery = mira.LastSpoken.ToLowerInvariant();
        Assert.That(discovery, Does.Contain("deliver").Or.Contain("business"), "conversation reveals the authored aspiration");
        yield return Say(mira, Encouragement, 10);
        yield return Say(mira, "Wishing you a wonderful birthday!", 20);
        yield return Say(mira, "Happy birthday again! I hope you have a lovely day.", 20);

        yield return Approach(protector, mira);
        yield return Say(protector, Encouragement, 0);
        yield return Say(protector, Safety, 10);
        yield return Say(protector, "Wishing you a wonderful birthday!", 20);
        Assert.That(Trust(mira), Is.EqualTo(20), "the other NPC's relationship remains independent");
        Assert.That(History(mira).Any(e => (string)Get(e, "text") == Safety), Is.False, "private player utterance does not leak to another NPC");
        Assert.That(History(protector).Any(e => (string)Get(e, "text") == "What would you like to do with your life?"), Is.False);

        player.transform.position = new Vector3(30, 1.7f, 0);
        yield return Wait(protector, () => !(bool)Get(protector.Director, "InteractionOpen"), "leave before replacing the NPC instances");
        protector.Npc.SetActive(false); mira.Npc.SetActive(false);
        UnityEngine.Object.Destroy(protector.Npc); UnityEngine.Object.Destroy(mira.Npc);
        yield return null;
        protector = Create(ProtectorPath, Vector3.zero);
        mira = Create(MiraPath, new Vector3(12, 0, 0));
        Assert.That(Get(protector.Director, "campaignId"), Is.EqualTo(testCampaign));
        Assert.That(Get(mira.Director, "campaignId"), Is.EqualTo(testCampaign));
        yield return Approach(mira);
        Assert.That(Trust(mira), Is.EqualTo(20));
        AssertGeneratedGreeting(mira);
        yield return Approach(protector, mira);
        Assert.That(Trust(protector), Is.EqualTo(20));
        AssertGeneratedGreeting(protector);
        Assert.That(History(mira).Any(e => (string)Get(e, "text") == Safety), Is.False);
    }

    [UnityTest, Category("US05")]
    public IEnumerator DisparagementRestoresWaryGreetingWithoutInventingWorldEvents()
    {
        yield return Approach(mira);
        yield return Say(mira, "Your business dream is worthless and you will never be any good at it.", -10);
        yield return Say(mira, "I gave you a cup and killed all the wasps yesterday.", -10);
        Assert.That(History(mira).Any(e => (string)Get(e, "kind") == "action_outcome"), Is.False);
        player.transform.position = new Vector3(30, 1.7f, 0);
        yield return Wait(mira, () => !(bool)Get(mira.Director, "InteractionOpen"), "leave before restoring the wary relationship");
        mira.Npc.SetActive(false); UnityEngine.Object.Destroy(mira.Npc);
        yield return null;
        mira = Create(MiraPath, new Vector3(12, 0, 0));
        yield return Approach(mira);
        Assert.That(Trust(mira), Is.EqualTo(-10));
        AssertGeneratedGreeting(mira);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (fixture != null) yield return fixture.Dispose();
        if (string.IsNullOrEmpty(originalCampaign)) PlayerPrefs.DeleteKey(CampaignKey);
        else PlayerPrefs.SetString(CampaignKey, originalCampaign);
        PlayerPrefs.Save();
    }
}
#endif
