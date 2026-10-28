using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Request NPC Response", story: "[Character] answers [Transcript] with [Response]", category: "NPC", id: "a6a761b0489f432db626260ef044c99e")]
public partial class RequestNpcResponse : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Transcript;
    [SerializeReference] public BlackboardVariable<string> Response;
    private NpcCharacterDirector director;
    private NpcProxyClient proxy;
    private int turnRevision;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        proxy = Character.Value ? Character.Value.GetComponent<NpcProxyClient>() : null;
        Response.Value = null;
        if (!director || !director.profile || !proxy || string.IsNullOrWhiteSpace(Transcript.Value)) return Status.Failure;
        if (NpcCharacterDirector.Selected != director) return Status.Failure;
        turnRevision = director.TurnRevision;
        proxy.Submit(director.CreateRequest(Transcript.Value)); return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (!director || NpcCharacterDirector.Selected != director || director.TurnRevision != turnRevision) return Status.Failure;
        if (proxy.IsPending) return Status.Running;
        Response.Value = proxy.Response?.text ?? director.profile.unavailableReply;
        return string.IsNullOrWhiteSpace(Response.Value) ? Status.Failure : Status.Success;
    }
    protected override void OnEnd() { if (proxy) proxy.Cancel(); }
}
