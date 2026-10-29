using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Commit NPC Player Input", story: "Commit [Transcript] to [Character] memory", category: "NPC/Memory", id: "b5cd5da1a72c4d5fbcc283c94d3190fd")]
public partial class CommitNpcInput : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Transcript;
    [SerializeReference] public BlackboardVariable<string> RequestError;
    NpcCharacterDirector director;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        RequestError.Value = null;
        if (!director || !director.Memory.Loaded || director.Memory.Error != null)
        { RequestError.Value = director ? director.Memory.Error ?? "memory_unavailable" : "invalid_character"; return Status.Failure; }
        if (NpcCharacterDirector.Selected != director || string.IsNullOrWhiteSpace(Transcript.Value))
        { RequestError.Value = "cancelled"; return Status.Failure; }
        director.Memory.BeginTurn(director.CreateRequest(Transcript.Value));
        return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (director.Memory.IsBusy) return Status.Running;
        RequestError.Value = director.Memory.Error;
        return director.Memory.Loaded && RequestError.Value == null ? Status.Success : Status.Failure;
    }
}
