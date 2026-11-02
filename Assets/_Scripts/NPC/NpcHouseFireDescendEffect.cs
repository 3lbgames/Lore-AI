using System;
using System.Collections;

// Conversation action: the Ranger agrees to climb down the ladder the player raised.
public class NpcHouseFireDescendEffect : NpcActionEffect
{
    public NpcHouseFireQuest quest;
    public override bool ControlsFacing => true;
    public override bool CanExecute(NpcCharacterDirector actor, out string error)
    {
        error = "quest_unavailable";
        return quest && quest.CanDescend(actor, out error);
    }
    public override IEnumerator Execute(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        if (!quest) { complete(false, "quest_unavailable"); yield break; }
        yield return quest.DescendByLadder(actor, eventId, complete);
    }
    public override void ResetExecution() { if (quest) quest.ResetExecution(); }
}
