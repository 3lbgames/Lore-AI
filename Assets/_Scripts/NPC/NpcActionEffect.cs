using System;
using System.Collections;
using UnityEngine;

public abstract class NpcActionEffect : MonoBehaviour
{
    public virtual bool ControlsFacing => false;
    public abstract bool CanExecute(NpcCharacterDirector actor, out string error);
    public abstract IEnumerator Execute(NpcCharacterDirector actor, string eventId, Action<bool, string> complete);
    public virtual void ResetExecution() { }
}
