using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Único punto de altas y bajas dinámicas durante la simulación. Los sistemas
/// encolan cambios y el runtime los aplica entre etapas del tick.
/// </summary>
public sealed class EntityLifecycleService
{
    private readonly EntityWorld world;
    private readonly RuntimeEntityIdAllocator idAllocator;
    private readonly MatchParticipantRegistry participants;
    private readonly MatchWorldBounds worldBounds;
    private readonly MatchEntityCatalog entityCatalog;
    private readonly Queue<EntitySpawnRequest> pendingSpawns = new();
    private readonly Queue<EntityDespawnRequest> pendingDespawns = new();
    private readonly Queue<EntityReplacementRequest> pendingReplacements = new();

    public int EntityCount => world.Count;
    public int PendingSpawnCount => pendingSpawns.Count;
    public int PendingDespawnCount => pendingDespawns.Count;
    public int PendingReplacementCount => pendingReplacements.Count;

    public event Action<EntitySpawnedEvent> EntitySpawned;
    public event Action<EntityDespawnedEvent> EntityDespawned;

    public EntityLifecycleService(
        EntityWorld world,
        RuntimeEntityIdAllocator idAllocator,
        MatchParticipantRegistry participants,
        MatchWorldBounds worldBounds,
        MatchEntityCatalog entityCatalog)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
        this.participants = participants ?? throw new ArgumentNullException(nameof(participants));
        this.worldBounds = worldBounds ?? throw new ArgumentNullException(nameof(worldBounds));
        this.entityCatalog = entityCatalog ?? throw new ArgumentNullException(nameof(entityCatalog));
    }

    public bool QueueSpawn(EntitySpawnRequest request, out string rejectionReason)
    {
        rejectionReason = ValidateSpawnRequest(request, out string resolvedEntityId);
        if (rejectionReason != null)
            return false;

        request.EntityDefinitionId = resolvedEntityId;
        pendingSpawns.Enqueue(request);
        return true;
    }

    public bool QueueReplacement(
        int sourceEntityId,
        EntitySpawnRequest replacement,
        EntityLifecycleReason reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (sourceEntityId <= 0)
        {
            rejectionReason = "El EntityId de origen debe ser mayor que cero.";
            return false;
        }

        if (!world.TryGet(sourceEntityId, out _))
        {
            rejectionReason = $"No existe la entidad runtime {sourceEntityId} que se quiere reemplazar.";
            return false;
        }

        rejectionReason = ValidateSpawnRequest(replacement, out string resolvedEntityId);
        if (rejectionReason != null)
            return false;

        replacement.EntityDefinitionId = resolvedEntityId;
        pendingReplacements.Enqueue(new EntityReplacementRequest
        {
            SourceEntityId = sourceEntityId,
            Replacement = replacement,
            Reason = reason
        });
        return true;
    }

    public bool TryTransferOwnership(
        int entityId,
        int ownerParticipantId,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (entityId <= 0 || !world.TryGet(entityId, out EntityRuntimeState entity))
        {
            rejectionReason = $"No existe la entidad runtime {entityId}.";
            return false;
        }

        MatchParticipantRuntimeState owner = null;
        if (ownerParticipantId > 0 && !participants.TryGet(ownerParticipantId, out owner))
        {
            rejectionReason = $"No existe el participante propietario {ownerParticipantId}.";
            return false;
        }

        if (owner == null)
        {
            entity.OwnerParticipantId = -1;
            entity.OwnerClientId = ulong.MaxValue;
            entity.TeamId = 0;
            entity.ColorId = PlayerColorPalette.Neutral;
        }
        else
        {
            entity.OwnerParticipantId = owner.ParticipantId;
            entity.OwnerClientId = owner.ClientId;
            entity.TeamId = owner.TeamId;
            entity.ColorId = owner.ColorId;
        }

        entity.InteractionTargetUnitId = -1;
        entity.Navigation?.ClearAll(entity.Position, "ownership-changed");
        entity.Destination = entity.Position;
        entity.Attack?.ClearTarget();
        if (entity.Worker != null)
        {
            entity.Worker.TargetResourceUnitId = -1;
            entity.Worker.ExtractionTimer = 0f;
            entity.Worker.IsExtracting = false;
        }

        return true;
    }

    public bool QueueDespawn(
        int entityId,
        EntityLifecycleReason reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (entityId <= 0)
        {
            rejectionReason = "El EntityId a eliminar debe ser mayor que cero.";
            return false;
        }

        if (!world.TryGet(entityId, out _))
        {
            rejectionReason = $"No existe la entidad runtime {entityId}.";
            return false;
        }

        pendingDespawns.Enqueue(new EntityDespawnRequest
        {
            EntityId = entityId,
            Reason = reason
        });
        return true;
    }

    public int FlushPending()
    {
        int applied = 0;
        HashSet<int> removedThisFlush = new();

        while (pendingDespawns.Count > 0)
        {
            EntityDespawnRequest request = pendingDespawns.Dequeue();
            if (!removedThisFlush.Add(request.EntityId) ||
                !world.TryGet(request.EntityId, out EntityRuntimeState entity))
            {
                continue;
            }

            world.RemoveImmediate(request.EntityId);
            ClearReferencesToRemovedEntity(request.EntityId);
            EntityDespawned?.Invoke(new EntityDespawnedEvent(entity, request.Reason));
            applied++;
        }

        while (pendingReplacements.Count > 0)
        {
            EntityReplacementRequest request = pendingReplacements.Dequeue();
            if (request?.Replacement == null ||
                !removedThisFlush.Add(request.SourceEntityId) ||
                !world.TryGet(request.SourceEntityId, out EntityRuntimeState source))
            {
                continue;
            }

            if (!TryCreateRuntimeState(
                    request.Replacement,
                    out EntityRuntimeState replacement,
                    out string error))
            {
                removedThisFlush.Remove(request.SourceEntityId);
                Debug.LogWarning($"[EntityLifecycleService] Reemplazo rechazado: {error}");
                continue;
            }

            world.RemoveImmediate(request.SourceEntityId);
            ClearReferencesToRemovedEntity(request.SourceEntityId);
            EntityDespawned?.Invoke(new EntityDespawnedEvent(source, request.Reason));

            world.AddImmediate(replacement);
            EntitySpawned?.Invoke(new EntitySpawnedEvent(replacement, request.Reason));
            applied += 2;
        }

        while (pendingSpawns.Count > 0)
        {
            EntitySpawnRequest request = pendingSpawns.Dequeue();
            if (!TryCreateRuntimeState(request, out EntityRuntimeState entity, out string error))
            {
                Debug.LogWarning($"[EntityLifecycleService] Spawn rechazado: {error}");
                continue;
            }

            world.AddImmediate(entity);
            EntitySpawned?.Invoke(new EntitySpawnedEvent(entity, request.Reason));
            applied++;
        }

        return applied;
    }

    public void ClearPending()
    {
        pendingSpawns.Clear();
        pendingDespawns.Clear();
        pendingReplacements.Clear();
    }

    private string ValidateSpawnRequest(EntitySpawnRequest request, out string resolvedEntityId)
    {
        resolvedEntityId = null;
        if (request == null)
            return "La solicitud de spawn es nula.";
        if (string.IsNullOrWhiteSpace(request.EntityDefinitionId))
            return "La solicitud de spawn no declara EntityDefinitionId.";

        bool initialization = request.Reason == EntityLifecycleReason.ScenarioInitialization;
        bool resolved = initialization
            ? entityCatalog.TryResolveLoaded(request.EntityDefinitionId, out resolvedEntityId, out _)
            : entityCatalog.TryResolveSpawnable(request.EntityDefinitionId, out resolvedEntityId, out _);
        if (!resolved)
        {
            return initialization
                ? $"La entidad '{request.EntityDefinitionId}' no fue cargada por el escenario."
                : $"La entidad '{request.EntityDefinitionId}' no está habilitada para spawn dinámico en esta partida.";
        }

        if (request.TeamId < 0)
            return "TeamId no puede ser negativo.";
        if (request.TeamId > 0 && request.OwnerParticipantId <= 0)
            return "Una entidad de equipo debe declarar OwnerParticipantId.";
        if (request.OwnerParticipantId > 0 && !participants.TryGet(request.OwnerParticipantId, out _))
            return $"No existe el participante propietario {request.OwnerParticipantId}.";
        return null;
    }

    private bool TryCreateRuntimeState(
        EntitySpawnRequest request,
        out EntityRuntimeState entity,
        out string rejectionReason)
    {
        entity = null;
        rejectionReason = ValidateSpawnRequest(request, out string resolvedEntityId);
        if (rejectionReason != null)
            return false;

        request.EntityDefinitionId = resolvedEntityId;
        EntityDefinition definition = EntityDefinitionRepository.Load(resolvedEntityId);
        if (definition == null)
        {
            rejectionReason = $"No existe la definición '{request.EntityDefinitionId}'.";
            return false;
        }

        int teamId = request.TeamId;
        int colorId = request.ColorId;
        ulong ownerClientId = ulong.MaxValue;
        if (request.OwnerParticipantId > 0)
        {
            participants.TryGet(request.OwnerParticipantId, out MatchParticipantRuntimeState owner);
            teamId = teamId > 0 ? teamId : owner.TeamId;
            colorId = colorId >= 0 ? colorId : owner.ColorId;
            ownerClientId = owner.ClientId;
        }
        else
        {
            teamId = 0;
            colorId = colorId >= 0 ? colorId : PlayerColorPalette.Neutral;
        }

        Vector3 position = worldBounds.Clamp(request.Position);
        position.y = ResolveGroundY(
            definition,
            request.AlignToDefinitionGround ? 0f : position.y);
        entity = EntityRuntimeFactory.Create(
            idAllocator.Next(),
            definition,
            request.InstanceAttributes,
            request.OwnerParticipantId,
            ownerClientId,
            teamId,
            colorId,
            position);
        entity.ScenarioInstanceId = request.ScenarioInstanceId;
        return true;
    }

    private void ClearReferencesToRemovedEntity(int entityId)
    {
        foreach (EntityRuntimeState state in world.Values)
        {
            if (state.InteractionTargetUnitId == entityId)
            {
                state.InteractionTargetUnitId = -1;
                state.Navigation?.ClearAll(state.Position, "target-removed");
                state.Destination = state.Position;
            }
            if (state.Worker != null && state.Worker.TargetResourceUnitId == entityId)
            {
                state.Worker.TargetResourceUnitId = -1;
                state.Worker.IsExtracting = false;
                state.Worker.ExtractionTimer = 0f;
                state.Destination = state.Position;
            }
        }
    }

    private static float ResolveGroundY(EntityDefinition definition, float requestedY)
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
}
