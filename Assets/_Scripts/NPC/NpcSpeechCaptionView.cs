using Core3lb;
using UnityEngine;

// Replace this view to render speech elsewhere. Keep it outside the hidden text holder.
public class NpcSpeechCaptionView : MonoBehaviour
{
    public NpcSpeechOutput output;
    public UI_TextDisplay display;

    private void OnEnable()
    {
        if (output) output.Changed += Refresh;
        Refresh();
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
        if (display) display._HideText();
    }
}
