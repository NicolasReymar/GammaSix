using System.Collections.Generic;

public sealed class MatchTeamRuntimeState
{
    private readonly List<int> participantIds = new();

    public int TeamId { get; }
    public RuntimeResourceCollection Resources { get; }
    public IReadOnlyList<int> ParticipantIds => participantIds;

    public MatchTeamRuntimeState(int teamId)
    {
        TeamId = teamId;
        Resources = new RuntimeResourceCollection("team", teamId);
    }

    public void AddParticipant(int participantId)
    {
        if (participantId > 0 && !participantIds.Contains(participantId))
            participantIds.Add(participantId);
    }
}
