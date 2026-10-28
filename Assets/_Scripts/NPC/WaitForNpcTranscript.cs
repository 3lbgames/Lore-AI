using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Wait For NPC Transcript", story: "[Character] waits for [Transcript]", category: "NPC", id: "b99c6c337aec4482ae988ab0063bc83b")]
public partial class WaitForNpcTranscript : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Transcript;
    private NpcCharacterDirector director;
    protected override Status OnStart()
    {
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        if (!director) return Status.Failure;
        Transcript.Value = null; director.AcceptingTranscript = true; return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (!director.TakeTranscript(out var text)) { director.AcceptingTranscript = true; return Status.Running; }
        Transcript.Value = text; return Status.Success;
    }
    protected override void OnEnd() { if (director) director.AcceptingTranscript = false; }
}
