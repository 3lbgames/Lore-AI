using System;
using UnityEngine;

[CreateAssetMenu(menuName = "NPC/Character Profile")]
public class NpcProfile : ScriptableObject
{
    public string npcId;
    [TextArea] public string personality;
    public string voice;
    [TextArea] public string[] facts = Array.Empty<string>();
    [TextArea] public string[] restrictions = Array.Empty<string>();
    public NpcProfileData ToData() => new NpcProfileData
    { npcId = npcId, personality = personality, voice = voice, facts = facts, restrictions = restrictions };
}
[Serializable]
public class NpcProfileData
{
    public string npcId, personality, voice;
    public string[] facts, restrictions;
}
[Serializable]
public class NpcServiceRequest
{
    public int version = 1;
    public string sessionId, interactionId, turnId, worldId, campaignId, npcId, writerId;
    public int expectedRevision;
    public NpcIdentity identity;
}
[Serializable]
public class NpcResponseRequest : NpcServiceRequest
{
    public string input, situation, intent;
    public bool npcInitiated;
    public NpcProfileData profile;
    public string[] actions = Array.Empty<string>();
}
[Serializable]
public class NpcIdentity { public string userId, nonce; }
[Serializable]
public class NpcResponse
{
    public int version, revision;
    public bool hasSpoken;
    public Core3lb.ConversationEvent[] history;
    public string sessionId, interactionId, turnId, status, text, error;
}

[Serializable]
public class NpcMemoryRequest : NpcServiceRequest
{
    public Core3lb.ConversationEvent @event;
}
