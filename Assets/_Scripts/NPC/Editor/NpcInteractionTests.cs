using NUnit.Framework;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;

public class NpcInteractionTests
{
    internal const string PrefabPath = "Assets/_Prefabs/NPCs/Barbarian_NPC Variant.prefab";

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
