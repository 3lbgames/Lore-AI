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
    private BaseAI_TextToSpeech speech;
    private UI_TextDisplay captions;

    protected override Status OnStart()
    {
        speech = Speaker.Value ? Speaker.Value.GetComponent<BaseAI_TextToSpeech>() : null;
        captions = Speaker.Value ? Speaker.Value.GetComponent<UI_TextDisplay>() : null;
        if (!speech || string.IsNullOrWhiteSpace(Text.Value)) return Status.Failure;
        speech._Speak(Text.Value);
        if (captions) captions._SetText(Text.Value);
        return Status.Running;
    }

    protected override Status OnUpdate() => speech.IsSpeechActive ? Status.Running : Status.Success;

    protected override void OnEnd()
    {
        if (speech) speech._StopAudio();
        if (captions) captions._HideText();
    }
}
