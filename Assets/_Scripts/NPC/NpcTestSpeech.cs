#if UNITY_EDITOR
using Core3lb;
using UnityEngine;

// Controlled provider for graph tests; never included in player builds.
public class NpcTestSpeech : BaseAI_TextToSpeech
{
    public float Duration = 0.05f;
    public bool StartPlayback = true;
    public bool AdvancePlayback = true;
    public int Starts, Completions, Cancellations;
    public string LastText;
    float finishTime;
    public override bool IsSpeechActive => Outcome == SpeechOutcome.Pending;
    public override bool HasSpeechPlaybackStarted => StartPlayback && IsSpeechActive;
    public override long SpeechPlaybackProgress => AdvancePlayback && HasSpeechPlaybackStarted ? (long)(Time.realtimeSinceStartup * 1000) : 0;
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
