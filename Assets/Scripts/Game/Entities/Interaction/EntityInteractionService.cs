using System.Collections.Generic;
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
        IDictionary<int, EntityRuntimeState> entities,
        ulong senderClientId,
        EntityInteractionCommand command,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (command == null ||
            !entities.TryGetValue(command.SourceUnitId, out EntityRuntimeState source) ||
            !entities.TryGetValue(command.TargetUnitId, out EntityRuntimeState target))
        {
            rejectionReason = "Entidad de origen o destino inexistente.";
            return false;
        }

        if (source.UnitId == target.UnitId)
        {
            rejectionReason = "Una entidad no puede seguirse a sí misma.";
            return false;
        }

        if (source.OwnerClientId != senderClientId)
        {
            rejectionReason = $"Cliente {senderClientId} intentó ordenar una entidad ajena ({source.UnitId}).";
            return false;
        }

        if (source.Attributes == null || !source.Attributes.Has(EntityAttributeIds.Controllable))
        {
            rejectionReason = $"La entidad {source.UnitId} no es controlable.";
            return false;
        }

        if (!EntityInteractionRules.CanFollow(source, target, senderClientId))
        {
            rejectionReason = "La relación o los atributos de las entidades no permiten seguimiento.";
            return false;
        }

        source.InteractionTargetUnitId = target.UnitId;
        source.Destination = target.Position;
        if (source.Worker != null)
        {
            source.Worker.TargetResourceUnitId = -1;
            source.Worker.ExtractionTimer = 0f;
            source.Worker.IsExtracting = false;
        }
        return true;
    }

    public static void Update(IDictionary<int, EntityRuntimeState> entities)
    {
        foreach (EntityRuntimeState source in entities.Values)
        {
            if (source.InteractionTargetUnitId < 0)
                continue;

            if (!entities.TryGetValue(source.InteractionTargetUnitId, out EntityRuntimeState target) ||
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
