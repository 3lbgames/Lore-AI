using System;
using System.Collections;
using Core3lb;
using DG.Tweening;
using UnityEngine;
using UnityEngine.AI;

// Ranger-on-the-roof quest. Two ways to finish:
//  1. Raise the ladder (RaiseLadder), then talk Flynn into using it (NpcHouseFireDescendEffect -> DescendByLadder).
//  2. Set the house on fire with the lit torch (IgniteHouse); Flynn jumps down without being asked.
// State key values: "" (stuck) -> "ladder_raised" -> "rescued_ladder", or "" / "ladder_raised" -> "rescued_fire".
public class NpcHouseFireQuest : NpcQuestBase
{
    public const string LadderRaised = "ladder_raised", RescuedByLadder = "rescued_ladder", RescuedByFire = "rescued_fire";

    [Header("Characters")]
    public NpcCharacterDirector ranger;
    [Tooltip("Villagers who learn the player burned the house down.")]
    public NpcCharacterDirector[] witnesses = Array.Empty<NpcCharacterDirector>();

    [Header("World")]
    public DOTweenAnimation ladderTween;
    public Collider ladderInteractCollider;
    public ParticleSystem houseFlames;
    public ParticleSystem headSmoke;
    [Tooltip("Torches returned to their start, unlit, when the campaign resets.")]
    public BaseGrabObject[] torches = Array.Empty<BaseGrabObject>();
    public string litTriggerName = "FireTrigger - lit OOS", unlitTriggerName = "FireTrigger - unlit";

    [Header("Ranger movement")]
    public Transform ladderTop;
    public Transform ladderBottom;
    public Transform fireLanding;
    public Facing facing;
    public Animator animator;
    public string speedParameter = "Speed";
    [Min(0.1f)] public float roofWalkSpeed = 1.2f;
    [Min(0.1f)] public float climbSeconds = 3f;
    [Min(0.1f)] public float jumpSeconds = 0.9f;
    [Min(0)] public float jumpHeight = 1.2f;
    [Min(0)] public float fireScareDelay = 0.6f;

    [Header("Objective text")]
    public string ladderRaisedText = "The ladder is up. Talk Flynn down";
    public string fireCompletedText = "Flynn is down. The house, less so";

    [Header("Memory")]
    [TextArea] public string ladderRaisedDescription =
        "The player raised a ladder against the roof I am stuck on. I can climb down it if I choose to.";
    [TextArea] public string fireDescription =
        "The player set the house I was standing on on fire with a torch. I had to jump off the roof to escape the flames. Reckless, but it worked, and I am grudgingly impressed.";
    [TextArea] public string witnessFireDescription =
        "The player deliberately set a house in the village on fire with a torch to scare Flynn the ranger off its roof.";

    [Header("Spoken reactions (NpcWorldEventInbox)")]
    [TextArea] public string ladderReactionIntent =
        "React out loud, in character, to the player having just raised a ladder against the roof you are stuck on. Protect your pride: you can grudgingly acknowledge it, but do not climb down yet unless they have already talked you into it.";
    [TextArea] public string fireReactionIntent =
        "React out loud, in character, having just been forced to jump off the roof because the player set the house on fire beneath you.";

    Vector3 roofPosition;
    Quaternion roofRotation;
    bool moving, previousFacing, eventPending, pendingWorldReset = true;
    Pose[] torchPoses = Array.Empty<Pose>();

    public override string ObjectiveText
    {
        get
        {
            switch (Status)
            {
                case LadderRaised: return ladderRaisedText;
                case RescuedByLadder: return completedText;
                case RescuedByFire: return fireCompletedText;
                default: return availableText;
            }
        }
    }

    void Awake()
    {
        if (ranger) { roofPosition = ranger.transform.position; roofRotation = ranger.transform.rotation; }
        torchPoses = new Pose[torches.Length];
        for (int i = 0; i < torches.Length; i++)
            if (torches[i]) torchPoses[i] = new Pose(torches[i].transform.position, torches[i].transform.rotation);
    }
    void OnEnable()
    {
        if (state) state.Changed += ProjectState;
        Projected = false;
        ProjectState();
    }
    void OnDisable()
    {
        if (state) state.Changed -= ProjectState;
        StopMovement();
        Completing = false; eventPending = false;
    }

    bool Configured => state && !string.IsNullOrWhiteSpace(stateKey) && ranger && ranger.profile;

    // ---- World events (wired from UnityEvents) ----

