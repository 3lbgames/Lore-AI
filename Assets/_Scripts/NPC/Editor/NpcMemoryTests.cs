using System;
using System.Reflection;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

public class NpcMemoryTests
{
    [Test]
    public void CampaignSurvivesReloadAndResetCreatesSeparateHistory()
    {
        var type = Type.GetType("NpcCampaign, Assembly-CSharp");
        Assert.That(type, Is.Not.Null, "Campaign identity must persist outside the scene");
        var world = "test-" + Guid.NewGuid().ToString("N");
        var current = type.GetMethod("Current");
        var reset = type.GetMethod("Reset");
        try
        {
            var first = (string)current.Invoke(null, new object[] { world });
            Assert.That(Guid.TryParse(first, out _), Is.True);
            Assert.That(current.Invoke(null, new object[] { world }), Is.EqualTo(first));
            var next = (string)reset.Invoke(null, new object[] { world });
            Assert.That(next, Is.Not.EqualTo(first));
            Assert.That(current.Invoke(null, new object[] { world }), Is.EqualTo(next));
        }
        finally { PlayerPrefs.DeleteKey("NpcCampaign." + world); PlayerPrefs.Save(); }
    }

    [Test]
    public void SelectingAnotherNpcDoesNotCancelNpcInitiatedDialogue()
    {
        var first = new GameObject("Autonomous NPC");
        var second = new GameObject("Conversation NPC");
        first.SetActive(false); second.SetActive(false);
        try
        {
            var directorType = Type.GetType("NpcCharacterDirector, Assembly-CSharp", true);
            var firstDirector = first.AddComponent(directorType);
            var secondDirector = second.AddComponent(directorType);
            directorType.GetMethod("BeginInteraction").Invoke(firstDirector, null);
            directorType.GetMethod("SelectForConversation").Invoke(firstDirector, null);
            var turnType = Type.GetType("NpcResponseRequest, Assembly-CSharp", true);
            var turn = Activator.CreateInstance(turnType);
            turnType.GetField("npcInitiated").SetValue(turn, true);
            var memory = first.GetComponent("NpcMemory");
            memory.GetType().GetMethod("BeginDialogue").Invoke(memory, new[] { turn });
            var revision = directorType.GetProperty("TurnRevision").GetValue(firstDirector);
            directorType.GetMethod("BeginInteraction").Invoke(secondDirector, null);
            directorType.GetMethod("SelectForConversation").Invoke(secondDirector, null);
            Assert.That(directorType.GetProperty("TurnRevision").GetValue(firstDirector), Is.EqualTo(revision));
            Assert.That(memory.GetType().GetProperty("CurrentTurn").GetValue(memory), Is.SameAs(turn));
            directorType.GetMethod("EndInteraction").Invoke(firstDirector, null);
            Assert.That(memory.GetType().GetProperty("CurrentTurn").GetValue(memory), Is.Null);
            directorType.GetMethod("EndInteraction").Invoke(secondDirector, null);
        }
        finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); }
    }

    [Test]
    public void HistoryBufferKeepsGeneratedSpeechSeparateFromAcknowledgedDelivery()
    {
        var go = new GameObject("History buffer test");
        try
        {
            var type = Type.GetType("Core3lb.BaseAI_Conversation, Assembly-CSharp", true);
            var buffer = go.AddComponent(type);
            var method = type.GetMethod("RestoreAcknowledgedHistory");
            Assert.That(method, Is.Not.Null, "Acknowledged events replace the local projection");
            var eventType = Type.GetType("Core3lb.ConversationEvent, Assembly-CSharp", true);
            var events = Array.CreateInstance(eventType, 2);
            for (int i = 0; i < 2; i++)
            {
                var item = Activator.CreateInstance(eventType);
                eventType.GetField("eventId").SetValue(item, "event-" + i);
                eventType.GetField("kind").SetValue(item, i == 0 ? "player_input" : "npc_speech");
                eventType.GetField("delivery").SetValue(item, i == 0 ? "completed" : "interrupted");
                eventType.GetField("text").SetValue(item, i == 0 ? " Exact input?! " : "Unheard generated speech");
                events.SetValue(item, i);
            }
            method.Invoke(buffer, new object[] { events });
            method.Invoke(buffer, new object[] { events });
            Assert.That(((IList)type.GetField("acknowledgedHistory").GetValue(buffer)).Count, Is.EqualTo(2), "restore replaces rather than appends");
            var conversation = (IList)type.GetField("conversationList").GetValue(buffer);
            Assert.That(conversation.Count, Is.EqualTo(1), "interrupted output is not remembered as spoken text");
            Assert.That(conversation[0].GetType().GetField("user").GetValue(conversation[0]), Is.EqualTo(" Exact input?! "));
            Assert.That(conversation[0].GetType().GetField("assistant").GetValue(conversation[0]), Is.Null);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
