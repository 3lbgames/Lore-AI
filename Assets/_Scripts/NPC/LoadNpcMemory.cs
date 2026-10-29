using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Load NPC Memory", story: "Load [Character] memory", category: "NPC/Memory", id: "28eeec88907f4f48b06a54db56accb51")]
public partial class LoadNpcMemory : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<bool> MemoryLoaded = new BlackboardVariable<bool>();
    [SerializeReference] public BlackboardVariable<bool> HasSpoken = new BlackboardVariable<bool>();
    [SerializeReference] public BlackboardVariable<string> MemoryError = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> RelationshipState = new BlackboardVariable<string>();
    NpcCharacterDirector director;
    protected override Status OnStart()
    {
        MemoryLoaded.Value = false; HasSpoken.Value = false; MemoryError.Value = null;
        RelationshipState.Value = "";
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || !director.profile) { MemoryError.Value = "invalid_character"; return Status.Failure; }
        director.Memory.Load(); return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (director.Memory.IsBusy) return Status.Running;
        MemoryLoaded.Value = director.Memory.Loaded;
        HasSpoken.Value = director.Memory.HasSpoken;
        MemoryError.Value = director.Memory.Error;
        if (director.Memory.Loaded)
        {
            var state = director.Memory.Relationship;
            RelationshipState.Value = state == null ? "" : JsonUtility.ToJson(state);
        }
        return Status.Success;
    }
}
