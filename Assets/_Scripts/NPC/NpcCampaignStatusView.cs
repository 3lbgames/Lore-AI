using TMPro;
using UnityEngine;
using Core3lb;

public class NpcCampaignStatusView : MonoBehaviour
{
    public NpcCampaignControls controls;
    public GameObject panel;
    public TMP_Text label;
    public Transform controllerAnchor;
    public Vector3 controllerOffset = new Vector3(0, 0.12f, 0);
    public string readyText = "Hold X to reset campaign";
    public string holdingText = "Reset campaign: {0:0}% — release to cancel";
    public string completedText = "Campaign reset — starting fresh";
    public string memoryErrorText = "Memory unavailable. Check the service connection.";
    public float confirmationSeconds = 3;
    private XRPlayerController player;

    private void OnEnable() { player = FindFirstObjectByType<XRPlayerController>(); }

    private void LateUpdate()
    {
        var camera = Camera.main;
        var anchor = controllerAnchor;
        if (!anchor && player) anchor = player.trackedLeftHand;
        bool visible = controls && controls.isActiveAndEnabled && controls.Target && camera && anchor;
        panel.SetActive(visible);
        if (!visible) return;
        var position = anchor.position + controllerOffset;
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - camera.transform.position, camera.transform.up));
        string status;
        if (Time.unscaledTime - controls.LastResetTime < confirmationSeconds) status = completedText;
        else if (controls.HoldProgress > 0) status = string.Format(holdingText, controls.HoldProgress * 100);
        else status = readyText;
        if (!string.IsNullOrEmpty(controls.Target.Memory.Error)) status += "\n" + memoryErrorText;
        label.text = status;
    }
    private void OnDisable() { if (panel) panel.SetActive(false); }
}
