using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Request NPC Response", story: "Generate [Response] for [Character] with [Intent] in [Situation]", category: "NPC", id: "a6a761b0489f432db626260ef044c99e")]
public partial class RequestNpcResponse : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Transcript;
    [SerializeReference] public BlackboardVariable<string> Response;
    [SerializeReference] public BlackboardVariable<string> RequestError;
    [SerializeReference] public BlackboardVariable<string> ActionProposal = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<bool> NpcInitiated = new BlackboardVariable<bool>();
    [SerializeReference] public BlackboardVariable<string> Intent = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> Situation = new BlackboardVariable<string>();
    private NpcCharacterDirector director;
    private NpcProxyClient proxy;
    private int turnRevision;
    private NpcResponseRequest request;
    private bool reconsidered;
    private string firstDraft;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        proxy = Character.Value ? Character.Value.GetComponent<NpcProxyClient>() : null;
        Response.Value = null; RequestError.Value = null; ActionProposal.Value = "";
        reconsidered = false;
        firstDraft = null;
        if (!director || !director.profile || !proxy || !director.InteractionOpen) return Fail("invalid_request");
        if (!director.Memory.Loaded || director.Memory.IsBusy || director.Memory.Error != null) return Fail("memory_unavailable");
        turnRevision = director.TurnRevision;
        if (NpcInitiated.Value)
        {
            if (string.IsNullOrWhiteSpace(Intent.Value)) return Fail("invalid_intent");
            request = director.CreateRequest("");
            request.npcInitiated = true;
            director.Memory.BeginDialogue(request);
        }
        else
        {
            if (NpcCharacterDirector.Selected != director) return Fail("cancelled");
            request = director.Memory.CurrentTurn;
            if (request == null || request.npcInitiated || string.IsNullOrWhiteSpace(Transcript?.Value) || request.input != Transcript.Value)
                return Fail("input_not_committed");
        }
        request.intent = Intent.Value ?? "";
        request.situation = Situation.Value ?? "";
        request.expectedRevision = director.Memory.Revision;
        var catalog = Character.Value.GetComponent<NpcActionCatalog>();
        request.actions = catalog ? catalog.Available(director) : Array.Empty<NpcActionOption>();
        if (catalog && catalog.gameState && catalog.gameState.Loaded)
            request.situation += "\nAccepted campaign state: " + JsonUtility.ToJson(catalog.gameState.Data);
        Trace("round=1 request");
        proxy.Submit(request); return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (!director || director.TurnRevision != turnRevision || director.Memory.CurrentTurn != request ||
            (!request.npcInitiated && NpcCharacterDirector.Selected != director)) return Fail("cancelled");
        if (proxy.IsPending) return Status.Running;
        if (!string.IsNullOrEmpty(proxy.Error)) return UseDraftOrFail(proxy.Error);
        var reply = proxy.Response;
        if (reply == null || reply.text == null) return UseDraftOrFail("invalid_response");
        var catalog = director.GetComponent<NpcActionCatalog>();
        bool canReconsider = !request.npcInitiated && !reconsidered && reply.actionProposal == null && catalog;
        var before = canReconsider ? catalog.Available(director) : Array.Empty<NpcActionOption>();
        director.Memory.ApplyAcceptedState(reply);
        if (canReconsider)
        {
            var available = catalog.Available(director);
            if (Array.Exists(available, action =>
                !Array.Exists(before, previous => previous.id == action.id && previous.targetId == action.targetId) &&
                !Array.Exists(request.actions, previous => previous.id == action.id && previous.targetId == action.targetId)))
            {
                // Keep the committed input and hold the draft until the final action decision.
                reconsidered = true;
                firstDraft = reply.text;
                request.actions = available;
                request.expectedRevision = director.Memory.Revision;
                request.intent += "\nReconsider this same player utterance using the accepted relationship and newly available actions. " +
                    "The earlier draft was not delivered. Choose the final reply and action together; this is not a new player turn.";
                Trace($"round=2 request reason=newly_unlocked_action available={available.Length}");
                proxy.Submit(request);
                return Status.Running;
            }
        }
        Response.Value = reply.text;
        ActionProposal.Value = reply.actionProposal == null ? "" : JsonUtility.ToJson(reply.actionProposal);
        Trace($"round={(reconsidered ? 2 : 1)} completed action={reply.actionProposal?.actionId ?? "none"}");
        return Status.Success;
    }
    private Status UseDraftOrFail(string error)
    {
        if (firstDraft == null) return Fail(error);
        Response.Value = firstDraft;
        ActionProposal.Value = "";
        Trace($"round=2 failed error={error} fallback=round_1_draft");
        return Status.Success;
    }
    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void Trace(string detail) => Debug.Log($"[NpcResponse] npc={request.npcId} turn={request.turnId} {detail}");
    private Status Fail(string error) { RequestError.Value = error; return Status.Failure; }
    protected override void OnEnd() { if (proxy) proxy.Cancel(); }
}
