using System;

public enum DiplomacyStance
{
    Ally,
    Neutral,
    Enemy
}

[Serializable]
public sealed class DiplomacyStanceSnapshotData
{
    public int SourceTeamId;
    public int TargetTeamId;
    public string Stance;
}

public sealed class DiplomacyStanceChangedEvent
{
    public int SourceTeamId { get; }
    public int TargetTeamId { get; }
    public DiplomacyStance PreviousStance { get; }
    public DiplomacyStance CurrentStance { get; }
    public int ChangedByParticipantId { get; }
    public string Reason { get; }

    public DiplomacyStanceChangedEvent(
        int sourceTeamId,
        int targetTeamId,
        DiplomacyStance previousStance,
        DiplomacyStance currentStance,
        int changedByParticipantId,
        string reason)
    {
        SourceTeamId = sourceTeamId;
        TargetTeamId = targetTeamId;
        PreviousStance = previousStance;
        CurrentStance = currentStance;
        ChangedByParticipantId = changedByParticipantId;
        Reason = reason;
    }
}
