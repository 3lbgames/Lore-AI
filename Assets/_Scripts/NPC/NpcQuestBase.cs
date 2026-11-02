using System;
using UnityEngine;

public class NpcQuestBase : MonoBehaviour
{
    public NpcGameState state;
    public string stateKey;

    public NpcRelationshipCondition[] requiredConditions = Array.Empty<NpcRelationshipCondition>();

    public string availableText = "Quest available";
    public string acceptedText = "Quest in progress";
    public string completedText = "Quest completed";
    [TextArea] public string completionDescription = "The delivery recipient accepted the item.";
    public string completionEvidenceKey;
    public string Error { get; set; }
    public string Status => state && state.Loaded ? state.Get(stateKey) : "";
    public virtual string ObjectiveText => Status == "completed" ? completedText : Status == "accepted" ? acceptedText : availableText;

    public bool Projected { get; set; }
    public bool Completing { get; set; }
    public string ProjectedStatus { get; set; }

}
