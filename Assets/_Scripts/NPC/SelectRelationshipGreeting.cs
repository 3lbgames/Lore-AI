using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Select Relationship Greeting", story: "Select [Character] relationship [Greeting] or [DefaultGreeting]", category: "NPC/Memory", id: "dfcc6d7c437a46b3a22fc14f98fd05a4")]
public partial class SelectRelationshipGreeting : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> DefaultGreeting;
    [SerializeReference] public BlackboardVariable<string> Greeting;
    [SerializeReference] public BlackboardVariable<string> Band = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<bool> RelationshipLoaded = new BlackboardVariable<bool>();

    protected override Status OnStart()
    {
        Greeting.Value = DefaultGreeting.Value;
        Band.Value = ""; RelationshipLoaded.Value = false;
        var director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || !director.profile || !director.Memory.Loaded) return Status.Success;
        var definition = director.profile.relationship;
        var state = director.Memory.Relationship;
        if (!definition) return Status.Success;
        RelationshipLoaded.Value = true;
        Greeting.Value = definition.SelectGreeting(state, DefaultGreeting.Value, out var band);
        Band.Value = band;
        return Status.Success;
    }
}
