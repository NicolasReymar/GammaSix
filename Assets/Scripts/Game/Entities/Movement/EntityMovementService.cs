using UnityEngine;

/// <summary>
/// Seguimiento autoritativo de caminos y evitación local. El pathfinding evita
/// obstáculos inmóviles; las unidades móviles se rodean localmente o solicitan
/// un nuevo camino si quedan bloqueadas.
/// </summary>
public static class EntityMovementService
{
    public static void Update(
        EntityWorld world,
        NavigationRuntimeSystem navigation,
        float deltaTime,
        float elapsedTime)
    {
        if (world == null)
            return;

        foreach (EntityRuntimeState entity in world.SnapshotValues())
        {
            if (entity.MoveSpeed <= 0f || entity.Life == null || !entity.Life.CanAct)
                continue;

            navigation?.AdvanceWaypointIfReached(entity, elapsedTime);
            Vector3 target = entity.Destination;
            if (navigation != null && navigation.TryGetNextWaypoint(entity, out Vector3 waypoint))
                target = waypoint;

            Vector3 difference = target - entity.Position;
            difference.y = 0f;
            if (difference.sqrMagnitude <= 0.01f)
            {
                entity.Position = new Vector3(target.x, entity.Position.y, target.z);
                navigation?.AdvanceWaypointIfReached(entity, elapsedTime);
                continue;
            }

            float step = Mathf.Max(0f, entity.MoveSpeed * deltaTime);
            Vector3 direction = difference.normalized;
            Vector3 candidate = entity.Position + direction * Mathf.Min(step, difference.magnitude);
            candidate.y = entity.Position.y;

            if (!IsPositionBlocked(world, entity, candidate))
            {
                entity.Position = candidate;
                if (entity.Navigation != null)
                    entity.Navigation.BlockedSince = -1f;
                navigation?.AdvanceWaypointIfReached(entity, elapsedTime);
                continue;
            }

            if (TryLocalAvoidance(world, entity, direction, step, out Vector3 avoided))
            {
                entity.Position = avoided;
                navigation?.NotifyBlocked(entity, elapsedTime);
                continue;
            }

            navigation?.NotifyBlocked(entity, elapsedTime);
        }
    }

    public static bool TryApplyMove(
        EntityWorld world,
        int issuerParticipantId,
        EntityMoveCommand command,
        MatchWorldBounds worldBounds,
        NavigationRuntimeSystem navigation,
        float elapsedTime,
        out string rejectionReason)
    {
        if (!TryResolveControllable(world, issuerParticipantId, command?.UnitId ?? -1, out EntityRuntimeState entity, out rejectionReason))
            return false;

        Vector3 requested = new(command.X, entity.Position.y, command.Z);
        requested = worldBounds != null ? worldBounds.Clamp(requested) : requested;
        ClearCompetingActions(entity);
        return navigation != null
            ? navigation.TrySetMove(entity, requested, EntityNavigationOrderType.Move, elapsedTime, out rejectionReason)
            : ApplyLegacyDestination(entity, requested, out rejectionReason);
    }

    public static bool TryApplyAttackMove(
        EntityWorld world,
        int issuerParticipantId,
        EntityAttackMoveCommand command,
        MatchWorldBounds worldBounds,
        NavigationRuntimeSystem navigation,
        float elapsedTime,
        out string rejectionReason)
    {
        if (!TryResolveControllable(world, issuerParticipantId, command?.UnitId ?? -1, out EntityRuntimeState entity, out rejectionReason))
            return false;
        if (entity.Attack == null)
        {
            rejectionReason = "La entidad no posee ataque para ejecutar attack-move.";
            return false;
        }

        Vector3 requested = new(command.X, entity.Position.y, command.Z);
        requested = worldBounds != null ? worldBounds.Clamp(requested) : requested;
        ClearCompetingActions(entity);
        return navigation != null
            ? navigation.TrySetMove(entity, requested, EntityNavigationOrderType.AttackMove, elapsedTime, out rejectionReason)
            : ApplyLegacyDestination(entity, requested, out rejectionReason);
    }

    public static bool TryApplyPatrol(
        EntityWorld world,
        int issuerParticipantId,
        EntityPatrolCommand command,
        MatchWorldBounds worldBounds,
        NavigationRuntimeSystem navigation,
        float elapsedTime,
        out string rejectionReason)
    {
        if (!TryResolveControllable(world, issuerParticipantId, command?.UnitId ?? -1, out EntityRuntimeState entity, out rejectionReason))
            return false;

        Vector3 requested = new(command.X, entity.Position.y, command.Z);
        requested = worldBounds != null ? worldBounds.Clamp(requested) : requested;
        ClearCompetingActions(entity);
        return navigation != null
            ? navigation.TrySetPatrol(entity, requested, elapsedTime, out rejectionReason)
            : ApplyLegacyDestination(entity, requested, out rejectionReason);
    }

