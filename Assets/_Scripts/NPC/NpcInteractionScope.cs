using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "NPC Interaction Scope", story: "Open interaction for [Character]", category: "NPC", id: "a680c2445c0647378d354ab502a85011")]
public partial class NpcInteractionScope : Modifier
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    private NpcCharacterDirector director;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || Child == null) return Status.Failure;
        director.BeginInteraction();
        var status = StartNode(Child);
        return status == Status.Success || status == Status.Failure ? status : Status.Running;
    }
    protected override Status OnUpdate() => Child.CurrentStatus == Status.Success || Child.CurrentStatus == Status.Failure ? Child.CurrentStatus : Status.Running;
    protected override void OnEnd() { if (director) director.EndInteraction(); }
}
