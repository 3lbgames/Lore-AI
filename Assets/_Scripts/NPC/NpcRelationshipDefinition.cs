using System;
using UnityEngine;

[CreateAssetMenu(menuName = "NPC/Relationship Definition")]
public class NpcRelationshipDefinition : ScriptableObject
{
    public NpcRelationshipConfig rules = new NpcRelationshipConfig();
    public NpcRelationshipGreeting[] greetings = Array.Empty<NpcRelationshipGreeting>();

    public bool TryGetValue(NpcRelationshipState state, string category, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(category)) return false;
        NpcRelationshipCategory definition = null;
        foreach (var candidate in rules.categories ?? Array.Empty<NpcRelationshipCategory>())
            if (candidate != null && candidate.id == category) { definition = candidate; break; }
        if (definition == null) return false;
        value = definition.initial;
        foreach (var entry in state?.values ?? Array.Empty<NpcRelationshipValue>())
            if (entry != null && entry.category == category) { value = entry.value; break; }
        return true;
    }

    public bool Matches(NpcRelationshipState state, NpcRelationshipCondition[] conditions)
    {
        foreach (var condition in conditions ?? Array.Empty<NpcRelationshipCondition>())
        {
            if (condition == null || !TryGetValue(state, condition.category, out var value)) return false;
            bool matched;
            switch (condition.comparison)
            {
                case "eq": matched = value == condition.value; break;
                case "ne": matched = value != condition.value; break;
                case "lt": matched = value < condition.value; break;
                case "lte": matched = value <= condition.value; break;
                case "gt": matched = value > condition.value; break;
                case "gte": matched = value >= condition.value; break;
                default: return false;
            }
            if (!matched) return false;
        }
        return true;
    }

    public string SelectGreeting(NpcRelationshipState state, string fallback, out string band)
    {
        NpcRelationshipGreeting selected = null;
        foreach (var candidate in greetings ?? Array.Empty<NpcRelationshipGreeting>())
            if (candidate != null && Matches(state, candidate.conditions) &&
                (selected == null || candidate.priority > selected.priority)) selected = candidate;
        band = selected?.name ?? "";
        return string.IsNullOrWhiteSpace(selected?.greeting) ? fallback : selected.greeting;
    }
}
