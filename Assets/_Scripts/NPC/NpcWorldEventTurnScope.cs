using System;
using Unity.Behavior;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "React To NPC World Event", story: "React to [Character] queued world event", category: "NPC/Events", id: "d5fcfe1840b14c4d94066fdc3e6293cb")]
public partial class NpcWorldEventTurnScope : NpcTurnScope
{
    NpcWorldEventInbox inbox;
    string eventId;
    int revision;
    bool started;
    readonly NpcRetryOnce memoryRetry = new NpcRetryOnce();

    protected override Status OnStart()
    {
        started = false;
        Director = Character.Value ? Character.Value.GetComponent<NpcCharacterDirector>() : null;
        inbox = Character.Value ? Character.Value.GetComponent<NpcWorldEventInbox>() : null;
        eventId = inbox ? inbox.Peek()?.eventId : null;
        if (eventId == null || !Director || !Director.profile || Child == null) return Status.Failure;
        revision = Director.TurnRevision;
        memoryRetry.Start(LoadMemory, CheckMemory, null);
        return Advance();
    }

    protected override Status OnUpdate() => Advance();

    Status Advance()
    {
        if (!Director || Director.TurnRevision != revision || !inbox || inbox.Peek()?.eventId != eventId)
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
        if (!Director.Memory.IsBusy && (!Director.Memory.Loaded || Director.Memory.Error != null)) Director.Memory.Load();
        return CheckMemory();
    }

    Status CheckMemory() => Director.Memory.IsBusy ? Status.Running :
        Director.Memory.Loaded && Director.Memory.Error == null ? Status.Success : Status.Failure;

    Status Complete(Status status)
    {
        if (status == Status.Success) inbox.Consume(eventId);
        return status == Status.Failure ? Status.Success : status;
    }
}
