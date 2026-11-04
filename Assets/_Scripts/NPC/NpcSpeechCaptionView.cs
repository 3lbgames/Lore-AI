using Core3lb;
using UnityEngine;

// Replace this view to render speech elsewhere. Keep it outside the hidden text holder.
public class NpcSpeechCaptionView : MonoBehaviour
{
    public NpcSpeechOutput output;
    public UI_TextDisplay display;

    [Header("Hide After Walking Away")]
    [Tooltip("Seconds the caption stays visible after the player walks away and the interaction ends.")]
    [Min(0)] public float hideDelay = 1.5f;

    [Header("Hide After Speaking")]
    [Tooltip("Hide the caption a while after the NPC finishes a line, even if the player stays nearby.")]
    public bool hideAfterSpeaking = true;
    [Tooltip("Seconds the caption stays visible after the NPC stops speaking.")]
    [Min(0)] public float afterSpeakingDelay = 4f;
    [Tooltip("Speech source to watch. Found on the output's GameObject if left empty.")]
    public BaseAI_TextToSpeech speech;

    private NpcCharacterDirector director;
    private float leaveHideAt = -1f;
    private float speechHideAt = -1f;
    private bool wasSpeaking;

    private void OnEnable()
    {
        if (!speech && output) speech = output.GetComponent<BaseAI_TextToSpeech>();
        if (output) output.Changed += OnTextChanged;
        director = GetComponentInParent<NpcCharacterDirector>();
        if (director)
        {
            director.InteractionEnded += ScheduleLeaveHide;
            director.InteractionStarted += CancelLeaveHide;
        }
        leaveHideAt = -1f;
        speechHideAt = -1f;
        wasSpeaking = speech && speech.IsSpeechActive;
        Refresh();
    }

    private void Update()
    {
        // NPC just finished a line: start the after-speaking timer.
        bool speaking = speech && speech.IsSpeechActive;
        if (speaking) speechHideAt = -1f;
        else if (wasSpeaking && hideAfterSpeaking && HasText) speechHideAt = Time.time + afterSpeakingDelay;
        wasSpeaking = speaking;

        if (Expired(ref leaveHideAt) | Expired(ref speechHideAt)) HideNow();
    }

    private bool HasText => output && !string.IsNullOrEmpty(output.Text);

    private static bool Expired(ref float at)
    {
        if (at < 0f || Time.time < at) return false;
        at = -1f;
        return true;
    }

    // Player walked away: keep the last line up briefly, then clear it.
    private void ScheduleLeaveHide()
    {
        if (hideDelay <= 0f) HideNow();
        else leaveHideAt = Time.time + hideDelay;
    }

    // Player came back before the delay ran out: keep the caption.
    private void CancelLeaveHide() => leaveHideAt = -1f;

    private void HideNow()
    {
        leaveHideAt = -1f;
        speechHideAt = -1f;
        if (output) output.Clear();
        else if (display) display._HideText();
    }

    private void OnTextChanged()
    {
        // A new line replaces the old one, so drop any pending after-speaking hide.
        if (HasText) speechHideAt = -1f;
        Refresh();
    }

    private void Refresh()
    {
        if (!display) return;
        if (HasText) display._SetText(output.Text);
        else display._HideText();
    }

    private void OnDisable()
    {
        if (output) output.Changed -= OnTextChanged;
        if (director)
        {
            director.InteractionEnded -= ScheduleLeaveHide;
            director.InteractionStarted -= CancelLeaveHide;
        }
        leaveHideAt = -1f;
        speechHideAt = -1f;
        if (display) display._HideText();
    }
}
