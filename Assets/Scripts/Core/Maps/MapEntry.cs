public class MapEntry
{
    public string ScenarioId { get; }
    public string DisplayName { get; }
    public string FilePath { get; }

    public MapEntry(string scenarioId, string displayName, string filePath)
    {
        ScenarioId = scenarioId;
        DisplayName = displayName;
        FilePath = filePath;
    }
}