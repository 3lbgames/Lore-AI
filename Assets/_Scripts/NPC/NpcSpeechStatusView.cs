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
    public string recordingText = "RECORDING";
    public string releaseText = "Release right trigger to send";
    public string unavailableText = "Microphone status unavailable";
    public string startingText = "Starting microphone...";
    public string processingText = "Processing speech...";
    public string failureText = "Speech not received. Try again.";
    public string readyText = "Hold right trigger to speak";
    public string waitingText = "Please wait...";

    private void LateUpdate()
    {
        var selected = NpcCharacterDirector.Selected;
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
        else status = selected.AcceptingTranscript ? readyText : waitingText;
        textUI.text = status;
    }
    private void OnDisable() { if (panel) panel.SetActive(false); }
}
