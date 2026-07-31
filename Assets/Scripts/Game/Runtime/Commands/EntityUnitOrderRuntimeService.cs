using System;

/// <summary>
/// Órdenes generales que no dependen de un tipo concreto de unidad. Se ejecutan
/// en la autoridad y comparten las mismas validaciones de propiedad que el resto
/// del Command Bus.
/// </summary>
public static class EntityUnitOrderRuntimeService
{
    public static bool TryStop(
        EntityWorld world,
        int issuerParticipantId,
        EntityStopCommand command,
        NavigationRuntimeSystem navigation,
        out string rejectionReason)
    {
        if (!TryResolveControllable(
                world,
                issuerParticipantId,
                command?.UnitId ?? -1,
                out EntityRuntimeState entity,
                out rejectionReason))
        {
            return false;
        }

        navigation?.ClearOrders(entity);
        entity.Destination = entity.Position;
        entity.InteractionTargetUnitId = -1;
        entity.Attack?.ClearTargetPreservingRecovery();
        ClearWorkerActivity(entity);
        return true;
    }

    public static bool TrySetCombatStance(
        EntityWorld world,
        int issuerParticipantId,
        EntityStanceCommand command,
        NavigationRuntimeSystem navigation,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (command == null ||
            !Enum.TryParse(command.Stance, true, out EntityCombatStance stance))
        {
            rejectionReason = "La postura de combate es inválida.";
            return false;
        }

        if (!TryResolveControllable(
                world,
                issuerParticipantId,
                command.UnitId,
                out EntityRuntimeState entity,
                out rejectionReason))
        {
            return false;
        }

        if (entity.Attack == null)
        {
            rejectionReason = "La entidad no posee un ataque ni una postura de combate.";
            return false;
        }

        entity.Attack.Stance = stance;
        if (stance == EntityCombatStance.Passive)
        {
            // Pasivo cancela el objetivo actual, pero no permite saltarse una
            // recuperación ya iniciada. El temporizador sigue perteneciendo a la entidad.
            entity.Attack.ClearTargetPreservingRecovery();
            entity.InteractionTargetUnitId = -1;
            navigation?.ClearOrders(entity, "passive");
            entity.Destination = entity.Position;
            ClearWorkerActivity(entity);
        }

        return true;
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
            rejectionReason = "La entidad indicada no existe.";
            return false;
        }

        if (entity.OwnerParticipantId != issuerParticipantId)
        {
            rejectionReason = $"El participante {issuerParticipantId} intentó ordenar una entidad ajena ({unitId}).";
            return false;
        }

        if (entity.Life == null || !entity.Life.CanAct)
        {
            rejectionReason = "La entidad no puede ejecutar órdenes en su estado actual.";
            return false;
        }

        if (entity.Attributes == null || !entity.Attributes.Has(EntityAttributeIds.Controllable))
        {
            rejectionReason = "La entidad no es controlable.";
            return false;
        }

        return true;
    }

    private static void ClearWorkerActivity(EntityRuntimeState entity)
    {
        if (entity?.Worker == null)
            return;

        entity.Worker.TargetResourceUnitId = -1;
        entity.Worker.ExtractionTimer = 0f;
        entity.Worker.IsExtracting = false;
    }
}
