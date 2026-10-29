using System;
using System.Collections;
using UnityEngine;

public class NpcGrantDeliveryEffect : NpcActionEffect
{
    public NpcDeliveryQuest quest;
    public override bool CanExecute(NpcCharacterDirector actor, out string error)
    {
        error = "quest_unavailable";
        return quest && quest.CanGrant(actor, out error);
    }
    public override IEnumerator Execute(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!quest) { complete(false, "quest_unavailable"); yield break; }
        yield return quest.Grant(actor, eventId, complete);
    }
}