    // Ladder XRInteract.interact
    public void RaiseLadder()
    {
        if (!Configured || eventPending || Completing || Status != "") return;
        eventPending = true;
        StartCoroutine(RecordLadder(Guid.NewGuid().ToString("N"), ranger.campaignId));
    }
    IEnumerator RecordLadder(string eventId, string campaign)
    {
        bool ok = false; string failure = null;
        yield return state.Transition(eventId, stateKey, "", LadderRaised, (success, error) => { ok = success; failure = error; });
        eventPending = false; Error = failure;
        if (!ok || !ranger || ranger.campaignId != campaign) yield break;
        ranger.Memory.RecordObservedOutcome(eventId + ":ladder", ladderRaisedDescription, "completed");
        NpcWorldEventInbox.Notify(ranger, eventId + ":ladder", ladderReactionIntent, ladderRaisedDescription);
    }

    // HouseFire/FireTrigger ItemIDTrigger.enterEvent
    public void IgniteHouse()
    {
        if (!Configured || eventPending || Completing) return;
        string from = Status;
        if (from != "" && from != LadderRaised) return;
        Completing = true;
        StartCoroutine(BurnOut(Guid.NewGuid().ToString("N"), from, ranger.campaignId));
    }
    IEnumerator BurnOut(string eventId, string from, string campaign)
    {
        bool ok = false; string failure = null;
        yield return state.Transition(eventId, stateKey, from, RescuedByFire, (success, error) => { ok = success; failure = error; });
        if (!ranger || ranger.campaignId != campaign) { Completing = false; yield break; }
        Error = failure;
        if (!ok) { Completing = false; Reproject(); yield break; }
        ranger.Memory.RecordObservedOutcome(eventId + ":fire", fireDescription, "completed", completionEvidenceKey);
        foreach (var witness in witnesses)
            if (witness && witness.isActiveAndEnabled && witness.campaignId == campaign)
                witness.Memory.RecordObservedOutcome(eventId + ":witness:" + witness.profile.npcId, witnessFireDescription, "completed");
        // Interrupt any ladder-climb in progress and let the fire scare him off the roof.
        StopMovement();
        // Speak when he sees the fire.
        if (ranger && ranger.campaignId == campaign)
            NpcWorldEventInbox.Notify(ranger, eventId + ":fire", fireReactionIntent, fireDescription);
        yield return new WaitForSeconds(fireScareDelay);
        yield return Jump(fireLanding);
        headSmoke.Play();
        Completing = false;
        //// Speak once he's on the ground.
        //if (ranger && ranger.campaignId == campaign)
        //    NpcWorldEventInbox.Notify(ranger, eventId + ":fire", fireReactionIntent, fireDescription);
    }

    // ---- Conversation action (NpcHouseFireDescendEffect) ----

