using System;
using Unity.Behavior;
using UnityEngine;

internal sealed class NpcRetryOnce
{
    bool retried, waiting;
    float retryAt;
    Func<Node.Status> startAttempt, updateAttempt;
    System.Action cleanupAttempt;

    public Node.Status Start(Func<Node.Status> start, Func<Node.Status> update, System.Action cleanup)
    {
        retried = waiting = false;
        startAttempt = start; updateAttempt = update; cleanupAttempt = cleanup;
        return Observe(startAttempt());
    }

    public Node.Status Update()
    {
        if (!waiting) return Observe(updateAttempt());
        if (Time.realtimeSinceStartup < retryAt) return Node.Status.Running;
        waiting = false;
        return Observe(startAttempt());
    }

    Node.Status Observe(Node.Status status)
    {
        if (status != Node.Status.Failure || retried) return status;
        cleanupAttempt?.Invoke();
        retried = waiting = true;
        retryAt = Time.realtimeSinceStartup + .5f;
        return Node.Status.Running;
    }
}
