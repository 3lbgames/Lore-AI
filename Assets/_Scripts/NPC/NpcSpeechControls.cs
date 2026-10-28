using Core3lb;
using UnityEngine;

public class NpcSpeechControls : MonoBehaviour
{
    public NpcSpeechInput input;
    public InputXR.Controller microphoneController = InputXR.Controller.Right;
    public InputXR.Button microphoneButton = InputXR.Button.Interact;
    public Rect desktopButtonRect = new Rect(20, 20, 200, 45);
    public string beginText = "Talk to NPC";
    public string finishText = "Finish speaking";
    private bool microphonePressed;

    private void Update()
    {
        if (!input || !input.isActiveAndEnabled) { microphonePressed = false; return; }
        bool pressed = InputXR.instance && InputXR.instance.GetButton(microphoneButton, microphoneController, true);
        if (pressed && !microphonePressed) input.BeginListening();
        if (!pressed && microphonePressed) input.EndListening();
        microphonePressed = pressed;
    }

    private void OnDisable()
    {
        if (microphonePressed && input) input.EndListening();
        microphonePressed = false;
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    private void OnGUI()
    {
        if (!input || !input.isActiveAndEnabled || !input.HasRecipient) return;
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && (input.IsListening || input.CanBeginListening);
        if (GUI.Button(desktopButtonRect, input.IsListening ? finishText : beginText))
        { if (input.IsListening) input.EndListening(); else input.BeginListening(); }
        GUI.enabled = wasEnabled;
    }
#endif
}
