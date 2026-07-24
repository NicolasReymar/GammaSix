using System.Collections.Generic;
using UnityEngine;

public static class MatchConfigFactory
{
    public static MatchConfig CreateSinglePlayerDefault()
    {
        return new MatchConfig(
            MatchMode.SinglePlayer,
            "test_scenario_01",
            CreateDefaultTwoTeams()
        );
    }

    public static MatchConfig CreateMultiplayerDefault()
    {
        return new MatchConfig(
            MatchMode.Multiplayer,
            "test_scenario_01",
            CreateDefaultTwoTeams()
        );
    }

    public static MatchConfig CreateMultiplayerDefault(string scenarioId)
    {
        string resolvedScenarioId = string.IsNullOrWhiteSpace(scenarioId)
            ? "test_scenario_01"
            : scenarioId;

        return new MatchConfig(
            MatchMode.Multiplayer,
            resolvedScenarioId,
            CreateDefaultTwoTeams()
        );
    }

    public static MatchConfig CreateCoopDefault()
    {
        return new MatchConfig(
            MatchMode.Coop,
            "test_scenario_01",
            CreateDefaultTwoTeams()
        );
    }

    private static IReadOnlyList<TeamSetup> CreateDefaultTwoTeams()
    {
        return new List<TeamSetup>
        {
            new TeamSetup(1, "Equipo Azul", Color.blue),
            new TeamSetup(2, "Equipo Rojo", Color.red)
        };
    }
    public static MatchConfig CreateSinglePlayerDefault(string scenarioId)
    {
        return new MatchConfig(
            MatchMode.SinglePlayer,
            scenarioId,
            CreateDefaultTwoTeams()
        );
    }
}