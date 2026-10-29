using UnityEngine;

[RequireComponent(typeof(NpcActionRunner), typeof(NpcMemory))]
public class NpcActionMemory : MonoBehaviour
{
    void OnEnable() => GetComponent<NpcActionRunner>().Finished += Record;
    void OnDisable() => GetComponent<NpcActionRunner>().Finished -= Record;
    void Record(NpcActionReceipt result)
    {
        if (result.campaignId != GetComponent<NpcCharacterDirector>().campaignId) return;
        GetComponent<NpcMemory>().RecordObservedOutcome(result.eventId,
            "Action " + result.actionId + " on " + result.targetId + (result.succeeded ? " completed." : " failed: " + result.error),
            result.succeeded ? "completed" : "failed");
    }
}
