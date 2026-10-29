using System;
using Core3lb;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Speak Text", story: "[Speaker] speaks [Text]",
    category: "Speech", id: "432a7fbe76da4e6e9e2bb5f5cb894c55")]
public partial class SpeakText : Unity.Behavior.Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Speaker;
    [SerializeReference] public BlackboardVariable<string> Text;
    [SerializeReference] public BlackboardVariable<string> SpeechResult;
    [SerializeReference] public BlackboardVariable<string> SpeechError;
    private BaseAI_TextToSpeech speech;
    private NpcSpeechOutput output;
    private bool started;

    protected override Status OnStart()
    {
        started = false;
        speech = Speaker.Value ? Speaker.Value.GetComponent<BaseAI_TextToSpeech>() : null;
        output = Speaker.Value ? Speaker.Value.GetComponent<NpcSpeechOutput>() : null;
        SpeechResult.Value = "Pending"; SpeechError.Value = null;
        if (string.IsNullOrWhiteSpace(Text.Value))
        { SpeechResult.Value = "Skipped"; return Status.Success; }
        if (!speech)
        { SpeechResult.Value = "Failed"; SpeechError.Value = "invalid_speech"; return Status.Failure; }
        started = true;
        speech._Speak(Text.Value);
        if (output) output.SetText(Text.Value);
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (speech.IsSpeechActive) return Status.Running;
        SpeechResult.Value = speech.Outcome.ToString(); SpeechError.Value = speech.SpeechError;
        return speech.Outcome == BaseAI_TextToSpeech.SpeechOutcome.Completed ? Status.Success : Status.Failure;
    }

    protected override void OnEnd()
    {
        if (!started) return;
        started = false;
        if (speech)
        {
            speech._StopAudio();
            SpeechResult.Value = speech.Outcome.ToString(); SpeechError.Value = speech.SpeechError;
        }
        if (output) output.Clear();
    }
}
