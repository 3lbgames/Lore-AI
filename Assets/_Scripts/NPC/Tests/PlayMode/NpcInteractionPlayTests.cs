#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Meta.WitAi.TTS.Utilities;
using NUnit.Framework;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.TestTools;

public class NpcInteractionPlayTests
{
    NpcPlayFixture fixture;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture = new NpcPlayFixture();
        var player = new GameObject("InteractionTestPlayer", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera";
        player.transform.position = new Vector3(10, 1.7f, 0);
        var light = new GameObject("Test Light", typeof(Light));
        light.GetComponent<Light>().type = LightType.Directional;
        var npc = fixture.InstantiateNpc();
        npc.name = "InteractionTestNpc";
        npc.transform.position = Vector3.zero;
        var adapter = npc.GetComponent("NpcProximityTrigger");
        adapter.GetType().GetField("playerOverride").SetValue(adapter, player.transform);
        npc.GetComponent<BehaviorGraphAgent>().SetVariableValue("OpeningIntent", "Address the player with a brief introduction in two sentences.");
        foreach (var awareness in npc.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c && c.GetType().Name == "PlayerAwareness"))
            awareness.GetType().GetField("checkTargetOverride").SetValue(awareness, player.transform);
        fixture.Activate(npc);
        yield return null;
        AudioListener.volume = 0f;
        Application.runInBackground = true;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return fixture.Dispose();
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator IdleNpcDoesNotMoveOnAWalkableNavMesh()
    {
        var npc = GameObject.Find("InteractionTestNpc");
        var agent = npc.GetComponent<UnityEngine.AI.NavMeshAgent>();
        var floor = new UnityEngine.AI.NavMeshBuildSource
        {
            shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(new Vector3(0, -0.1f, 0), Quaternion.identity, Vector3.one),
            size = new Vector3(20, 0.2f, 20)
        };
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(
            UnityEngine.AI.NavMesh.GetSettingsByID(agent.agentTypeID),
            new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource> { floor },
            new Bounds(Vector3.zero, new Vector3(20, 4, 20)), Vector3.zero, Quaternion.identity);
        Assert.That(data, Is.Not.Null);
        var surface = UnityEngine.AI.NavMesh.AddNavMeshData(data);
        try
        {
            agent.enabled = false;
            agent.enabled = true;
            Assert.That(agent.isOnNavMesh, Is.True, "The test must exercise active navigation.");
            yield return null;
            var start = npc.transform.position;
            float maximumDisplacement = 0;
            float end = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < end)
            {
                maximumDisplacement = Mathf.Max(maximumDisplacement, Vector3.Distance(start, npc.transform.position));
                yield return null;
            }
            Assert.That(maximumDisplacement, Is.LessThan(0.05f),
                "An idle NPC must not relocate to escape its own carving obstacle.");
        }
        finally
        {
            agent.enabled = false;
            surface.Remove();
            UnityEngine.Object.Destroy(data);
        }
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator ApproachCancelRepeatAndReturnToRoutine()
    {
        var npc = GameObject.Find("InteractionTestNpc");
        var player = GameObject.Find("InteractionTestPlayer").transform;
        var speaker = npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp", true));
        speaker.GetType().GetField("Duration").SetValue(speaker, 3f);
        var captions = npc.GetComponentInChildren<TTSSpeaker>(true).GetComponent("UI_TextDisplay");
        Assert.That(captions, Is.Not.Null, "The existing speech caption display must be wired.");
        var captionHolder = (GameObject)captions.GetType().GetField("textHolder").GetValue(captions);
        var captionText = (Component)captions.GetType().GetField("textUI").GetValue(captions);
        var pivot = npc.GetComponentInChildren<Animator>(true).transform;
        var idleRotation = pivot.localRotation;
        Func<int> starts = () => (int)speaker.GetType().GetField("Starts").GetValue(speaker);
        Func<int> completions = () => (int)speaker.GetType().GetField("Completions").GetValue(speaker);
        Func<int> cancellations = () => (int)speaker.GetType().GetField("Cancellations").GetValue(speaker);
        Func<string> requestedText = () => (string)speaker.GetType().GetField("LastText").GetValue(speaker);
        Func<bool> active = () => (bool)speaker.GetType().GetProperty("IsSpeechActive").GetValue(speaker);
        yield return null;
        Assert.That(FacingPlayer(pivot), Is.False);

        player.position = new Vector3(2, 1.7f, 0);
        yield return WaitFor(() => FacingPlayer(pivot), 2, "approach starts facing");
        yield return WaitFor(() => starts() == 1, 8, "controlled speech starts first playback");
        var direction = player.position - pivot.position;
        direction.y = 0;
        Assert.That(Vector3.Dot(pivot.forward, direction.normalized), Is.GreaterThan(0.9f));
        Assert.That(starts(), Is.EqualTo(1));
        Assert.That(requestedText(), Is.Not.Null.And.Not.Empty);
        Assert.That(captionHolder.activeInHierarchy, Is.True, "Spoken text must be visible above the NPC.");
        Assert.That(captionText.GetType().GetProperty("text").GetValue(captionText), Is.EqualTo(requestedText()));
        // Briefly crossing the boundary must preserve the current utterance.
        player.position = new Vector3(10, 1.7f, 0);
        yield return new WaitForSeconds(0.2f);
        Assert.That(FacingPlayer(pivot), Is.True);
        Assert.That(cancellations(), Is.Zero);
        player.position = new Vector3(2, 1.7f, 0);
        yield return new WaitForSeconds(0.2f);
        Assert.That(starts(), Is.EqualTo(1));

        player.position = new Vector3(10, 1.7f, 0);
        yield return WaitFor(() => !FacingPlayer(pivot) && !active(), 2, "departure cancels playback");
        Assert.That(cancellations(), Is.EqualTo(1));
        Assert.That(npc.GetComponent<BehaviorGraphAgent>().GetVariable<string>("SpeechResult", out var speechResult), Is.True);
        Assert.That(speechResult.Value, Is.EqualTo("Cancelled"), "graph records interrupted playback as cancellation");
        Assert.That(captionHolder.activeSelf, Is.True, "Cancelling audio must not remove readable response text.");
        Assert.That(captionText.GetType().GetProperty("text").GetValue(captionText), Is.EqualTo(requestedText()));
        Assert.That(Quaternion.Angle(pivot.localRotation, idleRotation), Is.GreaterThan(5f),
            "Departure must start a smooth return, not snap to idle.");

        player.position = new Vector3(2, 1.7f, 0);
        yield return WaitFor(() => starts() == 2, 8, "controlled speech starts second playback");
        Assert.That(captionHolder.activeInHierarchy, Is.True, "Repeat interaction must show the caption again.");
        Assert.That(captionText.GetType().GetProperty("text").GetValue(captionText), Is.EqualTo(requestedText()));
        yield return WaitFor(() => completions() == 1, 6, "playback completes");
        Assert.That(captionHolder.activeSelf, Is.True, "Audio completion must not clear the response caption.");
        yield return new WaitForSeconds(1.2f);
        Assert.That(FacingPlayer(pivot), Is.True, "Finishing speech must not end the interaction.");
        player.position = new Vector3(-2, 1.7f, 0);
        yield return WaitFor(() => Vector3.Dot(pivot.forward, Vector3.left) > 0.9f,
            5, "NPC keeps following the player after speech");
        player.position = new Vector3(-10, 1.7f, 0);
        yield return new WaitForSeconds(0.2f);
        Assert.That(FacingPlayer(pivot), Is.True, "Brief departure after speech must preserve facing.");
        player.position = new Vector3(-2, 1.7f, 0);
        yield return null;
        Assert.That(starts(), Is.EqualTo(2), "Remaining nearby must not repeat or overlap speech.");
        Assert.That(requestedText(), Is.Not.Null.And.Not.Empty);
        Assert.That(active(), Is.False);
        player.position = new Vector3(-10, 1.7f, 0);
        yield return WaitFor(() => !FacingPlayer(pivot), 2, "sustained departure ends facing after speech");
        yield return WaitFor(() => Quaternion.Angle(pivot.localRotation, idleRotation) < 0.1f,
            5, "return after immediate re-entry reaches the original idle orientation");
        Assert.That(Quaternion.Angle(pivot.localRotation, idleRotation), Is.LessThan(0.1f));
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator GraphRestoresFacingWithoutProximityTrigger()
    {
        var npc = GameObject.Find("InteractionTestNpc");
        ((Behaviour)npc.GetComponent("NpcProximityTrigger")).enabled = false;
        var agent = npc.GetComponent<BehaviorGraphAgent>();
        var player = GameObject.Find("InteractionTestPlayer");
        var pivot = npc.GetComponentInChildren<Animator>(true).transform;
        var facing = pivot.GetComponent("Facing");
        var originalRotation = pivot.localRotation;
        var originalTarget = facing.GetType().GetField("faceTo").GetValue(facing);
        var originalFacePlayer = facing.GetType().GetField("facePlayer").GetValue(facing);
        var speech = npc.GetComponentInChildren(Type.GetType("NpcTestSpeech, Assembly-CSharp", true));
        Assert.That(agent.GetVariable("Interaction", out BlackboardVariable variable), Is.True);
        var channel = (EventChannel)variable.ObjectValue;
        agent.Start();
        agent.SetVariableValue("Player", player);
        agent.SetVariableValue("InteractionActive", true);
        channel.SendEventMessage();
        yield return WaitFor(() => FacingPlayer(pivot), 2, "graph starts facing without the proximity component");
        yield return WaitFor(() => Quaternion.Angle(pivot.localRotation, originalRotation) > 5f,
            2, "the visible character model turns toward the player");
        Assert.That(Mathf.Abs(Vector3.Dot(pivot.up, Vector3.up)), Is.GreaterThan(0.999f),
            "The character must turn horizontally without tilting toward the player's head.");
        pivot.localRotation = Quaternion.Euler(0, 45, 0);
        var turnedRotation = pivot.localRotation;

        agent.End();
        Assert.That(Quaternion.Angle(pivot.localRotation, turnedRotation), Is.LessThan(0.1f),
            "Ending the graph must not instantly change orientation.");
        yield return WaitFor(() => Quaternion.Angle(pivot.localRotation, turnedRotation) > 1f,
            5, "return turn makes gradual progress");
        Assert.That(FacingPlayer(pivot), Is.False);
        Assert.That(Quaternion.Angle(pivot.localRotation, originalRotation), Is.GreaterThan(1f));
        Assert.That(Quaternion.Angle(pivot.localRotation, turnedRotation), Is.GreaterThan(1f));
        yield return WaitFor(() => Quaternion.Angle(pivot.localRotation, originalRotation) < 0.1f,
            5, "smooth return reaches the original orientation");
        Assert.That(Quaternion.Angle(pivot.localRotation, originalRotation), Is.LessThan(0.1f));
        Assert.That(facing.GetType().GetField("faceTo").GetValue(facing), Is.EqualTo(originalTarget));
        Assert.That(facing.GetType().GetField("facePlayer").GetValue(facing), Is.EqualTo(originalFacePlayer));
        Assert.That((int)speech.GetType().GetField("Starts").GetValue(speech), Is.Zero, "Stopping during facing must not start speech.");
    }

    private static bool FacingPlayer(Transform pivot)
    {
        var facing = pivot.GetComponent("Facing");
        return (bool)facing.GetType().GetField("isFacing").GetValue(facing);
    }

    private static IEnumerator WaitFor(Func<bool> condition, float timeout, string expectation)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, "Timed out: " + expectation);
    }
}

#endif
