using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Load NPC Memory", story: "Load [Character] memory", category: "NPC/Memory", id: "28eeec88907f4f48b06a54db56accb51")]
public partial class LoadNpcMemory : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    NpcCharacterDirector director;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || !director.profile) return Status.Failure;
        director.Memory.Load(); return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (director.Memory.IsBusy) return Status.Running;
        return Status.Success;
    }
}
