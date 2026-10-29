using System;
using System.Collections;
using UnityEngine;

public class NpcAnimationEffect : NpcActionEffect
{
    [Serializable]
    public class LayerBinding
    {
        public string layer;
        public string returnState;
    }

    public Animator animator;
    public string trigger, state;
    public LayerBinding[] layers = Array.Empty<LayerBinding>();
    [Min(.1f)] public float timeoutSeconds = 10;

    sealed class Progress
    {
        public int layer, stateHash, returnHash;
        public bool started, completed;
    }
    Progress[] progress;
    bool playing, interrupted;

    public override bool CanExecute(NpcCharacterDirector actor, out string error)
    {
        error = "animation_unavailable";
        if (!animator || !animator.isActiveAndEnabled || string.IsNullOrEmpty(state) ||
            layers == null || layers.Length == 0 ||
            !Array.Exists(animator.parameters, p => p.name == trigger && p.type == AnimatorControllerParameterType.Trigger)) return false;
        foreach (var binding in layers)
        {
            if (binding == null) return false;
            int layer = animator.GetLayerIndex(binding.layer);
            if (layer < 0 || !animator.HasState(layer, Animator.StringToHash(binding.layer + "." + state)) ||
                !animator.HasState(layer, Animator.StringToHash(binding.layer + "." + binding.returnState))) return false;
        }
        foreach (var binding in layers)
        {
            int layer = animator.GetLayerIndex(binding.layer);
            int hash = Animator.StringToHash(binding.layer + "." + state);
            if (animator.GetCurrentAnimatorStateInfo(layer).fullPathHash == hash ||
                (animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).fullPathHash == hash))
            { error = "animation_in_progress"; return false; }
        }
        error = null; return true;
    }

    public override IEnumerator Execute(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!CanExecute(actor, out var error)) { complete(false, error); yield break; }
        progress = Array.ConvertAll(layers, binding => new Progress {
            layer = animator.GetLayerIndex(binding.layer),
            stateHash = Animator.StringToHash(binding.layer + "." + state),
            returnHash = Animator.StringToHash(binding.layer + "." + binding.returnState)
        });
        playing = true; interrupted = false;
        NpcAnimationStateEvents.Entered += StateEntered;
        NpcAnimationStateEvents.Exited += StateExited;
        animator.ResetTrigger(trigger);
        animator.SetTrigger(trigger);
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (playing && animator && animator.isActiveAndEnabled && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            if (!playing) yield break;
            if (interrupted) { ResetExecution(); complete(false, "animation_interrupted"); yield break; }
            if (Array.Exists(progress, p => p.started) && Array.TrueForAll(progress, p => !p.started || p.completed))
            {
                StopObserving(); animator.ResetTrigger(trigger);
                complete(true, null); yield break;
            }
        }
        ResetExecution(); complete(false, "animation_failed");
    }

    void StateEntered(Animator source, AnimatorStateInfo stateInfo, int layer)
    {
        if (!playing || source != animator) return;
        var item = Array.Find(progress, p => p.layer == layer && p.stateHash == stateInfo.fullPathHash);
        if (item != null) item.started = true;
    }

    void StateExited(Animator source, AnimatorStateInfo stateInfo, int layer)
    {
        if (!playing || source != animator) return;
        var item = Array.Find(progress, p => p.layer == layer && p.stateHash == stateInfo.fullPathHash);
        if (item == null || !item.started) return;
        // Exit callbacks retain the departed state's time even after Animator clears transition info.
        bool clipCompleted = stateInfo.normalizedTime >= 1f || Mathf.Approximately(stateInfo.normalizedTime, 1f);
        bool normalReturn = clipCompleted &&
            (source.GetNextAnimatorStateInfo(layer).fullPathHash == item.returnHash ||
             source.GetCurrentAnimatorStateInfo(layer).fullPathHash == item.returnHash);
        if (normalReturn) item.completed = true;
        else interrupted = true;
    }

    void StopObserving()
    {
        NpcAnimationStateEvents.Entered -= StateEntered;
        NpcAnimationStateEvents.Exited -= StateExited;
        playing = false; progress = null;
    }

    void OnDisable() => ResetExecution();

    public override void ResetExecution()
    {
        if (!playing) return;
        NpcAnimationStateEvents.Entered -= StateEntered;
        NpcAnimationStateEvents.Exited -= StateExited;
        playing = false;
        if (animator)
        {
            animator.ResetTrigger(trigger);
            // Only cancellation/failure forces an owned gesture back; normal exits belong to the controller.
            if (animator.isActiveAndEnabled)
                foreach (var item in progress)
                {
                    bool transitioning = animator.IsInTransition(item.layer);
                    int next = transitioning ? animator.GetNextAnimatorStateInfo(item.layer).fullPathHash : 0;
                    bool ownReturn = transitioning && next == item.returnHash && !animator.GetAnimatorTransitionInfo(item.layer).anyState;
                    bool ownsState = animator.GetCurrentAnimatorStateInfo(item.layer).fullPathHash == item.stateHash;
                    if (next == item.stateHash || (ownsState && (!transitioning || ownReturn)))
                        animator.CrossFade(item.returnHash, .1f, item.layer);
                }
        }
        progress = null;
    }
}
