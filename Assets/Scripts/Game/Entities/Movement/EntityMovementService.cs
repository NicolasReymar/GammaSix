using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reglas autoritativas de movimiento y colisión de entidades.
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
                entity.Position = next;
            else
                entity.Destination = entity.Position;
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
        return true;
    }

    private static bool IsPositionBlocked(
        IDictionary<int, EntityRuntimeState> entities,
        EntityRuntimeState moving,
        Vector3 candidate)
    {
        if (!moving.Solid)
            return false;

        float movingRadius = Mathf.Max(moving.BoundsSize.x, moving.BoundsSize.z) * 0.5f;
        foreach (EntityRuntimeState other in entities.Values)
        {
            if (other.UnitId == moving.UnitId || !other.Solid)
                continue;

            if (other.Attributes != null && other.Attributes.Has(EntityAttributeIds.Building))
            {
                float halfX = other.BoundsSize.x * 0.5f + movingRadius;
                float halfZ = other.BoundsSize.z * 0.5f + movingRadius;
                if (Mathf.Abs(candidate.x - other.Position.x) < halfX &&
                    Mathf.Abs(candidate.z - other.Position.z) < halfZ)
                    return true;
            }
            else
            {
                float otherRadius = Mathf.Max(other.BoundsSize.x, other.BoundsSize.z) * 0.5f;
                Vector2 delta = new(candidate.x - other.Position.x, candidate.z - other.Position.z);
                float combinedRadius = movingRadius + otherRadius;
                if (delta.sqrMagnitude < combinedRadius * combinedRadius)
                    return true;
            }
        }

        return false;
    }
}
