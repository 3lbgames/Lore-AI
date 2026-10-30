using Core3lb;
using TMPro;
using UnityEngine;

public class NpcObjectiveView : MonoBehaviour
{
    public NpcDeliveryQuest quest;
    public GameObject panel;
    public TMP_Text label;
    public bool attachToController = true;
    public Transform controllerAnchor;
    public Vector3 controllerOffset = new Vector3(0, 0.22f, 0);
    public string loadingText = "Loading objective…";
    public string errorText = "Objective progress unavailable";
    XRPlayerController player;

    void OnEnable() => player = FindFirstObjectByType<XRPlayerController>();
    void LateUpdate()
    {
        var camera = Camera.main;
        var anchor = controllerAnchor ? controllerAnchor : player ? player.trackedLeftHand : null;
        bool visible = quest && panel && label && camera && anchor;
        if (panel) panel.SetActive(visible);
        if (!visible) return;
        if (attachToController)
        {
            var position = anchor.position + controllerOffset;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - camera.transform.position, camera.transform.up));
        }
        string text = !quest.state || !quest.state.Loaded ? loadingText : quest.ObjectiveText;
        if (!string.IsNullOrEmpty(quest.Error) || (quest.state && !string.IsNullOrEmpty(quest.state.Error)))
            text += "\n" + errorText;
        if (label.text != text) label.text = text;
    }
    void OnDisable() { if (panel) panel.SetActive(false); }
}
