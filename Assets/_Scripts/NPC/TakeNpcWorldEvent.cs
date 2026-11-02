using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// Takes the next queued world event (NpcWorldEventInbox) as an NPC-initiated Intent and Situation,
// and makes sure memory is loaded so the reaction request can run. Fails when nothing is queued,
// letting the selector fall through to the normal greeting.
[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Take NPC World Event", story: "Take [Character] world event as [Intent] in [Situation]", category: "NPC/Events", id: "e3b6f1c27d4a4f0b9a5d2c8e71f4a609")]
public partial class TakeNpcWorldEvent : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Intent = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> Situation = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> EventId = new BlackboardVariable<string>();
    NpcCharacterDirector director;
    protected override Status OnStart()
    {
        var inbox = Character.Value ? Character.Value.GetComponent<NpcWorldEventInbox>() : null;
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        var next = inbox ? inbox.Peek() : null;
        if (next == null || !director || !director.profile) return Status.Failure;
        // Consume up front: if the reaction fails we fall back to the greeting rather than retrying forever.
        // The quest has already recorded what happened in memory, so nothing is lost.
        inbox.Consume(next.eventId);
        EventId.Value = next.eventId; Intent.Value = next.intent; Situation.Value = next.situation;
        if (director.Memory.Loaded && !director.Memory.IsBusy) return Status.Success;
        if (!director.Memory.IsBusy) director.Memory.Load();
        return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (director.Memory.IsBusy) return Status.Running;
        return director.Memory.Loaded && director.Memory.Error == null ? Status.Success : Status.Failure;
    }
}
