#if UNITY_EDITOR
using Core3lb;
using UnityEngine;

// Controlled provider for graph tests; never included in player builds.
public class NpcTestSpeech : BaseAI_TextToSpeech
{
    public float Duration = 0.05f;
    public int Starts, Completions, Cancellations;
    public string LastText;
    float finishTime;
    public override bool IsSpeechActive => Outcome == SpeechOutcome.Pending;
    public override void _Speak(string text)
    {
        _StopAudio();
        LastText = text; Starts++;
        Outcome = SpeechOutcome.Pending;
        finishTime = Time.realtimeSinceStartup + Duration;
    }
    void Update()
    {
        if (IsSpeechActive && Time.realtimeSinceStartup >= finishTime)
        { Outcome = SpeechOutcome.Completed; Completions++; }
    }
    public override void _StopAudio()
    {
        if (!IsSpeechActive) return;
        Outcome = SpeechOutcome.Cancelled; Cancellations++;
    }
}
#endif
