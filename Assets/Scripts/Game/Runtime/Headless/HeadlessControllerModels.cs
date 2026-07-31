using System;
using System.Collections.Generic;

public enum HeadlessControllerRuntimeStatus
{
    Ready,
    Running,
    Suspended,
    ProfileNotFound,
    ProfileNotImplemented,
    ControllerNotRegistered,
    Failed
}

public sealed class HeadlessControllerInstanceState
{
    public int ParticipantId { get; internal set; }
    public string ParticipantName { get; internal set; }
    public string ProfileId { get; internal set; }
    public string RuntimeControllerId { get; internal set; }
    public HeadlessControllerRuntimeStatus Status { get; internal set; }
    public float NextUpdateAt { get; internal set; }
    public float LastUpdateAt { get; internal set; }
    public long DecisionsEvaluated { get; internal set; }
    public long OrdersIssued { get; internal set; }
    public int LastSourceEntityId { get; internal set; } = -1;
    public int LastTargetEntityId { get; internal set; } = -1;
    public string LastDecision { get; internal set; }
    public string LastError { get; internal set; }
}

public interface IHeadlessController
{
    string ControllerId { get; }

    void Initialize(HeadlessControllerInitializationContext context);

    void Tick(HeadlessControllerUpdateContext context);
}

public sealed class HeadlessControllerInitializationContext
{
    public MatchParticipantRuntimeState Participant { get; }
    public HeadlessProfileDefinition Profile { get; }
    public HeadlessControllerInstanceState State { get; }

    public HeadlessControllerInitializationContext(
        MatchParticipantRuntimeState participant,
        HeadlessProfileDefinition profile,
        HeadlessControllerInstanceState state)
    {
        Participant = participant;
        Profile = profile;
        State = state;
    }
}

public sealed class HeadlessControllerUpdateContext
{
    private readonly Func<MatchCommandType, object, long> enqueueCommand;

    public float ElapsedTime { get; }
    public MatchParticipantRuntimeState Participant { get; }
    public HeadlessProfileDefinition Profile { get; }
    public ScenarioHeadlessControllerSettings Settings { get; }
    public HeadlessPerceptionContext Perception { get; }
    public HeadlessControllerInstanceState State { get; }

    public HeadlessControllerUpdateContext(
        float elapsedTime,
        MatchParticipantRuntimeState participant,
        HeadlessProfileDefinition profile,
        ScenarioHeadlessControllerSettings settings,
        HeadlessPerceptionContext perception,
        HeadlessControllerInstanceState state,
        Func<MatchCommandType, object, long> enqueueCommand)
    {
        ElapsedTime = elapsedTime;
        Participant = participant;
        Profile = profile;
        Settings = settings;
        Perception = perception;
        State = state;
        this.enqueueCommand = enqueueCommand;
    }

    public long EnqueueCommand(MatchCommandType commandType, object payload)
    {
        if (enqueueCommand == null)
            return -1;

        long sequence = enqueueCommand(commandType, payload);
        if (sequence > 0)
            State.OrdersIssued++;
        return sequence;
    }
}
