using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Load NPC Game State", story: "Load [Character] campaign facts", category: "NPC/Actions", id: "a33a4847a110459686c2725857ee027e")]
public partial class LoadNpcGameState : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    NpcGameState state;
    protected override Status OnStart()
    {
        var catalog = Character.Value ? Character.Value.GetComponent<NpcActionCatalog>() : null;
        state = catalog ? catalog.gameState : null;
        if (!state) return Status.Success;
        state.StartCoroutine(state.EnsureLoaded());
        return Status.Running;
    }
    protected override Status OnUpdate() => state.IsBusy ? Status.Running : state.Loaded && state.Error == null ? Status.Success : Status.Failure;
}
