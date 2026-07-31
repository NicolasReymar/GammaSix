using UnityEngine;

/// <summary>
/// Procesa interacciones contextuales no especializadas. El seguimiento usa
/// caminos recalculables para acompañar objetivos móviles y rodear obstáculos.
/// </summary>
public static class EntityInteractionService
{
    public static bool TryAssignFollow(
        EntityWorld world,
        DiplomacyRuntimeService diplomacy,
        NavigationRuntimeSystem navigation,
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
        if (!EntityInteractionRules.CanFollow(source, target, issuerParticipantId, diplomacy))
        {
            rejectionReason = "La relación o los atributos de las entidades no permiten seguimiento.";
            return false;
        }

        navigation?.ClearOrders(source, "follow");
        source.InteractionTargetUnitId = target.UnitId;
        source.Attack?.ClearTargetPreservingRecovery();
        if (source.Worker != null)
        {
            source.Worker.TargetResourceUnitId = -1;
            source.Worker.ExtractionTimer = 0f;
            source.Worker.IsExtracting = false;
        }
        return true;
    }

    public static void Update(
        EntityWorld world,
        DiplomacyRuntimeService diplomacy,
        NavigationRuntimeSystem navigation,
        float elapsedTime)
    {
        if (world == null)
            return;

        foreach (EntityRuntimeState source in world.Values)
        {
            if (source.Life == null || !source.Life.CanAct)
            {
                Clear(source);
                navigation?.HoldPosition(source, false, "cannot-follow");
                continue;
            }

            if (source.InteractionTargetUnitId < 0)
                continue;

            if (!world.TryGet(source.InteractionTargetUnitId, out EntityRuntimeState target) ||
                EntityInteractionRules.BlocksContextualInteraction(target.Attributes) ||
                (source.OwnerParticipantId != target.OwnerParticipantId &&
                 diplomacy?.GetStance(source.TeamId, target.TeamId) == DiplomacyStance.Enemy))
            {
                Clear(source);
                navigation?.HoldPosition(source, false, "follow-invalid");
                continue;
            }

            float sourceRadius = Mathf.Max(source.BoundsSize.x, source.BoundsSize.z) * 0.5f;
            float targetRadius = Mathf.Max(target.BoundsSize.x, target.BoundsSize.z) * 0.5f;
            float stopDistance = sourceRadius + targetRadius + Mathf.Max(0.1f, source.InteractionRange);
            Vector2 difference = new(source.Position.x - target.Position.x, source.Position.z - target.Position.z);

            if (difference.sqrMagnitude <= stopDistance * stopDistance)
            {
                navigation?.HoldPosition(source, false, "follow-range");
                continue;
            }

            navigation?.SetFollowDestination(source, target.Position, elapsedTime);
        }
    }

    public static void Clear(EntityRuntimeState source)
    {
        if (source != null)
            source.InteractionTargetUnitId = -1;
    }
}
