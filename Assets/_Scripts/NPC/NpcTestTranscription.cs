#if UNITY_EDITOR
using Core3lb;
using UnityEngine.Events;

// Editor-only speech provider controlled by conversation regression tests.
public class NpcTestTranscription : BaseAI_SpeechToText
{
    public int Starts;
    public int Stops;
    public int Cancels;

    private void Awake()
    {
        aiCompleted = new UnityEvent<string>();
        aiFailed = new UnityEvent<string>();
    }

    public override void Start() { }
    public override void _StartListening() => Starts++;
    public override void _StopRecordingAndProcess() => Stops++;
    public override void _CancelListening() => Cancels++;
    public void Complete(string exactTranscript) => aiCompleted.Invoke(exactTranscript);
}
#endif
