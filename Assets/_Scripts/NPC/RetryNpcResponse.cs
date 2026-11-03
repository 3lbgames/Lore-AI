using System;
using Unity.Behavior;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Generate NPC Response With One Retry", category: "NPC/Events", id: "4131f9cac38344a48176c8c517786092")]
public partial class RetryNpcResponse : RequestNpcResponse
{
    readonly NpcRetryOnce retry = new NpcRetryOnce();
    protected override Status OnStart() => retry.Start(base.OnStart, base.OnUpdate, base.OnEnd);
    protected override Status OnUpdate() => retry.Update();
}
