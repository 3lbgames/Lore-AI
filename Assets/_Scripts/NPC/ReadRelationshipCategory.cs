using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Read Relationship Category", story: "Read [Character] relationship [Category] into [Value]", category: "NPC/Memory", id: "77ff612de8f74c68a409d6bdab472de1")]
public partial class ReadRelationshipCategory : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Category;
    [SerializeReference] public BlackboardVariable<int> Value = new BlackboardVariable<int>();
    [SerializeReference] public BlackboardVariable<bool> Found = new BlackboardVariable<bool>();
    [SerializeReference] public BlackboardVariable<bool> RelationshipLoaded = new BlackboardVariable<bool>();

    protected override Status OnStart()
    {
        Value.Value = 0; Found.Value = false; RelationshipLoaded.Value = false;
        var director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || !director.profile || !director.Memory.Loaded) return Status.Success;
        RelationshipLoaded.Value = true;
        var definition = director.profile.relationship;
        if (!definition) return Status.Success;
        Found.Value = definition.TryGetValue(director.Memory.Relationship, Category.Value, out var value);
        Value.Value = value;
        return Status.Success;
    }
}
