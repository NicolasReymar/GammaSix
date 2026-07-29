using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Convierte colocaciones del escenario en solicitudes del ciclo de vida común.
/// Las oleadas y reglas futuras utilizarán la misma ruta de spawn.
/// </summary>
public static class ScenarioEntitySpawner
{
    private const string DefaultHumanoidId = "unit.humanoid.default";

    public static bool TryPopulate(
        EntityLifecycleService lifecycle,
        ScenarioDefinition scenario,
        IReadOnlyList<MatchParticipantRuntimeState> participants)
    {
        if (lifecycle == null)
            throw new ArgumentNullException(nameof(lifecycle));
        if (scenario?.entities == null || scenario.entities.Length == 0)
            return false;

        Dictionary<int, List<MatchParticipantRuntimeState>> participantsByTeam = participants
            .GroupBy(participant => participant.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(participant => participant.SlotIndex)
                    .ThenBy(participant => participant.ParticipantId)
                    .ToList());

        int initialCount = lifecycle.EntityCount;
        int placementIndex = 0;
        foreach (ScenarioEntityPlacement placement in scenario.entities)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.entityId))
                continue;

            if (!TryResolveOwner(
                    placement,
                    participantsByTeam,
                    out int ownerParticipantId,
                    out int colorId))
            {
                continue;
            }

            Vector3 spawnPosition = placement.position != null
                ? placement.position.ToVector3()
                : GetSpawnPosition(placementIndex, scenario.entities.Length);

            EntitySpawnRequest request = new()
            {
                EntityDefinitionId = placement.entityId,
                ScenarioInstanceId = placement.id,
                InstanceAttributes = placement.attributes,
                OwnerParticipantId = ownerParticipantId,
                TeamId = placement.teamId,
                ColorId = colorId,
                Position = spawnPosition,
                Reason = EntityLifecycleReason.ScenarioInitialization
            };

            if (lifecycle.QueueSpawn(request, out string rejection))
            {
                placementIndex++;
            }
            else
            {
                Debug.LogWarning($"[ScenarioEntitySpawner] No se pudo encolar '{placement.id}': {rejection}");
            }
        }

        lifecycle.FlushPending();
        return lifecycle.EntityCount > initialCount;
    }

    public static void CreateFallback(
        EntityLifecycleService lifecycle,
        IReadOnlyList<MatchParticipantRuntimeState> participants)
    {
        if (lifecycle == null)
            throw new ArgumentNullException(nameof(lifecycle));

        int index = 0;
        foreach (MatchParticipantRuntimeState participant in participants
                     .OrderBy(item => item.SlotIndex)
                     .ThenBy(item => item.ParticipantId))
        {
            EntitySpawnRequest request = new()
            {
                EntityDefinitionId = DefaultHumanoidId,
                ScenarioInstanceId = $"fallback.{participant.ParticipantId}",
                OwnerParticipantId = participant.ParticipantId,
                TeamId = participant.TeamId,
                ColorId = participant.ColorId,
                Position = GetSpawnPosition(index, participants.Count),
                Reason = EntityLifecycleReason.ScenarioInitialization
            };

            if (!lifecycle.QueueSpawn(request, out string rejection))
                Debug.LogWarning($"[ScenarioEntitySpawner] Fallback rechazado: {rejection}");
            index++;
        }

        lifecycle.FlushPending();
    }

    private static bool TryResolveOwner(
        ScenarioEntityPlacement placement,
        IReadOnlyDictionary<int, List<MatchParticipantRuntimeState>> participantsByTeam,
        out int ownerParticipantId,
        out int colorId)
    {
        ownerParticipantId = -1;
        colorId = PlayerColorPalette.Neutral;

        if (placement.teamId == 0)
            return true;

        if (!participantsByTeam.TryGetValue(
                placement.teamId,
                out List<MatchParticipantRuntimeState> teamParticipants) ||
            teamParticipants.Count == 0)
        {
            Debug.LogWarning($"[ScenarioEntitySpawner] La instancia '{placement.id}' pertenece al equipo " +
                             $"{placement.teamId}, pero ese equipo no tiene participantes asignados.");
            return false;
        }

        int requestedSlot = Mathf.Max(1, placement.ownerTeamSlot);
        int ownerIndex = Mathf.Clamp(requestedSlot - 1, 0, teamParticipants.Count - 1);
        MatchParticipantRuntimeState owner = teamParticipants[ownerIndex];
        ownerParticipantId = owner.ParticipantId;
        colorId = owner.ColorId;
        return true;
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
