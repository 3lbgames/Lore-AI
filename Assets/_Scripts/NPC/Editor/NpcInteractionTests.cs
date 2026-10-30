using NUnit.Framework;
using System.Linq;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;

public class NpcInteractionTests
{
    internal const string PrefabPath = "Assets/_Prefabs/NPCs/Barbarian_NPC Variant.prefab";

    [Test]
    public void BaseScenePreservesDistinctNpcProfilesAndOneCampaignResetOwner()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/_Scenes/BaseScene.unity");
        try
        {
            var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c).ToArray();
            var directors = components.Where(c => c.GetType().Name == "NpcCharacterDirector").ToArray();
            Assert.That(directors.Length, Is.EqualTo(2));
            var ids = directors.Select(c => new SerializedObject(new SerializedObject(c).FindProperty("profile").objectReferenceValue)
                .FindProperty("npcId").stringValue).ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(2), "NPCs must not share a persistent identity after prefab changes");
            var mira = directors.Single(c => c.gameObject.name == "Mira_NPC");
            var profile = new SerializedObject(mira).FindProperty("profile").objectReferenceValue;
            Assert.That(AssetDatabase.GetAssetPath(profile), Is.EqualTo("Assets/_Behavior/MiraProfile.asset"));
            var overrides = new SerializedObject(mira.GetComponent<BehaviorGraphAgent>()).FindProperty("m_BlackboardVariableOverridesList");
            var greetings = Enumerable.Range(0, overrides.arraySize)
                .Select(i => (BlackboardVariable)overrides.GetArrayElementAtIndex(i).managedReferenceValue).OfType<BlackboardVariable<string>>().ToArray();
            Assert.That(greetings.Single(v => v.Name == "FirstGreeting").Value, Is.EqualTo("Hello! I'm Mira. Have you got a moment?"));
            Assert.That(greetings.Single(v => v.Name == "ReturningGreeting").Value, Is.EqualTo("Hello again! How are you?"));
            var resetOwners = components.Where(c => c.GetType().Name == "NpcCampaignControls" && c.isActiveAndEnabled).ToArray();
            Assert.That(resetOwners.Length, Is.EqualTo(1));
            Assert.That(resetOwners[0].gameObject, Is.Not.EqualTo(mira.gameObject));
            Assert.That(components.Count(c => c.GetType().Name == "NpcSpeechInput"), Is.EqualTo(1));
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void SampleRuntimeGraphContainsAuthoredOpeningSettings()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var blackboard = prefab.GetComponent<BehaviorGraphAgent>().Graph.BlackboardReference;
        Assert.That(blackboard.GetVariable<bool>("OpeningNpcInitiated", out _), Is.False);
        foreach (var name in new[] { "FirstGreeting", "ReturningGreeting", "MemoryUnavailableGreeting", "ReplyIntent", "ReplySituation", "FallbackText" })
        {
            Assert.That(blackboard.GetVariable<string>(name, out var value), Is.True, name);
            Assert.That(value.Value, Is.Not.Null.And.Not.Empty, name);
        }
    }

    [Test]
    public void NpcSpeechUsesOriginalWitPlayer()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var wit = prefab.GetComponentInChildren<Meta.WitAi.TTS.Integrations.TTSWit>(true);
        Assert.That(wit.RequestSettings.audioReadyDuration, Is.EqualTo(1.5f));
        var player = prefab.GetComponentInChildren<Meta.Voice.Audio.BaseAudioPlayer>(true);
        Assert.That(player.GetType().Name, Is.EqualTo("UnityAudioPlayer"));
    }

    [TestCase("Completed", "Success")]
    [TestCase("Cancelled", "Failure")]
    public void GraphDistinguishesSpeechCompletionAndCancellation(string outcome, string status)
    {
        var speaker = new GameObject("Speech outcome test");
        try
        {
            var speech = speaker.AddComponent(System.Type.GetType("Core3lb.BaseAI_TextToSpeech, Assembly-CSharp", true));
            var property = speech.GetType().GetProperty("Outcome");
            property.GetSetMethod(true).Invoke(speech, new[] { System.Enum.Parse(property.PropertyType, outcome) });
            var type = System.Type.GetType("SpeakText, Assembly-CSharp", true);
            var node = System.Activator.CreateInstance(type);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            type.GetField("speech", flags).SetValue(node, speech);
            var result = new BlackboardVariable<string>();
            type.GetField("SpeechResult").SetValue(node, result);
            type.GetField("SpeechError").SetValue(node, new BlackboardVariable<string>());
            Assert.That(type.GetMethod("OnUpdate", flags).Invoke(node, null).ToString(), Is.EqualTo(status));
            Assert.That(result.Value, Is.EqualTo(outcome));
        }
        finally { Object.DestroyImmediate(speaker); }
    }

    [Test]
    public void InvalidSpeechRetainsItsFailureWhenNodeEnds()
    {
        var speaker = new GameObject("Invalid speech test");
        try
        {
            var type = System.Type.GetType("SpeakText, Assembly-CSharp", true);
            var node = System.Activator.CreateInstance(type);
            type.GetField("Speaker").SetValue(node, new BlackboardVariable<GameObject> { Value = speaker });
            type.GetField("Text").SetValue(node, new BlackboardVariable<string> { Value = "Hello" });
            var result = new BlackboardVariable<string>();
            var error = new BlackboardVariable<string>();
            type.GetField("SpeechResult").SetValue(node, result);
            type.GetField("SpeechError").SetValue(node, error);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Assert.That(type.GetMethod("OnStart", flags).Invoke(node, null).ToString(), Is.EqualTo("Failure"));
            type.GetMethod("OnEnd", flags).Invoke(node, null);
            Assert.That(result.Value, Is.EqualTo("Failed"));
            Assert.That(error.Value, Is.EqualTo("invalid_speech"));
        }
        finally { Object.DestroyImmediate(speaker); }
    }

    [TestCase("Assets/_Scenes/BaseScene.unity")]
    [TestCase("Assets/_Scenes/VillageScene.unity")]
    public void SceneSpeechInputsHaveConnectedRecordingIndicators(string path)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        bool opened = !scene.isLoaded;
        if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            var inputs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(c => c && c.GetType().Name == "NpcSpeechInput").ToArray();
            Assert.That(inputs, Is.Not.Empty, path + " must contain speech input");
            foreach (var input in inputs)
            {
                var controls = input.GetComponent("NpcSpeechControls");
                Assert.That(controls, Is.Not.Null, "The shipped input needs its replaceable controls.");
                Assert.That(controls.GetType().GetField("input").GetValue(controls), Is.SameAs(input));
                var views = input.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(c => c && c.GetType().Name == "NpcSpeechStatusView").ToArray();
                Assert.That(views.Length, Is.EqualTo(1), input.name + " must have one recording indicator in the shipped scene");
                Assert.That(views[0].GetType().GetField("input").GetValue(views[0]), Is.SameAs(input));
                Assert.That(views[0].isActiveAndEnabled, Is.True);
            }
        }
        finally { if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
    }

    [Test]
    public void RecordingIndicatorIsAnAuthoredNestedPrefab()
    {
        var input = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/NpcSpeechInput.prefab");
        var view = input.GetComponentsInChildren<MonoBehaviour>(true)
            .Single(c => c && c.GetType().Name == "NpcSpeechStatusView");
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.gameObject),
            Is.EqualTo("Assets/_Prefabs/NpcSpeechStatus.prefab"));
        Assert.That(view.GetType().GetField("input").GetValue(view), Is.Not.Null);
        Assert.That(view.GetType().GetField("textUI").GetValue(view), Is.Not.Null);
        Assert.That(view.GetType().GetField("imageUI").GetValue(view), Is.Not.Null);
    }

    [Test]
    public void CaptionViewCanBeHiddenAndRestoredWithoutChangingSpeechState()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var source = prefab.GetComponentsInChildren<MonoBehaviour>(true)
            .Single(c => c && c.GetType().Name == "NpcSpeechCaptionView");
        // Clone inactive so the view can be tested without starting the NPC graph or audio.
        var parent = new GameObject("Caption test");
        parent.SetActive(false);
        try
        {
            var instance = Object.Instantiate(source.gameObject, parent.transform);
            var view = (Behaviour)instance.GetComponent("NpcSpeechCaptionView");
            var output = instance.GetComponent("NpcSpeechOutput");
            var display = instance.GetComponent("UI_TextDisplay");
            Assert.That(view.GetType().GetField("output").GetValue(view), Is.SameAs(output));
            Assert.That(view.GetType().GetField("display").GetValue(view), Is.SameAs(display));
            var holder = (GameObject)display.GetType().GetField("textHolder").GetValue(display);
            var text = (Component)display.GetType().GetField("textUI").GetValue(display);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var viewType = view.GetType();
            viewType.GetMethod("OnEnable", flags).Invoke(view, null);
            const string spoken = "An exact reply.\nSecond line.";
            output.GetType().GetMethod("SetText").Invoke(output, new object[] { spoken });
            Assert.That(holder.activeSelf, Is.True);
            Assert.That(text.GetType().GetProperty("text").GetValue(text), Is.EqualTo(spoken));
            viewType.GetMethod("OnDisable", flags).Invoke(view, null);
            Assert.That(holder.activeSelf, Is.False);
            Assert.That(output.GetType().GetProperty("Text").GetValue(output), Is.EqualTo(spoken));
            viewType.GetMethod("OnEnable", flags).Invoke(view, null);
            Assert.That(holder.activeSelf, Is.True, "A replacement or re-enabled view reads current speech.");
            output.GetType().GetMethod("Clear").Invoke(output, null);
            Assert.That(holder.activeSelf, Is.False);
            viewType.GetMethod("OnDisable", flags).Invoke(view, null);
        }
        finally { Object.DestroyImmediate(parent); }
    }

    [Test]
    public void BarbarianHasInteractionGraph()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var agent = prefab.GetComponent<BehaviorGraphAgent>();
        Assert.That(agent, Is.Not.Null, "The Barbarian needs a Behavior Graph agent.");
        Assert.That(agent.Graph, Is.Not.Null);
    }

    [Test]
    public void FacingTargetIsTheCharacterModel()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var agent = new SerializedObject(prefab.GetComponent<BehaviorGraphAgent>());
        var overrides = agent.FindProperty("m_BlackboardVariableOverridesList");
        GameObject target = null;
        for (int i = 0; i < overrides.arraySize; i++)
        {
            var variable = (BlackboardVariable)overrides.GetArrayElementAtIndex(i).managedReferenceValue;
            if (variable.Name == "FacingObject") target = (GameObject)variable.ObjectValue;
        }
        Assert.That(target, Is.EqualTo(prefab.GetComponentInChildren<Animator>(true).gameObject),
            "The graph must turn the character model, not the UI pivot.");
        Assert.That(target.GetComponent("Facing"), Is.Not.Null);
    }

    [Test]
    public void DirectTranscriptCallbacksAreDisabled()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!component) continue;
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.name != "m_MethodName" || property.stringValue != "_PlayRandom") continue;
                string callPath = property.propertyPath.Substring(0, property.propertyPath.LastIndexOf('.'));
                Assert.That(serialized.FindProperty(callPath + ".m_CallState").enumValueIndex, Is.Zero,
                    component.name + " must not start speech outside the graph.");
            }
        }
    }
}
