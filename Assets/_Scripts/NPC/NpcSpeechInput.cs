using Core3lb;
using UnityEngine;

public class NpcSpeechInput : MonoBehaviour
{
    public BaseAI_SpeechToText transcription;
    private NpcCharacterDirector recipient;
    private string interactionId;
    private int turnRevision;
    private bool listening;
    private bool recording;
    public float captureTimeout = 40;
    public bool IsListening => listening;
    public bool HasRecipient => NpcCharacterDirector.Selected;
    public bool CanBeginListening => isActiveAndEnabled && !listening && transcription &&
        NpcCharacterDirector.Selected && NpcCharacterDirector.Selected.AcceptingTranscript;
    public bool IsCaptureStatusUnavailable => recording && !(transcription is INpcSpeechCaptureStatus);
    public bool IsRecording => recording && (transcription as INpcSpeechCaptureStatus)?.IsRecording == true;
    public bool IsStarting => recording && transcription is INpcSpeechCaptureStatus && !IsRecording && (transcription as INpcSpeechCaptureStatus)?.IsProcessing != true;
    public string PartialTranscript => (transcription as INpcSpeechCaptureStatus)?.PartialTranscript;
    public float LastFailureTime { get; private set; } = float.NegativeInfinity;
    private float deadline;
    private void OnEnable()
    {
        if (transcription) { transcription.aiCompleted.AddListener(OnTranscript); transcription.aiFailed.AddListener(OnFailure); }
    }
    private void OnDisable()
    {
        if (transcription) { transcription.aiCompleted.RemoveListener(OnTranscript); transcription.aiFailed.RemoveListener(OnFailure); transcription._CancelListening(); }
        recipient = null; listening = false; recording = false;
    }
    public void BeginListening()
    {
        if (!CanBeginListening) return;
        recipient = NpcCharacterDirector.Selected;
        interactionId = recipient.InteractionId;
        turnRevision = recipient.TurnRevision;
        listening = true;
        recording = true;
        LastFailureTime = float.NegativeInfinity;
        deadline = Time.realtimeSinceStartup + captureTimeout;
        transcription._StartListening();
    }
    public void EndListening()
    {
        if (listening && recording && transcription)
        { recording = false; transcription._StopRecordingAndProcess(); }
    }
    private void OnTranscript(string text)
    {
        listening = false;
        recording = false;
        if (recipient && recipient == NpcCharacterDirector.Selected && recipient.InteractionId == interactionId && recipient.TurnRevision == turnRevision)
            recipient.SubmitTranscript(text);
        recipient = null;
    }
    private void OnFailure(string reason)
    { recipient = null; listening = false; recording = false; LastFailureTime = Time.realtimeSinceStartup; }
    private void Update()
    {
        if (listening && (Time.realtimeSinceStartup > deadline || !recipient || recipient != NpcCharacterDirector.Selected || recipient.InteractionId != interactionId || recipient.TurnRevision != turnRevision))
        {
            string reason = Time.realtimeSinceStartup > deadline ? "capture_timeout" :
                !recipient || recipient != NpcCharacterDirector.Selected ? "capture_recipient_changed" :
                recipient.InteractionId != interactionId ? "capture_interaction_changed" : "capture_turn_changed";
            Debug.Log("NPC speech capture cancelled: " + reason, this);
            transcription._CancelListening();
            OnFailure(reason);
        }
    }
}
