using Core3lb;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class NpcSpeechInput : MonoBehaviour
{
    public BaseAI_SpeechToText transcription;
    public InputXR.Controller microphoneController = InputXR.Controller.Right;
    public InputXR.Button microphoneButton = InputXR.Button.Interact;
    private bool microphonePressed;
    private NpcCharacterDirector recipient;
    private string interactionId;
    private int turnRevision;
    private bool listening;
    private bool recording;
    private GameObject statusPanel;
    private TextMeshProUGUI statusText;
    private float failureUntil;
    private float deadline;
    private void OnEnable()
    {
        if (transcription) { transcription.aiCompleted.AddListener(OnTranscript); transcription.aiFailed.AddListener(OnFailure); }
    }
    private void OnDisable()
    {
        if (transcription) { transcription.aiCompleted.RemoveListener(OnTranscript); transcription.aiFailed.RemoveListener(OnFailure); transcription._CancelListening(); }
        recipient = null; listening = false; recording = false; microphonePressed = false;
        if (statusPanel) statusPanel.SetActive(false);
    }
    public void BeginListening()
    {
        if (listening || !transcription || !NpcCharacterDirector.Selected || !NpcCharacterDirector.Selected.AcceptingTranscript) return;
        recipient = NpcCharacterDirector.Selected;
        interactionId = recipient.InteractionId;
        turnRevision = recipient.TurnRevision;
        listening = true;
        recording = true;
        failureUntil = 0;
        deadline = Time.realtimeSinceStartup + 40;
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
    { recipient = null; listening = false; recording = false; failureUntil = Time.realtimeSinceStartup + 5; }
    private void Update()
    {
        var pressed = InputXR.instance && InputXR.instance.GetButton(microphoneButton, microphoneController, true);
        if (pressed && !microphonePressed) BeginListening();
        if (!pressed && microphonePressed) EndListening();
        microphonePressed = pressed;
        if (listening && (Time.realtimeSinceStartup > deadline || !recipient || recipient != NpcCharacterDirector.Selected || recipient.InteractionId != interactionId || recipient.TurnRevision != turnRevision))
        { transcription._CancelListening(); OnFailure("capture_cancelled"); }
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        var selected = NpcCharacterDirector.Selected;
        var camera = Camera.main;
        if (!selected || !camera) { if (statusPanel) statusPanel.SetActive(false); return; }
        if (!statusPanel)
        {
            statusPanel = new GameObject("NPC Microphone Status", typeof(RectTransform), typeof(Canvas), typeof(Image));
            statusPanel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var panel = statusPanel.GetComponent<Image>();
            panel.color = new Color(0.02f, 0.02f, 0.02f, 0.85f); panel.raycastTarget = false;
            statusPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 80);
            var label = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(statusPanel.transform, false);
            statusText = label.GetComponent<TextMeshProUGUI>();
            statusText.rectTransform.sizeDelta = new Vector2(580, 76);
            statusText.fontSize = 26; statusText.alignment = TextAlignmentOptions.Center;
            statusText.raycastTarget = false;
            statusText.richText = false;
        }
        if (statusPanel.transform.parent != camera.transform) statusPanel.transform.SetParent(camera.transform, false);
        statusPanel.transform.localPosition = new Vector3(0, -0.18f, 0.7f);
        statusPanel.transform.localRotation = Quaternion.identity;
        statusPanel.transform.localScale = Vector3.one * 0.001f;
        statusPanel.SetActive(true);
        var wit = transcription as NpcWitTranscription;
        bool capturing = recording && (!wit || wit.IsRecording);
        statusText.color = capturing ? new Color(1, 0.35f, 0.35f) : Color.white;
        statusText.text = capturing ? "RECORDING\nRelease right trigger to send" :
            recording && (!wit || !wit.IsProcessing) ? "Starting microphone..." : listening ? "Processing speech..." :
            Time.realtimeSinceStartup < failureUntil ? "Speech not received. Try again." :
            selected.AcceptingTranscript ? "Hold right trigger to speak" : "Please wait...";
        if (listening && wit && !string.IsNullOrEmpty(wit.PartialTranscript))
            statusText.text = (capturing ? "RECORDING" : "Processing speech...") + "\n" + wit.PartialTranscript;
    }
    private void OnDestroy() { if (statusPanel) Destroy(statusPanel); }
#if UNITY_EDITOR || UNITY_STANDALONE
    private void OnGUI()
    {
        var selected = NpcCharacterDirector.Selected;
        if (!selected) return;
        GUI.enabled = listening || selected.AcceptingTranscript;
        if (GUI.Button(new Rect(20, 20, 200, 45), listening ? "Finish speaking" : "Talk to NPC"))
        { if (listening) EndListening(); else BeginListening(); }
        GUI.enabled = true;
    }
#endif
}
