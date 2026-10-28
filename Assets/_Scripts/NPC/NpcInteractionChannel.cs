using Unity.Behavior;
using UnityEngine;

[CreateAssetMenu(menuName = "NPC/Interaction Channel")]
[EventChannelDescription(name: "NPC Interaction", message: "Interaction changed",
    id: "a714393cb62544e4b00c0069dc4e0a11")]
public class NpcInteractionChannel : EventChannel { }
