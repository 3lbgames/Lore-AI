using System;
using System.Collections;
using Core3lb;
using UnityEngine;

// Druid wasp-nest quest.
//  1. Talk Sorrel into handing over her smokebombs (NpcGrantSmokebombsEffect -> Grant): "" -> "accepted".
//     Smokebombs stay hidden until then. Then smoke the base of the tree (UseSmoke): "accepted" -> "cleared_smoke".
//  2. Burn the nest with the lit torch at any point (BurnNest): "" | "accepted" -> "cleared_fire".
//     Burning it without ever talking to her stings more.
// Either way the other villagers learn the wasps are gone.
public class NpcWaspNestQuest : NpcQuestBase
{
    public const string Accepted = "accepted";
    public const string ClearedBySmoke = "cleared_smoke", ClearedByFire = "cleared_fire";

    [Header("Characters")]
    public NpcCharacterDirector druid;
    [Tooltip("Villagers who learn the wasps are gone.")]
    public NpcCharacterDirector[] villagers = Array.Empty<NpcCharacterDirector>();

    [Header("World (restored on campaign reset)")]
    public GameObject nest;
    public Rigidbody nestBody;
    public ParticleSystem wasps;
    public ParticleSystem nestFlames;
    public GameObject smokeEffect;
    public GameObject triggers;
    public BaseTimerEvent smokeTimer, burnTimer;
    public BaseGrabObject[] smokebombs = Array.Empty<BaseGrabObject>();

    [Header("Objective text")]
    public string fireCompletedText = "The nest is ash. Sorrel is not pleased";

    [Header("Memory")]
    [TextArea] public string smokeDescription =
        "The player set off a smokebomb at the base of the tree. The wasps grew drowsy and moved along unharmed, and the path to my herb garden is clear. I am grateful and relieved.";
    [TextArea] public string fireWithoutAskingDescription =
        "The player burned the wasp nest with a torch without ever speaking to me about it. The wasps are dead. The path is clear, but they did not even ask before destroying them.";
    [TextArea] public string fireDescription =
        "The player burned the wasp nest with a torch after I asked them not to. The wasps are dead. The path is clear, but it hurts that they were destroyed instead of moved along.";
    public string fireEvidenceKey;
    [TextArea] public string villagerSmokeDescription =
        "The wasp nest in the orchard has been cleared. The player smoked the wasps out for Sorrel the druid, and they no longer threaten the village.";
    [TextArea] public string villagerFireDescription =
        "The wasp nest in the orchard has been cleared. The player burned it with a torch, and the wasps no longer threaten the village.";

    Pose nestPose;
    Pose[] smokebombPoses = Array.Empty<Pose>();

    public override string ObjectiveText
    {
        get
        {
            switch (Status)
            {
                case Accepted: return acceptedText;
                case ClearedBySmoke: return completedText;
                case ClearedByFire: return fireCompletedText;
                default: return availableText;
            }
        }
    }

    void Awake()
    {
        if (nest) nestPose = new Pose(nest.transform.localPosition, nest.transform.localRotation);
        smokebombPoses = new Pose[smokebombs.Length];
        for (int i = 0; i < smokebombs.Length; i++)
            if (smokebombs[i]) smokebombPoses[i] = new Pose(smokebombs[i].transform.position, smokebombs[i].transform.rotation);
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
        Completing = false;
    }

    bool Configured => state && !string.IsNullOrWhiteSpace(stateKey) && druid && druid.profile;

    // ---- Conversation action (NpcGrantSmokebombsEffect) ----

