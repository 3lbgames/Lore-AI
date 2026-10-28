using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Remember NPC Action Outcome", story: "Remember [Character] action [Description] with [Outcome]", category: "NPC/Memory", id: "a37a8a874c8d4e248411ec620df694c6")]
public partial class RememberNpcAction : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Description;
    [SerializeReference] public BlackboardVariable<string> Outcome;
    [SerializeReference] public BlackboardVariable<string> MemoryError = new BlackboardVariable<string>();
    private NpcMemory memory;
    protected override Status OnStart()
    {
        MemoryError.Value = null;
        var director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || !director.profile || string.IsNullOrWhiteSpace(Description.Value) || Description.Value.Length > 12000 ||
            (Outcome.Value != "completed" && Outcome.Value != "failed" && Outcome.Value != "interrupted"))
        { MemoryError.Value = "invalid_action_outcome"; return Status.Failure; }
        memory = director.Memory;
        memory.RecordActionOutcome(Description.Value, Outcome.Value);
        return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (memory.IsBusy) return Status.Running;
        MemoryError.Value = memory.Error;
        return memory.Loaded && MemoryError.Value == null ? Status.Success : Status.Failure;
    }
}
