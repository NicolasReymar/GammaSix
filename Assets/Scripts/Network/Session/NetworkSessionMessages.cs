using System;
using System.Collections.Generic;

[Serializable]
public sealed class PlayerRosterPayload
{
    public List<NetworkPlayerInfo> Players;
    public PlayerRosterPayload(List<NetworkPlayerInfo> players) => Players = players;
}

[Serializable]
public sealed class ColorRequestPayload
{
    public ulong TargetClientId;
    public int ColorId;
}

[Serializable]
public sealed class TeamRequestPayload
{
    public ulong TargetClientId;
    public int TeamId;
}

[Serializable]
public sealed class LobbySettingsPayload
{
    public bool FixedColors;
    public bool FixedTeams;
    public bool FixedTeamsForcedByScenario;
    public string SelectedContentId;
    public GameContentType SelectedContentType;
    public int ScenarioMaxPlayers;
    public int ScenarioMaxTeams;
    public List<ActiveSettingOverride> Overrides;
}

[Serializable]
public sealed class ScenarioSelectionPayload
{
    public string ContentId;
    public GameContentType ContentType;
    public string ScenarioId;
}
