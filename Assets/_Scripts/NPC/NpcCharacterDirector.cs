using System;
using Core3lb;
using Unity.Behavior;
using UnityEngine;

[RequireComponent(typeof(NpcProxyClient), typeof(BehaviorGraphAgent))]
public class NpcCharacterDirector : MonoBehaviour
{
    public NpcProfile profile;
    public string worldId = "local-world", campaignId = "local-campaign";
    [TextArea] public string situation = "The player is nearby and speaking to this character.";
    public BaseAI_TextToSpeech speech;
    public static NpcCharacterDirector Selected { get; private set; }
    public string InteractionId { get; private set; }
    public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
    public bool AcceptingTranscript { get; set; }
    public int TurnRevision { get; private set; }
    private string pendingTranscript;
    private BehaviorGraphAgent agent;
    private NpcProxyClient proxy;
    private bool wasActive;

    private void Awake()
    {
        agent = GetComponent<BehaviorGraphAgent>(); proxy = GetComponent<NpcProxyClient>();
        if (speech && profile && !string.IsNullOrEmpty(profile.voice)) speech._ChangeVoice(profile.voice);
    }
    private void Update()
    {
        bool active = agent.GetVariable<bool>("InteractionActive", out var variable) && variable.Value;
        if (active && !wasActive) InteractionId = Guid.NewGuid().ToString("N");
        if (!active && wasActive) EndInteraction();
        wasActive = active;
        if (active && !Selected) Selected = this;
    }
    public void SelectForConversation() { if (wasActive) { if (Selected && Selected != this) Selected.CancelTurn(); Selected = this; } }
    public bool SubmitTranscript(string exactTranscript)
    {
        if (Selected != this || !wasActive || !AcceptingTranscript || string.IsNullOrWhiteSpace(exactTranscript)) return false;
        pendingTranscript = exactTranscript; AcceptingTranscript = false; return true;
    }
    public bool TakeTranscript(out string transcript)
    { transcript = pendingTranscript; pendingTranscript = null; return transcript != null; }
    public NpcResponseRequest CreateRequest(string transcript) => new NpcResponseRequest
    {
        sessionId = SessionId, interactionId = InteractionId, turnId = Guid.NewGuid().ToString("N"),
        worldId = worldId, campaignId = campaignId, npcId = profile.npcId, profile = profile.ToData(),
        input = transcript, situation = situation
    };
    public void CancelTurn() { TurnRevision++; pendingTranscript = null; AcceptingTranscript = false; proxy.Cancel(); if (speech) speech._StopAudio(); }
    private void EndInteraction() { CancelTurn(); if (Selected == this) Selected = null; InteractionId = null; }
    private void OnDisable() { if (proxy) EndInteraction(); wasActive = false; }
}
