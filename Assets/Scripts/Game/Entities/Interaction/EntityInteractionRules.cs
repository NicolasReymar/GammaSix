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
    ExtractResource,
    Attack
}

/// <summary>
/// Centraliza las reglas de interacción contextual para evitar condiciones
/// duplicadas en input, networking y servicios especializados.
/// </summary>
public static class EntityInteractionRules
{
    public static bool BlocksContextualInteraction(EntityAttributeSet targetAttributes)
    {
        return EntityAttributeOverrideService.IsEffectivelyBlocked(
            targetAttributes,
            EntityAttributeIds.NotSelectable);
    }

    public static EntityRelation GetRelation(
        int localParticipantId,
        int sourceUnitId,
        int sourceOwnerParticipantId,
        int sourceTeamId,
        int targetUnitId,
        int targetOwnerParticipantId,
        int targetTeamId)
    {
        if (sourceUnitId == targetUnitId)
            return EntityRelation.Self;

        if (targetTeamId == 0)
            return EntityRelation.Neutral;

        if (targetOwnerParticipantId == localParticipantId)
            return EntityRelation.Owned;

        if (sourceTeamId != 0 && sourceTeamId == targetTeamId)
            return EntityRelation.Allied;

        return EntityRelation.Enemy;
    }

    public static ContextualEntityAction Resolve(
        NetworkEntityView source,
        NetworkEntityView target,
        int localParticipantId)
    {
        if (source == null || target == null)
            return ContextualEntityAction.None;

        if (source.OwnerParticipantId != localParticipantId ||
            source.LifeState != EntityLifeState.Alive ||
            target.LifeState == EntityLifeState.Dead ||
            !source.HasAttribute(EntityAttributeIds.Controllable) ||
            BlocksContextualInteraction(target.Attributes))
        {
            return ContextualEntityAction.None;
        }

        EntityRelation relation = GetRelation(
            localParticipantId,
            source.UnitId,
            source.OwnerParticipantId,
            source.TeamId,
            target.UnitId,
            target.OwnerParticipantId,
            target.TeamId);

        if (relation == EntityRelation.Self)
            return ContextualEntityAction.None;

        if (relation == EntityRelation.Enemy)
            return source.HasAttack ? ContextualEntityAction.Attack : ContextualEntityAction.None;

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

    public static bool CanFollow(
        EntityRuntimeState source,
        EntityRuntimeState target,
        int issuerParticipantId)
    {
        if (source == null || target == null || source.UnitId == target.UnitId)
            return false;

        if (source.Life == null || !source.Life.CanAct ||
            target.Life == null || target.Life.State == EntityLifeState.Dead)
            return false;

        if (source.OwnerParticipantId != issuerParticipantId ||
            source.Attributes == null ||
            !source.Attributes.Has(EntityAttributeIds.Controllable) ||
            BlocksContextualInteraction(target.Attributes))
        {
            return false;
        }

        bool targetCanBeFollowed = target.Attributes != null &&
            (target.Attributes.Has(EntityAttributeIds.Unit) ||
             target.Attributes.Has(EntityAttributeIds.Building));

        if (!targetCanBeFollowed)
            return false;

        EntityRelation relation = GetRelation(
            issuerParticipantId,
            source.UnitId,
            source.OwnerParticipantId,
            source.TeamId,
            target.UnitId,
            target.OwnerParticipantId,
            target.TeamId);

        return relation == EntityRelation.Owned ||
               relation == EntityRelation.Allied ||
               relation == EntityRelation.Neutral;
    }
}
