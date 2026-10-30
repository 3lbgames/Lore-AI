#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

public class NpcSpeechFailureIntegrationTests
{
    private NpcPlayFixture fixture;
    [UnityTearDown] public IEnumerator TearDown()
    {
        if (fixture != null) yield return fixture.Dispose();
        fixture = null;
    }

    [UnityTest, Explicit("Temporarily remove the local ElevenLabs resource key before running."), Category("SpeechMissingKey")]
    public IEnumerator MissingElevenLabsKeyKeepsCaptionsAndConversationResponsive()
    {
        Assert.That(Resources.Load<TextAsset>("ElevenLabsKey.private"), Is.Null, "Use the actual missing-resource path.");
        fixture = new NpcPlayFixture();
        var player = new GameObject("Player", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera";
        player.transform.position = new Vector3(2, 1.7f, 0);
        var npc = fixture.InstantiateNpc();
        var director = npc.GetComponent("NpcCharacterDirector");
        var old = (Component)director.GetType().GetField("speech").GetValue(director);
        var speaker = old.gameObject;
        UnityEngine.Object.DestroyImmediate(old);
        var speech = speaker.AddComponent(Type.GetType("Core3lb.ElevenlabsTTS, Assembly-CSharp", true));
        speech.GetType().GetField("speakerSource").SetValue(speech, speaker.AddComponent<AudioSource>());
        foreach (string name in new[] { "aiStarted", "aiCompleted", "aiFailed" })
            speech.GetType().GetField(name).SetValue(speech, new UnityEvent<string>());
        speech.GetType().GetMethod("SetAPIKey").Invoke(speech, new object[] { "" });
        director.GetType().GetField("speech").SetValue(director, speech);
        var proximity = npc.GetComponent("NpcProximityTrigger");
        proximity.GetType().GetField("playerOverride").SetValue(proximity, player.transform);
        foreach (var component in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (component && component.GetType().Name == "PlayerAwareness")
                component.GetType().GetField("checkTargetOverride").SetValue(component, player.transform);
        LogAssert.Expect(LogType.Error, "AI Failure: missing_key_voice_or_speaker");
        fixture.Activate(npc);
        speech.GetType().GetField("voiceID").SetValue(speech, "JBFqnCBsd6RMkjVDRZzb");
        var output = speaker.GetComponent("NpcSpeechOutput");
        Func<bool> ready = () => (bool)director.GetType().GetProperty("AcceptingTranscript").GetValue(director);
        yield return WaitFor(ready, "failed greeting returns to listening");
        Assert.That(output.GetType().GetProperty("Text").GetValue(output), Is.EqualTo("Hello, traveler. What brings you here?"));
        director.GetType().GetMethod("SelectForConversation").Invoke(director, null);
        for (int turn = 0; turn < 2; turn++)
        {
            LogAssert.Expect(LogType.Error, "AI Failure: missing_key_voice_or_speaker");
            Assert.That(director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Where is the inn?" }), Is.True);
            int frame = Time.frameCount;
            yield return WaitFor(ready, "another input is accepted despite missing TTS credentials");
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(Time.frameCount, Is.GreaterThan(frame));
            Assert.That(output.GetType().GetProperty("Text").GetValue(output), Is.EqualTo("The inn is east of here."));
            var view = npc.GetComponentInChildren(Type.GetType("NpcSpeechCaptionView, Assembly-CSharp", true), true);
            var display = view.GetType().GetField("display").GetValue(view);
            Assert.That(((GameObject)display.GetType().GetField("textHolder").GetValue(display)).activeInHierarchy, Is.True);
            Assert.That(speech.GetType().GetProperty("Outcome").GetValue(speech).ToString(), Is.EqualTo("Failed"));
        }

    }

    private static IEnumerator WaitFor(Func<bool> predicate, string message)
    {
        float deadline = Time.realtimeSinceStartup + 5;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, message);
    }
}
#endif
