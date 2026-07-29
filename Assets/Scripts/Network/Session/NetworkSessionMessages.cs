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
    public int TargetParticipantId;
    public int ColorId;
}

[Serializable]
public sealed class TeamRequestPayload
{
    public int TargetParticipantId;
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
    public int ScenarioMaxParticipants;
    public int ScenarioMaxTeams;
    public string GameModeId;
    public List<HeadlessProfileDefinition> AvailableHeadlessProfiles;
    public List<ActiveSettingOverride> Overrides;
}

[Serializable]
public sealed class ScenarioSelectionPayload
{
    public string ContentId;
    public GameContentType ContentType;
    public string ScenarioId;
    public string PackageId;
    public string PackageVersion;
    public string ContentHash;
}

[Serializable]
public sealed class ContentCompatibilityPayload
{
    public string ScenarioId;
    public string PackageId;
    public string PackageVersion;
    public string ContentHash;
    public bool Compatible;
    public string Status;
}
