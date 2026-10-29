#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

public class NpcTestActionEffect : NpcActionEffect
{
    public float Duration = .5f;
    public int Starts, Completions;
    public bool Fail;
    public override bool CanExecute(NpcCharacterDirector actor, out string error) { error = null; return true; }
    public override IEnumerator Execute(NpcCharacterDirector actor, string eventId, Action<bool, string> complete)
    {
        Starts++;
        float until = Time.realtimeSinceStartup + Duration;
        while (Time.realtimeSinceStartup < until) yield return null;
        Completions++; complete(!Fail, Fail ? "test_execution_failed" : null);
    }
}
#endif
