using System;

public static class EntityCombatRules
{
    public static bool CanAttack(
        EntityRuntimeState source,
        EntityRuntimeState target,
        int issuerParticipantId,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (source == null || target == null)
        {
            rejectionReason = "Entidad atacante u objetivo inexistente.";
            return false;
        }

        if (source.UnitId == target.UnitId)
        {
            rejectionReason = "Una entidad no puede atacarse a sí misma.";
            return false;
        }

        if (source.OwnerParticipantId != issuerParticipantId)
        {
            rejectionReason = $"El participante {issuerParticipantId} intentó ordenar una entidad ajena ({source.UnitId}).";
            return false;
        }

        if (source.Life == null || !source.Life.CanAct || source.Health <= 0)
        {
            rejectionReason = "La entidad atacante no está en un estado que permita combatir.";
            return false;
        }

        if (target.Life == null || !target.Life.CanReceiveDamage || target.Health <= 0)
        {
            rejectionReason = "El objetivo no puede recibir daño.";
            return false;
        }

        if (source.Attributes == null || !source.Attributes.Has(EntityAttributeIds.Controllable))
        {
            rejectionReason = "La entidad atacante no es controlable.";
            return false;
        }

        if (source.Attack == null || source.Attack.BaseDamage <= 0)
        {
            rejectionReason = "La entidad no posee un ataque configurado.";
            return false;
        }

        if (EntityInteractionRules.BlocksContextualInteraction(target.Attributes))
        {
            rejectionReason = "El objetivo bloquea la interacción contextual.";
            return false;
        }

        if (target.Attributes != null && target.Attributes.Has(EntityAttributeIds.EntityArea))
        {
            rejectionReason = "Las entidades de área no son objetivos de combate.";
            return false;
        }

        if (source.TeamId > 0 && target.TeamId > 0 && source.TeamId == target.TeamId)
        {
            rejectionReason = "No se puede atacar a una entidad aliada.";
            return false;
        }

        string delivery = NormalizeDelivery(source.Attack.Delivery);
        if (delivery == EntityAttackDeliveryTypes.Melee &&
            (source.Attributes == null || !source.Attributes.Has(EntityAttributeIds.Melee)))
        {
            rejectionReason = "El ataque melee requiere el atributo 'melee'.";
            return false;
        }

        if (delivery != EntityAttackDeliveryTypes.Melee)
        {
            rejectionReason = $"El tipo de entrega '{source.Attack.Delivery}' todavía no está implementado.";
            return false;
        }

        return true;
    }

    public static bool IsStillValidTarget(EntityRuntimeState source, EntityRuntimeState target)
    {
        if (source == null || target == null || source.UnitId == target.UnitId)
            return false;
        if (source.Life == null || !source.Life.CanAct || source.Health <= 0 ||
            source.Attack == null || source.Attack.BaseDamage <= 0)
            return false;
        if (target.Life == null || !target.Life.CanReceiveDamage || target.Health <= 0)
            return false;
        if (EntityInteractionRules.BlocksContextualInteraction(target.Attributes) ||
            (target.Attributes != null && target.Attributes.Has(EntityAttributeIds.EntityArea)))
            return false;
        if (NormalizeDelivery(source.Attack.Delivery) == EntityAttackDeliveryTypes.Melee &&
            (source.Attributes == null || !source.Attributes.Has(EntityAttributeIds.Melee)))
            return false;
        return source.TeamId <= 0 || target.TeamId <= 0 || source.TeamId != target.TeamId;
    }

    public static float GetInteractionDistance(EntityRuntimeState source, EntityRuntimeState target)
    {
        float sourceRadius = Math.Max(source.BoundsSize.x, source.BoundsSize.z) * 0.5f;
        float targetRadius = Math.Max(target.BoundsSize.x, target.BoundsSize.z) * 0.5f;
        return sourceRadius + targetRadius + Math.Max(0.05f, source.Attack?.AttackRange ?? 0.05f);
    }

    public static string NormalizeDelivery(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? EntityAttackDeliveryTypes.Melee
            : value.Trim().ToLowerInvariant();
    }
}
