using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class NpcMoveEffect : NpcActionEffect
{
    public NavMeshAgent agent;
    public Transform destination;
    public Facing facing;
    public Animator animator;
    public string speedParameter = "Speed";
    [Min(0)] public float animationSpeedAtMaxVelocity = .5f;
    [Min(1)] public float timeoutSeconds = 30;
    bool moving, previousFacing, previousRotation;
    public override bool ControlsFacing => true;
    public override bool CanExecute(NpcCharacterDirector actor, out string error)
    {
        error = "destination_unreachable";
        if (!agent || !agent.isActiveAndEnabled || !agent.isOnNavMesh || !destination) return false;
        var path = new NavMeshPath();
        if (!agent.CalculatePath(destination.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        error = null; return true;
    }
    public override IEnumerator Execute(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!CanExecute(actor, out var error)) { complete(false, error); yield break; }
        moving = true; previousRotation = agent.updateRotation; agent.updateRotation = true;
        if (facing) { previousFacing = facing.isFacing; facing.isFacing = false; DG.Tweening.DOTween.Kill(facing.transform); }
        SetAnimationSpeed(0);
        agent.isStopped = false;
        if (!agent.SetDestination(destination.position)) { ResetExecution(); complete(false, "destination_unreachable"); yield break; }
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        // SetDestination may not have populated remainingDistance until the next frame.
        yield return null;
        while (agent && agent.isActiveAndEnabled && agent.isOnNavMesh && Time.realtimeSinceStartup < deadline)
        {
            SetAnimationSpeed(agent.speed > 0 ? agent.velocity.magnitude / agent.speed * animationSpeedAtMaxVelocity : 0);
            if (!agent.pathPending)
            {
                if (agent.pathStatus != NavMeshPathStatus.PathComplete) break;
                if (agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, .15f))
                { ResetExecution(); complete(true, null); yield break; }
            }
            yield return null;
        }
        ResetExecution(); complete(false, "movement_failed");
    }
    void SetAnimationSpeed(float speed)
    {
        if (animator && !string.IsNullOrEmpty(speedParameter)) animator.SetFloat(speedParameter, speed);
    }

    public override void ResetExecution()
    {
        if (!moving) return;
        moving = false;
        if (agent && agent.isActiveAndEnabled && agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped = true; agent.updateRotation = previousRotation; }
        if (facing)
        {
            var actor = GetComponentInParent<NpcCharacterDirector>();
            facing.isFacing = previousFacing && actor && actor.InteractionOpen;
        }
        SetAnimationSpeed(0);
    }
}
