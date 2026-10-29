using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class ItemHandoffTests
{
    [TestCase("Barbarian_NPC Variant")]
    public void BaseSceneKeepsItsConfiguredTransferAndNpcRecipient(string npcName)
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/_Scenes/BaseScene.unity");
        var hand = new GameObject("Scene handoff test hand");
        try
        {
            var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c).ToArray();
            var source = components.Single(c => c.GetType().FullName == "Core3lb.ItemHandoffTrigger"
                && ((GameObject)c.GetType().GetField("recipient").GetValue(c)).name == npcName);
            var objective = components.Single(c => c.GetType().Name == "NpcObjectiveView");
            Assert.That(objective.gameObject.activeInHierarchy, Is.True, "objective updates must remain active");
            var recipient = (GameObject)source.GetType().GetField("recipient").GetValue(source);
            Assert.That(recipient.GetComponent("NpcCharacterDirector"), Is.Not.Null);
            var inbox = recipient.GetComponent("NpcHandoffInbox");
            Assert.That(((Array)inbox.GetType().GetField("sources").GetValue(inbox)).Cast<object>(), Does.Contain(source));
            var cup = components.Single(c => c.GetType().Name == "GroKitGrabObject" && c.gameObject.name == "GroKitGrabObject - mug");
            Assert.That(cup.gameObject.activeSelf, Is.False, "quest cup waits for acceptance");
            cup.gameObject.SetActive(true);
            var giverSource = components.Single(c => c.GetType().FullName == "Core3lb.ItemHandoffTrigger"
                && ((GameObject)c.GetType().GetField("recipient").GetValue(c)).name == "Mira_NPC");
            Assert.That(giverSource.gameObject.activeInHierarchy, Is.False, "the giver cannot consume the delivery cup");
            var enter = (UnityEngine.Events.UnityEvent)source.GetType().GetField("enterEvent").GetValue(source);
            var replacement = (GameObject)enter.GetPersistentTarget(0);
            Assert.That(replacement.activeSelf, Is.False);
            enter.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            var reaction = cup.GetComponent("TriggerReaction");
            var transfer = (UnityEngine.Events.UnityEvent)reaction.GetType().GetField("triggerRight").GetValue(reaction);
            for (int i = 0; i < transfer.GetPersistentEventCount(); i++) transfer.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            var handComponent = hand.AddComponent(Type.GetType("Core3lb.BaseGrabHand, Assembly-CSharp", true));
            cup.GetType().GetField("currentHand").SetValue(cup, handComponent);
            cup.GetType().GetField("isGrabbed").SetValue(cup, true);
            handComponent.GetType().GetField("currentHeldObject").SetValue(handComponent, cup);
            source.GetType().GetMethod("OnTriggerEnter").Invoke(source, new object[] { cup.GetComponent<Collider>() });
            Assert.That(cup.gameObject.activeSelf, Is.False);
            Assert.That(replacement.activeSelf, Is.True, "the authored visual transfer remains connected");
            var receipt = source.GetType().GetProperty("LastReceipt").GetValue(source);
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.GetType().GetField("recipient").GetValue(receipt), Is.EqualTo(recipient));
        }
        finally { UnityEngine.Object.DestroyImmediate(hand); EditorSceneManager.ClosePreviewScene(scene); }
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void WrongItemUnheldItemOrMissingPhysicalTransferProducesNoReceipt(bool matches, bool held, bool transfer)
    {
        var type = Type.GetType("Core3lb.ItemHandoffTrigger, Assembly-CSharp", true);
        var receiver = new GameObject("Receiver");
        var hand = new GameObject("Player hand");
        var item = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Items/GroKitGrabObject ItemID Variant.prefab"));
        try
        {
            var trigger = receiver.AddComponent(type);
            type.GetField("enterEvent").SetValue(trigger, new UnityEngine.Events.UnityEvent());
            var reaction = item.GetComponent("TriggerReaction");
            var transferEvent = (UnityEngine.Events.UnityEvent)reaction.GetType().GetField("triggerRight").GetValue(reaction);
            for (int i = 0; i < transferEvent.GetPersistentEventCount(); i++)
                transferEvent.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            type.GetField("triggerIncorrectEnter").SetValue(trigger, new UnityEngine.Events.UnityEvent());
            type.GetField("useTriggerReaction").SetValue(trigger, transfer);
            var field = type.GetField("itemID"); field.SetValue(trigger, Enum.ToObject(field.FieldType, matches ? 3 : 10));
            field = type.GetField("groupID"); field.SetValue(trigger, Enum.ToObject(field.FieldType, 2));
            var grab = item.GetComponent("GroKitGrabObject");
            var handComponent = hand.AddComponent(Type.GetType("Core3lb.BaseGrabHand, Assembly-CSharp", true));
            grab.GetType().GetField("currentHand").SetValue(grab, held ? handComponent : null);
            grab.GetType().GetField("isGrabbed").SetValue(grab, held);
            handComponent.GetType().GetField("currentHeldObject").SetValue(handComponent, held ? grab : null);
            type.GetMethod("OnTriggerEnter").Invoke(trigger, new object[] { item.GetComponent<Collider>() });
            Assert.That(type.GetProperty("LastReceipt").GetValue(trigger), Is.Null);
            Assert.That(item.activeSelf, Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(item); UnityEngine.Object.DestroyImmediate(receiver); UnityEngine.Object.DestroyImmediate(hand); }
    }

    [TestCase(10, "Mug")]
    [TestCase(1, "Red Potion")]
    public void AcceptedGroKitItemProducesOneReceiptAfterTransfer(int itemId, string name)
    {
        var type = Type.GetType("Core3lb.ItemHandoffTrigger, Assembly-CSharp");
        Assert.That(type, Is.Not.Null, "GroKit handoffs need a reusable confirmed receipt source");
        var receiver = new GameObject("Receiver");
        var hand = new GameObject("Player hand");
        var item = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Items/GroKitGrabObject ItemID Variant.prefab"));
        try
        {
            var trigger = receiver.AddComponent(type);
            type.GetField("enterEvent").SetValue(trigger, new UnityEngine.Events.UnityEvent());
            var reaction = item.GetComponent("TriggerReaction");
            var transferEvent = (UnityEngine.Events.UnityEvent)reaction.GetType().GetField("triggerRight").GetValue(reaction);
            for (int i = 0; i < transferEvent.GetPersistentEventCount(); i++)
                transferEvent.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            var id = item.GetComponent("ItemID");
            id.GetType().GetField("DisplayName").SetValue(id, name);
            var field = id.GetType().GetField("itemIDEnum");
            field.SetValue(id, Enum.ToObject(field.FieldType, itemId));
            field = type.GetField("itemID"); field.SetValue(trigger, Enum.ToObject(field.FieldType, itemId));
            field = type.GetField("groupID"); field.SetValue(trigger, Enum.ToObject(field.FieldType, 2));
            var grab = item.GetComponent("GroKitGrabObject");
            var handComponent = hand.AddComponent(Type.GetType("Core3lb.BaseGrabHand, Assembly-CSharp", true));
            grab.GetType().GetField("currentHand").SetValue(grab, handComponent);
            grab.GetType().GetField("isGrabbed").SetValue(grab, true);
            handComponent.GetType().GetField("currentHeldObject").SetValue(handComponent, grab);
            var collider = item.GetComponent<Collider>();
            type.GetMethod("OnTriggerEnter").Invoke(trigger, new object[] { collider });
            Assert.That(item.activeSelf, Is.False, "existing TriggerReaction consumes the item");
            Assert.That(grab.GetType().GetField("isGrabbed").GetValue(grab), Is.False);
            var receipt = type.GetProperty("LastReceipt").GetValue(trigger);
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.GetType().GetField("itemName").GetValue(receipt), Is.EqualTo(name));
            Assert.That(receipt.GetType().GetField("giver").GetValue(receipt), Is.EqualTo(hand));
            type.GetMethod("OnTriggerEnter").Invoke(trigger, new object[] { collider });
            Assert.That(type.GetProperty("LastReceipt").GetValue(trigger), Is.SameAs(receipt), "duplicate callbacks retain the original receipt");
        }
        finally { UnityEngine.Object.DestroyImmediate(item); UnityEngine.Object.DestroyImmediate(receiver); UnityEngine.Object.DestroyImmediate(hand); }
    }
}
