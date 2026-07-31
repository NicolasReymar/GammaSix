using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Vista de solo lectura que un controlador Headless utiliza para tomar
/// decisiones. No expone métodos que modifiquen EntityWorld.
/// </summary>
public sealed class HeadlessPerceptionContext
{
    private readonly EntityWorld world;
    private readonly MatchParticipantRuntimeState participant;
    private readonly ScenarioHeadlessControllerSettings settings;
    private readonly DiplomacyRuntimeService diplomacy;
    private readonly List<EntityRuntimeState> controlledEntities;
    private readonly List<EntityRuntimeState> potentialTargets;

    public IReadOnlyList<EntityRuntimeState> ControlledEntities => controlledEntities;
    public IReadOnlyList<EntityRuntimeState> PotentialTargets => potentialTargets;

    public HeadlessPerceptionContext(
        EntityWorld world,
        MatchParticipantRuntimeState participant,
        ScenarioHeadlessControllerSettings settings,
        DiplomacyRuntimeService diplomacy)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.participant = participant ?? throw new ArgumentNullException(nameof(participant));
        this.settings = settings ?? new ScenarioHeadlessControllerSettings();
        this.diplomacy = diplomacy ?? throw new ArgumentNullException(nameof(diplomacy));

        controlledEntities = world.Values
            .Where(IsControllableCombatEntity)
            .OrderBy(item => item.UnitId)
            .ToList();

        potentialTargets = world.Values
            .Where(IsPotentialTarget)
            .OrderBy(item => item.UnitId)
            .ToList();
    }

    public bool IsValidHostileTarget(EntityRuntimeState source, EntityRuntimeState target)
    {
        if (source == null || target == null)
            return false;
        if (!potentialTargets.Contains(target))
            return false;
        DiplomacyStance stance = diplomacy.GetStance(source.TeamId, target.TeamId);
        return stance == DiplomacyStance.Enemy ||
               (settings.includeNeutralTargets && stance == DiplomacyStance.Neutral);
    }

    public EntityRuntimeState FindNearestHostile(EntityRuntimeState source)
    {
        if (source == null)
            return null;

        EntityRuntimeState best = null;
        float bestDistance = float.MaxValue;
        foreach (EntityRuntimeState target in potentialTargets)
        {
            if (!IsValidHostileTarget(source, target))
                continue;

            Vector3 offset = target.Position - source.Position;
            offset.y = 0f;
            float distance = offset.sqrMagnitude;
            if (distance < bestDistance ||
                (Mathf.Approximately(distance, bestDistance) &&
                 (best == null || target.UnitId < best.UnitId)))
            {
                best = target;
                bestDistance = distance;
            }
        }

        return best;
    }

    public bool TryGetEntity(int entityId, out EntityRuntimeState entity)
    {
        return world.TryGet(entityId, out entity);
    }

    private bool IsControllableCombatEntity(EntityRuntimeState entity)
    {
        if (entity == null || entity.OwnerParticipantId != participant.ParticipantId)
            return false;
        if (entity.Life == null || !entity.Life.CanAct || entity.Health <= 0)
            return false;
        if (entity.Attack == null || entity.Attack.BaseDamage <= 0)
            return false;
        if (entity.Attributes == null ||
            !entity.Attributes.Has(EntityAttributeIds.Controllable) ||
            entity.Attributes.Has(EntityAttributeIds.EntityArea))
        {
            return false;
        }

        return MatchesAttributeFilters(
            entity.Attributes,
            settings.controlledRequiredAttributes,
            settings.controlledExcludedAttributes);
    }

    private bool IsPotentialTarget(EntityRuntimeState entity)
    {
        if (entity == null || entity.OwnerParticipantId == participant.ParticipantId)
            return false;
        if (entity.Life == null || !entity.Life.CanReceiveDamage || entity.Health <= 0)
            return false;
        if (entity.Attributes != null && entity.Attributes.Has(EntityAttributeIds.EntityArea))
            return false;
        if (EntityInteractionRules.BlocksContextualInteraction(entity.Attributes))
            return false;

        DiplomacyStance stance = diplomacy.GetStance(participant.TeamId, entity.TeamId);
        bool hostileTeam = stance == DiplomacyStance.Enemy;
        bool allowedNeutral = settings.includeNeutralTargets && stance == DiplomacyStance.Neutral;
        if (!hostileTeam && !allowedNeutral)
            return false;

        return MatchesAttributeFilters(
            entity.Attributes,
            settings.targetRequiredAttributes,
            settings.targetExcludedAttributes);
    }

    private static bool MatchesAttributeFilters(
        EntityAttributeSet attributes,
        IEnumerable<string> required,
        IEnumerable<string> excluded)
    {
        if (required != null)
        {
            foreach (string attribute in required)
            {
                if (string.IsNullOrWhiteSpace(attribute))
                    continue;
                if (attributes == null || !attributes.Has(attribute.Trim()))
                    return false;
            }
        }

        if (excluded != null && attributes != null)
        {
            foreach (string attribute in excluded)
            {
                if (!string.IsNullOrWhiteSpace(attribute) && attributes.Has(attribute.Trim()))
                    return false;
            }
        }

        return true;
    }
}
