using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Select Text", story: "Set [Text] from [WhenTrue] or [WhenFalse] using [Condition]", category: "Action/Blackboard", id: "aad471cd68c74b16a1c249cc6015342d")]
public partial class SelectText : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<bool> Condition;
    [SerializeReference] public BlackboardVariable<string> WhenTrue;
    [SerializeReference] public BlackboardVariable<string> WhenFalse;
    [SerializeReference] public BlackboardVariable<string> Text;

    protected override Status OnStart()
    {
        Text.Value = Condition.Value ? WhenTrue.Value : WhenFalse.Value;
        return Status.Success;
    }
}
