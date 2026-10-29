using System;
using System.Collections;
using UnityEngine;

[Serializable]
public class NpcGameStateData
{
    public int revision;
    public NpcGameValue[] values = Array.Empty<NpcGameValue>();
    public string Get(string key)
    {
        foreach (var entry in values ?? Array.Empty<NpcGameValue>())
            if (entry != null && entry.key == key) return entry.value ?? "";
        return "";
    }
}

[Serializable] public class NpcGameValue { public string key, value; }
[Serializable] public class NpcGameMutation { public string eventId, key, expected, value; }
[Serializable] public class NpcGameRequest : NpcServiceRequest { public NpcGameMutation mutation; }

public class NpcGameState : MonoBehaviour
{
    public NpcCharacterDirector transport;
    public bool Loaded { get; private set; }
    public bool IsBusy { get; private set; }
    public string Error { get; private set; }
    public NpcGameStateData Data { get; private set; }
    public event Action Changed;
    string campaign;
    int epoch;

    void OnEnable()
    {
        NpcCampaign.Changed += CampaignChanged;
        if (transport) ResetState(NpcCampaign.Current(transport.worldId));
        StartCoroutine(LoadAfterStart());
    }
    IEnumerator LoadAfterStart() { yield return null; yield return EnsureLoaded(); }
    void OnDisable()
    {
        NpcCampaign.Changed -= CampaignChanged;
        epoch++;
        StopAllCoroutines();
        IsBusy = false;
        Loaded = false;
    }
    void CampaignChanged(string world, string value)
    {
        if (!transport || transport.worldId != world) return;
        ResetState(value);
        StartCoroutine(LoadAfterStart());
    }
    void ResetState(string value)
    {
        epoch++;
        StopAllCoroutines();
        campaign = value;
        Loaded = false; IsBusy = false; Error = null; Data = null;
        Changed?.Invoke();
    }
    public string Get(string key) => Data?.Get(key) ?? "";
    NpcGameRequest Request(NpcGameMutation mutation = null) => new NpcGameRequest
    {
        sessionId = transport.SessionId, interactionId = transport.InteractionId ?? "campaign-state",
        turnId = mutation?.eventId ?? Guid.NewGuid().ToString("N"), worldId = transport.worldId,
        campaignId = campaign, npcId = transport.profile.npcId, writerId = NpcCampaign.WriterId,
        expectedRevision = Data?.revision ?? 0, mutation = mutation
    };
    bool ValidTransport => transport && transport.profile && transport.GetComponent<NpcProxyClient>();
    void Apply(NpcResponse response)
    {
        if (response?.status != "completed" || response.gameState == null)
        {
            Error = response?.error ?? "invalid_game_state";
            return;
        }
        Data = response.gameState; Loaded = true; Error = null;
        Changed?.Invoke();
    }
    public IEnumerator EnsureLoaded()
    {
        int expectedEpoch = epoch;
        while (IsBusy && expectedEpoch == epoch) yield return null;
        if (expectedEpoch != epoch || (Loaded && Error == null)) yield break;
        if (!ValidTransport) { Error = "invalid_game_state_transport"; yield break; }
        if (string.IsNullOrEmpty(campaign)) campaign = NpcCampaign.Current(transport.worldId);
        IsBusy = true;
        try
        {
            NpcResponse response = null;
            yield return transport.GetComponent<NpcProxyClient>().Exchange("/v1/game/load", Request(), result => response = result);
            if (expectedEpoch == epoch) Apply(response);
        }
        finally { if (expectedEpoch == epoch) IsBusy = false; }
    }
    public IEnumerator Transition(string eventId, string key, string expected, string value, Action<bool, string> complete)
    {
        int expectedEpoch = epoch;
        yield return EnsureLoaded();
        while (IsBusy && expectedEpoch == epoch) yield return null;
        if (expectedEpoch != epoch) { complete(false, "campaign_changed"); yield break; }
        if (!Loaded || Error != null || !ValidTransport) { complete(false, Error ?? "game_state_unavailable"); yield break; }
        IsBusy = true; Error = null;
        var mutation = new NpcGameMutation { eventId = eventId, key = key, expected = expected, value = value };
        NpcResponse response = null;
        try
        {
            yield return transport.GetComponent<NpcProxyClient>().Exchange("/v1/game/commit", Request(mutation), result => response = result);
            if (expectedEpoch != epoch) { complete(false, "campaign_changed"); yield break; }
            if (response?.error == "revision_conflict")
            {
                yield return transport.GetComponent<NpcProxyClient>().Exchange("/v1/game/load", Request(), result => response = result);
                if (expectedEpoch != epoch) { complete(false, "campaign_changed"); yield break; }
                Apply(response);
                if (Error == null && Get(key) == expected)
                    yield return transport.GetComponent<NpcProxyClient>().Exchange("/v1/game/commit", Request(mutation), result => response = result);
                else if (Error == null) response = new NpcResponse { status = "failed", error = "state_conflict" };
            }
            if (expectedEpoch != epoch) { complete(false, "campaign_changed"); yield break; }
            Apply(response);
        }
        finally { if (expectedEpoch == epoch) IsBusy = false; }
        complete(Error == null, Error);
    }
}
