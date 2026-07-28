using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reglas autoritativas de movimiento y colisión de entidades.
/// La solidez efectiva se calcula al crear la entidad y ya contempla
/// physics.solid, physics.not_solid y los overrides de la partida.
/// </summary>
public static class EntityMovementService
{
    private const float MapLimit = 19f;

    public static void Update(IDictionary<int, EntityRuntimeState> entities, float deltaTime)
    {
        foreach (EntityRuntimeState entity in entities.Values)
        {
            if (entity.Attributes == null ||
                !entity.Attributes.Has(EntityAttributeIds.Controllable) ||
                entity.MoveSpeed <= 0f)
                continue;

            Vector3 difference = entity.Destination - entity.Position;
            difference.y = 0f;

            if (difference.sqrMagnitude <= 0.01f)
            {
                entity.Position = new Vector3(entity.Destination.x, entity.Position.y, entity.Destination.z);
                continue;
            }

            Vector3 next = Vector3.MoveTowards(entity.Position, entity.Destination, entity.MoveSpeed * deltaTime);
            next.y = entity.Position.y;
            if (!IsPositionBlocked(entities, entity, next))
            {
                entity.Position = next;
            }
            else
            {
                entity.Destination = entity.Position;
            }
        }
    }

    public static bool TryApplyMove(
        IDictionary<int, EntityRuntimeState> entities,
        ulong senderClientId,
        EntityMoveCommand command,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (command == null || !entities.TryGetValue(command.UnitId, out EntityRuntimeState entity))
        {
            rejectionReason = "Entidad inexistente.";
            return false;
        }

        if (entity.OwnerClientId != senderClientId)
        {
            rejectionReason = $"Cliente {senderClientId} intentó mover una entidad ajena ({command.UnitId}).";
            return false;
        }

        if (entity.Attributes == null || !entity.Attributes.Has(EntityAttributeIds.Controllable))
        {
            rejectionReason = $"La entidad {command.UnitId} no posee el atributo de control.";
            return false;
        }

        Vector3 requestedDestination = new(command.X, entity.Position.y, command.Z);
        requestedDestination.x = Mathf.Clamp(requestedDestination.x, -MapLimit, MapLimit);
        requestedDestination.z = Mathf.Clamp(requestedDestination.z, -MapLimit, MapLimit);
        entity.Destination = requestedDestination;
        entity.InteractionTargetUnitId = -1;
        if (entity.Worker != null)
        {
            entity.Worker.TargetResourceUnitId = -1;
            entity.Worker.ExtractionTimer = 0f;
            entity.Worker.IsExtracting = false;
        }
        return true;
    }

    private static bool IsPositionBlocked(
        IDictionary<int, EntityRuntimeState> entities,
        EntityRuntimeState moving,
        Vector3 candidate)
    {
        // Una entidad no sólida no bloquea ni es bloqueada por otras entidades.
        if (!moving.Solid)
            return false;

        float movingRadius = Mathf.Max(moving.BoundsSize.x, moving.BoundsSize.z) * 0.5f;
        foreach (EntityRuntimeState other in entities.Values)
        {
            if (other.UnitId == moving.UnitId || !other.Solid)
                continue;

            bool blocked = other.Attributes != null && other.Attributes.Has(EntityAttributeIds.Building)
                ? BlocksBuilding(moving, other, candidate, movingRadius)
                : BlocksCircularEntity(moving, other, candidate, movingRadius);

            if (blocked)
                return true;
        }

        return false;
    }

    private static bool BlocksBuilding(
        EntityRuntimeState moving,
        EntityRuntimeState building,
        Vector3 candidate,
        float movingRadius)
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

        // Una entidad que haya aparecido solapada puede salir, pero no profundizar
        // el solapamiento. Esta protección es general y no depende de atributos de interacción.
        float currentPenetration = Mathf.Min(halfX - currentX, halfZ - currentZ);
        float candidatePenetration = Mathf.Min(halfX - candidateX, halfZ - candidateZ);
        return candidatePenetration >= currentPenetration - 0.0001f;
    }

    private static bool BlocksCircularEntity(
        EntityRuntimeState moving,
        EntityRuntimeState other,
        Vector3 candidate,
        float movingRadius)
    {
        float otherRadius = Mathf.Max(other.BoundsSize.x, other.BoundsSize.z) * 0.5f;
        float combinedRadius = movingRadius + otherRadius;
        float combinedRadiusSquared = combinedRadius * combinedRadius;

        Vector2 candidateDelta = new(candidate.x - other.Position.x, candidate.z - other.Position.z);
        if (candidateDelta.sqrMagnitude >= combinedRadiusSquared)
            return false;

        Vector2 currentDelta = new(
            moving.Position.x - other.Position.x,
            moving.Position.z - other.Position.z);

        return currentDelta.sqrMagnitude >= combinedRadiusSquared ||
               candidateDelta.sqrMagnitude <= currentDelta.sqrMagnitude + 0.0001f;
    }
}
