using System;

/// <summary>
/// Relación lógica entre dos entidades. No depende de si son trabajador,
/// soldado, edificio u otra definición concreta.
/// </summary>
public enum EntityRelation
{
    Self,
    Owned,
    Allied,
    Neutral,
    Enemy
}

public enum ContextualEntityAction
{
    None,
    Follow,
    ExtractResource
}

/// <summary>
/// Centraliza las reglas de interacción contextual para evitar condiciones
/// duplicadas en input, networking y servicios especializados.
/// </summary>
public static class EntityInteractionRules
{
    /// <summary>
    /// Un objetivo con interaction.not_selectable activo se comporta como parte
    /// del terreno para las órdenes: no puede recibir seguimiento, extracción ni
    /// otras interacciones contextuales. El override de partida sigue siendo
    /// respetado porque la evaluación se realiza sobre el atributo efectivo.
    /// </summary>
    public static bool BlocksContextualInteraction(EntityAttributeSet targetAttributes)
    {
        return EntityAttributeOverrideService.IsEffectivelyBlocked(
            targetAttributes,
            EntityAttributeIds.NotSelectable);
    }

    public static EntityRelation GetRelation(
        ulong localClientId,
        int sourceUnitId,
        ulong sourceOwnerClientId,
        int sourceTeamId,
        int targetUnitId,
        ulong targetOwnerClientId,
        int targetTeamId)
    {
        if (sourceUnitId == targetUnitId)
            return EntityRelation.Self;

        if (targetTeamId == 0)
            return EntityRelation.Neutral;

        if (targetOwnerClientId == localClientId)
            return EntityRelation.Owned;

        if (sourceTeamId != 0 && sourceTeamId == targetTeamId)
            return EntityRelation.Allied;

        return EntityRelation.Enemy;
    }

    public static ContextualEntityAction Resolve(
        NetworkEntityView source,
        NetworkEntityView target,
        ulong localClientId)
    {
        if (source == null || target == null)
            return ContextualEntityAction.None;

        if (source.OwnerClientId != localClientId ||
            !source.HasAttribute(EntityAttributeIds.Controllable) ||
            BlocksContextualInteraction(target.Attributes))
            return ContextualEntityAction.None;

        EntityRelation relation = GetRelation(
            localClientId,
            source.UnitId,
            source.OwnerClientId,
            source.TeamId,
            target.UnitId,
            target.OwnerClientId,
            target.TeamId);

        if (relation == EntityRelation.Self || relation == EntityRelation.Enemy)
            return ContextualEntityAction.None;

        if (target.HasAttribute(EntityAttributeIds.Resource))
        {
            return source.HasAttribute(EntityAttributeIds.Worker)
                ? ContextualEntityAction.ExtractResource
                : ContextualEntityAction.None;
        }

        bool targetCanBeFollowed = target.HasAttribute(EntityAttributeIds.Unit) ||
                                   target.HasAttribute(EntityAttributeIds.Building);

        return targetCanBeFollowed
            ? ContextualEntityAction.Follow
            : ContextualEntityAction.None;
    }

    public static bool CanFollow(EntityRuntimeState source, EntityRuntimeState target, ulong senderClientId)
    {
        if (source == null || target == null || source.UnitId == target.UnitId)
            return false;

        if (source.OwnerClientId != senderClientId ||
            source.Attributes == null ||
            !source.Attributes.Has(EntityAttributeIds.Controllable) ||
            BlocksContextualInteraction(target.Attributes))
            return false;

        bool targetCanBeFollowed = target.Attributes != null &&
            (target.Attributes.Has(EntityAttributeIds.Unit) ||
             target.Attributes.Has(EntityAttributeIds.Building));

        if (!targetCanBeFollowed)
            return false;

        EntityRelation relation = GetRelation(
            senderClientId,
            source.UnitId,
            source.OwnerClientId,
            source.TeamId,
            target.UnitId,
            target.OwnerClientId,
            target.TeamId);

        return relation == EntityRelation.Owned ||
               relation == EntityRelation.Allied ||
               relation == EntityRelation.Neutral;
    }
}
