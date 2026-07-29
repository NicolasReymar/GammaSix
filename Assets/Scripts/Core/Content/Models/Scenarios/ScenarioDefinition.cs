using System;
using UnityEngine;

[Serializable]
public sealed class ScenarioDefinition
{
    public string contentType = "scenario";
    public string id;
    public string name;
    public string description;
    [NonSerialized] public string sourcePackageId;
    [NonSerialized] public string sourcePackageVersion;
    [NonSerialized] public string sourceContentHash;
    public int maxTeams = 4;
    public int maxPlayers = 8;
    public bool fixedTeams;
    public string gameModeId = "base:game-mode.normal";
    public ScenarioParticipantConfiguration participantConfiguration;
    public ScenarioHeadlessProfileDefinition[] headlessProfiles;
    public ScenarioWorldSize worldSize;
    public ScenarioTerrainDefinition terrain;
    public ScenarioEntityCatalogDefinition entityCatalog;
    public ScenarioSpawnPoint[] spawnPoints;
    public ScenarioEntityPlacement[] entities;
    public ScenarioMissionDefinition[] missions;
    public ScenarioTeamResourceDefinition[] teamResources;
    public ScenarioParticipantResourceDefinition[] participantResources;
    public ScenarioRuleDefinition[] rules;
    public ScenarioSettingOverride[] settingOverrides;
}


[Serializable]
public sealed class ScenarioParticipantConfiguration
{
    /// <summary>
    /// Cantidad máxima de humanos conectados. Si es 0 se utiliza maxPlayers.
    /// </summary>
    public int maximumHumanPlayers;

    /// <summary>
    /// Cantidad total de casillas, incluyendo headless. Si es 0 se utiliza
    /// maximumHumanPlayers/maxPlayers y se amplía para participantes obligatorios.
    /// </summary>
    public int maximumParticipants;

    public string[] availableHeadlessProfiles;
    public ScenarioRequiredParticipantDefinition[] requiredParticipants;
}

[Serializable]
public sealed class ScenarioRequiredParticipantDefinition
{
    public string slotId;
    public int slotIndex = -1;
    public string displayName;
    public string controllerProfileId;
    public int teamId = 1;
    public int colorId = -1;
    public bool participantLocked = true;
    public bool teamLocked = true;
    public bool colorLocked = true;
}

[Serializable]
public sealed class ScenarioHeadlessProfileDefinition
{
    public string id;
    public string displayName;
    public string description;
    [NonSerialized] public string sourcePackageId;
    [NonSerialized] public string sourcePackageVersion;
    [NonSerialized] public string sourceContentHash;
    public string sourceId;
    public string sourceLabel = "Escenario";
    public string gameModeId;
    public int maximumInstances = 1;
    public bool runtimeImplemented;
}


[Serializable]
public sealed class ScenarioEntityCatalogDefinition
{
    /// <summary>
    /// Si está activo, el escenario hereda el catálogo de entidades dinámicas
    /// registrado por su modo de juego. Por ejemplo, el modo normal incorpora
    /// las unidades clásicas base. El escenario puede desactivarlo para definir
    /// un catálogo completamente aislado.
    /// </summary>
    public bool includeGameModeDefaults = true;

    /// <summary>
    /// Definiciones adicionales que pueden crearse dinámicamente durante esta
    /// partida. Las colocaciones iniciales se cargan siempre; una oleada, regla,
    /// construcción o comando solo puede crear IDs heredados del modo o listados
    /// aquí.
    /// </summary>
    public string[] spawnableEntityIds;
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
    [NonSerialized] public string sourcePackageId;
    [NonSerialized] public string sourcePackageVersion;
    [NonSerialized] public string sourceContentHash;
    public bool optional;
}

[Serializable]
public sealed class ScenarioTeamResourceDefinition
{
    public int teamId;
    public int gold;
    public ScenarioResourceAmount[] resources;
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
