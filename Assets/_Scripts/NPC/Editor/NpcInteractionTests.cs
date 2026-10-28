using NUnit.Framework;
using System.Linq;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;

public class NpcInteractionTests
{
    internal const string PrefabPath = "Assets/_Prefabs/NPCs/Barbarian_NPC Variant.prefab";

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
            speaker.AddComponent(System.Type.GetType("Core3lb.BaseAI_TextToSpeech, Assembly-CSharp", true));
            var type = System.Type.GetType("SpeakText, Assembly-CSharp", true);
            var node = System.Activator.CreateInstance(type);
            type.GetField("Speaker").SetValue(node, new BlackboardVariable<GameObject> { Value = speaker });
            type.GetField("Text").SetValue(node, new BlackboardVariable<string> { Value = " " });
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

    [Test]
    public void SetupPreservesAuthoredConversation()
    {
        const string path = "Assets/_Behavior/Conversation.asset";
        var before = System.IO.File.ReadAllBytes(path);
        var type = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("NpcConversationSetup")).First(t => t != null);
        type.GetMethod("SetUp").Invoke(null, null);
        AssetDatabase.SaveAssets();
        Assert.That(System.IO.File.ReadAllBytes(path), Is.EqualTo(before));
    }

    [Test]
    public void BaseSceneSpeechInputsHaveConnectedRecordingIndicators()
    {
        const string path = "Assets/_Scenes/BaseScene.unity";
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        bool opened = !scene.isLoaded;
        if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            var inputs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(c => c && c.GetType().Name == "NpcSpeechInput").ToArray();
            Assert.That(inputs, Is.Not.Empty, "BaseScene must contain speech input");
            foreach (var input in inputs)
            {
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
        Assert.That(view.GetType().GetField("label").GetValue(view), Is.Not.Null);
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
