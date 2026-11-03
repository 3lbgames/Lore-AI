using System;
using Unity.Behavior;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Load NPC Game State With One Retry", category: "NPC/Events", id: "a8825951f47b48d3a177e327744e7148")]
public partial class RetryNpcGameState : LoadNpcGameState
{
    readonly NpcRetryOnce retry = new NpcRetryOnce();
    protected override Status OnStart() => retry.Start(base.OnStart, base.OnUpdate, base.OnEnd);
    protected override Status OnUpdate() => retry.Update();
}
