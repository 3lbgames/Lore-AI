using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class NpcActionOption { public string id, description, targetId; }
[Serializable]
public class NpcActionProposal { public string actionId, targetId; }
[Serializable]
public class NpcActionBinding
{
    public string id, targetId;
    [TextArea] public string description;
    public NpcRelationshipCondition[] conditions = Array.Empty<NpcRelationshipCondition>();
    public NpcActionEffect effect;
}

public class NpcActionCatalog : MonoBehaviour
{
    public NpcGameState gameState;
    public NpcActionBinding[] actions = Array.Empty<NpcActionBinding>();
    public NpcActionBinding Find(string id, string targetId)
    {
        NpcActionBinding match = null;
        foreach (var action in actions)
            if (action != null && action.id == id)
            {
                if (match != null || action.targetId != targetId) return null;
                match = action;
            }
        return match;
    }
    public bool Permits(NpcActionBinding action, NpcCharacterDirector actor, out string error)
    {
        error = "action_unavailable";
        if (action == null || !actor || !actor.Memory.Loaded || !action.effect || !action.effect.isActiveAndEnabled) return false;
        if (action.conditions.Length > 0 && (!actor.profile.relationship ||
            !actor.profile.relationship.Matches(actor.Memory.Relationship, action.conditions)))
        { error = "requirements_not_met"; return false; }
        return action.effect.CanExecute(actor, out error);
    }
    public NpcActionOption[] Available(NpcCharacterDirector actor)
    {
        var result = new List<NpcActionOption>();
        var runner = GetComponent<NpcActionRunner>();
        if (runner && runner.IsBusy) return result.ToArray();
        foreach (var action in actions)
            if (action != null && !string.IsNullOrWhiteSpace(action.id) && !string.IsNullOrWhiteSpace(action.targetId) &&
                !string.IsNullOrWhiteSpace(action.description) && Find(action.id, action.targetId) == action && Permits(action, actor, out _))
                result.Add(new NpcActionOption { id = action.id, description = action.description, targetId = action.targetId });
        return result.ToArray();
    }
}
