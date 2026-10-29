using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class NpcDeliveryQuestTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);

    [Test]
    public void CampaignStateReadsAuthoredKeysAndMissingKeysAreEmpty()
    {
        var type = Runtime("NpcGameStateData");
        var state = JsonUtility.FromJson("{\"revision\":3,\"values\":[{\"key\":\"delivery-one\",\"value\":\"accepted\"},{\"key\":\"gate\",\"value\":\"open\"}]}", type);
        Assert.That(type.GetMethod("Get").Invoke(state, new object[] { "delivery-one" }), Is.EqualTo("accepted"));
        Assert.That(type.GetMethod("Get").Invoke(state, new object[] { "gate" }), Is.EqualTo("open"));
        Assert.That(type.GetMethod("Get").Invoke(state, new object[] { "unseen" }), Is.EqualTo(""));
    }

    [Test]
    public void DeliveryCannotBeGrantedWithoutItsConfiguredGameState()
    {
        var go = new GameObject("Unconfigured delivery");
        go.SetActive(false);
        try
        {
            var quest = go.AddComponent(Runtime("NpcDeliveryQuest"));
            object[] args = { null, null };
            Assert.That(quest.GetType().GetMethod("CanGrant").Invoke(quest, args), Is.False);
            Assert.That(args[1], Is.EqualTo("invalid_delivery_configuration"));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void QuestRestoresAcceptedItemOnceAndHidesCompletedItem()
    {
        var host = new GameObject("Delivery state"); host.SetActive(false);
        var visual = new GameObject("Received visual");
        var pickup = new GameObject("Pickup"); pickup.transform.position = new Vector3(2, 1, 3);
        var item = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Items/GroKitGrabObject ItemID Variant.prefab"));
        try
        {
            var state = host.AddComponent(Runtime("NpcGameState"));
            var quest = host.AddComponent(Runtime("NpcDeliveryQuest")); var questType = quest.GetType();
            questType.GetField("state").SetValue(quest, state);
            questType.GetField("stateKey").SetValue(quest, "parcel");
            questType.GetField("receivedVisual").SetValue(quest, visual);
            questType.GetField("item").SetValue(quest, item.GetComponent("GroKitGrabObject"));
            questType.GetField("pickupPoint").SetValue(quest, pickup.transform);
            questType.GetMethod("OnEnable", Hidden).Invoke(quest, null);
            Assert.That(item.activeSelf, Is.False, "Unloaded progress cannot offer an item");
            Assert.That(visual.activeSelf, Is.False);
            ApplyState(state, 1, "accepted");
            Assert.That(item.activeSelf, Is.True);
            Assert.That(item.transform.position, Is.EqualTo(pickup.transform.position));
            item.transform.position = new Vector3(5, 1, 9);
            ApplyState(state, 2, "accepted");
            Assert.That(item.transform.position, Is.EqualTo(new Vector3(5, 1, 9)), "An unrelated accepted-state refresh must not move the player's item");
            ApplyState(state, 3, "completed");
            Assert.That(item.activeSelf, Is.False);
            Assert.That(visual.activeSelf, Is.True);
            Assert.That(questType.GetProperty("ObjectiveText").GetValue(quest), Is.EqualTo("Delivery completed"));
            state.GetType().GetMethod("ResetState", Hidden).Invoke(state, new object[] { "new-campaign" });
            Assert.That(item.activeSelf, Is.False);
            Assert.That(visual.activeSelf, Is.False);
            ApplyState(state, 0, "");
            Assert.That(item.activeSelf, Is.False);
            Assert.That(questType.GetProperty("ObjectiveText").GetValue(quest), Is.EqualTo("Delivery available"));
            questType.GetMethod("OnDisable", Hidden).Invoke(quest, null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(pickup);
            UnityEngine.Object.DestroyImmediate(visual);
        }
    }

    [Test]
    public void GrantUsesAuthoredRelationshipRequirementAndRejectsAlreadyAcceptedQuest()
    {
        var host = new GameObject("Delivery rules"); host.SetActive(false);
        var pickup = new GameObject("Pickup");
        var item = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Items/GroKitGrabObject ItemID Variant.prefab"));
        var profile = ScriptableObject.CreateInstance(Runtime("NpcProfile"));
        var relationship = ScriptableObject.CreateInstance(Runtime("NpcRelationshipDefinition"));
        try
        {
            JsonUtility.FromJsonOverwrite("{\"rules\":{\"categories\":[{\"id\":\"confidence\",\"initial\":0}]}}", relationship);
            profile.GetType().GetField("relationship").SetValue(profile, relationship);
            var giver = host.AddComponent(Runtime("NpcCharacterDirector"));
            giver.GetType().GetField("profile").SetValue(giver, profile);
            var memory = host.GetComponent("NpcMemory");
            memory.GetType().GetMethod("Awake", Hidden).Invoke(memory, null);
            var state = host.AddComponent(Runtime("NpcGameState"));
            var quest = host.AddComponent(Runtime("NpcDeliveryQuest")); var questType = quest.GetType();
            JsonUtility.FromJsonOverwrite("{\"stateKey\":\"parcel\",\"requiredConditions\":[{\"category\":\"confidence\",\"comparison\":\"gte\",\"value\":17}]}", quest);
            questType.GetField("state").SetValue(quest, state);
            questType.GetField("giver").SetValue(quest, giver);
            questType.GetField("recipientCharacter").SetValue(quest, giver);
            questType.GetField("recipient").SetValue(quest, host.AddComponent(Runtime("Core3lb.ItemHandoffTrigger")));
            questType.GetField("item").SetValue(quest, item.GetComponent("GroKitGrabObject"));
            questType.GetField("pickupPoint").SetValue(quest, pickup.transform);
            ApplyState(state, 0, "");
            Action<int> acceptRelationship = value => memory.GetType().GetMethod("Apply", Hidden).Invoke(memory, new[]
            {
                JsonUtility.FromJson("{\"status\":\"completed\",\"relationship\":{\"values\":[{\"category\":\"confidence\",\"value\":" + value + "}]}}", Runtime("NpcResponse"))
            });
            object[] args = { giver, null };
            acceptRelationship(16);
            Assert.That(questType.GetMethod("CanGrant").Invoke(quest, args), Is.False);
            Assert.That(args[1], Is.EqualTo("relationship_requirement_not_met"));
            acceptRelationship(17);
            Assert.That(questType.GetMethod("CanGrant").Invoke(quest, args), Is.True);
            ApplyState(state, 1, "accepted");
            Assert.That(questType.GetMethod("CanGrant").Invoke(quest, args), Is.False);
            Assert.That(args[1], Is.EqualTo("delivery_already_granted"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(pickup);
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(relationship);
        }
    }

    static void ApplyState(Component state, int revision, string value)
    {
        var response = JsonUtility.FromJson("{\"status\":\"completed\",\"gameState\":{\"revision\":" + revision +
            ",\"values\":[{\"key\":\"parcel\",\"value\":\"" + value + "\"}]}}", Runtime("NpcResponse"));
        state.GetType().GetMethod("Apply", Hidden).Invoke(state, new[] { response });
    }
}
