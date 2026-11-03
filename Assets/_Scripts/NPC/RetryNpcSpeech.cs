using System;
using Unity.Behavior;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Speak NPC Text With One Retry", category: "NPC/Events", id: "7529a0da32744c6d855b142136e2ce90")]
public partial class RetryNpcSpeech : SpeakText
{
    readonly NpcRetryOnce retry = new NpcRetryOnce();
    protected override Status OnStart() => retry.Start(base.OnStart, base.OnUpdate, base.OnEnd);
    protected override Status OnUpdate() => retry.Update();
}
