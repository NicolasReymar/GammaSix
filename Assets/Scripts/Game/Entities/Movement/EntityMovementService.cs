using UnityEngine;

/// <summary>
/// Reglas autoritativas de movimiento y colisión de entidades.
/// La solidez efectiva se calcula al crear la entidad y ya contempla
/// physics.solid, physics.not_solid y los overrides de la partida.
/// </summary>
public static class EntityMovementService
{
    public static void Update(EntityWorld world, float deltaTime)
    {
        if (world == null)
            return;

        foreach (EntityRuntimeState entity in world.Values)
        {
            // La capacidad de desplazarse no depende de ser controlable por un
            // humano. Un headless o una regla puede asignar Destination a cualquier
            // entidad móvil; el atributo controllable solo valida órdenes directas.
            if (entity.MoveSpeed <= 0f || entity.Life == null || !entity.Life.CanAct)
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
            if (!IsPositionBlocked(world, entity, next))
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
        EntityWorld world,
        int issuerParticipantId,
        EntityMoveCommand command,
        MatchWorldBounds worldBounds,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (world == null || command == null ||
            !world.TryGet(command.UnitId, out EntityRuntimeState entity))
        {
            rejectionReason = "Entidad inexistente.";
            return false;
        }

        if (entity.Life == null || !entity.Life.CanAct)
        {
            rejectionReason = $"La entidad {command.UnitId} no puede moverse en su estado actual.";
            return false;
        }

        if (entity.OwnerParticipantId != issuerParticipantId)
        {
            rejectionReason = $"El participante {issuerParticipantId} intentó mover una entidad ajena ({command.UnitId}).";
            return false;
        }

        if (entity.Attributes == null || !entity.Attributes.Has(EntityAttributeIds.Controllable))
        {
            rejectionReason = $"La entidad {command.UnitId} no posee el atributo de control.";
            return false;
        }

        Vector3 requestedDestination = new(command.X, entity.Position.y, command.Z);
        requestedDestination = worldBounds != null
            ? worldBounds.Clamp(requestedDestination)
            : requestedDestination;
        entity.Destination = requestedDestination;
        entity.InteractionTargetUnitId = -1;
        entity.Attack?.ClearTargetPreservingRecovery();
        if (entity.Worker != null)
        {
            entity.Worker.TargetResourceUnitId = -1;
            entity.Worker.ExtractionTimer = 0f;
            entity.Worker.IsExtracting = false;
        }
        return true;
    }

    private static bool IsPositionBlocked(
        EntityWorld world,
        EntityRuntimeState moving,
        Vector3 candidate)
    {
        // Una entidad no sólida no bloquea ni es bloqueada por otras entidades.
        if (!moving.Solid)
            return false;

        float movingRadius = Mathf.Max(moving.BoundsSize.x, moving.BoundsSize.z) * 0.5f;
        foreach (EntityRuntimeState other in world.Values)
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
