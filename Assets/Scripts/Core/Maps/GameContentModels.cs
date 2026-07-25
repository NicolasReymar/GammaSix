using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameContentType
{
    Scenario,
    Campaign
}

[Serializable]
public class GameContentEntry
{
    public string ContentId;
    public string DisplayName;
    public string Description;
    public GameContentType ContentType;
    public string FilePath;
    public string FirstScenarioId;
}

[Serializable]
public class ScenarioDefinition
{
    public string contentType = "scenario";
    public string id;
    public string name;
    public string description;
    public int maxTeams = 4;
    public int maxPlayers = 8;
    public bool fixedTeams;
    public ScenarioWorldSize worldSize;
    public ScenarioSpawnPoint[] spawnPoints;
    public ScenarioEntityDefinition[] entities;
    public ScenarioMissionDefinition[] missions;
    public ScenarioTeamResourceDefinition[] teamResources;
    public ScenarioSettingOverride[] settingOverrides;
}

[Serializable]
public class ScenarioWorldSize
{
    public float width;
    public float height;
}

[Serializable]
public class ScenarioVector3
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToVector3() => new(x, y, z);
}

[Serializable]
public class ScenarioSpawnPoint
{
    public int teamId;
    public ScenarioVector3 position;
}

[Serializable]
public class ScenarioEntityDefinition
{
    // Identificador único de esta instancia dentro del escenario.
    public string id;

    // Identificador del archivo de definición almacenado en GameContent/Entities.
    public string entityId;

    // Team 0 es neutral y no necesita un jugador propietario.
    public int teamId;

    // Posición del jugador dentro del equipo, comenzando desde 1.
    // Se ignora para teamId 0.
    public int ownerTeamSlot = 1;

    // Atributos adicionales aplicados únicamente a esta instancia.
    // Se combinan con los atributos definidos por entityId.
    public string[] attributes;

    public ScenarioVector3 position;
}

[Serializable]
public class ScenarioMissionDefinition
{
    public string id;
    public string title;
    public string description;
    public bool optional;
}

[Serializable]
public class ScenarioTeamResourceDefinition
{
    public int teamId;
    public int gold;
}

[Serializable]
public class ScenarioSettingOverride
{
    public string key;
    public string displayName;
    public string value;
    public bool enabled = true;
}

[Serializable]
public class CampaignDefinition
{
    public string contentType = "campaign";
    public string id;
    public string name;
    public string description;
    public CampaignStepDefinition[] steps;
}

[Serializable]
public class CampaignStepDefinition
{
    public string id;
    public string type;
    public string scenarioId;
    public string resource;
}

[Serializable]
public class ActiveSettingOverride
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
