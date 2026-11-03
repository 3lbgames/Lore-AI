#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using Meta.WitAi.TTS.Utilities;
using NUnit.Framework;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.TestTools;

public class NpcConversationPlayTests
{
    NpcPlayFixture fixture;
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture = new NpcPlayFixture(TestContext.CurrentContext.Test.Properties["Category"].Contains("NpcLive"));
        var player = new GameObject("Player", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera"; player.transform.position = new Vector3(2, 1.7f, 0);
        new GameObject("Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
        var npc = fixture.InstantiateNpc();
        npc.name = "ConversationNpc";
        var proximity = npc.GetComponent("NpcProximityTrigger");
        proximity.GetType().GetField("playerOverride").SetValue(proximity, player.transform);
        foreach (var component in npc.GetComponentsInChildren<MonoBehaviour>(true))
            if (component && component.GetType().Name == "PlayerAwareness")
                component.GetType().GetField("checkTargetOverride").SetValue(component, player.transform);
        fixture.Activate(npc);
        yield return null;
        AudioListener.volume = 0;
        Application.runInBackground = true;
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator ArrivalUsesAuthoredGreetingAndReturnUsesModel()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "authored greeting completes");
        var speech = npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp"));
        Assert.That(speech.GetType().GetField("LastText").GetValue(speech), Is.EqualTo("Hello, traveler. What brings you here?"));
        var proxy = npc.GetComponent("NpcProxyClient");
        Assert.That((string)proxy.GetType().GetField("textOutput").GetValue(proxy), Is.Null.Or.Empty);
        Invoke(director, "SelectForConversation");
        Assert.That((bool)director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Where is the inn?" }), Is.True);
        yield return WaitFor(() => (string)speech.GetType().GetField("LastText").GetValue(speech) == "The inn is east of here." && Accepting(director), 5, "player reply completes");
        var player = GameObject.Find("Player");
        player.transform.position = new Vector3(100, 0, 0);
        yield return WaitFor(() => !(bool)director.GetType().GetProperty("InteractionOpen").GetValue(director), 5, "leave interaction");
        player.transform.position = new Vector3(2, 1.7f, 0);
        yield return WaitFor(() => (string)speech.GetType().GetField("LastText").GetValue(speech) == "Hello, traveler." && Accepting(director), 5, "generated returning greeting");
        Assert.That((string)proxy.GetType().GetField("textOutput").GetValue(proxy), Is.EqualTo("Hello, traveler."), "returning greeting requests an NPC-initiated model response");
        Assert.That(fixture.Service.LastRecordedSpeech, Is.EqualTo("Hello, traveler."), "generated greeting is persisted");
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator WorldReactionRetriesTransientFailureWithoutLeaving()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "initial greeting completes");
        var inbox = npc.AddComponent(Type.GetType("NpcWorldEventInbox, Assembly-CSharp", true));
        fixture.Service.NpcFailuresRemaining = 1;
        Invoke(inbox, "Post", "wasps-retry", "React to the completed task.", "The wasps moved along unharmed.");
        yield return WaitFor(() => fixture.Service.NpcResponseRequests == 2 && Accepting(director), 5,
            "transient failure retries automatically while the player stays nearby");
        Assert.That(inbox.GetType().GetMethod("Peek").Invoke(inbox, null), Is.Null);
        Assert.That(fixture.Service.LastNpcIntent, Is.EqualTo("React to the completed task."));
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator WorldReactionDepartureCancelsPendingRetry()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "initial greeting completes");
        var inbox = npc.AddComponent(Type.GetType("NpcWorldEventInbox, Assembly-CSharp", true));
        var proximity = npc.GetComponent("NpcProximityTrigger");
        proximity.GetType().GetField("departureGraceSeconds").SetValue(proximity, 0f);
        fixture.Service.FailNpcResponses = true;
        Invoke(inbox, "Post", "wasps-depart", "React to the completed task.", "The wasps moved along unharmed.");
        var proxy = npc.GetComponent("NpcProxyClient");
        yield return WaitFor(() => proxy.GetType().GetProperty("Error").GetValue(proxy) != null, 5, "failed response schedules retry");
        GameObject.Find("Player").transform.position = new Vector3(100, 0, 0);
        yield return WaitFor(() => !(bool)director.GetType().GetProperty("InteractionOpen").GetValue(director), 5, "player leaves");
        yield return new WaitForSecondsRealtime(.8f);
        Assert.That(fixture.Service.NpcResponseRequests, Is.EqualTo(1), "Departure cancels the automatic retry");
        Assert.That(inbox.GetType().GetMethod("Peek").Invoke(inbox, null), Is.Not.Null);
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator WorldReactionRetriesSpeechWithoutRegeneratingResponse()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "initial greeting completes");
        var inbox = npc.AddComponent(Type.GetType("NpcWorldEventInbox, Assembly-CSharp", true));
        var speech = npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp"));
        var type = speech.GetType();
        int starts = (int)type.GetField("Starts").GetValue(speech);
        int cancellations = (int)type.GetField("Cancellations").GetValue(speech);
        type.GetField("Duration").SetValue(speech, 100f);
        type.GetField("StartPlayback").SetValue(speech, false);
        type.GetField("speechStartTimeout").SetValue(speech, .1f);
        Invoke(inbox, "Post", "wasps-audio", "React to the completed task.", "The wasps moved along unharmed.");
        yield return WaitFor(() => (int)type.GetField("Cancellations").GetValue(speech) > cancellations, 5, "first speech attempt times out");
        type.GetField("Duration").SetValue(speech, .05f);
        type.GetField("StartPlayback").SetValue(speech, true);
        yield return WaitFor(() => inbox.GetType().GetMethod("Peek").Invoke(inbox, null) == null && Accepting(director), 5, "speech retry succeeds");
        Assert.That((int)type.GetField("Starts").GetValue(speech), Is.EqualTo(starts + 2));
        Assert.That(fixture.Service.NpcResponseRequests, Is.EqualTo(1), "Audio retry must not regenerate dialogue or restart its action branch");
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator FailedWorldReactionStaysQueuedWithoutRepeatingGreeting()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "initial greeting completes");
        var inbox = npc.AddComponent(Type.GetType("NpcWorldEventInbox, Assembly-CSharp", true));
        var speech = npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp"));
        var lastText = speech.GetType().GetField("LastText");
        lastText.SetValue(speech, "reaction sentinel");
        fixture.Service.FailNpcResponses = true;
        Invoke(inbox, "Post", "wasps-completed", "React to the completed task.", "The wasps moved along unharmed.");
        yield return WaitFor(() => fixture.Service.NpcResponseRequests > 0, 5, "reaction requested");
        yield return WaitFor(() => Accepting(director), 5, "failed reaction returns to listening");
        Assert.That(inbox.GetType().GetMethod("Peek").Invoke(inbox, null), Is.Not.Null,
            "A failed reaction must remain queued for the next encounter.");
        Assert.That(lastText.GetValue(speech), Is.EqualTo("reaction sentinel"),
            "A failed task reaction must not fall through to the intro greeting.");
        Assert.That(fixture.Service.NpcResponseRequests, Is.EqualTo(2), "Stop after one automatic retry");
        fixture.Service.FailNpcResponses = false;
        var player = GameObject.Find("Player");
        player.transform.position = new Vector3(100, 0, 0);
        yield return WaitFor(() => !(bool)director.GetType().GetProperty("InteractionOpen").GetValue(director), 5, "leave interaction");
        player.transform.position = new Vector3(2, 1.7f, 0);
        yield return WaitFor(() => inbox.GetType().GetMethod("Peek").Invoke(inbox, null) == null && Accepting(director), 5, "reaction succeeds on return");
        Assert.That(fixture.Service.LastNpcIntent, Is.EqualTo("React to the completed task."));
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator FailedReturningGreetingUsesRelationshipFallbackAndRecordsSpeech()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "first greeting completes");
        var speech = npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp"));
        Invoke(director, "SelectForConversation");
        Invoke(director, "SubmitTranscript", "Hello there.");
        yield return WaitFor(() => (string)speech.GetType().GetField("LastText").GetValue(speech) == "The inn is east of here." && Accepting(director), 5, "player input recorded");
        var profileField = director.GetType().GetField("profile");
        var original = (ScriptableObject)profileField.GetValue(director);
        var profile = UnityEngine.Object.Instantiate(original);
        var relationship = ScriptableObject.CreateInstance(Type.GetType("NpcRelationshipDefinition, Assembly-CSharp"));
        JsonUtility.FromJsonOverwrite("{\"rules\":{\"categories\":[{\"id\":\"trust\"}]},\"greetings\":[{\"name\":\"friendly\",\"priority\":20,\"conditions\":[{\"category\":\"trust\",\"comparison\":\"gte\",\"value\":20}],\"greeting\":\"Welcome back, friend.\"},{\"name\":\"wary\",\"priority\":10,\"conditions\":[{\"category\":\"trust\",\"comparison\":\"lt\",\"value\":0}],\"greeting\":\"Keep your distance.\"}]}", relationship);
        profile.GetType().GetField("relationship").SetValue(profile, relationship);
        profileField.SetValue(director, profile);
        fixture.Service.FailNpcResponses = true;
        try
        {
            foreach (var trust in new[] { 30, -10 })
            {
                var player = GameObject.Find("Player");
                player.transform.position = new Vector3(100, 0, 0);
                yield return WaitFor(() => !(bool)director.GetType().GetProperty("InteractionOpen").GetValue(director), 5, "leave interaction");
                fixture.Service.Trust = trust;
                player.transform.position = new Vector3(2, 1.7f, 0);
                string expected = trust > 0 ? "Welcome back, friend." : "Keep your distance.";
                yield return WaitFor(() => (string)speech.GetType().GetField("LastText").GetValue(speech) == expected && Accepting(director), 5, "relationship fallback finishes");
                var memory = npc.GetComponent("NpcMemory");
                yield return WaitFor(() => !(bool)memory.GetType().GetProperty("IsBusy").GetValue(memory), 5, "fallback speech recorded");
                Assert.That(memory.GetType().GetProperty("Error").GetValue(memory), Is.Null);
                Assert.That(fixture.Service.LastRecordedSpeech, Is.EqualTo(expected), "fallback greeting is persisted");
            }
        }
        finally
        {
            profileField.SetValue(director, original);
            UnityEngine.Object.Destroy(profile);
            UnityEngine.Object.Destroy(relationship);
        }
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator QueuedReceiptWithStaleRevisionRecoversWithoutRestart()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "initial greeting saved");
        var memory = npc.GetComponent("NpcMemory");
        int previous = (int)memory.GetType().GetProperty("Revision").GetValue(memory);
        Assert.That(previous, Is.GreaterThan(0));
        // Simulate startup with existing server history and no successful local restore.
        Invoke(memory, "ClearForCampaign");
        Invoke(memory, "RecordActionOutcome", "Observed recovery test action", "completed", null);
        yield return WaitFor(() => !(bool)memory.GetType().GetProperty("IsBusy").GetValue(memory), 5, "receipt attempt finishes");
        Assert.That((bool)memory.GetType().GetProperty("Loaded").GetValue(memory), Is.True, "revision conflict must reload history and retry the queued receipt");
        Assert.That(memory.GetType().GetProperty("Error").GetValue(memory), Is.Null);
        Assert.That((int)memory.GetType().GetProperty("Revision").GetValue(memory), Is.EqualTo(previous + 1));
        Invoke(memory, "Load");
        yield return WaitFor(() => !(bool)memory.GetType().GetProperty("IsBusy").GetValue(memory), 5, "next restore finishes");
        Assert.That((int)memory.GetType().GetProperty("Revision").GetValue(memory), Is.EqualTo(previous + 1), "receipt must not be duplicated");
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator StalledSpeechTimesOutWithoutReplayAndNextUtteranceWorks()
    {
        var npc = GameObject.Find("ConversationNpc");
        ((Behaviour)npc.GetComponent("NpcProximityTrigger")).enabled = false;
        var speaker = npc.GetComponentInChildren<TTSSpeaker>(true);
        var speech = speaker.GetComponent("Witt_TextToSpeech");
        Invoke(speech, "_StopAudio");
        const string text = "Playback watchdog test.";
        var clip = speaker.TTSService.GetClipData(text, speaker.VoiceSettings, null);
        var samples = new float[24000];
        clip.clipStream.AddSamples(samples, 0, samples.Length);
        clip.clipStream.SetExpectedSamples(samples.Length);
        Assert.That(speaker.TTSService.RuntimeCacheHandler.AddClip(clip), Is.True);
        clip.loadState = Meta.WitAi.TTS.Data.TTSClipLoadState.Loaded;
        int starts = 0;
        speaker.Events.OnPlaybackStart.AddListener((s, c) => starts++);
        speech.GetType().GetField("speechTimeoutSeconds").SetValue(speech, 1f);
        Invoke(speech, "_Speak", text);
        yield return WaitFor(() => starts == 1, 2, "cached speech starts without a network request");
        speaker.Pause();
        yield return WaitFor(() => !(bool)speech.GetType().GetProperty("IsSpeechActive").GetValue(speech), 3, "watchdog stops stalled playback");
        Assert.That(speech.GetType().GetProperty("Outcome").GetValue(speech).ToString(), Is.EqualTo("Failed"));
        Assert.That(speech.GetType().GetProperty("SpeechError").GetValue(speech), Is.EqualTo("speech_timeout"));
        Assert.That(starts, Is.EqualTo(1), "do not replay an utterance that already began");
        Assert.That(speaker.IsActive, Is.False);
        speech.GetType().GetField("speechTimeoutSeconds").SetValue(speech, 5f);
        Invoke(speech, "_Speak", text);
        yield return WaitFor(() => !(bool)speech.GetType().GetProperty("IsSpeechActive").GetValue(speech), 6, "next utterance completes");
        Assert.That(speech.GetType().GetProperty("Outcome").GetValue(speech).ToString(), Is.EqualTo("Completed"));
        Assert.That(starts, Is.EqualTo(2));
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator FailedRequestReturnsToListeningAndOnlySelectedNpcAcceptsInput()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        var proxy = npc.GetComponent("NpcProxyClient");
        proxy.GetType().GetField("timeoutSeconds").SetValue(proxy, 2);
        proxy.GetType().GetField("localToken", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, "test-token");
        var other = UnityEngine.Object.Instantiate(npc);
        other.name = "OtherConversationNpc";
        fixture.Service.Configure(other);
        try
        {
            yield return WaitFor(() => (bool)director.GetType().GetProperty("AcceptingTranscript").GetValue(director), 5, "first NPC ready");
            var otherDirector = other.GetComponent("NpcCharacterDirector");
            yield return WaitFor(() => (bool)otherDirector.GetType().GetProperty("AcceptingTranscript").GetValue(otherDirector), 5, "nearby NPC ready");
            proxy.GetType().GetField("serviceUrl").SetValue(proxy, "http://127.0.0.1:1");
            director.GetType().GetMethod("SelectForConversation").Invoke(director, null);
            Assert.That((bool)otherDirector.GetType().GetMethod("SubmitTranscript").Invoke(otherDirector, new object[] { "Where is the inn?" }), Is.False);
            Assert.That((bool)director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Where is the inn?" }), Is.True);
            var graph = npc.GetComponent<BehaviorGraphAgent>();
            Assert.That(graph.GetVariable<string>("RequestError", out var requestError), Is.True,
                "Request failures must be available to authored graph branches.");
            yield return WaitFor(() => requestError.Value == "invalid_or_failed_response", 8, "connection failure is explicit before generation");
            Assert.That(requestError.Value, Is.EqualTo("invalid_or_failed_response"));
            Assert.That(npc.GetComponent("NpcMemory").GetType().GetProperty("Error").GetValue(npc.GetComponent("NpcMemory")), Is.EqualTo(requestError.Value));
            yield return WaitFor(() => (bool)director.GetType().GetProperty("AcceptingTranscript").GetValue(director), 5, "failure returns to listening");
            Assert.That((string)other.GetComponent("NpcProxyClient").GetType().GetField("textOutput").GetValue(other.GetComponent("NpcProxyClient")), Is.Null.Or.Empty, "Authored greetings do not call the model");
        }
        finally { other.SetActive(false); UnityEngine.Object.Destroy(other); }
    }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator MicrophoneCompletionPreservesExactTranscript()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        fixture.Service.HoldPlayerResponses = true;
        var inputObject = CreateSpeechInput(out var input, out var transcription);
        var hud = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/NpcSpeechStatus.prefab"));
        hud.SetActive(false);
        var view = hud.GetComponent("NpcSpeechStatusView");
        view.GetType().GetField("input").SetValue(view, input);
        Assert.That(view.GetType().GetField("transcriptDuration").GetValue(view), Is.EqualTo(8f));
        var label = view.GetType().GetField("textUI").GetValue(view);
        Func<string> display = () => (string)label.GetType().GetProperty("text").GetValue(label);
        hud.SetActive(true);
        try
        {
            yield return WaitFor(() => Accepting(director), 5, "ready for microphone input");
            Invoke(director, "SelectForConversation");
            Invoke(input, "BeginListening");
            Invoke(input, "EndListening");
            Assert.That(transcription.GetType().GetField("Starts").GetValue(transcription), Is.EqualTo(1));
            Assert.That(transcription.GetType().GetField("Stops").GetValue(transcription), Is.EqualTo(1));
            const string exact = "  Where's the inn?!\nPlease, tell me.  ";
            Invoke(transcription, "Complete", exact);
            var graph = npc.GetComponent<BehaviorGraphAgent>();
            yield return WaitFor(() => graph.GetVariable<string>("Transcript", out var value) && value.Value == exact,
                3, "speech completion reaches the graph without normalization");
            Assert.That(Accepting(director), Is.False);
            yield return null;
            Assert.That(display(), Does.Contain(exact), "final recognition stays visible after capture finishes");
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(display(), Does.Contain(exact), "waiting for the NPC reply must not erase recognized text");
            view.GetType().GetField("transcriptDuration").SetValue(view, .1f);
            yield return null;
            yield return null;
            Assert.That(display(), Does.Not.Contain(exact), "the HUD owns expiration of its retained transcript");
        }
        finally
        {
            Invoke(director, "CancelTurn");
            UnityEngine.Object.Destroy(hud);
            UnityEngine.Object.Destroy(inputObject);
        }
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator ReselectionRestoresListeningAndRejectsStaleMicrophoneCompletion()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        var other = UnityEngine.Object.Instantiate(npc);
        fixture.Service.Configure(other);
        var otherDirector = other.GetComponent("NpcCharacterDirector");
        var inputObject = CreateSpeechInput(out var input, out var transcription);
        try
        {
            yield return WaitFor(() => Accepting(director) && Accepting(otherDirector), 5, "both NPCs waiting");
            Invoke(director, "SelectForConversation");
            Invoke(input, "BeginListening");
            Invoke(input, "EndListening");
            // Return to A in the same frame, before NpcSpeechInput.Update can observe B.
            Invoke(otherDirector, "SelectForConversation");
            Invoke(director, "SelectForConversation");
            yield return WaitFor(() => Accepting(director), 3, "A resumes its waiting node after A-B-A");
            Invoke(transcription, "Complete", "stale selection transcript");
            AssertNoTranscript(director);
            Assert.That(Accepting(director), Is.True);

            Invoke(input, "BeginListening");
            Invoke(input, "EndListening");
            // End and restart this NPC's interaction without changing the input component.
            npc.SetActive(false);
            npc.SetActive(true);
            yield return WaitFor(() => Accepting(director) && director.GetType().GetProperty("InteractionId").GetValue(director) != null,
                5, "A starts a new interaction");
            Invoke(director, "SelectForConversation");
            Invoke(transcription, "Complete", "stale interaction transcript");
            AssertNoTranscript(director);
            Invoke(input, "BeginListening");
            Invoke(input, "EndListening");
            const string fresh = " Fresh question? ";
            Invoke(transcription, "Complete", fresh);
            var arguments = new object[] { null };
            Assert.That(director.GetType().GetMethod("TakeTranscript").Invoke(director, arguments), Is.True);
            Assert.That(arguments[0], Is.EqualTo(fresh), "new capture works after cancellation");
        }
        finally
        {
            other.SetActive(false);
            UnityEngine.Object.Destroy(other);
            UnityEngine.Object.Destroy(inputObject);
        }
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator SwitchingWhileRequestPendingDoesNotPlayFallbackAndAllowsReselection()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        var proxy = npc.GetComponent("NpcProxyClient");
        fixture.Service.HoldPlayerResponses = true;
        var other = UnityEngine.Object.Instantiate(npc);
        fixture.Service.Configure(other);
        var otherDirector = other.GetComponent("NpcCharacterDirector");
        var controlledSpeech = npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp", true));
        Func<int> starts = () => (int)controlledSpeech.GetType().GetField("Starts").GetValue(controlledSpeech);
        try
        {
            yield return WaitFor(() => Accepting(director) && Accepting(otherDirector), 5, "both NPCs ready");
            Invoke(director, "SelectForConversation");
            Assert.That(director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Where is the inn?" }), Is.True);
            yield return WaitFor(() => (bool)proxy.GetType().GetProperty("IsPending").GetValue(proxy), 3, "HTTP response held pending");
            int beforeSwitch = starts();
            Invoke(otherDirector, "SelectForConversation");
            yield return WaitFor(() => !(bool)proxy.GetType().GetProperty("IsPending").GetValue(proxy),
                1, "graph cancels HTTP on the next update after selection changes");
            yield return WaitFor(() => Accepting(director), 3, "cancelled response returns A to listening");
            npc.GetComponent<BehaviorGraphAgent>().GetVariable<string>("Response", out var response);
            Assert.That(response.Value, Is.Null.Or.Empty, "cancelled request must not produce the unavailable reply");
            yield return new WaitForSeconds(1);
            Assert.That(starts(), Is.EqualTo(beforeSwitch), "deselected NPC must not start fallback playback");
            Invoke(director, "SelectForConversation");
            Assert.That(director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Can we try again?" }), Is.True);
            yield return WaitFor(() => (bool)proxy.GetType().GetProperty("IsPending").GetValue(proxy), 3, "second request is pending");
            Invoke(otherDirector, "SelectForConversation");
            Invoke(director, "SelectForConversation");
            yield return WaitFor(() => Accepting(director), 3, "rapid A-B-A cancels the old request and resumes listening");
            Assert.That(response.Value, Is.Null.Or.Empty, "reselection must not revive the cancelled response");
            Assert.That(starts(), Is.EqualTo(beforeSwitch));
        }
        finally
        {
            Invoke(director, "CancelTurn");
            other.SetActive(false);
            UnityEngine.Object.Destroy(other);
        }
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator RemainingNpcIsSelectedWhenCurrentNpcLeaves()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        var other = UnityEngine.Object.Instantiate(npc);
        fixture.Service.Configure(other);
        var otherDirector = other.GetComponent("NpcCharacterDirector");
        try
        {
            yield return WaitFor(() => Accepting(director) && Accepting(otherDirector), 5, "both NPCs ready");
            Invoke(director, "SelectForConversation");
            npc.SetActive(false);
            yield return WaitFor(() => otherDirector.GetType().GetProperty("Selected").GetValue(null) == otherDirector,
                2, "graph selects remaining NPC");
            Assert.That(otherDirector.GetType().GetMethod("SubmitTranscript").Invoke(otherDirector, new object[] { "Hello" }), Is.True);
        }
        finally { other.SetActive(false); UnityEngine.Object.Destroy(other); }
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator SpeechTimeoutKeepsCaptionAndAllowsAnotherTurn()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        yield return WaitFor(() => Accepting(director), 5, "greeting finishes");
        Invoke(director, "SelectForConversation");
        var speech = (Component)director.GetType().GetField("speech").GetValue(director);
        foreach (bool startsPlayback in new[] { false, true })
        {
            string expectedError = startsPlayback ? "speech_playback_stalled" : "speech_start_timeout";
            speech.GetType().GetField("Duration").SetValue(speech, 100f);
            speech.GetType().GetField("StartPlayback").SetValue(speech, startsPlayback);
            speech.GetType().GetField("AdvancePlayback").SetValue(speech, false);
            speech.GetType().GetField("speechStartTimeout").SetValue(speech, startsPlayback ? 2f : .1f);
            speech.GetType().GetField("speechPlaybackStallTimeout").SetValue(speech, .1f);
            LogAssert.Expect(LogType.Warning, "NPC speech output abandoned: " + expectedError);
            Assert.That(director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Where is the inn?" }), Is.True);
            yield return WaitFor(() => Accepting(director), 5, "stalled audio does not block conversation");
            Assert.That(speech.GetType().GetProperty("IsSpeechActive").GetValue(speech), Is.False);
            var output = speech.GetComponent("NpcSpeechOutput");
            Assert.That(output.GetType().GetProperty("Text").GetValue(output), Is.EqualTo("The inn is east of here."));
            var graph = npc.GetComponent<BehaviorGraphAgent>();
            Assert.That(graph.GetVariable<string>("SpeechError", out var error), Is.True);
            Assert.That(error.Value, Is.EqualTo(expectedError));
            speech.GetType().GetField("Duration").SetValue(speech, .25f);
            speech.GetType().GetField("StartPlayback").SetValue(speech, true);
            speech.GetType().GetField("AdvancePlayback").SetValue(speech, true);
            speech.GetType().GetField("speechStartTimeout").SetValue(speech, .1f);
            speech.GetType().GetField("speechPlaybackStallTimeout").SetValue(speech, .1f);
            Assert.That(director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Where is the inn?" }), Is.True);
            yield return WaitFor(() => Accepting(director), 5, "healthy playback can exceed the startup deadline");
            Assert.That(speech.GetType().GetProperty("Outcome").GetValue(speech).ToString(), Is.EqualTo("Completed"));
        }
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator SpeechWithoutPlaybackFailsInsteadOfReportingDelivery()
    {
        var speaker = new GameObject("Silent speech provider");
        try
        {
            var speech = speaker.AddComponent(Type.GetType("Core3lb.BaseAI_TextToSpeech, Assembly-CSharp", true));
            Invoke(speech, "_Speak", "This produces no audio");
            yield return WaitFor(() => !(bool)speech.GetType().GetProperty("IsSpeechActive").GetValue(speech), 2, "provider finishes");
            Assert.That(speech.GetType().GetProperty("Outcome").GetValue(speech).ToString(), Is.EqualTo("Failed"));
            var nodeType = Type.GetType("SpeakText, Assembly-CSharp", true);
            var node = Activator.CreateInstance(nodeType);
            nodeType.GetField("speech", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(node, speech);
            nodeType.GetField("SpeechResult").SetValue(node, new BlackboardVariable<string>());
            nodeType.GetField("SpeechError").SetValue(node, new BlackboardVariable<string>());
            var update = nodeType.GetMethod("OnUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(update.Invoke(node, null).ToString(), Is.EqualTo("Failure"));
            Assert.That(((BlackboardVariable<string>)nodeType.GetField("SpeechError").GetValue(node)).Value, Is.EqualTo("no_playback"));
        }
        finally { UnityEngine.Object.Destroy(speaker); }
    }

    static bool Accepting(Component director) => (bool)director.GetType().GetProperty("AcceptingTranscript").GetValue(director);
    static void Invoke(Component target, string method, params object[] arguments) => target.GetType().GetMethod(method).Invoke(target, arguments);
    static void AssertNoTranscript(Component director)
    {
        Assert.That(director.GetType().GetMethod("TakeTranscript").Invoke(director, new object[] { null }), Is.False);
    }
    static GameObject CreateSpeechInput(out Component input, out Component transcription)
    {
        var instance = new GameObject("TestSpeechInput");
        instance.SetActive(false);
        transcription = instance.AddComponent(Type.GetType("NpcTestTranscription, Assembly-CSharp", true));
        Assert.That(transcription, Is.Not.Null, "test speech provider attaches as a runtime component");
        input = instance.AddComponent(Type.GetType("NpcSpeechInput, Assembly-CSharp", true));
        input.GetType().GetField("transcription").SetValue(input, transcription);
        instance.SetActive(true);
        return instance;
    }
    [UnityTest, Explicit("Requires real Meta generation and Wit playback"), Category("NpcLive")]
    public IEnumerator RealModelReplyReachesWitAndDepartureCancelsNextTurn()
    {
        var npc = GameObject.Find("ConversationNpc");
        var director = npc.GetComponent("NpcCharacterDirector");
        var proxy = npc.GetComponent("NpcProxyClient");
        var speaker = npc.GetComponentInChildren<TTSSpeaker>(true);
        int starts = 0;
        string lastText = null;
        speaker.Events.OnPlaybackStart.AddListener((s, c) => { starts++; lastText = c.textToSpeak; });
        yield return WaitFor(() => (bool)director.GetType().GetProperty("AcceptingTranscript").GetValue(director), 45, "ready for a question after greeting");
        int greetingStarts = starts;
        bool accepted = (bool)director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Where can I get lodging for the night?" });
        Assert.That(accepted, Is.True);
        yield return WaitFor(() => starts > greetingStarts, 60, "real response reaches Wit playback");
        Assert.That(lastText.ToLowerInvariant(), Does.Contain("inn"), "Proxy error: " + proxy.GetType().GetProperty("Error").GetValue(proxy));
        Assert.That(lastText.ToLowerInvariant(), Does.Contain("east"));
        yield return WaitFor(() => (bool)director.GetType().GetProperty("AcceptingTranscript").GetValue(director), 45, "returns to listening after response");
        Assert.That((string)proxy.GetType().GetField("textOutput").GetValue(proxy), Is.Not.Empty);
        Assert.That(npc.GetComponent<BehaviorGraphAgent>().GetVariable<string>("SpeechResult", out var speechResult), Is.True);
        Assert.That(speechResult.Value, Is.EqualTo("Completed"), "actual playback completion reaches the graph");
        director.GetType().GetMethod("SubmitTranscript").Invoke(director, new object[] { "Tell me more about lodging." });
        yield return WaitFor(() => (bool)proxy.GetType().GetProperty("IsPending").GetValue(proxy), 3, "request is pending");
        GameObject.Find("Player").transform.position = new Vector3(20, 1.7f, 0);
        yield return WaitFor(() => {
            npc.GetComponent<BehaviorGraphAgent>().GetVariable<bool>("InteractionActive", out var active);
            return !active.Value;
        }, 4, "departure ends interaction after grace");
        yield return null;
        int departureStarts = starts;
        Assert.That((bool)proxy.GetType().GetProperty("IsPending").GetValue(proxy), Is.False);
        yield return new WaitForSeconds(5);
        Assert.That(starts, Is.EqualTo(departureStarts), "Departure must not allow a late reply to play.");
    }
    static IEnumerator WaitFor(Func<bool> condition, float timeout, string label)
    {
        float until = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Assert.That(condition(), Is.True, label);
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return fixture.Dispose();
    }
}
#endif
