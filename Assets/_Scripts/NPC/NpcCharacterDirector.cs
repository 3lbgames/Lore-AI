using System;
using Core3lb;
using Unity.Behavior;
using UnityEngine;

[RequireComponent(typeof(NpcProxyClient), typeof(BehaviorGraphAgent), typeof(NpcMemory))]
public class NpcCharacterDirector : MonoBehaviour
{
    public NpcProfile profile;
    public string worldId = "local-world", campaignId = "local-campaign";
    public BaseAI_TextToSpeech speech;
    public static NpcCharacterDirector Selected { get; private set; }
    public string InteractionId { get; private set; }
    public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
    public bool AcceptingTranscript { get; set; }
    public int TurnRevision { get; private set; }
    private string pendingTranscript;
    public bool InteractionOpen => InteractionId != null;

    public NpcMemory Memory => GetComponent<NpcMemory>();
    private void Awake()
    {
        campaignId = NpcCampaign.Current(worldId);
        if (speech && profile && !string.IsNullOrEmpty(profile.voice)) speech._ChangeVoice(profile.voice);
    }
    private void OnEnable()
    {
        NpcCampaign.Changed += CampaignChanged;
        string current = NpcCampaign.Current(worldId);
        if (campaignId != current) CampaignChanged(worldId, current);
    }
    public void ResetCampaign() => NpcCampaign.Reset(worldId);
    private void CampaignChanged(string world, string campaign)
    {
        if (world != worldId) return;
        var proximity = GetComponent<NpcProximityTrigger>();
        bool wasEnabled = proximity && proximity.enabled;
        if (wasEnabled) proximity.enabled = false;
        EndInteraction(); GetComponent<NpcProxyClient>().Cancel();
        campaignId = campaign; Memory.ClearForCampaign();
        if (wasEnabled) proximity.enabled = true;
    }
    public void BeginInteraction() { InteractionId = Guid.NewGuid().ToString("N"); }
    public void SelectForConversation()
    {
        if (!InteractionOpen || Selected == this) return;
        if (Selected && Selected.Memory.CurrentTurn?.npcInitiated != true) Selected.CancelTurn();
        Selected = this;
    }
    public bool SubmitTranscript(string exactTranscript)
    {
        if (Selected != this || !InteractionOpen || !AcceptingTranscript || string.IsNullOrWhiteSpace(exactTranscript)) return false;
        pendingTranscript = exactTranscript; AcceptingTranscript = false; return true;
    }
    public bool TakeTranscript(out string transcript)
    { transcript = pendingTranscript; pendingTranscript = null; return transcript != null; }
    public NpcResponseRequest CreateRequest(string transcript) => new NpcResponseRequest
    {
        sessionId = SessionId, interactionId = InteractionId, turnId = Guid.NewGuid().ToString("N"),
        worldId = worldId, campaignId = campaignId, npcId = profile.npcId, profile = profile.ToData(),
        input = transcript, situation = "", writerId = NpcCampaign.WriterId, expectedRevision = Memory.Revision
    };
    public void CancelTurn() { if (Memory) Memory.CancelTurn(); TurnRevision++; pendingTranscript = null; AcceptingTranscript = false; }
    public void EndInteraction() { CancelTurn(); if (Selected == this) Selected = null; InteractionId = null; }
    private void OnDisable() { NpcCampaign.Changed -= CampaignChanged; EndInteraction(); }
}
