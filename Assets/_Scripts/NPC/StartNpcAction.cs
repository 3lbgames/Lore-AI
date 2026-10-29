using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Start NPC Action", story: "Start [Character] action [Proposal]", category: "NPC/Actions", id: "ab45ab3f945a462dbca558135145fb26")]
public partial class StartNpcAction : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Proposal = new BlackboardVariable<string>();
    protected override Status OnStart()
    {
        if (string.IsNullOrEmpty(Proposal.Value)) return Status.Success;
        var actor = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        var runner = Character.Value ? Character.Value.GetComponent<NpcActionRunner>() : null;
        if (!actor || !runner) return Status.Failure;
        NpcActionProposal command;
        try { command = JsonUtility.FromJson<NpcActionProposal>(Proposal.Value); }
        catch (ArgumentException) { return Status.Failure; }
        runner.StartProposal(actor.Memory.CurrentTurn, command);
        // Rejection is an action outcome, not a reason to speak the request fallback.
        return Status.Success;
    }
}
