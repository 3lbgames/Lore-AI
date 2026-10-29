using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Remember NPC Speech Outcome", story: "Remember [Character] speech [Text] with [SpeechResult]", category: "NPC/Memory", id: "31433cecb4554f76b4ede76df6222f128")]
public partial class RememberNpcSpeech : Modifier
{
    [SerializeReference] public BlackboardVariable<GameObject> Character;
    [SerializeReference] public BlackboardVariable<string> Text;
    [SerializeReference] public BlackboardVariable<string> SpeechResult;
    NpcMemory memory;
    NpcResponseRequest turn;
    string text;
    bool recorded;
    protected override Status OnStart()
    {
        turn = null; recorded = false;
        memory = Character.Value ? Character.Value.GetComponent<NpcMemory>() : null;
        if (!memory || Child == null) return Status.Failure;
        var director = Character.Value.GetComponent<NpcCharacterDirector>();
        if (!director || !director.profile || !director.InteractionOpen) return Status.Failure;
        turn = memory.CurrentTurn ?? director.CreateRequest(""); text = Text.Value;
        StartNode(Child); return Status.Running;
    }
    protected override Status OnUpdate()
    {
        if (Child.CurrentStatus != Status.Success && Child.CurrentStatus != Status.Failure) return Status.Running;
        Record();
        return memory.IsBusy ? Status.Running : Child.CurrentStatus;
    }
    void Record()
    {
        if (recorded) return;
        recorded = true;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (memory && memory.CurrentTurn == turn) memory.CancelTurn();
            return;
        }
        if (memory) memory.RecordSpeech(turn, text, SpeechResult.Value);
    }
    // Unity Behavior ends children before their parent's OnEnd.
    protected override void OnEnd() => Record();
}
