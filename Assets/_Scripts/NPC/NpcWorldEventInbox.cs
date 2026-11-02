using System.Collections.Generic;
using Unity.Behavior;
using UnityEngine;

// Queues things that happen in the world (a ladder raised, a nest burned) for an NPC to react to out loud.
// Like NpcHandoffInbox, posting pings the NPC's interaction channel; the Conversation graph's
// "Take NPC World Event" branch then generates an in-character reaction instead of the greeting.
// If the player isn't nearby, the event waits and plays the next time they approach.
[RequireComponent(typeof(BehaviorGraphAgent))]
public class NpcWorldEventInbox : MonoBehaviour
{
    public sealed class WorldEvent
    {
        public readonly string eventId, intent, situation;
        public WorldEvent(string eventId, string intent, string situation) { this.eventId = eventId; this.intent = intent; this.situation = situation; }
    }

    [Tooltip("The graph chooses whether a message interrupts, queues, or is ignored.")]
    public string channelVariable = "Interaction";
    readonly Queue<WorldEvent> pending = new Queue<WorldEvent>();
    readonly HashSet<string> seen = new HashSet<string>();
    bool notify;
    string campaignId;

    // Convenience for quests: posts to the NPC's inbox if it has one.
    public static bool Notify(Component npc, string eventId, string intent, string situation)
    {
        var inbox = npc ? npc.GetComponent<NpcWorldEventInbox>() : null;
        return inbox && inbox.Post(eventId, intent, situation);
    }

    void OnEnable()
    {
        var director = GetComponent<NpcCharacterDirector>();
        if (director) CampaignChanged(director.worldId, NpcCampaign.Current(director.worldId));
        NpcCampaign.Changed += CampaignChanged;
    }
    void OnDisable() => NpcCampaign.Changed -= CampaignChanged;
    void CampaignChanged(string world, string campaign)
    {
        var director = GetComponent<NpcCharacterDirector>();
        if (!director || director.worldId != world || campaignId == campaign) return;
        campaignId = campaign;
        pending.Clear(); seen.Clear(); notify = false;
    }
    public bool Post(string eventId, string intent, string situation)
    {
        if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(intent) || !seen.Add(eventId)) return false;
        pending.Enqueue(new WorldEvent(eventId, intent, situation ?? "")); notify = true;
        return true;
    }
    void Update()
    {
        if (!notify || !GetComponent<BehaviorGraphAgent>().GetVariable<NpcInteractionChannel>(channelVariable, out var channel) || !channel.Value) return;
        notify = false;
        channel.Value.SendEventMessage();
    }
    public WorldEvent Peek() => pending.Count > 0 ? pending.Peek() : null;
    public bool Consume(string eventId)
    {
        if (Peek()?.eventId != eventId) return false;
        pending.Dequeue(); notify = pending.Count > 0; return true;
    }
}
