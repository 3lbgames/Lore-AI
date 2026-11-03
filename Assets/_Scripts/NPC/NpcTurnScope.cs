using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Cancel NPC Turn On Selection Change", story: "Cancel [Character] turn when selection changes", category: "NPC", id: "8168a46391b646f4b7a15ef490f0ca30")]
public partial class NpcTurnScope : Modifier
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    public NpcCharacterDirector Director {  get; set; }
    public int Revision { get; set; }
    protected override Status OnStart()
    {
        Director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!Director || Child == null) return Status.Failure;
        Revision = Director.TurnRevision;
        var status = StartNode(Child);
        return status == Status.Success || status == Status.Failure ? status : Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (!Director || Director.TurnRevision != Revision) { EndNode(Child); return Status.Failure; }
        return Child.CurrentStatus == Status.Success || Child.CurrentStatus == Status.Failure ? Child.CurrentStatus : Status.Running;
    }
}
