using UnityEngine;

/// <summary>
/// Procesa interacciones contextuales no especializadas. Por ahora permite que
/// entidades controlables sigan a unidades o edificios aliados y neutrales.
/// Las interacciones especializadas, como recursos, se resuelven antes en su
/// servicio correspondiente.
/// </summary>
public static class EntityInteractionService
{
    public static bool TryAssignFollow(
        EntityWorld world,
        int issuerParticipantId,
        EntityInteractionCommand command,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (world == null || command == null ||
            !world.TryGet(command.SourceUnitId, out EntityRuntimeState source) ||
            !world.TryGet(command.TargetUnitId, out EntityRuntimeState target))
        {
            rejectionReason = "Entidad de origen o destino inexistente.";
            return false;
        }

        if (source.Life == null || !source.Life.CanAct)
        {
            rejectionReason = "La entidad de origen no puede actuar en su estado actual.";
            return false;
        }

        if (source.UnitId == target.UnitId)
        {
            rejectionReason = "Una entidad no puede seguirse a sí misma.";
            return false;
        }

        if (source.OwnerParticipantId != issuerParticipantId)
        {
            rejectionReason = $"El participante {issuerParticipantId} intentó ordenar una entidad ajena ({source.UnitId}).";
            return false;
        }

        if (source.Attributes == null || !source.Attributes.Has(EntityAttributeIds.Controllable))
        {
            rejectionReason = $"La entidad {source.UnitId} no es controlable.";
            return false;
        }

        if (!EntityInteractionRules.CanFollow(source, target, issuerParticipantId))
        {
            rejectionReason = "La relación o los atributos de las entidades no permiten seguimiento.";
            return false;
        }

        source.InteractionTargetUnitId = target.UnitId;
        source.Attack?.ClearTargetPreservingRecovery();
        source.Destination = target.Position;
        if (source.Worker != null)
        {
            source.Worker.TargetResourceUnitId = -1;
            source.Worker.ExtractionTimer = 0f;
            source.Worker.IsExtracting = false;
        }
        return true;
    }

    public static void Update(EntityWorld world)
    {
        if (world == null)
            return;

        foreach (EntityRuntimeState source in world.Values)
        {
            if (source.Life == null || !source.Life.CanAct)
            {
                Clear(source);
                source.Destination = source.Position;
                continue;
            }

            if (source.InteractionTargetUnitId < 0)
                continue;

            if (!world.TryGet(source.InteractionTargetUnitId, out EntityRuntimeState target) ||
                EntityInteractionRules.BlocksContextualInteraction(target.Attributes))
            {
                Clear(source);
                source.Destination = source.Position;
                continue;
            }

            float sourceRadius = Mathf.Max(source.BoundsSize.x, source.BoundsSize.z) * 0.5f;
            float targetRadius = Mathf.Max(target.BoundsSize.x, target.BoundsSize.z) * 0.5f;
            float stopDistance = sourceRadius + targetRadius + Mathf.Max(0.1f, source.InteractionRange);
            Vector2 difference = new(source.Position.x - target.Position.x, source.Position.z - target.Position.z);

            if (difference.sqrMagnitude <= stopDistance * stopDistance)
            {
                source.Destination = source.Position;
                continue;
            }

            // Se actualiza en cada frame para seguir correctamente objetivos móviles.
            source.Destination = new Vector3(target.Position.x, source.Position.y, target.Position.z);
        }
    }

    public static void Clear(EntityRuntimeState source)
    {
        if (source == null)
            return;
        source.InteractionTargetUnitId = -1;
    }
}
