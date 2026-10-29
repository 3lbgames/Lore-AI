using System.Collections.Generic;
using Core3lb;
using Unity.Behavior;
using UnityEngine;

[RequireComponent(typeof(BehaviorGraphAgent))]
public class NpcHandoffInbox : MonoBehaviour
{
    public ItemHandoffTrigger[] sources = new ItemHandoffTrigger[0];
    [Tooltip("The graph chooses whether a message interrupts, queues, or is ignored.")]
    public string channelVariable = "Interaction";
    readonly Queue<ItemHandoff> pending = new Queue<ItemHandoff>();
    readonly HashSet<string> seen = new HashSet<string>();
    bool notify;
    string campaignId;
    void OnEnable()
    {
        var director = GetComponent<NpcCharacterDirector>();
        if (director) CampaignChanged(director.worldId, NpcCampaign.Current(director.worldId));
        foreach (var source in sources) if (source) source.Transferred += Receive;
        NpcCampaign.Changed += CampaignChanged;
    }
    void OnDisable()
    {
        foreach (var source in sources) if (source) source.Transferred -= Receive;
        NpcCampaign.Changed -= CampaignChanged;
    }
    void CampaignChanged(string world, string campaign)
    {
        var director = GetComponent<NpcCharacterDirector>();
        if (!director || director.worldId != world || campaignId == campaign) return;
        campaignId = campaign;
        pending.Clear(); seen.Clear(); notify = false;
    }
    void Receive(ItemHandoff receipt)
    {
        if (receipt.recipient != gameObject || !seen.Add(receipt.eventId)) return;
        pending.Enqueue(receipt); notify = true;
    }
    void Update()
    {
        if (!notify || !GetComponent<BehaviorGraphAgent>().GetVariable<NpcInteractionChannel>(channelVariable, out var channel) || !channel.Value) return;
        notify = false;
        channel.Value.SendEventMessage();
    }
    public ItemHandoff Peek() => pending.Count > 0 ? pending.Peek() : null;
    public bool Consume(string eventId)
    {
        if (Peek()?.eventId != eventId) return false;
        pending.Dequeue(); notify = pending.Count > 0; return true;
    }
}
