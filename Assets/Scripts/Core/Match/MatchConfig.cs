using System.Collections.Generic;

public class MatchConfig
{
    public MatchMode Mode { get; }
    public string ScenarioId { get; }
    public IReadOnlyList<TeamSetup> Teams { get; }

    public MatchConfig(MatchMode mode, string scenarioId, IReadOnlyList<TeamSetup> teams)
    {
        
        Mode = mode;
        ScenarioId = scenarioId;
        Teams = teams;
    }
}