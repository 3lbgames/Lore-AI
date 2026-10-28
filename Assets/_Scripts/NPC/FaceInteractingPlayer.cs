using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Face Interacting Player", story: "Face [Player] while [InteractionActive]",
    category: "NPC", id: "5c9136a42e344bf1a33ce9e2b8de154b")]
public partial class FaceInteractingPlayer : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Player;
    [SerializeReference] public BlackboardVariable<bool> InteractionActive;
    [SerializeReference] public BlackboardVariable<GameObject> FacingObject;
    private float finishTime;

    protected override Status OnStart()
    {
        var facing = FacingObject.Value ? FacingObject.Value.GetComponent<Facing>() : null;
        if (!InteractionActive.Value || !Player.Value || !facing) return Status.Failure;
        facing.facePlayer = false;
        facing.faceTo = Player.Value.transform;
        facing.isFacing = true;
        finishTime = Time.time + facing.lookTime;
        return Status.Running;
    }

    protected override Status OnUpdate() => Time.time >= finishTime ? Status.Success : Status.Running;

}
