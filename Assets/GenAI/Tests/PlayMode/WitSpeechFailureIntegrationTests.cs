#if UNITY_EDITOR
using System;
using System.Collections;
using Meta.WitAi.Data.Configuration;
using Meta.WitAi.TTS.Data;
using Meta.WitAi.TTS.Integrations;
using Meta.WitAi.TTS.Utilities;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

public class WitSpeechFailureIntegrationTests
{
    NpcPlayFixture fixture;
    GameObject npc;
    Component director, speech, output;
    TTSSpeaker speaker;
    WitConfiguration emptyConfiguration;
    bool previousIgnoreErrors;
    object SpeechProperty(string name) => speech.GetType().GetProperty(name).GetValue(speech);
    void Set(string name, object value) => speech.GetType().GetField(name).SetValue(speech, value);
    bool Ready() => (bool)director.GetType().GetProperty("AcceptingTranscript").GetValue(director);

    void Create(bool missingKey = false)
    {
        previousIgnoreErrors = LogAssert.ignoreFailingMessages;
        fixture = new NpcPlayFixture();
        var player = new GameObject("Player", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera";
        player.transform.position = new Vector3(2, 1.7f, 0);
        npc = fixture.InstantiateNpc();
        director = npc.GetComponent("NpcCharacterDirector");
        var old = (Component)director.GetType().GetField("speech").GetValue(director);
        var owner = old.gameObject;
        UnityEngine.Object.DestroyImmediate(old);
        speech = owner.AddComponent(Type.GetType("Core3lb.Witt_TextToSpeech, Assembly-CSharp", true));
        speaker = npc.GetComponentInChildren<TTSSpeaker>(true);
        Set("mySpeaker", speaker);
        foreach (string name in new[] { "aiStarted", "aiCompleted", "aiFailed" })
            Set(name, new UnityEvent<string>());
        director.GetType().GetField("speech").SetValue(director, speech);
        output = owner.GetComponent("NpcSpeechOutput");
        var proximity = npc.GetComponent("NpcProximityTrigger");
        proximity.GetType().GetField("playerOverride").SetValue(proximity, player.transform);
        foreach (var component in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (component && component.GetType().Name == "PlayerAwareness")
                component.GetType().GetField("checkTargetOverride").SetValue(component, player.transform);
        var wit = (TTSWit)speaker.TTSService;
        if (missingKey)
        {
            // Clone the real configuration so the test never writes credentials to disk.
            emptyConfiguration = UnityEngine.Object.Instantiate(wit.Configuration);
            emptyConfiguration.SetClientAccessToken("");
            wit.Configuration = emptyConfiguration;
            Assert.That(wit.GetInvalidError(), Is.EqualTo("No WitConfiguration Client Token"));
            LogAssert.ignoreFailingMessages = true; // SDK emits multiple error logs; assert its concrete failure below.
        }
        string voice = speaker.VoiceID;
        fixture.Activate(npc);
        Set("voiceID", voice);
        if (!missingKey) Cache("Hello, traveler. What brings you here?", .2f);
    }

    void Cache(string text, float seconds, bool preparing = false)
    {
        var clip = speaker.TTSService.GetClipData(text, speaker.VoiceSettings, null);
        if (!preparing)
        {
            var samples = new float[(int)(24000 * seconds)];
            clip.clipStream.AddSamples(samples, 0, samples.Length);
            clip.clipStream.SetExpectedSamples(samples.Length);
        }
        Assert.That(speaker.TTSService.RuntimeCacheHandler.AddClip(clip), Is.True);
        clip.loadState = preparing ? TTSClipLoadState.Preparing : TTSClipLoadState.Loaded;
    }

    void Submit(string reply)
    {
        fixture.Service.PlayerReply = reply;
        director.GetType().GetMethod("SelectForConversation").Invoke(director, null);
        Assert.That(director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Please tell me more." }), Is.True);
    }

    void AssertCaption(string text)
    {
        Assert.That(output.GetType().GetProperty("Text").GetValue(output), Is.EqualTo(text));
        var view = npc.GetComponentInChildren(Type.GetType("NpcSpeechCaptionView, Assembly-CSharp", true), true);
        var display = view.GetType().GetField("display").GetValue(view);
        Assert.That(((GameObject)display.GetType().GetField("textHolder").GetValue(display)).activeInHierarchy, Is.True);
    }

    [UnityTest, Category("WitSpeech")]
    public IEnumerator MissingWitKeyKeepsCaptionsAndConversationResponsive()
    {
        Create(true);
        yield return WaitFor(Ready, 8, "missing-key greeting returns to listening");
        AssertCaption("Hello, traveler. What brings you here?");
        for (int i = 0; i < 2; i++)
        {
            int frame = Time.frameCount;
            Submit("The inn is east of here.");
            yield return WaitFor(Ready, 8, "missing-key response returns to listening");
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(Time.frameCount, Is.GreaterThan(frame));
            AssertCaption("The inn is east of here.");
            Assert.That(SpeechProperty("Outcome").ToString(), Is.EqualTo("Failed"));
            Assert.That(SpeechProperty("SpeechError") as string, Does.Contain("No WitConfiguration Client Token"));
            Assert.That(speaker.IsActive, Is.False);
        }
    }

    [UnityTest, Category("WitSpeech")]
    public IEnumerator WitStartupAndPlaybackStallsRetainTextAndRecover()
    {
        Create();
        yield return WaitFor(Ready, 5, "cached greeting completes");
        Set("speechStartTimeout", .3f);
        Set("speechPlaybackStallTimeout", .3f);
        const string startup = "Waiting for withheld audio.";
        Cache(startup, 0, true);
        LogAssert.Expect(LogType.Warning, "NPC speech output abandoned: speech_start_timeout");
        Submit(startup);
        yield return WaitFor(Ready, 3, "startup timeout releases conversation");
        AssertCaption(startup);
        Assert.That(speaker.IsActive, Is.False);

        const string stalled = "Audio pauses in this test.";
        Cache(stalled, 3);
        Submit(stalled);
        yield return WaitFor(() => (bool)SpeechProperty("IsSpeechActive") && (bool)SpeechProperty("HasSpeechPlaybackStarted"), 3, "cached playback begins");
        speaker.Pause();
        LogAssert.Expect(LogType.Warning, "NPC speech output abandoned: speech_playback_stalled");
        yield return WaitFor(Ready, 3, "playback stall releases conversation");
        AssertCaption(stalled);
        Assert.That(speaker.IsActive, Is.False);

        const string recovery = "Healthy playback continues beyond the stall interval.";
        Cache(recovery, 2);
        Submit(recovery);
        yield return WaitFor(Ready, 5, "next healthy utterance completes");
        AssertCaption(recovery);
        Assert.That(SpeechProperty("Outcome").ToString(), Is.EqualTo("Completed"));
    }

    [UnityTest, Category("WitSpeech")]
    public IEnumerator FaultedWitLoadReturnsWithoutWaitingForStartupTimeout()
    {
        Create();
        yield return WaitFor(Ready, 5, "cached greeting completes");
        var wit = (TTSWit)speaker.TTSService;
        wit.RequestSettings.audioType = (Meta.WitAi.TTSWitAudioType)999;
        LogAssert.ignoreFailingMessages = true;
        Submit("Unsupported decoder test.");
        yield return WaitFor(Ready, 2, "faulted SDK task must release the conversation before the five-second deadline");
        AssertCaption(fixture.Service.PlayerReply);
        Assert.That(SpeechProperty("Outcome").ToString(), Is.EqualTo("Failed"));
        Assert.That(SpeechProperty("SpeechError") as string, Does.Contain("audio decoder not supported"));
        Assert.That(speaker.IsActive, Is.False);
    }

    [UnityTest, Category("WitSpeech")]
    public IEnumerator StreamFailureDuringPlaybackPreservesProviderError()
    {
        Create();
        yield return WaitFor(Ready, 5, "cached greeting completes");
        const string reply = "Speech interrupted by a stream failure.";
        Cache(reply, 2);
        Submit(reply);
        yield return WaitFor(() => speaker.IsSpeaking, 3, "cached response starts playback");
        speaker.SpeakingClip.LoadError = "timeout";
        speaker.Stop();
        yield return WaitFor(Ready, 2, "stream failure releases the conversation");
        AssertCaption(reply);
        Assert.That(SpeechProperty("Outcome").ToString(), Is.EqualTo("Failed"));
        Assert.That(SpeechProperty("SpeechError") as string, Is.EqualTo("timeout"));
    }

    [Test, Category("WitSpeech")]
    public void FastHttpDecoderDoesNotStrandFollowingChunks()
    {
        var type = typeof(Meta.WitAi.WitRequestSettings).Assembly.GetType("Meta.WitAi.Requests.AudioStreamHandler", true);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        int decodedSamples = 0;
        Meta.Voice.Audio.Decoding.AudioSampleDecodeDelegate onSamples =
            (samples, offset, count) => decodedSamples += count;
        var decoder = Meta.WitAi.WitRequestSettings.GetTtsAudioDecoder(Meta.WitAi.TTSWitAudioType.PCM, null);
        var handler = Activator.CreateInstance(type, new object[] { decoder, onSamples });
        try
        {
            var enqueue = type.GetMethod("EnqueueAndDecodeChunkAsync", flags);
            var chunk = new byte[1024];
            // Execute on a worker so Background completes inline, deterministically
            // reproducing the fast-worker-before-task-assignment interleaving.
            System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 4; i++)
                    enqueue.Invoke(handler, new object[] { chunk, 0, chunk.Length });
            }).GetAwaiter().GetResult();
            Assert.That(decodedSamples, Is.EqualTo(2048), "every received PCM byte must reach the sample stream");
            Assert.That(type.GetField("_decodedBytes", flags).GetValue(handler), Is.EqualTo((ulong)4096));
        }
        finally { ((IDisposable)handler).Dispose(); }
    }