    public bool CanDescend(NpcCharacterDirector actor, out string error)
    {
        error = null;
        if (!Configured || !ladderTop || !ladderBottom || actor != ranger) error = "invalid_house_fire_configuration";
        else if (!state.Loaded || state.Error != null || !actor.Memory.Loaded) error = "game_state_unavailable";
        else if (state.IsBusy || Completing || eventPending || moving) error = "game_state_busy";
        else if (Status == "") error = "no_ladder";
        else if (Status != LadderRaised) error = "already_down";
        else if (requiredConditions != null && requiredConditions.Length > 0 && (!actor.profile.relationship ||
                 !actor.profile.relationship.Matches(actor.Memory.Relationship, requiredConditions)))
            error = "relationship_requirement_not_met";
        return error == null;
    }
    public IEnumerator DescendByLadder(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!CanDescend(actor, out var error)) { complete(false, error); yield break; }
        Completing = true;
        string campaign = actor.campaignId;
        bool ok = false; string failure = null;
        yield return state.Transition(eventId, stateKey, LadderRaised, RescuedByLadder, (success, e) => { ok = success; failure = e; });
        Error = failure;
        if (!ok || actor.campaignId != campaign) { Completing = false; complete(false, failure ?? "campaign_changed"); yield break; }
        yield return Climb();
        Completing = false;
        if (Status == RescuedByLadder)
            actor.Memory.RecordObservedOutcome(eventId + ":rescue", completionDescription, "completed", completionEvidenceKey);
        complete(true, null);
    }
    public void ResetExecution() { if (moving && Status != RescuedByFire) { StopMovement(); Reproject(); } }

    // ---- Movement ----

    IEnumerator Climb()
    {
        BeginMovement();
        var t = ranger.transform;
        var top = ladderTop.position;
        float walk = Vector3.Distance(t.position, top) / roofWalkSpeed;
        Face(top - t.position);
        SetSpeed(.5f);
        yield return Tween(t.DOMove(top, walk).SetEase(Ease.Linear));
        if (!moving) yield break;
        Face(ladderTop.forward);
        SetSpeed(.25f);
        yield return Tween(t.DOMove(ladderBottom.position, climbSeconds).SetEase(Ease.Linear));
        if (!moving) yield break;
        EndMovement(ladderBottom.position, ladderBottom.rotation);
    }
    IEnumerator Jump(Transform landing)
    {
        if (!landing) yield break;
        BeginMovement();
        var t = ranger.transform;
        Face(landing.position - t.position);
        animator.SetBool("Jump", true);
        yield return Tween(t.DOJump(landing.position, jumpHeight, 1, jumpSeconds).SetEase(Ease.Linear).OnComplete(Land));
        if (!moving) yield break;
        EndMovement(landing.position, landing.rotation);
    }
    void Land()
    {
        animator.SetBool("Jump", false);
    }
    IEnumerator Tween(Tween tween)
    {
        tween.SetId(this);
        while (moving && tween.IsActive() && !tween.IsComplete()) yield return null;
    }
    void Face(Vector3 direction)
    {
        direction.y = 0;
        if (direction.sqrMagnitude > .0001f) ranger.transform.rotation = Quaternion.LookRotation(direction);
    }
    void SetSpeed(float speed)
    {
        if (animator && !string.IsNullOrEmpty(speedParameter)) animator.SetFloat(speedParameter, speed);
    }
    NavMeshAgent Agent => ranger ? ranger.GetComponent<NavMeshAgent>() : null;
    void BeginMovement()
    {
        StopMovement();
        moving = true;
        var agent = Agent;
        if (agent) agent.enabled = false;
        if (facing) { previousFacing = facing.isFacing; facing.isFacing = false; DOTween.Kill(facing.transform); }
    }
    void EndMovement(Vector3 position, Quaternion rotation)
    {
        moving = false;
        SetSpeed(0);
        PlaceRanger(position, rotation);
        if (facing) facing.isFacing = previousFacing && ranger && ranger.InteractionOpen;
    }
    void StopMovement()
    {
        DOTween.Kill(this);
        if (!moving) return;
        moving = false;
        SetSpeed(0);
        var agent = Agent;
        if (agent && !agent.enabled) agent.enabled = true;
        if (facing) facing.isFacing = previousFacing && ranger && ranger.InteractionOpen;
    }
    void PlaceRanger(Vector3 position, Quaternion rotation)
    {
        if (!ranger) return;
        var agent = Agent;
        if (agent && !agent.enabled) agent.enabled = true;
        if (agent && agent.isActiveAndEnabled && NavMesh.SamplePosition(position, out var hit, 1.5f, agent.areaMask))
        {
            agent.Warp(hit.position);
            if (agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped = true; }
        }
        else ranger.transform.position = position;
        ranger.transform.rotation = rotation;
    }

    // ---- Projection: make the world match the accepted state (load, campaign reset) ----

    void Reproject() { Projected = false; ProjectState(); }
    void ProjectState()
    {
        if (!state || !state.Loaded) { Projected = false; Error = null; pendingWorldReset = true; return; }
        string value = Status;
        if (Projected && ProjectedStatus == value) return;
        Projected = true; ProjectedStatus = value;
        if (Completing || moving) return; // live event in progress owns the scene

        switch (value)
        {
            case "":
                PlaceRanger(roofPosition, roofRotation);
                SetLadder(false);
                SetFlames(false);
                // Only on a fresh load or campaign reset, never on a failed-commit re-projection mid-play.
                if (pendingWorldReset) ResetTorches();
                break;
            case LadderRaised:
                PlaceRanger(roofPosition, roofRotation);
                SetLadder(true);
                SetFlames(false);
                break;
            case RescuedByLadder:
                if (ladderBottom) PlaceRanger(ladderBottom.position, ladderBottom.rotation);
                SetLadder(true);
                SetFlames(false);
                break;
            case RescuedByFire:
                if (fireLanding) PlaceRanger(fireLanding.position, fireLanding.rotation);
                SetFlames(true);
                break;
        }
        pendingWorldReset = false;
    }
    void ResetTorches()
    {
        for (int i = 0; i < torches.Length; i++)
        {
            var torch = torches[i];
            if (!torch) continue;
            if (torch.isGrabbed) torch.ForceDrop();
            torch.transform.SetPositionAndRotation(torchPoses[i].position, torchPoses[i].rotation);
            var body = torch.myBody;
            if (body && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            torch.gameObject.SetActive(true);
            var lit = torch.transform.Find(litTriggerName);
            var unlit = torch.transform.Find(unlitTriggerName);
            if (lit) lit.gameObject.SetActive(false);
            if (unlit) unlit.gameObject.SetActive(true);
        }
    }
    void SetLadder(bool raised)
    {
        if (ladderTween) { if (raised) ladderTween.DOPlayForward(); else ladderTween.DORewind(); }
        if (ladderInteractCollider) ladderInteractCollider.enabled = !raised;
    }
    void SetFlames(bool burning)
    {
        if (!houseFlames) return;
        if (burning)
        {
            if (!houseFlames.isPlaying) houseFlames.Play(true);
        }
        else
        {
            houseFlames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            headSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
