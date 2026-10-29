#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class NpcAnimationEffectPlayTests
{
    NpcPlayFixture fixture;
    GameObject npc;
    Animator animator;
    Component effect;
    bool? result;
    string failure;
    static void Set(object target, string field, object value) => target.GetType().GetField(field).SetValue(target, value);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        fixture = new NpcPlayFixture();
        npc = fixture.InstantiateNpc();
        foreach (var behaviour in npc.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        effect = npc.AddComponent(Type.GetType("NpcAnimationEffect, Assembly-CSharp", true));
        animator = npc.GetComponentInChildren<Animator>(true);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Set(effect, "animator", animator);
        Set(effect, "state", "Wave");
        Set(effect, "trigger", "Wave");
        var bindingType = effect.GetType().GetNestedType("LayerBinding");
        var bindings = Array.CreateInstance(bindingType, 2);
        for (int i = 0; i < 2; i++)
        {
            var binding = Activator.CreateInstance(bindingType);
            Set(binding, "layer", i == 0 ? "Base Layer" : "Torso");
            Set(binding, "returnState", i == 0 ? "MoveBlend" : "New State");
            bindings.SetValue(binding, i);
        }
        Set(effect, "layers", bindings);
        npc.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
        foreach (var obstacle in npc.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true)) obstacle.enabled = false;
        npc.transform.position = Vector3.zero;
        fixture.Activate(npc);
        yield return null;
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator MovingWaveUsesTorsoAndPreservesLocomotion()
    {
        animator.SetFloat("Speed", .5f);
        StartEffect(effect);
        yield return new WaitForSeconds(.4f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("MoveBlend"), Is.True, "walking must keep using the existing blend tree");
        Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Wave"), Is.True, "the controller selects the torso wave while moving");
        yield return Finish();
        Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("New State"), Is.True);
        Assert.That(animator.GetFloat("Speed"), Is.EqualTo(.5f));
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator StationaryWaveCompletesThroughControllerReturn() { yield return Gesture("Wave", 0, 0); }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator StationaryCheerCompletesThroughControllerReturn() { yield return Gesture("Cheer", 0, 0); }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator MovingCheerCompletesThroughTorsoReturn() { yield return Gesture("Cheer", .5f, 1); }

    IEnumerator Gesture(string name, float speed, int layer)
    {
        Set(effect, "trigger", name); Set(effect, "state", name);
        animator.SetFloat("Speed", speed);
        StartEffect(effect);
        yield return new WaitForSeconds(.4f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName(name), Is.True);
        Assert.That(result, Is.Null, "entry must not count as completion");
        yield return Finish();
        Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName(layer == 0 ? "MoveBlend" : "New State"), Is.True);
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator InterruptedWaveIsNotRecordedAsCompleted()
    {
        StartEffect(effect);
        yield return new WaitForSeconds(.4f);
        animator.CrossFade("Hit", .1f, 0);
        yield return Finish(false);
        Assert.That(failure, Is.EqualTo("animation_interrupted"));
        bool playingHit = animator.GetCurrentAnimatorStateInfo(0).IsName("Hit") ||
            (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Hit"));
        Assert.That(playingHit, Is.True, "failure cleanup must not overwrite the interrupting animation");
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator FrameHitchAcrossNormalExitStillCompletes()
    {
        StartEffect(effect);
        yield return new WaitForSeconds(.4f);
        var current = animator.GetCurrentAnimatorStateInfo(0);
        animator.Update(Mathf.Max(0, (.86f - current.normalizedTime) * current.length));
        // Advance a maximum-length game frame without letting the effect sample in between.
        animator.Update(Time.maximumDeltaTime);
        yield return Finish();
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("MoveBlend"), Is.True);
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator QueuedTriggerDoesNotInvalidateFinishedGesture()
    {
        StartEffect(effect);
        yield return new WaitForSeconds(.4f);
        animator.SetTrigger("Hit");
        yield return Finish();
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator ForcedReturnToIdleIsAnInterruption()
    {
        StartEffect(effect);
        yield return new WaitForSeconds(.4f);
        animator.CrossFade("MoveBlend", 0, 0);
        yield return Finish(false);
        Assert.That(failure, Is.EqualTo("animation_interrupted"));
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator NeverStartedGestureTimesOutAndClearsPendingTrigger()
    {
        animator.speed = 0;
        Set(effect, "timeoutSeconds", .2f);
        StartEffect(effect);
        yield return Finish(false);
        animator.speed = 1;
        yield return new WaitForSeconds(.5f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("MoveBlend"), Is.True);
        Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("New State"), Is.True);
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator ResetStopsOwnedGestureWithoutReportingCompletion()
    {
        StartEffect(effect);
        yield return new WaitForSeconds(.4f);
        ((MonoBehaviour)effect).StopAllCoroutines();
        effect.GetType().GetMethod("ResetExecution").Invoke(effect, null);
        yield return new WaitForSeconds(.5f);
        Assert.That(result, Is.Null);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("MoveBlend"), Is.True);
    }

    [UnityTest, Category("NpcRoutine")]
    public IEnumerator NavigationDrivesSpeedAndReturnsToIdle() { yield return Movement(false); }
    [UnityTest, Category("NpcRoutine")]
    public IEnumerator NavigationResetClearsSpeedAndStopsAgent() { yield return Movement(true); }

    IEnumerator Movement(bool cancel)
    {
        var agent = npc.GetComponent<UnityEngine.AI.NavMeshAgent>();
        var floor = new UnityEngine.AI.NavMeshBuildSource {
            shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(new Vector3(0, -.1f, 0), Quaternion.identity, Vector3.one),
            size = new Vector3(20, .2f, 20)
        };
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(UnityEngine.AI.NavMesh.GetSettingsByID(agent.agentTypeID),
            new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource> { floor },
            new Bounds(Vector3.zero, new Vector3(20, 4, 20)), Vector3.zero, Quaternion.identity);
        var surface = UnityEngine.AI.NavMesh.AddNavMeshData(data);
        try
        {
            agent.enabled = true; agent.speed = 2; agent.stoppingDistance = .1f;
            Assert.That(agent.isOnNavMesh, Is.True);
            var destination = new GameObject("Movement target").transform;
            destination.position = new Vector3(3, 0, 0);
            var move = npc.AddComponent(Type.GetType("NpcMoveEffect, Assembly-CSharp", true));
            Set(move, "agent", agent); Set(move, "animator", animator); Set(move, "destination", destination);
            StartEffect(move);
            float deadline = Time.realtimeSinceStartup + 3;
            while (animator.GetFloat("Speed") < .05f && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(animator.GetFloat("Speed"), Is.InRange(.05f, .51f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("MoveBlend"), Is.True);
            if (cancel)
            {
                ((MonoBehaviour)move).StopAllCoroutines();
                move.GetType().GetMethod("ResetExecution").Invoke(move, null);
                var stopped = npc.transform.position;
                yield return new WaitForSeconds(.3f);
                Assert.That(result, Is.Null);
                Assert.That(Vector3.Distance(stopped, npc.transform.position), Is.LessThan(.05f));
            }
            else
            {
                yield return Finish();
                Assert.That(Vector3.Distance(npc.transform.position, destination.position), Is.LessThan(.25f));
            }
            Assert.That(animator.GetFloat("Speed"), Is.Zero);
            Assert.That(agent.isStopped, Is.True);
        }
        finally { agent.enabled = false; surface.Remove(); UnityEngine.Object.Destroy(data); }
    }

    void StartEffect(Component target)
    {
        result = null; failure = null;
        var operation = (IEnumerator)target.GetType().GetMethod("Execute").Invoke(target,
            new object[] { null, "test", new Action<bool, string>((ok, error) => { result = ok; failure = error; }) });
        ((MonoBehaviour)target).StartCoroutine(operation);
    }

    IEnumerator Finish(bool expected = true)
    {
        float deadline = Time.realtimeSinceStartup + 12;
        while (!result.HasValue && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(result, Is.EqualTo(expected), failure);
    }

    [UnityTearDown]
    public IEnumerator TearDown() { yield return fixture.Dispose(); }
}
#endif
