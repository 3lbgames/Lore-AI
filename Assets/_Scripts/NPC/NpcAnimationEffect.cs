using System;
using System.Collections;
using UnityEngine;

public class NpcAnimationEffect : NpcActionEffect
{
    public Animator animator;
    public string state, returnState;
    [Min(.1f)] public float timeoutSeconds = 10;
    bool playing;
    public override bool CanExecute(NpcCharacterDirector actor, out string error)
    {
        bool valid = animator && animator.isActiveAndEnabled && !string.IsNullOrEmpty(state) && animator.HasState(0, Animator.StringToHash(state));
        error = valid ? null : "animation_unavailable"; return valid;
    }
    public override IEnumerator Execute(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!CanExecute(actor, out var error)) { complete(false, error); yield break; }
        playing = true; animator.CrossFade(state, .1f);
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (animator && animator.isActiveAndEnabled && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            var current = animator.GetCurrentAnimatorStateInfo(0);
            if (current.IsName(state) && current.normalizedTime >= 1f)
            { ResetExecution(); complete(true, null); yield break; }
        }
        ResetExecution(); complete(false, "animation_failed");
    }
    public override void ResetExecution()
    {
        if (!playing) return;
        playing = false;
        if (animator && !string.IsNullOrEmpty(returnState) && animator.HasState(0, Animator.StringToHash(returnState))) animator.CrossFade(returnState, .1f);
    }
}
