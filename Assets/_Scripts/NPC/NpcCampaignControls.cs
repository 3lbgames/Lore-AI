using UnityEngine;
using Core3lb;

public class NpcCampaignControls : MonoBehaviour
{
    public NpcCharacterDirector character;
    public Rect buttonRect = new Rect(20, 75, 200, 40);
    public string resetText = "Reset campaign";
    public InputXR.Controller resetController = InputXR.Controller.Left;
    public InputXR.Button resetButton = InputXR.Button.AltInteract;
    [Min(0.1f)] public float holdSeconds = 2;
    public float HoldProgress { get; private set; }
    public float LastResetTime { get; private set; } = float.NegativeInfinity;
    public NpcCharacterDirector Target => character ? character : NpcCharacterDirector.Selected;
    private bool resetUntilRelease;

    private void Update()
    {
        bool pressed = InputXR.instance && InputXR.instance.GetButton(resetButton, resetController, true);
        if (!pressed) { HoldProgress = 0; resetUntilRelease = false; return; }
        if (resetUntilRelease || !Target) return;
        HoldProgress = Mathf.Clamp01(HoldProgress + Time.unscaledDeltaTime / Mathf.Max(0.1f, holdSeconds));
        if (HoldProgress >= 1) { resetUntilRelease = true; ResetCampaign(); }
    }
    private void OnDisable() { HoldProgress = 0; resetUntilRelease = true; }
    private void OnApplicationFocus(bool focused)
    {
        if (!focused) { HoldProgress = 0; resetUntilRelease = true; }
    }
    public void ResetCampaign()
    {
        if (!Target) return;
        Target.ResetCampaign();
        LastResetTime = Time.unscaledTime;
    }
#if UNITY_EDITOR || UNITY_STANDALONE
    void OnGUI()
    {
        var target = Target;
        if (!target) return;
        if (GUI.Button(buttonRect, resetText)) ResetCampaign();
        if (!string.IsNullOrEmpty(target.Memory.Error))
            GUI.Label(new Rect(buttonRect.x, buttonRect.yMax + 5, 420, 40), "Memory unavailable: " + target.Memory.Error);
    }
#endif
}
