using System;
using System.Collections;
using System.Collections.Generic;
using Core3lb;
using UnityEngine;

[RequireComponent(typeof(NpcProxyClient), typeof(BaseAI_Conversation))]
public class NpcMemory : MonoBehaviour
{
    public bool Loaded { get; private set; }
    public bool HasSpoken { get; private set; }
    public bool IsBusy { get; private set; }
    public int Revision { get; private set; }
    public string Error { get; private set; }
    public NpcResponseRequest CurrentTurn { get; private set; }
    public BaseAI_Conversation Buffer { get; private set; }
    NpcCharacterDirector director;
    NpcProxyClient proxy;
    sealed class PendingCommit
    {
        public NpcMemoryRequest Request;
        public bool Attempted;
    }
    readonly Queue<PendingCommit> pending = new Queue<PendingCommit>();
    int epoch;

    void Awake()
    {
        director = GetComponent<NpcCharacterDirector>(); proxy = GetComponent<NpcProxyClient>();
        Buffer = GetComponent<BaseAI_Conversation>(); proxy.AIConversation = Buffer;
    }
    public NpcMemoryRequest Request(string turnId = null) => new NpcMemoryRequest
    {
        sessionId = director.SessionId, interactionId = director.InteractionId ?? "restore",
        turnId = turnId ?? Guid.NewGuid().ToString("N"), worldId = director.worldId,
        campaignId = director.campaignId, npcId = director.profile.npcId,
        writerId = NpcCampaign.WriterId, expectedRevision = Revision
    };
    public void Load()
    {
        if (gameObject.activeInHierarchy && isActiveAndEnabled && !IsBusy) StartCoroutine(Restore(epoch));
    }
    IEnumerator Restore(int expected)
    {
        IsBusy = true; Error = null;
        do
        {
            yield return Flush(expected);
            if (expected != epoch) yield break;
            if (Error != null) break;
            NpcResponse result = null;
            yield return proxy.Exchange("/v1/memory/load", Request(), value => result = value);
            if (expected != epoch) yield break;
            if (result.status == "completed") Apply(result);
            else { Loaded = false; Error = result.error; }
        } while (Error == null && pending.Count > 0);
        IsBusy = false;
    }
    void Apply(NpcResponse result)
    {
        Revision = result.revision; HasSpoken = result.hasSpoken; Loaded = true;
        Buffer.RestoreAcknowledgedHistory(result.history);
    }
    public void BeginTurn(NpcResponseRequest turn)
    {
        CurrentTurn = turn;
        Enqueue(new NpcMemoryRequest
        {
            sessionId = turn.sessionId, interactionId = turn.interactionId, turnId = turn.turnId,
            worldId = turn.worldId, campaignId = turn.campaignId, npcId = turn.npcId,
            writerId = turn.writerId, expectedRevision = Revision,
            @event = new ConversationEvent { eventId = turn.turnId + ":input", turnId = turn.turnId,
                kind = "player_input", text = turn.input, delivery = "completed" }
        });
        Load();
    }
    public void CancelTurn() { CurrentTurn = null; }
    public void BeginDialogue(NpcResponseRequest turn) { CurrentTurn = turn; }
    public void RecordSpeech(NpcResponseRequest turn, string text, string outcome)
    {
        if (turn == null || turn.campaignId != director.campaignId) return;
        if (CurrentTurn == turn) CurrentTurn = null;
        var request = Request(turn.turnId);
        request.campaignId = turn.campaignId;
        request.interactionId = turn.interactionId;
        request.@event = new ConversationEvent { eventId = turn.turnId + ":speech", turnId = turn.turnId,
            kind = "npc_speech", text = outcome == "Completed" ? text : "",
            delivery = outcome == "Completed" ? "completed" : outcome == "Cancelled" ? "interrupted" : "failed" };
        Enqueue(request);
        Load();
    }
    public void RecordActionOutcome(string description, string outcome)
    {
        var request = Request();
        request.@event = new ConversationEvent { eventId = request.turnId + ":action", turnId = request.turnId,
            kind = "action_outcome", text = description, delivery = outcome };
        Enqueue(request);
        Load();
    }
    void Enqueue(NpcMemoryRequest request) => pending.Enqueue(new PendingCommit { Request = request });
    IEnumerator Flush(int expected)
    {
        while (pending.Count > 0 && expected == epoch)
        {
            var commit = pending.Peek();
            var request = commit.Request;
            if (!commit.Attempted)
            {
                request.expectedRevision = Revision;
                commit.Attempted = true;
            }
            // A pending receipt keeps its identity and expected revision across retries.
            NpcResponse result = null;
            yield return proxy.Exchange("/v1/memory/commit", request, value => result = value);
            if (expected != epoch) yield break;
            if (result.status != "completed") { Loaded = false; Error = result.error; yield break; }
            Revision = Math.Max(Revision, result.revision);
            pending.Dequeue();
        }
    }
    public void ClearForCampaign()
    {
        epoch++; StopAllCoroutines(); pending.Clear(); CurrentTurn = null;
        IsBusy = false; Loaded = false; HasSpoken = false; Revision = 0; Error = null;
        Buffer.RestoreAcknowledgedHistory(null);
    }
    void OnDisable() { epoch++; StopAllCoroutines(); IsBusy = false; Loaded = false; }
}
