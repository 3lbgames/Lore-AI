using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "React To NPC World Event", story: "React to [Character] queued world event", category: "NPC/Events", id: "d5fcfe1840b14c4d94066fdc3e6293cb")]
public partial class NpcWorldEventTurnScope : NpcTurnScope
{
    NpcCharacterDirector director;
    NpcWorldEventInbox inbox;
    string eventId;
    int revision;
    bool started;
    readonly NpcRetryOnce memoryRetry = new NpcRetryOnce();

    protected override Status OnStart()
    {
        started = false;
        director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        inbox = Character.Value ? Character.Value.GetComponent<NpcWorldEventInbox>() : null;
        eventId = inbox ? inbox.Peek()?.eventId : null;
        if (eventId == null || !director || !director.profile || Child == null) return Status.Failure;
        revision = director.TurnRevision;
        memoryRetry.Start(LoadMemory, CheckMemory, null);
        return Advance();
    }

    protected override Status OnUpdate() => Advance();

    Status Advance()
    {
        if (!director || director.TurnRevision != revision || !inbox || inbox.Peek()?.eventId != eventId)
        {
            if (started) EndNode(Child);
            return Status.Success;
        }
        if (!started)
        {
            var memoryStatus = memoryRetry.Update();
            if (memoryStatus == Status.Running) return Status.Running;
            if (memoryStatus == Status.Failure) return Status.Success;
            started = true;
            return Complete(base.OnStart());
        }
        return Complete(base.OnUpdate());
    }

    Status LoadMemory()
    {
        if (!director.Memory.IsBusy && (!director.Memory.Loaded || director.Memory.Error != null)) director.Memory.Load();
        return CheckMemory();
    }

    Status CheckMemory() => director.Memory.IsBusy ? Status.Running :
        director.Memory.Loaded && director.Memory.Error == null ? Status.Success : Status.Failure;

    Status Complete(Status status)
    {
        if (status == Status.Success) inbox.Consume(eventId);
        return status == Status.Failure ? Status.Success : status;
    }
}
