using System;
using UnityEngine;

[Serializable]
public sealed class ScenarioDefinition
{
    public string contentType = "scenario";
    public string id;
    public string name;
    public string description;
    public int maxTeams = 4;
    public int maxPlayers = 8;
    public bool fixedTeams;
    public ScenarioWorldSize worldSize;
    public ScenarioTerrainDefinition terrain;
    public ScenarioSpawnPoint[] spawnPoints;
    public ScenarioEntityPlacement[] entities;
    public ScenarioMissionDefinition[] missions;
    public ScenarioTeamResourceDefinition[] teamResources;
    public ScenarioSettingOverride[] settingOverrides;
}

[Serializable]
public sealed class ScenarioTerrainDefinition
{
    public string defaultTerrainId = "praderas_primavera";
    public ScenarioTerrainTilePlacement[] tiles;
}

[Serializable]
public sealed class ScenarioTerrainTilePlacement
{
    public int x;
    public int z;
    public string terrainId;
}

[Serializable]
public sealed class ScenarioWorldSize
{
    public float width;
    public float height;
}

[Serializable]
public sealed class ScenarioVector3
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToVector3() => new(x, y, z);
}

[Serializable]
public sealed class ScenarioSpawnPoint
{
    public int teamId;
    public ScenarioVector3 position;
}

/// <summary>
/// Instancia colocada dentro de un escenario. La definición completa vive en
/// GameContent/Entities y se referencia mediante entityId.
/// </summary>
[Serializable]
public sealed class ScenarioEntityPlacement
{
    public string id;
    public string entityId;
    public int teamId;
    public int ownerTeamSlot = 1;
    public string[] attributes;
    public ScenarioVector3 position;
}

[Serializable]
public sealed class ScenarioMissionDefinition
{
    public string id;
    public string title;
    public string description;
    public bool optional;
}

[Serializable]
public sealed class ScenarioTeamResourceDefinition
{
    public int teamId;
    public int gold;
}

[Serializable]
public sealed class ScenarioSettingOverride
{
    public string key;
    public string displayName;
    public string value;
    public bool enabled = true;
}

[Serializable]
public sealed class ActiveSettingOverride
{
    public string Key;
    public string DisplayName;
    public string Value;
    public bool Enabled;

    public ActiveSettingOverride() { }

    public ActiveSettingOverride(ScenarioSettingOverride source)
    {
        Key = source.key;
        DisplayName = string.IsNullOrWhiteSpace(source.displayName) ? source.key : source.displayName;
        Value = source.value;
        Enabled = source.enabled;
    }
}
