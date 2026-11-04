using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("House burn-down")]
    [Tooltip("The house Flynn is standing on. Found automatically by name near the flames when left empty.")]
    public Transform houseRoot;
    public string houseObjectName = "House_01";
    [Min(1f)] public float burnSeconds = 14f;
    [Tooltip("Height the charred remains are squashed to, as a fraction of the standing house.")]
    [Range(0.05f, 0.5f)] public float burnedHeightScale = 0.16f;
    public Color charredColor = new Color(0.07f, 0.055f, 0.045f, 1f);

    [Header("Ranger movement")]
    public Transform ladderTop;
    public Transform ladderBottom;
    public Transform fireLanding;
    public Facing facing;
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

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    bool houseResolved;
    Vector3 houseLocalPosition, houseLocalScale;
    Quaternion houseLocalRotation;
    readonly List<Renderer> houseRenderers = new List<Renderer>();
    readonly List<ParticleSystem> flameSystems = new List<ParticleSystem>();
    readonly List<Vector3> flameOrigins = new List<Vector3>();
    readonly List<float> flameSinks = new List<float>();
    Material[] houseOriginalMaterials = Array.Empty<Material>();
    Material[] houseBurnMaterials = Array.Empty<Material>();
    Color[] houseOriginalColors = Array.Empty<Color>();
    Transform rubbleRoot;
    Coroutine burnRoutine;
    Transform debrisRoot;
    readonly List<ChunkPiece> chunks = new List<ChunkPiece>();

    sealed class ChunkPiece
    {
        public Transform piece;
        public Vector3 startPosition;
        public Quaternion startRotation;
        public Vector3 fallDirection;
        public Vector3 rotationAxis;
        public float delay;
        public float travel;
        public float restY;
        public float rotationAmount;
    }

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
        ResolveHouse();
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
        SetFlames(true);
        // Speak when he sees the fire.
        if (ranger && ranger.campaignId == campaign)
            NpcWorldEventInbox.Notify(ranger, eventId + ":fire", fireReactionIntent, fireDescription);
        yield return new WaitForSeconds(fireScareDelay);
        yield return Jump(fireLanding);
        // The collapse must never start with Flynn still on the roof.
        if (!fireLanding || !ranger || Vector3.Distance(ranger.transform.position, fireLanding.position) > 2f)
        {
            Completing = false;
            yield break;
        }
        if (headSmoke) headSmoke.Play();
        StartBurnDown();
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
        if (ranger.animator) ranger.animator.SetBool("Jump", true);
        yield return Tween(t.DOJump(landing.position, jumpHeight, 1, jumpSeconds).SetEase(Ease.Linear).OnComplete(Land));
        if (!moving) yield break;
        EndMovement(landing.position, landing.rotation);
    }
    void Land()
    {
        if (ranger.animator) ranger.animator.SetBool("Jump", false);
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
        if (ranger.animator && !string.IsNullOrEmpty(speedParameter)) ranger.animator.SetFloat(speedParameter, speed);
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
                RestoreHouse();
                // Only on a fresh load or campaign reset, never on a failed-commit re-projection mid-play.
                if (pendingWorldReset) ResetTorches();
                break;
            case LadderRaised:
                PlaceRanger(roofPosition, roofRotation);
                SetLadder(true);
                SetFlames(false);
                RestoreHouse();
                break;
            case RescuedByLadder:
                if (ladderBottom) PlaceRanger(ladderBottom.position, ladderBottom.rotation);
                SetLadder(true);
                SetFlames(false);
                RestoreHouse();
                break;
            case RescuedByFire:
                if (fireLanding) PlaceRanger(fireLanding.position, fireLanding.rotation);
                SetFlames(true);
                ApplyBurnedEndState();
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
        ResolveHouse();
        foreach (var flames in flameSystems)
        {
            if (!flames) { continue; }
            var flameRenderer = flames.GetComponent<ParticleSystemRenderer>();
            if (flameRenderer) { flameRenderer.enabled = burning; }
            if (burning)
            {
                if (!flames.isPlaying) { flames.Play(true); }
            }
            else
            {
                flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
        if (!burning && headSmoke) { headSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    }

    // ---- Burn-down animation: char, collapse into rubble, then smoulder ----

    void ResolveHouse()
    {
        if (houseResolved) { return; }
        houseResolved = true;

        if (houseFlames)
        {
            flameSystems.Add(houseFlames);
            if (houseFlames.transform.parent)
            {
                foreach (var sibling in houseFlames.transform.parent.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (sibling != houseFlames) { flameSystems.Add(sibling); }
                }
            }
        }

        if (!houseRoot && !string.IsNullOrEmpty(houseObjectName))
        {
            Vector3 anchor = houseFlames ? houseFlames.transform.position : transform.position;
            float best = float.MaxValue;
            foreach (var candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (candidate.name != houseObjectName) { continue; }
                Vector3 delta = candidate.position - anchor;
                delta.y = 0f;
                if (delta.sqrMagnitude < best) { best = delta.sqrMagnitude; houseRoot = candidate; }
            }
        }
        if (!houseRoot) { return; }

        houseLocalPosition = houseRoot.localPosition;
        houseLocalRotation = houseRoot.localRotation;
        houseLocalScale = houseRoot.localScale;
        foreach (var flames in flameSystems)
        {
            if (!flames) { continue; }
            flameOrigins.Add(flames.transform.localPosition);
            flameSinks.Add(Mathf.Max(0f, flames.transform.position.y - houseRoot.position.y - 0.5f));
        }
        houseRoot.GetComponentsInChildren(true, houseRenderers);
        houseOriginalMaterials = new Material[houseRenderers.Count];
        houseOriginalColors = new Color[houseRenderers.Count];
        for (int i = 0; i < houseRenderers.Count; i++)
        {
            houseOriginalMaterials[i] = houseRenderers[i].sharedMaterial;
            houseOriginalColors[i] = houseOriginalMaterials[i] && houseOriginalMaterials[i].HasProperty(BaseColorId)
                ? houseOriginalMaterials[i].GetColor(BaseColorId) : Color.white;
        }
    }

    void StartBurnDown()
    {
        ResolveHouse();
        if (!houseRoot) { return; }
        if (burnRoutine != null) { StopCoroutine(burnRoutine); }
        burnRoutine = StartCoroutine(BurnDown());
    }

    IEnumerator BurnDown()
    {
        EnsureBurnMaterials();
        EnsureRubble();
        EnsureChunks();

        float elapsed = 0f;
        while (elapsed < burnSeconds)
        {
            elapsed += Time.deltaTime;
            ApplyBurnProgress(Mathf.Clamp01(elapsed / burnSeconds), true);
            yield return null;
        }
        ApplyBurnProgress(1f, true);
        SetFlames(false);
        burnRoutine = null;
    }

    void ApplyBurnProgress(float progress, bool animated)
    {
        if (!houseRoot) { return; }
        float charProgress = Mathf.Clamp01(progress / 0.45f);
        float collapse = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.3f) / 0.65f));
        for (int i = 0; i < houseBurnMaterials.Length; i++)
        {
            var material = houseBurnMaterials[i];
            if (!material) { continue; }
            Color charred = charredColor;
            charred.a = houseOriginalColors[i].a;
            material.SetColor(BaseColorId, Color.Lerp(houseOriginalColors[i], charred, charProgress));
            float glow = Mathf.Sin(Mathf.Clamp01(progress / 0.7f) * Mathf.PI);
            if (material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, new Color(1f, 0.35f, 0.05f) * (glow * 0.35f));
            }
        }

        houseRoot.localScale = houseLocalScale;
        houseRoot.localRotation = houseLocalRotation;
        if (animated && progress > 0.1f && collapse < 1f)
        {
            float shake = 0.02f * Mathf.Sin(progress * Mathf.PI);
            houseRoot.localPosition = houseLocalPosition + new Vector3(
                UnityEngine.Random.Range(-shake, shake), 0f, UnityEngine.Random.Range(-shake, shake));
        }
        else
        {
            houseRoot.localPosition = houseLocalPosition;
        }
        ApplyChunkProgress(collapse);

        for (int i = 0; i < flameSystems.Count; i++)
        {
            var flames = flameSystems[i];
            if (!flames) { continue; }
            flames.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.5f, Mathf.Clamp01(progress / 0.3f));
            if (i < flameOrigins.Count)
            {
                Vector3 origin = flameOrigins[i];
                origin.y -= flameSinks[i] * collapse;
                flames.transform.localPosition = origin;
            }
        }
        if (rubbleRoot)
        {
            rubbleRoot.gameObject.SetActive(collapse > 0.05f);
            float rubbleScale = Mathf.Lerp(0.6f, 1f, collapse);
            rubbleRoot.localScale = new Vector3(rubbleScale, rubbleScale, rubbleScale);
        }
    }

    void ApplyChunkProgress(float collapse)
    {
        if (collapse <= 0f)
        {
            if (debrisRoot) { debrisRoot.gameObject.SetActive(false); }
            foreach (var houseRenderer in houseRenderers)
            {
                if (houseRenderer) { houseRenderer.enabled = true; }
            }
            return;
        }
        EnsureChunks();
        if (!debrisRoot || chunks.Count == 0) { return; }
        debrisRoot.gameObject.SetActive(true);
        foreach (var houseRenderer in houseRenderers)
        {
            if (houseRenderer) { houseRenderer.enabled = false; }
        }
        foreach (var chunk in chunks)
        {
            float p = Mathf.Clamp01((collapse - chunk.delay) / Mathf.Max(0.05f, 1f - chunk.delay));
            float outward = 1f - (1f - p) * (1f - p);
            Vector3 position = chunk.startPosition + chunk.fallDirection * (chunk.travel * outward);
            position.y = Mathf.Lerp(chunk.startPosition.y, chunk.restY, p * p);
            chunk.piece.position = position;
            chunk.piece.rotation = Quaternion.AngleAxis(chunk.rotationAmount * p, chunk.rotationAxis) * chunk.startRotation;
        }
    }

    void EnsureChunks()
    {
        if (debrisRoot || !houseRoot) { return; }
        var debris = new GameObject("HouseDebris (burned)");
        debrisRoot = debris.transform;
        var charredMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        charredMaterial.SetColor(BaseColorId, charredColor);
        charredMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        var filters = houseRoot.GetComponentsInChildren<MeshFilter>(true);
        var localVertices = new List<Vector3>();
        foreach (var filter in filters)
        {
            if (!filter || !filter.sharedMesh || !filter.sharedMesh.isReadable) { continue; }
            var mesh = filter.sharedMesh;
            var matrix = houseRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            foreach (var vertex in mesh.vertices) { localVertices.Add(matrix.MultiplyPoint3x4(vertex)); }
        }
        if (localVertices.Count == 0) { return; }
        Vector3 min = localVertices[0], max = localVertices[0];
        foreach (var vertex in localVertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }
        Vector3 size = Vector3.Max(max - min, Vector3.one * 0.01f);

        var cells = new Dictionary<Vector3Int, List<Vector3>>();
        var random = new System.Random(23);
        foreach (var filter in filters)
        {
            if (!filter || !filter.sharedMesh || !filter.sharedMesh.isReadable) { continue; }
            var mesh = filter.sharedMesh;
            var matrix = houseRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                Vector3 b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                Vector3 c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                AddSubdivided(cells, min, size, a, b, c, 0);
            }
        }

        foreach (var pair in cells)
        {
            var points = pair.Value;
            if (points.Count < 3) { continue; }
            Vector3 centroid = Vector3.zero;
            foreach (var point in points) { centroid += point; }
            centroid /= points.Count;
            var chunkVertices = new Vector3[points.Count];
            var chunkTriangles = new int[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                chunkVertices[i] = points[i] - centroid;
                chunkTriangles[i] = i;
            }
            var chunkMesh = new Mesh { name = "HouseChunkMesh" };
            chunkMesh.vertices = chunkVertices;
            chunkMesh.triangles = chunkTriangles;
            chunkMesh.RecalculateNormals();
            chunkMesh.RecalculateBounds();

            var pieceObject = new GameObject("HouseChunk");
            pieceObject.transform.SetParent(debrisRoot, false);
            var pieceFilter = pieceObject.AddComponent<MeshFilter>();
            pieceFilter.sharedMesh = chunkMesh;
            var pieceRenderer = pieceObject.AddComponent<MeshRenderer>();
            pieceRenderer.sharedMaterial = charredMaterial;

            Vector3 worldStart = houseRoot.TransformPoint(centroid);
            pieceObject.transform.position = worldStart;
            pieceObject.transform.rotation = houseRoot.rotation;
            Vector3 localOutward = new Vector3(centroid.x - (min.x + max.x) * 0.5f, 0f, centroid.z - (min.z + max.z) * 0.5f);
            Vector3 fallDirection = houseRoot.TransformDirection(localOutward);
            fallDirection.y = 0f;
            if (fallDirection.sqrMagnitude < 0.001f)
            {
                fallDirection = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f);
            }
            fallDirection.Normalize();
            float heightFraction = Mathf.Clamp01((centroid.y - min.y) / size.y);
            var chunk = new ChunkPiece
            {
                piece = pieceObject.transform,
                startPosition = worldStart,
                startRotation = houseRoot.rotation,
                fallDirection = fallDirection,
                rotationAxis = Vector3.Cross(Vector3.up, fallDirection).normalized,
                delay = Mathf.Clamp01(0.4f * (1f - heightFraction) + (float)random.NextDouble() * 0.2f),
                travel = 0.2f + (float)random.NextDouble() * 0.8f,
                restY = houseRoot.position.y + chunkMesh.bounds.extents.magnitude * 0.35f + (float)random.NextDouble() * 1.0f,
                rotationAmount = 25f + (float)random.NextDouble() * 40f
            };
            chunks.Add(chunk);
        }
        debrisRoot.gameObject.SetActive(false);
    }

    void AddSubdivided(Dictionary<Vector3Int, List<Vector3>> cells, Vector3 min, Vector3 size, Vector3 a, Vector3 b, Vector3 c, int depth)
    {
        float maxEdge = Mathf.Max(Vector3.Distance(a, b), Vector3.Distance(b, c), Vector3.Distance(c, a));
        if (maxEdge > 1.3f && depth < 3)
        {
            Vector3 ab = (a + b) * 0.5f;
            Vector3 bc = (b + c) * 0.5f;
            Vector3 ca = (c + a) * 0.5f;
            AddSubdivided(cells, min, size, a, ab, ca, depth + 1);
            AddSubdivided(cells, min, size, ab, b, bc, depth + 1);
            AddSubdivided(cells, min, size, ca, bc, c, depth + 1);
            AddSubdivided(cells, min, size, ab, bc, ca, depth + 1);
            return;
        }
        Vector3 centroid = (a + b + c) / 3f;
        var cell = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt((centroid.x - min.x) / size.x * 7f), 0, 6),
            Mathf.Clamp(Mathf.FloorToInt((centroid.y - min.y) / size.y * 5f), 0, 4),
            Mathf.Clamp(Mathf.FloorToInt((centroid.z - min.z) / size.z * 7f), 0, 6));
        if (!cells.TryGetValue(cell, out var points))
        {
            points = new List<Vector3>();
            cells.Add(cell, points);
        }
        points.Add(a);
        points.Add(b);
        points.Add(c);
    }

    void ApplyBurnedEndState()
    {
        ResolveHouse();
        if (!houseRoot) { return; }
        if (burnRoutine != null) { StopCoroutine(burnRoutine); burnRoutine = null; }
        EnsureBurnMaterials();
        EnsureRubble();
        ApplyBurnProgress(1f, false);
    }

    void RestoreHouse()
    {
        ResolveHouse();
        if (burnRoutine != null) { StopCoroutine(burnRoutine); burnRoutine = null; }
        if (!houseRoot) { return; }
        houseRoot.localPosition = houseLocalPosition;
        houseRoot.localRotation = houseLocalRotation;
        houseRoot.localScale = houseLocalScale;
        for (int i = 0; i < houseRenderers.Count; i++)
        {
            if (houseRenderers[i]) { houseRenderers[i].sharedMaterial = houseOriginalMaterials[i]; }
        }
        foreach (var material in houseBurnMaterials)
        {
            if (material) { Destroy(material); }
        }
        houseBurnMaterials = Array.Empty<Material>();
        for (int i = 0; i < flameSystems.Count; i++)
        {
            var flames = flameSystems[i];
            if (!flames) { continue; }
            flames.transform.localScale = Vector3.one;
            if (i < flameOrigins.Count) { flames.transform.localPosition = flameOrigins[i]; }
        }
        if (rubbleRoot) { rubbleRoot.gameObject.SetActive(false); }
        if (debrisRoot) { Destroy(debrisRoot.gameObject); debrisRoot = null; chunks.Clear(); }
        foreach (var houseRenderer in houseRenderers)
        {
            if (houseRenderer) { houseRenderer.enabled = true; }
        }
    }

    void EnsureBurnMaterials()
    {
        if (houseBurnMaterials.Length == houseRenderers.Count && houseBurnMaterials.Length > 0) { return; }
        houseBurnMaterials = new Material[houseRenderers.Count];
        for (int i = 0; i < houseRenderers.Count; i++)
        {
            if (!houseRenderers[i] || !houseOriginalMaterials[i]) { continue; }
            houseBurnMaterials[i] = new Material(houseOriginalMaterials[i]);
            houseRenderers[i].material = houseBurnMaterials[i];
        }
    }

    void EnsureRubble()
    {
        if (rubbleRoot || !houseRoot) { return; }
        var rubble = new GameObject("HouseRubble (burned)");
        rubbleRoot = rubble.transform;
        rubbleRoot.SetPositionAndRotation(
            new Vector3(houseRoot.position.x, houseRoot.position.y + 0.05f, houseRoot.position.z), houseRoot.rotation);
        var charredMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        charredMaterial.SetColor(BaseColorId, charredColor);
        var burntWoodMaterial = new Material(charredMaterial);
        burntWoodMaterial.SetColor(BaseColorId, new Color(0.16f, 0.09f, 0.05f, 1f));

        var random = new System.Random(7);
        for (int i = 0; i < 12; i++)
        {
            var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = "CharredRubble";
            chunk.transform.SetParent(rubbleRoot, false);
            double angle = random.NextDouble() * Math.PI * 2.0;
            double radius = 0.8 + random.NextDouble() * 2.2;
            chunk.transform.localPosition = new Vector3((float)(Math.Cos(angle) * radius), 0.12f + (float)random.NextDouble() * 0.25f, (float)(Math.Sin(angle) * radius));
            chunk.transform.localRotation = Quaternion.Euler((float)(random.NextDouble() * 40.0 - 20.0), (float)(random.NextDouble() * 180.0), (float)(random.NextDouble() * 40.0 - 20.0));
            chunk.transform.localScale = new Vector3(0.5f + (float)random.NextDouble() * 1.1f, 0.18f + (float)random.NextDouble() * 0.3f, 0.35f + (float)random.NextDouble() * 0.7f);
            var chunkRenderer = chunk.GetComponent<Renderer>();
            if (chunkRenderer) { chunkRenderer.sharedMaterial = i % 3 == 0 ? burntWoodMaterial : charredMaterial; }
            var chunkCollider = chunk.GetComponent<Collider>();
            if (chunkCollider) { Destroy(chunkCollider); }
        }

        var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "FallenBeam";
        beam.transform.SetParent(rubbleRoot, false);
        beam.transform.localPosition = new Vector3(0.4f, 0.35f, 0.2f);
        beam.transform.localRotation = Quaternion.Euler(8f, 24f, 68f);
        beam.transform.localScale = new Vector3(0.28f, 3.4f, 0.28f);
        var beamRenderer = beam.GetComponent<Renderer>();
        if (beamRenderer) { beamRenderer.sharedMaterial = burntWoodMaterial; }
        var beamCollider = beam.GetComponent<Collider>();
        if (beamCollider) { Destroy(beamCollider); }

        var scorch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        scorch.name = "ScorchedGround";
        scorch.transform.SetParent(rubbleRoot, false);
        scorch.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        scorch.transform.localScale = new Vector3(7.5f, 0.03f, 7.5f);
        var scorchRenderer = scorch.GetComponent<Renderer>();
        if (scorchRenderer) { scorchRenderer.sharedMaterial = charredMaterial; }
        var scorchCollider = scorch.GetComponent<Collider>();
        if (scorchCollider) { Destroy(scorchCollider); }

        rubbleRoot.gameObject.SetActive(false);
    }

}
