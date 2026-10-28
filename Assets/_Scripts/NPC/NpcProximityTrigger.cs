using Core3lb;
using Unity.Behavior;
using UnityEngine;

[RequireComponent(typeof(BehaviorGraphAgent))]
public class NpcProximityTrigger : MonoBehaviour
{
    [Min(0.1f)] public float interactionRadius = 4f;
    [Min(0f)] public float departureGraceSeconds = 1f;
    public Transform playerOverride;
    public NpcInteractionChannel interactionChannel;

    private BehaviorGraphAgent agent;
    private NpcInteractionChannel runtimeChannel;
    private Transform selectedPlayer;
    private bool playerInside;
    private float outsideSince = -1f;

    private void Awake()
    {
        agent = GetComponent<BehaviorGraphAgent>();
    }

    private void OnEnable()
    {
        if (!runtimeChannel) runtimeChannel = Instantiate(interactionChannel);
        agent.Init();
        agent.SetVariableValue("Interaction", runtimeChannel);
        agent.SetVariableValue<GameObject>("Player", null);
        agent.SetVariableValue("InteractionActive", false);
        playerInside = false;
        outsideSince = -1f;
        selectedPlayer = null;
        agent.Start();
    }

    private void Update()
    {
        var player = playerOverride;
        if (!player && XRPlayerController.instance && XRPlayerController.instance.headCamera)
            player = XRPlayerController.instance.headCamera.transform;
        if (!player && Camera.main) player = Camera.main.transform;

        var offset = player ? player.position - transform.position : Vector3.zero;
        offset.y = 0;
        bool inside = player && offset.sqrMagnitude <= interactionRadius * interactionRadius;
        if (inside) outsideSince = -1f;
        else if (playerInside && player)
        {
            if (outsideSince < 0f) outsideSince = Time.time;
            if (Time.time - outsideSince < departureGraceSeconds) return;
        }
        if (inside == playerInside && (!inside || player == selectedPlayer)) return;

        playerInside = inside;
        selectedPlayer = inside ? player : null;
        agent.SetVariableValue("Player", selectedPlayer ? selectedPlayer.gameObject : null);
        agent.SetVariableValue("InteractionActive", inside);
        runtimeChannel.SendEventMessage();
    }

    private void OnDisable()
    {
        agent.End();
        playerInside = false;
        outsideSince = -1f;
        selectedPlayer = null;
    }

    private void OnDestroy()
    {
        if (runtimeChannel) Destroy(runtimeChannel);
    }
}
