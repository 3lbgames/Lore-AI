using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Wait For NPC Action", story: "Wait for [Character] action result", category: "NPC/Actions", id: "70e8ac0b6ff143c99fed3c51f8499f99")]
public partial class WaitForNpcAction : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    protected override Status OnStart() => Check();
    protected override Status OnUpdate() => Check();
    Status Check()
    {
        var runner = Character.Value ? Character.Value.GetComponent<NpcActionRunner>() : null;
        return runner && runner.IsBusy ? Status.Running : Status.Success;
    }
}
