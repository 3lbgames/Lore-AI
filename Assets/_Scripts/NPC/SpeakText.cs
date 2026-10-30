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
    private bool finished;
    private bool playbackObserved;
    private long playbackProgress;
    private float deadline;

    protected override Status OnStart()
    {
        started = false;
        finished = false;
        playbackObserved = false;
        speech = Speaker.Value ? Speaker.Value.GetComponent<BaseAI_TextToSpeech>() : null;
        output = Speaker.Value ? Speaker.Value.GetComponent<NpcSpeechOutput>() : null;
        SpeechResult.Value = "Pending"; SpeechError.Value = null;
        if (string.IsNullOrWhiteSpace(Text.Value))
        { SpeechResult.Value = "Skipped"; return Status.Success; }
        if (output) output.SetText(Text.Value);
        if (!speech)
            return Fail("invalid_speech");
        started = true;
        deadline = Time.realtimeSinceStartup + Mathf.Max(.1f, speech.speechStartTimeout);
        try
        {
            string caption = speech.GetCaptionText(Text.Value);
            if (output && !string.IsNullOrWhiteSpace(caption) && caption != Text.Value) output.SetText(caption);
            speech._Speak(Text.Value);
        }
        catch (Exception error) { return Fail("speech_start_failed: " + error.GetType().Name); }
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (finished) return SpeechResult.Value == "Completed" ? Status.Success : Status.Failure;
        if (!speech) return Fail("speech_unavailable");
        if (speech.Outcome == BaseAI_TextToSpeech.SpeechOutcome.Failed)
            return Fail(speech.SpeechError ?? "speech_failed");
        if (speech.IsSpeechActive)
        {
            if (!playbackObserved && speech.HasSpeechPlaybackStarted)
            {
                playbackObserved = true;
                playbackProgress = speech.SpeechPlaybackProgress;
                deadline = Time.realtimeSinceStartup + Mathf.Max(.1f, speech.speechPlaybackStallTimeout);
            }
            if (playbackObserved && speech.SpeechPlaybackProgress != playbackProgress)
            {
                playbackProgress = speech.SpeechPlaybackProgress;
                deadline = Time.realtimeSinceStartup + Mathf.Max(.1f, speech.speechPlaybackStallTimeout);
            }
            if (Time.realtimeSinceStartup >= deadline)
                return Fail(playbackObserved ? "speech_playback_stalled" : "speech_start_timeout");
            return Status.Running;
        }
        finished = true;
        SpeechResult.Value = speech.Outcome.ToString(); SpeechError.Value = speech.SpeechError;
        if (speech.Outcome != BaseAI_TextToSpeech.SpeechOutcome.Completed)
            Debug.LogWarning("NPC speech output ended: " + SpeechResult.Value + "; " + SpeechError.Value, speech);
        return speech.Outcome == BaseAI_TextToSpeech.SpeechOutcome.Completed ? Status.Success : Status.Failure;
    }

    private Status Fail(string reason)
    {
        finished = true;
        SpeechResult.Value = "Failed";
        SpeechError.Value = reason;
        Debug.LogWarning("NPC speech output abandoned: " + reason, speech);
        StopSpeech();
        started = false;
        return Status.Failure;
    }

    private void StopSpeech()
    {
        if (!speech) return;
        try { speech._StopAudio(); }
        catch (Exception error) { Debug.LogWarning("NPC speech cleanup failed: " + error.GetType().Name, speech); }
    }

    protected override void OnEnd()
    {
        if (!started) return;
        started = false;
        StopSpeech();
        if (speech && !finished)
        {
            SpeechResult.Value = speech.Outcome.ToString(); SpeechError.Value = speech.SpeechError;
        }
    }
}
