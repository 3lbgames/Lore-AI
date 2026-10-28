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
    public bool InteractionOpen => InteractionId != null;

    private void Awake()
    {
        if (speech && profile && !string.IsNullOrEmpty(profile.voice)) speech._ChangeVoice(profile.voice);
    }
    public void BeginInteraction() { InteractionId = Guid.NewGuid().ToString("N"); }
    public void SelectForConversation()
    {
        if (!InteractionOpen || Selected == this) return;
        if (Selected) Selected.CancelTurn();
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
        input = transcript, situation = situation
    };
    public void CancelTurn() { TurnRevision++; pendingTranscript = null; AcceptingTranscript = false; }
    public void EndInteraction() { CancelTurn(); if (Selected == this) Selected = null; InteractionId = null; }
    private void OnDisable() { EndInteraction(); }
}
