using System;
using UnityEngine;

[Serializable]
public class NpcRelationshipConfig
{
    public NpcRelationshipCategory[] categories = Array.Empty<NpcRelationshipCategory>();
    public NpcRelationshipRule[] rules = Array.Empty<NpcRelationshipRule>();
}

[Serializable]
public class NpcRelationshipCategory
{
    public string id;
    [TextArea] public string description;
    [TextArea] public string expression;
    public int initial;
    public int min = -100;
    public int max = 100;
}

[Serializable]
public class NpcRelationshipRule
{
    public string id;
    [Tooltip("dialogue interprets a committed utterance; action requires a confirmed gameplay event.")]
    public string source = "dialogue";
    [TextArea] public string condition;
    [TextArea] public string meaning;
    public string evidenceKey;
    public NpcRelationshipEffect[] effects = Array.Empty<NpcRelationshipEffect>();
    public NpcRelationshipRepeat repeat = new NpcRelationshipRepeat();
}

[Serializable]
public class NpcRelationshipEffect
{
    public string category;
    public int delta;
}

[Serializable]
public class NpcRelationshipRepeat
{
    [Tooltip("Maximum applications per player/NPC relationship. Zero means unlimited.")]
    [Min(0)] public int maxApplications = 1;
    [Tooltip("Minimum server-clock seconds between accepted applications.")]
    [Min(0)] public int cooldownSeconds;
}

[Serializable]
public class NpcRelationshipState
{
    public NpcRelationshipValue[] values = Array.Empty<NpcRelationshipValue>();
    public NpcRelationshipRuleUsage[] ruleUsage = Array.Empty<NpcRelationshipRuleUsage>();
    public string[] evaluatedEventIds = Array.Empty<string>();
    public NpcRelationshipExperience[] experiences = Array.Empty<NpcRelationshipExperience>();
}

[Serializable]
public class NpcRelationshipValue
{
    public string category;
    public int value;
}

[Serializable]
public class NpcRelationshipRuleUsage
{
    public string ruleId;
    public int applications;
    public string lastAppliedAt;
}

[Serializable]
public class NpcRelationshipExperience
{
    public string ruleId, sourceEventId, source, evidence, meaning, occurredAt;
    public NpcRelationshipEffect[] effects = Array.Empty<NpcRelationshipEffect>();
}

[Serializable]
public class NpcRelationshipCondition
{
    public string category;
    [Tooltip("eq, ne, lt, lte, gt, or gte")]
    public string comparison = "gte";
    public int value;
}

[Serializable]
public class NpcRelationshipGreeting
{
    public int priority;
    public string name;
    public NpcRelationshipCondition[] conditions = Array.Empty<NpcRelationshipCondition>();
    [TextArea] public string greeting;
}