    [UnityTest, Category("WitLive")]
    public IEnumerator LiveWitCompletesAndCancellationPreservesCaption()
    {
        Create();
        yield return WaitFor(Ready, 5, "cached greeting completes");
        string text = "Welcome, traveler. The village is peaceful today. You can find the inn beside the bridge, and the market opens in the morning. Test " + DateTime.UtcNow.Ticks;
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:NpcProfile", new[] { "Assets/_Behavior" }))
        {
            var profile = UnityEditor.AssetDatabase.LoadMainAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            string voice = new UnityEditor.SerializedObject(profile).FindProperty("voice").stringValue;
            Set("voiceID", voice);
            string reply = text + " Voice check for " + profile.name;
            Submit(reply);
            yield return WaitFor(Ready, 35, "repeated real Wit response completes");
            Assert.That(SpeechProperty("Outcome").ToString(), Is.EqualTo("Completed"), SpeechProperty("SpeechError") as string);
            Assert.That(speaker.VoiceSettings.SettingsId, Is.EqualTo(voice), "The authored voice must play without a default fallback.");
            AssertCaption(reply);
        }
        Submit(text + " Let me explain the rest of the journey.");
        yield return WaitFor(() => (bool)SpeechProperty("IsSpeechActive") && (bool)SpeechProperty("HasSpeechPlaybackStarted"), 8, "second real response begins");
        speech.GetType().GetMethod("_StopAudio").Invoke(speech, null);
        yield return WaitFor(Ready, 5, "cancelled response returns to listening");
        Assert.That(SpeechProperty("Outcome").ToString(), Is.EqualTo("Cancelled"));
        Assert.That(speaker.IsActive, Is.False);
        AssertCaption(fixture.Service.PlayerReply);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (fixture != null) yield return fixture.Dispose();
        if (emptyConfiguration) UnityEngine.Object.Destroy(emptyConfiguration);
        LogAssert.ignoreFailingMessages = previousIgnoreErrors;
        fixture = null;
    }

    static IEnumerator WaitFor(Func<bool> predicate, float seconds, string reason)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(predicate(), Is.True, reason);
    }
}
#endif
