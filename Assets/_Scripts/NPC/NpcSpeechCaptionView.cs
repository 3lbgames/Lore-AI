using Core3lb;
using UnityEngine;

// Replace this view to render speech elsewhere. Keep it outside the hidden text holder.
public class NpcSpeechCaptionView : MonoBehaviour
{
    public NpcSpeechOutput output;
    public UI_TextDisplay display;
    [Tooltip("Seconds the caption stays visible after the player walks away and the interaction ends.")]
    [Min(0)] public float hideDelay = 1.5f;

    private NpcCharacterDirector director;
    private float hideAt = -1f;

    private void OnEnable()
    {
        if (output) output.Changed += Refresh;
        director = GetComponentInParent<NpcCharacterDirector>();
        if (director)
        {
            director.InteractionEnded += ScheduleHide;
            director.InteractionStarted += CancelHide;
        }
        hideAt = -1f;
        Refresh();
    }

    private void Update()
    {
        if (hideAt < 0f || Time.time < hideAt) return;
        hideAt = -1f;
        HideNow();
    }

    // Player walked away: keep the last line up briefly, then clear it.
    private void ScheduleHide()
    {
        if (hideDelay <= 0f) HideNow();
        else hideAt = Time.time + hideDelay;
    }

    // Player came back before the delay ran out: keep the caption.
    private void CancelHide() => hideAt = -1f;

    private void HideNow()
    {
        if (output) output.Clear();
        else if (display) display._HideText();
    }

    private void Refresh()
    {
        if (!display) return;
        if (output && !string.IsNullOrEmpty(output.Text)) display._SetText(output.Text);
        else display._HideText();
    }

    private void OnDisable()
    {
        if (output) output.Changed -= Refresh;
        if (director)
        {
            director.InteractionEnded -= ScheduleHide;
            director.InteractionStarted -= CancelHide;
        }
        hideAt = -1f;
        if (display) display._HideText();
    }
}
