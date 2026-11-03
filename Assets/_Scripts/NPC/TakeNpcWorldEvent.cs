using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Take NPC World Event", story: "Take [Character] world event as [Intent] in [Situation]", category: "NPC/Events", id: "e3b6f1c27d4a4f0b9a5d2c8e71f4a609")]
public partial class TakeNpcWorldEvent : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Intent = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> Situation = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> EventId = new BlackboardVariable<string>();
    protected override Status OnStart()
    {
        var inbox = Character.Value ? Character.Value.GetComponent<NpcWorldEventInbox>() : null;
        var director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        var next = inbox ? inbox.Peek() : null;
        if (next == null || !director || !director.profile) return Status.Failure;
        EventId.Value = next.eventId; Intent.Value = next.intent; Situation.Value = next.situation;
        return Status.Success;
    }
}
