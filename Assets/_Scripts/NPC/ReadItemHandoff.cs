using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Read Item Handoff", story: "Read [Character] handoff as [Description]", category: "NPC/Events", id: "58fbdfe89c5148479ba45a70f12c3004")]
public partial class ReadItemHandoff : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> DescriptionTemplate = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> Description = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> EventId = new BlackboardVariable<string>();
    [SerializeReference] public BlackboardVariable<string> ItemName = new BlackboardVariable<string>();
    protected override Status OnStart()
    {
        var receipt = Character.Value ? Character.Value.GetComponent<NpcHandoffInbox>()?.Peek() : null;
        if (receipt == null) return Status.Failure;
        EventId.Value = receipt.eventId; ItemName.Value = receipt.itemName;
        Description.Value = (DescriptionTemplate.Value ?? "").Replace("{item}", receipt.itemName)
            .Replace("{itemId}", receipt.itemId).Replace("{groupId}", receipt.groupId);
        return Status.Success;
    }
}
