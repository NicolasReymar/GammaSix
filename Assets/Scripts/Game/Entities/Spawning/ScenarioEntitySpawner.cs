using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Convierte colocaciones de escenario en estados runtime autoritativos.
/// No conoce mensajes de red ni vistas de Unity.
/// </summary>
public static class ScenarioEntitySpawner
{
    private const string DefaultHumanoidId = "unit.humanoid.default";

    public static bool TryPopulate(
        IDictionary<int, EntityRuntimeState> target,
        ScenarioDefinition scenario,
        IReadOnlyList<NetworkPlayerInfo> players)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (scenario?.entities == null || scenario.entities.Length == 0)
            return false;

        Dictionary<int, List<NetworkPlayerInfo>> playersByTeam = players
            .GroupBy(player => player.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(player => player.ClientId).ToList());

        int runtimeId = 1;
        foreach (ScenarioEntityPlacement placement in scenario.entities)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.entityId))
                continue;

            EntityDefinition definition = EntityDefinitionRepository.Load(placement.entityId);
            if (definition == null)
                continue;

            if (!TryResolveOwner(placement, playersByTeam, out ulong ownerClientId, out int colorId))
                continue;

            Vector3 spawnPosition = placement.position != null
                ? placement.position.ToVector3()
                : GetSpawnPosition(runtimeId - 1, scenario.entities.Length);
            spawnPosition.y = GetEntityGroundY(definition, spawnPosition.y);

            EntityRuntimeState entity = CreateRuntimeState(
                runtimeId,
                definition,
                placement.attributes,
                ownerClientId,
                placement.teamId,
                colorId,
                spawnPosition);

            target.Add(entity.UnitId, entity);
            runtimeId++;
        }

        return target.Count > 0;
    }

    public static void CreateFallback(
        IDictionary<int, EntityRuntimeState> target,
        IReadOnlyList<NetworkPlayerInfo> players)
    {
        EntityDefinition definition = EntityDefinitionRepository.Load(DefaultHumanoidId);
        if (definition == null)
            return;

        int index = 0;
        foreach (NetworkPlayerInfo player in players.OrderBy(item => item.TeamId).ThenBy(item => item.ClientId))
        {
            Vector3 spawnPosition = GetSpawnPosition(index, players.Count);
            EntityRuntimeState entity = CreateRuntimeState(
                index + 1,
                definition,
                null,
                player.ClientId,
                player.TeamId,
                player.ColorId,
                spawnPosition);

            target.Add(entity.UnitId, entity);
            index++;
        }
    }

    private static bool TryResolveOwner(
        ScenarioEntityPlacement placement,
        IReadOnlyDictionary<int, List<NetworkPlayerInfo>> playersByTeam,
        out ulong ownerClientId,
        out int colorId)
    {
        ownerClientId = ulong.MaxValue;
        colorId = PlayerColorPalette.Neutral;

        if (placement.teamId == 0)
            return true;

        if (!playersByTeam.TryGetValue(placement.teamId, out List<NetworkPlayerInfo> teamPlayers) ||
            teamPlayers.Count == 0)
        {
            Debug.LogWarning($"[ScenarioEntitySpawner] La instancia '{placement.id}' pertenece al equipo " +
                             $"{placement.teamId}, pero ese equipo no tiene jugadores conectados.");
            return false;
        }

        int requestedSlot = Mathf.Max(1, placement.ownerTeamSlot);
        int ownerIndex = Mathf.Clamp(requestedSlot - 1, 0, teamPlayers.Count - 1);
        NetworkPlayerInfo owner = teamPlayers[ownerIndex];
        ownerClientId = owner.ClientId;
        colorId = owner.ColorId;
        return true;
    }

    private static EntityRuntimeState CreateRuntimeState(
        int runtimeId,
        EntityDefinition definition,
        IEnumerable<string> instanceAttributes,
        ulong ownerClientId,
        int teamId,
        int colorId,
        Vector3 position)
    {
        return EntityRuntimeFactory.Create(
            runtimeId,
            definition,
            instanceAttributes,
            ownerClientId,
            teamId,
            colorId,
            position);
    }

    private static float GetEntityGroundY(EntityDefinition definition, float requestedY)
    {
        if (requestedY > 0f)
            return requestedY;

        if (definition.groundOffset >= 0f)
            return definition.groundOffset;

        Vector3 scale = definition.GetScale(new Vector3(0.8f, 1f, 0.8f));
        return string.Equals(definition.kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase)
            ? 0.5f
            : scale.y * 0.5f;
    }

    private static Vector3 GetSpawnPosition(int index, int entityCount)
    {
        if (entityCount <= 1)
            return new Vector3(0f, 0.5f, 0f);

        float angle = index * Mathf.PI * 2f / entityCount;
        const float radius = 7f;
        return new Vector3(Mathf.Cos(angle) * radius, 0.5f, Mathf.Sin(angle) * radius);
    }
}
