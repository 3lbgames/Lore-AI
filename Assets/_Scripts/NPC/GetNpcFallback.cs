using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Get NPC Request Fallback", story: "Use [Character] fallback for [RequestError] as [Response]", category: "NPC", id: "089288e4985b411580a925ec1cf594b1")]
public partial class GetNpcFallback : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> RequestError;
    [SerializeReference] public BlackboardVariable<string> Response;
    protected override Status OnStart()
    {
        var director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || !director.profile || NpcCharacterDirector.Selected != director ||
            string.IsNullOrEmpty(RequestError.Value) || RequestError.Value == "cancelled") return Status.Failure;
        Response.Value = director.profile.unavailableReply;
        return string.IsNullOrWhiteSpace(Response.Value) ? Status.Failure : Status.Success;
    }
}
