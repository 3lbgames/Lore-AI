using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(NpcActionCatalog), typeof(NpcCharacterDirector))]
public class NpcActionRunner : MonoBehaviour
{
    public bool IsBusy { get; private set; }
    public bool ControlsFacing => IsBusy && activeEffect && activeEffect.ControlsFacing;
    public string LastError { get; private set; }
    public event Action<NpcActionReceipt> Finished;
    readonly HashSet<string> seen = new HashSet<string>();
    NpcActionEffect activeEffect;
    NpcCharacterDirector actor;
    int epoch;
    void Awake() => actor = GetComponent<NpcCharacterDirector>();
    void OnEnable() => NpcCampaign.Changed += CampaignChanged;
    void OnDisable() { NpcCampaign.Changed -= CampaignChanged; ResetExecution(); }
    void CampaignChanged(string world, string campaign) { if (actor && actor.worldId == world) { ResetExecution(); seen.Clear(); } }
    void ResetExecution()
    {
        epoch++; StopAllCoroutines();
        if (activeEffect) activeEffect.ResetExecution();
        activeEffect = null; IsBusy = false; LastError = null;
    }
    public bool StartProposal(NpcResponseRequest turn, NpcActionProposal proposal)
    {
        if (proposal == null) return true;
        if (!actor) actor = GetComponent<NpcCharacterDirector>();
        if (turn == null || actor.Memory.CurrentTurn != turn || !actor.InteractionOpen ||
            actor.campaignId != turn.campaignId || actor.InteractionId != turn.interactionId ||
            (!turn.npcInitiated && NpcCharacterDirector.Selected != actor)) return false;
        if (!seen.Add(turn.turnId)) return false;
        var catalog = GetComponent<NpcActionCatalog>();
        var binding = catalog.Find(proposal.actionId, proposal.targetId);
        bool offered = Array.Exists(turn.actions, a => a.id == proposal.actionId && a.targetId == proposal.targetId);
        string error = "action_not_offered";
        if (!offered || IsBusy || !catalog.Permits(binding, actor, out error))
        {
            Publish(turn, proposal, false, IsBusy ? "action_in_progress" : error);
            return false;
        }
        IsBusy = true; LastError = null; activeEffect = binding.effect;
        StartCoroutine(Run(turn, proposal, epoch));
        return true;
    }
    IEnumerator Run(NpcResponseRequest turn, NpcActionProposal proposal, int expected)
    {
        bool success = false, reported = false;
        string error = "action_no_result";
        var operation = activeEffect.Execute(actor, turn.turnId + ":effect", (ok, reason) => { success = ok; error = reason; reported = true; });
        var stack = new Stack<IEnumerator>(); stack.Push(operation);
        while (stack.Count > 0 && expected == epoch)
        {
            object next = null; bool moved = false;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception exception) { error = "execution_failed:" + exception.GetType().Name; success = false; reported = true; break; }
            if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
        if (expected != epoch) yield break;
        if (!success && activeEffect) activeEffect.ResetExecution();
        activeEffect = null; IsBusy = false;
        Publish(turn, proposal, reported && success, error);
    }
    void Publish(NpcResponseRequest turn, NpcActionProposal proposal, bool success, string error)
    {
        LastError = success ? null : error;
        Finished?.Invoke(new NpcActionReceipt { eventId = turn.turnId + ":effect", campaignId = turn.campaignId,
            actionId = proposal.actionId, targetId = proposal.targetId, succeeded = success, error = LastError });
    }
}
public sealed class NpcActionReceipt
{
    public string eventId, campaignId, actionId, targetId, error;
    public bool succeeded;
}
