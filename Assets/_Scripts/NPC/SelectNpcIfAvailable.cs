using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Select NPC If Available", story: "Select [Character] if no NPC is selected", category: "NPC", id: "382e4c3a191641b6b4410a217ecb2282")]
public partial class SelectNpcIfAvailable : Modifier
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    private NpcCharacterDirector director;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director || Child == null) return Status.Failure;
        if (!NpcCharacterDirector.Selected) director.SelectForConversation();
        var status = StartNode(Child);
        return status == Status.Success || status == Status.Failure ? status : Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (!NpcCharacterDirector.Selected && director) director.SelectForConversation();
        return Child.CurrentStatus == Status.Success || Child.CurrentStatus == Status.Failure ? Child.CurrentStatus : Status.Running;
    }
}
