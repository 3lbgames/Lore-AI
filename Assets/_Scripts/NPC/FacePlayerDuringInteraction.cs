using System;
using DG.Tweening;
using UnityEngine;
using Unity.Behavior;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Face Player During Interaction", story: "Face player with [FacingObject] during interaction",
    category: "NPC", id: "618e9eb092124ed2b6257c2f9c305ca1")]
public partial class FacePlayerDuringInteraction : Modifier
{
    [SerializeReference] public BlackboardVariable<GameObject> FacingObject;
    private Facing facing;
    private Quaternion rotation;
    private Transform target;
    private bool facePlayer;
    private bool isFacing;
    private Tween returnTurn;

    protected override Status OnStart()
    {
        facing = FacingObject.Value ? FacingObject.Value.GetComponent<Facing>() : null;
        if (!facing) return Status.Failure;
        if (returnTurn != null && returnTurn.IsActive())
        {
            returnTurn.Kill();
        }
        else
        {
            rotation = facing.transform.localRotation;
            target = facing.faceTo;
            facePlayer = facing.facePlayer;
            isFacing = facing.isFacing;
        }
        if (Child == null) return Status.Failure;
        var status = StartNode(Child);
        return status == Status.Success || status == Status.Failure ? status : Status.Running;
    }

    protected override Status OnUpdate() =>
        Child.CurrentStatus == Status.Success || Child.CurrentStatus == Status.Failure
            ? Child.CurrentStatus : Status.Running;

    protected override void OnEnd()
    {
        if (!facing) return;
        facing.transform.DOKill();
        facing.faceTo = target;
        facing.facePlayer = facePlayer;
        facing.isFacing = false;
        returnTurn = facing.transform.DOLocalRotateQuaternion(rotation, facing.lookTime)
            .SetEase(Ease.InOutSine)
            .SetLink(GameObject)
            .OnComplete(() => { if (facing) facing.isFacing = isFacing; });
    }
}
