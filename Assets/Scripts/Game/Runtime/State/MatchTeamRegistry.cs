using System;
using System.Collections.Generic;
using System.Linq;

public sealed class MatchTeamRegistry
{
    private readonly Dictionary<int, MatchTeamRuntimeState> byId = new();

    public IReadOnlyList<MatchTeamRuntimeState> All => byId.Values
        .OrderBy(item => item.TeamId)
        .ToList();

    public int Count => byId.Count;

    public event Action<RuntimeResourceChangedEvent> TeamResourceChanged;

    public MatchTeamRegistry(
        ScenarioDefinition scenario,
        IEnumerable<MatchParticipantRuntimeState> participants)
    {
        if (participants != null)
        {
            foreach (MatchParticipantRuntimeState participant in participants)
            {
                if (participant == null || participant.TeamId <= 0)
                    continue;

                MatchTeamRuntimeState team = GetOrCreate(participant.TeamId);
                team.AddParticipant(participant.ParticipantId);
            }
        }

        if (scenario?.teamResources != null)
        {
            foreach (ScenarioTeamResourceDefinition definition in scenario.teamResources)
            {
                if (definition == null || definition.teamId <= 0)
                    continue;

                MatchTeamRuntimeState team = GetOrCreate(definition.teamId);
                if (definition.gold > 0)
                    team.Resources.Set("gold", definition.gold, false);

                if (definition.resources == null)
                    continue;

                foreach (ScenarioResourceAmount resource in definition.resources)
                {
                    if (resource == null || string.IsNullOrWhiteSpace(resource.resourceId))
                        continue;
                    team.Resources.Set(resource.resourceId, resource.amount, false);
                }
            }
        }
    }

    public bool TryGet(int teamId, out MatchTeamRuntimeState team)
    {
        return byId.TryGetValue(teamId, out team);
    }

    public MatchTeamRuntimeState GetOrCreate(int teamId)
    {
        if (teamId <= 0)
            throw new ArgumentOutOfRangeException(nameof(teamId), "TeamId debe ser mayor que cero.");

        if (!byId.TryGetValue(teamId, out MatchTeamRuntimeState team))
        {
            team = new MatchTeamRuntimeState(teamId);
            team.Resources.Changed += OnTeamResourceChanged;
            byId.Add(teamId, team);
        }

        return team;
    }
    private void OnTeamResourceChanged(RuntimeResourceChangedEvent resourceEvent)
    {
        TeamResourceChanged?.Invoke(resourceEvent);
    }
}
