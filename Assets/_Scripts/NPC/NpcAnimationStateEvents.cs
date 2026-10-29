using System;
using UnityEngine;

// Reports Animator evaluation callbacks even when a frame spans an entire transition.
public class NpcAnimationStateEvents : StateMachineBehaviour
{
    public static event Action<Animator, AnimatorStateInfo, int> Entered;
    public static event Action<Animator, AnimatorStateInfo, int> Exited;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        => Entered?.Invoke(animator, stateInfo, layerIndex);

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        => Exited?.Invoke(animator, stateInfo, layerIndex);
}
