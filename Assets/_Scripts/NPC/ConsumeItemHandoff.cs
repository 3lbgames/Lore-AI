using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Consume Item Handoff", story: "Consume [Character] handoff [EventId]", category: "NPC/Events", id: "428c9c149a76470d8b5c726400dc1304")]
public partial class ConsumeItemHandoff : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> EventId;
    protected override Status OnStart() => Character.Value && Character.Value.GetComponent<NpcHandoffInbox>()?.Consume(EventId.Value) == true ? Status.Success : Status.Failure;
}