    public bool CanGrant(NpcCharacterDirector actor, out string error)
    {
        error = null;
        if (!Configured || actor != druid || smokebombs.Length == 0) error = "invalid_wasp_configuration";
        else if (!state.Loaded || state.Error != null || !actor.Memory.Loaded) error = "game_state_unavailable";
        else if (state.IsBusy || Completing) error = "game_state_busy";
        else if (Status != "") error = "smokebombs_already_given";
        else if (requiredConditions != null && requiredConditions.Length > 0 && (!actor.profile.relationship ||
                 !actor.profile.relationship.Matches(actor.Memory.Relationship, requiredConditions)))
            error = "relationship_requirement_not_met";
        return error == null;
    }
    public IEnumerator Grant(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!CanGrant(actor, out var error)) { complete(false, error); yield break; }
        yield return state.Transition(eventId, stateKey, "", Accepted, (success, failure) =>
        {
            Error = failure;
            complete(success, failure);
        });
    }

    // ---- World events ----

    // SmokeTrigger ItemIDTrigger.enterEvent (only reachable once she has handed the smokebombs over)
    public void UseSmoke() { if (Status == Accepted) Clear(Accepted, ClearedBySmoke); }
    // FireTrigger ItemIDTrigger.enterEvent (allowed with or without her blessing)
    public void BurnNest() { if (Status == "" || Status == Accepted) Clear(Status, ClearedByFire); }

    void Clear(string from, string outcome)
    {
        if (!Configured || Completing) return;
        Completing = true;
        StartCoroutine(Commit(Guid.NewGuid().ToString("N"), from, outcome, druid.campaignId));
    }
    IEnumerator Commit(string eventId, string from, string outcome, string campaign)
    {
        bool ok = false; string failure = null;
        yield return state.Transition(eventId, stateKey, from, outcome, (success, error) => { ok = success; failure = error; });
        Completing = false;
        if (!druid || druid.campaignId != campaign) yield break;
        Error = failure;
        if (!ok) { Reproject(); yield break; }

        bool smoke = outcome == ClearedBySmoke;
        druid.Memory.RecordObservedOutcome(eventId + ":wasps", smoke ? smokeDescription : from == "" ? fireWithoutAskingDescription : fireDescription,
            "completed", smoke ? completionEvidenceKey : fireEvidenceKey);
        string news = smoke ? villagerSmokeDescription : villagerFireDescription;
        foreach (var villager in villagers)
            if (villager && villager != druid && villager.isActiveAndEnabled && villager.campaignId == campaign)
                villager.Memory.RecordObservedOutcome(eventId + ":news:" + villager.profile.npcId, news, "completed");
    }

    // ---- Projection: make the world match the accepted state (load, campaign reset) ----

    void Reproject() { Projected = false; ProjectState(); }
    void ProjectState()
    {
        if (!state || !state.Loaded) { Projected = false; Error = null; SetSmokebombs(false); return; }
        string value = Status;
        if (Projected && ProjectedStatus == value) return;
        Projected = true; ProjectedStatus = value;
        if (Completing) return; // the scene's own trigger events are playing this out live

        StopTimer(smokeTimer); StopTimer(burnTimer);
        if (smokeEffect) smokeEffect.SetActive(false);
        if (nestFlames) nestFlames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        switch (value)
        {
            case "":
            case Accepted:
                RestoreNest(true);
                if (wasps && !wasps.isPlaying) wasps.Play(true);
                if (triggers) triggers.SetActive(true);
                SetSmokebombs(value == Accepted);
                break;
            case ClearedBySmoke:
                RestoreNest(true);
                StopWasps();
                if (triggers) triggers.SetActive(false);
                SetSmokebombs(false);
                break;
            case ClearedByFire:
                RestoreNest(false);
                StopWasps();
                if (triggers) triggers.SetActive(false);
                SetSmokebombs(false);
                break;
        }
    }
    void RestoreNest(bool present)
    {
        if (!nest) return;
        if (nestBody)
        {
            if (!nestBody.isKinematic) { nestBody.linearVelocity = Vector3.zero; nestBody.angularVelocity = Vector3.zero; }
            nestBody.isKinematic = true;
        }
        nest.transform.SetLocalPositionAndRotation(nestPose.position, nestPose.rotation);
        nest.SetActive(present);
    }
    void StopWasps() { if (wasps) wasps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    static void StopTimer(BaseTimerEvent timer) { if (timer) timer._StopAndReset(); }
    void SetSmokebombs(bool available)
    {
        for (int i = 0; i < smokebombs.Length; i++)
        {
            var bomb = smokebombs[i];
            if (!bomb) continue;
            if (bomb.isGrabbed) bomb.ForceDrop();
            bomb.transform.SetPositionAndRotation(smokebombPoses[i].position, smokebombPoses[i].rotation);
            var body = bomb.myBody;
            if (body && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            bomb.gameObject.SetActive(available);
        }
    }
}
