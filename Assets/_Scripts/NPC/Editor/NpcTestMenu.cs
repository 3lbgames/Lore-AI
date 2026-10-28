using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class NpcTestMenu
{
    [MenuItem("Tools/NPC Tests/Routine Play Mode")]
    public static void RunRoutine()
    {
        var runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.PlayMode,
            categoryNames = new[] { "NpcRoutine" }
        }));
    }

    [MenuItem("Tools/NPC Tests/Edit Mode")]
    public static void RunEditMode()
    {
        var runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            testNames = new[] { "NpcInteractionTests", "NpcMemoryTests" }
        }));
    }
}
