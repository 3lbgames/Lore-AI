using System;
using System.Collections;
using Core3lb;
using UnityEngine;

public class NpcDeliveryQuest : MonoBehaviour
{
    public NpcGameState state;
    public string stateKey;
    public BaseGrabObject item;
    public Transform pickupPoint;
    public ItemHandoffTrigger recipient;
    public GameObject receivedVisual;
    public NpcCharacterDirector recipientCharacter;
    public NpcCharacterDirector giver;
    public NpcRelationshipCondition[] requiredConditions = Array.Empty<NpcRelationshipCondition>();
    public string availableText = "Delivery available";
    public string acceptedText = "Delivery in progress";
    public string completedText = "Delivery completed";
    [TextArea] public string completionDescription = "The delivery recipient accepted the item.";
    public string completionEvidenceKey;
    public string Error { get; private set; }
    public string Status => state && state.Loaded ? state.Get(stateKey) : "";
    public string ObjectiveText => Status == "completed" ? completedText : Status == "accepted" ? acceptedText : availableText;
    bool projected, completing;
    string projectedStatus;

    void OnEnable()
    {
        if (state) state.Changed += ProjectState;
        if (recipient) recipient.Transferred += Receive;
        projected = false;
        ProjectState();
    }
    void OnDisable()
    {
        if (state) state.Changed -= ProjectState;
        if (recipient) recipient.Transferred -= Receive;
    }
    public bool CanGrant(NpcCharacterDirector actor, out string error)
    {
        error = null;
        if (!state || string.IsNullOrWhiteSpace(stateKey) || !item || !item.GetComponent<ItemID>() ||
            !pickupPoint || !recipient || !recipientCharacter || !giver || !giver.profile || actor != giver)
            error = "invalid_delivery_configuration";
        else if (!state.Loaded || state.Error != null || !actor.Memory.Loaded) error = "game_state_unavailable";
        else if (state.IsBusy || completing) error = "game_state_busy";
        else if (Status != "") error = "delivery_already_granted";
        else if (requiredConditions != null && requiredConditions.Length > 0 && (!actor.profile.relationship ||
                 !actor.profile.relationship.Matches(actor.Memory.Relationship, requiredConditions)))
            error = "relationship_requirement_not_met";
        else if (item.isGrabbed) error = "item_unavailable";
        return error == null;
    }
    public IEnumerator Grant(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!CanGrant(actor, out var error)) { complete(false, error); yield break; }
        yield return state.Transition(eventId, stateKey, "", "accepted", (success, failure) =>
        {
            Error = failure;
            complete(success, failure);
        });
    }
    void ProjectState()
    {
        if (!state || !state.Loaded)
        {
            projected = false; completing = false; Error = null;
            if (receivedVisual) receivedVisual.SetActive(false);
            Place(false);
            return;
        }
        string value = Status;
        if (projected && projectedStatus == value) return;
        projected = true; projectedStatus = value;
        if (receivedVisual) receivedVisual.SetActive(value == "completed");
        Place(value == "accepted");
    }
    void Place(bool available)
    {
        if (!item) return;
        if (item.isGrabbed) item.ForceDrop();
        if (pickupPoint) item.transform.SetPositionAndRotation(pickupPoint.position, pickupPoint.rotation);
        var body = item.myBody;
        if (body && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        item.gameObject.SetActive(available);
    }
    void Receive(ItemHandoff receipt)
    {
        if (completing || Status != "accepted" || !item || receipt.item != item.gameObject ||
            !recipientCharacter || receipt.recipient != recipientCharacter.gameObject) return;
        completing = true;
        StartCoroutine(Complete(receipt, giver.campaignId));
    }
    IEnumerator Complete(ItemHandoff receipt, string campaign)
    {
        bool accepted = false;
        string failure = null;
        yield return state.Transition(receipt.eventId, stateKey, "accepted", "completed", (success, error) =>
        { accepted = success; failure = error; });
        if (!giver || giver.campaignId != campaign) yield break;
        completing = false; Error = failure;
        giver.Memory.RecordObservedOutcome(receipt.eventId + ":delivery", accepted ? completionDescription :
            "The item was handed over, but delivery progress could not be confirmed: " + failure,
            accepted ? "completed" : "failed", accepted ? completionEvidenceKey : null);
    }
}