    private static bool TryResolveControllable(
        EntityWorld world,
        int issuerParticipantId,
        int unitId,
        out EntityRuntimeState entity,
        out string rejectionReason)
    {
        entity = null;
        rejectionReason = null;
        if (world == null || unitId <= 0 || !world.TryGet(unitId, out entity))
        {
            rejectionReason = "Entidad inexistente.";
            return false;
        }
        if (entity.Life == null || !entity.Life.CanAct)
        {
            rejectionReason = $"La entidad {unitId} no puede moverse en su estado actual.";
            return false;
        }
        if (entity.OwnerParticipantId != issuerParticipantId)
        {
            rejectionReason = $"El participante {issuerParticipantId} intentó mover una entidad ajena ({unitId}).";
            return false;
        }
        if (entity.Attributes == null || !entity.Attributes.Has(EntityAttributeIds.Controllable))
        {
            rejectionReason = $"La entidad {unitId} no posee el atributo de control.";
            return false;
        }
        if (entity.MoveSpeed <= 0f)
        {
            rejectionReason = $"La entidad {unitId} no puede desplazarse.";
            return false;
        }
        return true;
    }

    private static void ClearCompetingActions(EntityRuntimeState entity)
    {
        entity.InteractionTargetUnitId = -1;
        entity.Attack?.ClearTargetPreservingRecovery();
        if (entity.Worker != null)
        {
            entity.Worker.TargetResourceUnitId = -1;
            entity.Worker.ExtractionTimer = 0f;
            entity.Worker.IsExtracting = false;
        }
    }

    private static bool ApplyLegacyDestination(EntityRuntimeState entity, Vector3 destination, out string rejectionReason)
    {
        entity.Destination = destination;
        rejectionReason = null;
        return true;
    }

    private static bool TryLocalAvoidance(
        EntityWorld world,
        EntityRuntimeState entity,
        Vector3 forward,
        float step,
        out Vector3 candidate)
    {
        candidate = entity.Position;
        if (step <= 0f)
            return false;

        Vector3 side = new(-forward.z, 0f, forward.x);
        float sideWeight = 0.85f;
        Vector3 leftDirection = (forward * 0.35f + side * sideWeight).normalized;
        Vector3 rightDirection = (forward * 0.35f - side * sideWeight).normalized;

        Vector3 left = entity.Position + leftDirection * step;
        left.y = entity.Position.y;
        if (!IsPositionBlocked(world, entity, left))
        {
            candidate = left;
            return true;
        }

        Vector3 right = entity.Position + rightDirection * step;
        right.y = entity.Position.y;
        if (!IsPositionBlocked(world, entity, right))
        {
            candidate = right;
            return true;
        }

        return false;
    }

    private static bool IsPositionBlocked(EntityWorld world, EntityRuntimeState moving, Vector3 candidate)
    {
        if (!moving.Solid)
            return false;

        float movingRadius = Mathf.Max(moving.BoundsSize.x, moving.BoundsSize.z) * 0.5f;
        foreach (EntityRuntimeState other in world.Values)
        {
            if (other.UnitId == moving.UnitId || !other.Solid ||
                other.Life == null || other.Life.State == EntityLifeState.Dead)
            {
                continue;
            }

            bool blocked = other.Attributes != null && other.Attributes.Has(EntityAttributeIds.Building)
                ? BlocksBuilding(moving, other, candidate, movingRadius)
                : BlocksCircularEntity(moving, other, candidate, movingRadius);
            if (blocked)
                return true;
        }

        return false;
    }

    private static bool BlocksBuilding(EntityRuntimeState moving, EntityRuntimeState building, Vector3 candidate, float movingRadius)
    {
        float halfX = building.BoundsSize.x * 0.5f + movingRadius;
        float halfZ = building.BoundsSize.z * 0.5f + movingRadius;
        float candidateX = Mathf.Abs(candidate.x - building.Position.x);
        float candidateZ = Mathf.Abs(candidate.z - building.Position.z);
        if (candidateX >= halfX || candidateZ >= halfZ)
            return false;

        float currentX = Mathf.Abs(moving.Position.x - building.Position.x);
        float currentZ = Mathf.Abs(moving.Position.z - building.Position.z);
        bool currentlyInside = currentX < halfX && currentZ < halfZ;
        if (!currentlyInside)
            return true;

        float currentPenetration = Mathf.Min(halfX - currentX, halfZ - currentZ);
        float candidatePenetration = Mathf.Min(halfX - candidateX, halfZ - candidateZ);
        return candidatePenetration >= currentPenetration - 0.0001f;
    }

    private static bool BlocksCircularEntity(EntityRuntimeState moving, EntityRuntimeState other, Vector3 candidate, float movingRadius)
    {
        float otherRadius = Mathf.Max(other.BoundsSize.x, other.BoundsSize.z) * 0.5f;
        float combinedRadius = movingRadius + otherRadius;
        float combinedRadiusSquared = combinedRadius * combinedRadius;
        Vector2 candidateDelta = new(candidate.x - other.Position.x, candidate.z - other.Position.z);
        if (candidateDelta.sqrMagnitude >= combinedRadiusSquared)
            return false;

        Vector2 currentDelta = new(moving.Position.x - other.Position.x, moving.Position.z - other.Position.z);
        return currentDelta.sqrMagnitude >= combinedRadiusSquared ||
               candidateDelta.sqrMagnitude <= currentDelta.sqrMagnitude + 0.0001f;
    }
}
