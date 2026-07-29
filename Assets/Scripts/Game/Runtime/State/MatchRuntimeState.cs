using System;

public enum MatchPhaseState
{
    Loading,
    Running,
    Completed
}

public enum MatchResultState
{
    None,
    Victory,
    Defeat,
    Draw
}

public sealed class MatchResultDeclaredEvent
{
    public MatchResultState Result { get; }
    public int TeamId { get; }
    public string Reason { get; }

    public MatchResultDeclaredEvent(MatchResultState result, int teamId, string reason)
    {
        Result = result;
        TeamId = teamId;
        Reason = reason;
    }
}

public sealed class MatchRuntimeState
{
    public MatchPhaseState Phase { get; private set; } = MatchPhaseState.Loading;
    public MatchResultState Result { get; private set; } = MatchResultState.None;
    public int ResultTeamId { get; private set; }
    public string ResultReason { get; private set; }
    public bool IsCompleted => Phase == MatchPhaseState.Completed;

    public event Action<MatchResultDeclaredEvent> ResultDeclared;

    public void Start()
    {
        if (Phase == MatchPhaseState.Loading)
            Phase = MatchPhaseState.Running;
    }

    public bool DeclareResult(MatchResultState result, int teamId, string reason)
    {
        if (IsCompleted || result == MatchResultState.None)
            return false;

        Result = result;
        ResultTeamId = teamId;
        ResultReason = reason;
        Phase = MatchPhaseState.Completed;
        ResultDeclared?.Invoke(new MatchResultDeclaredEvent(result, teamId, reason));
        return true;
    }
}
