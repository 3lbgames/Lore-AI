//using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NpcSpeechStatusView : MonoBehaviour
{
    public NpcSpeechInput input;
    public GameObject panel;
    //public TMP_Text label;
    public Text textUI;
    public Image imageUI;
    public Vector3 cameraOffset = new Vector3(0, -0.18f, 0.7f);
    public Color recordingColor = new Color(1, 0.35f, 0.35f);
    public Color normalColor = Color.white;
    public float failureDuration = 5;
    [Min(0)] public float transcriptDuration = 8;
    public string transcriptPrefix = "You: ";
    public string recordingText = "RECORDING";
    public string releaseText = "Release right trigger to send";
    public string unavailableText = "Microphone status unavailable";
    public string startingText = "Starting microphone...";
    public string processingText = "Processing speech...";
    public string failureText = "Speech not received. Try again.";
    public string readyText = "Hold right trigger to speak";
    public string waitingText = "Please wait...";

    private string acceptedTranscript;
    private float acceptedAt;
    private NpcCharacterDirector transcriptRecipient;

    private void OnEnable()
    {
        if (!input) return;
        input.CaptureStarted += ClearTranscript;
        input.TranscriptAccepted += ShowTranscript;
    }

    private void ShowTranscript(string text)
    {
        acceptedTranscript = text;
        acceptedAt = Time.realtimeSinceStartup;
        transcriptRecipient = NpcCharacterDirector.Selected;
    }

    private void ClearTranscript()
    {
        acceptedTranscript = null;
        transcriptRecipient = null;
    }

    private void LateUpdate()
    {
        var selected = NpcCharacterDirector.Selected;
        if (selected != transcriptRecipient || Time.realtimeSinceStartup - acceptedAt >= transcriptDuration)
            ClearTranscript();
        var camera = Camera.main;
        bool visible = input && input.isActiveAndEnabled && selected && camera;
        panel.SetActive(visible);
        if (!visible) return;
        transform.SetPositionAndRotation(camera.transform.TransformPoint(cameraOffset), camera.transform.rotation);
        textUI.color = input.IsRecording ? recordingColor : normalColor;
        imageUI.color = input.IsRecording ? recordingColor : normalColor;
        string status;
        if (input.IsListening && !string.IsNullOrEmpty(input.PartialTranscript))
            status = (input.IsRecording ? recordingText : processingText) + "\n" + input.PartialTranscript;
        else if (input.IsRecording) status = recordingText + "\n" + releaseText;
        else if (input.IsCaptureStatusUnavailable) status = unavailableText;
        else if (input.IsStarting) status = startingText;
        else if (input.IsListening) status = processingText;
        else if (Time.realtimeSinceStartup - input.LastFailureTime < failureDuration) status = failureText;
        else if (!string.IsNullOrEmpty(acceptedTranscript))
            status = transcriptPrefix + acceptedTranscript + "\n" + (selected.AcceptingTranscript ? readyText : waitingText);
        else status = selected.AcceptingTranscript ? readyText : waitingText;
        textUI.text = status;
    }
    private void OnDisable()
    {
        if (input)
        {
            input.CaptureStarted -= ClearTranscript;
            input.TranscriptAccepted -= ShowTranscript;
        }
        ClearTranscript();
        if (panel) panel.SetActive(false);
    }
}
