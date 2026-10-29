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
    [SerializeReference] public BlackboardVariable<bool> NpcInitiated = new BlackboardVariable<bool>();
    [SerializeReference] public BlackboardVariable<string> Intent = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> Situation = new BlackboardVariable<string>();
    private NpcCharacterDirector director;
    private NpcProxyClient proxy;
    private int turnRevision;
    private NpcResponseRequest request;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        proxy = Character.Value ? Character.Value.GetComponent<NpcProxyClient>() : null;
        Response.Value = null; RequestError.Value = null;
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
        proxy.Submit(request); return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (!director || director.TurnRevision != turnRevision || director.Memory.CurrentTurn != request ||
            (!request.npcInitiated && NpcCharacterDirector.Selected != director)) return Fail("cancelled");
        if (proxy.IsPending) return Status.Running;
        Response.Value = proxy.Response?.text;
        if (!string.IsNullOrEmpty(proxy.Error)) return Fail(proxy.Error);
        if (string.IsNullOrWhiteSpace(Response.Value)) return Fail("empty_response");
        director.Memory.ApplyAcceptedState(proxy.Response);
        return Status.Success;
    }
    private Status Fail(string error) { RequestError.Value = error; return Status.Failure; }
    protected override void OnEnd() { if (proxy) proxy.Cancel(); }
}
