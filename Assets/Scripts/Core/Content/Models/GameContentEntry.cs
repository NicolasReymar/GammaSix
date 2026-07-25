using System;

[Serializable]
public sealed class GameContentEntry
{
    public string ContentId;
    public string DisplayName;
    public string Description;
    public GameContentType ContentType;
    public string FilePath;
    public string FirstScenarioId;
}
