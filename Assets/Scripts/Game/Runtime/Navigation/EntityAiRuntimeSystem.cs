using System;
using UnityEngine;

/// <summary>
/// IA individual reutilizable. No decide economía ni estrategia; únicamente
/// adquiere amenazas cercanas para unidades agresivas y reanuda rutas persistentes
/// después de un combate.
/// </summary>
public sealed class EntityAiRuntimeSystem
{
    private readonly EntityWorld world;
    private readonly DiplomacyRuntimeService diplomacy;
    private readonly CombatRuntimeSystem combat;
    private readonly NavigationRuntimeSystem navigation;
    private readonly float acquisitionRange;
    private readonly float updateInterval;
    private float nextUpdate;

    public EntityAiRuntimeSystem(
        EntityWorld world,
        DiplomacyRuntimeService diplomacy,
        CombatRuntimeSystem combat,
        NavigationRuntimeSystem navigation,
        ScenarioNavigationDefinition settings)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.diplomacy = diplomacy ?? throw new ArgumentNullException(nameof(diplomacy));
        this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        acquisitionRange = Mathf.Max(0.5f, settings?.attackMoveAcquisitionRange ?? 6.5f);
        updateInterval = Mathf.Max(0.05f, settings?.individualAiInterval ?? 0.2f);
    }

    public void Update(float elapsedTime)
    {
        if (elapsedTime < nextUpdate)
            return;
        nextUpdate = elapsedTime + updateInterval;

        foreach (EntityRuntimeState entity in world.SnapshotValues())
        {
            if (!CanAcquire(entity))
                continue;

            if (entity.Attack.TargetEntityId > 0)
                continue;

            EntityRuntimeState target = FindNearestHostile(entity);
            if (target != null)
            {
                combat.TryAssignAttack(
                    entity.OwnerParticipantId,
                    new EntityAttackCommand
                    {
                        SourceUnitId = entity.UnitId,
                        TargetUnitId = target.UnitId,
                        ForceTarget = false,
                        PreserveNavigationOrder = entity.Navigation?.HasBaseOrder == true
                    },
                    out _);
                continue;
            }

            if (entity.Navigation?.HasBaseOrder == true &&
                entity.Navigation.PathPurpose != EntityPathPurpose.BaseOrder)
            {
                navigation.ResumeBaseOrder(entity, elapsedTime);
            }
        }
    }

    private bool CanAcquire(EntityRuntimeState entity)
    {
        if (entity == null || entity.Attack == null || entity.Life == null || !entity.Life.CanAct)
            return false;
        if (entity.Attack.Stance != EntityCombatStance.Aggressive)
            return false;
        if (entity.TeamId <= 0)
            return false;
        if (entity.Attributes != null && entity.Attributes.Has(EntityAttributeIds.EntityArea))
            return false;

        // Una unidad con movimiento directo normal no abandona su orden por un
        // objetivo cercano. AttackMove y Patrulla sí incluyen adquisición.
        EntityNavigationOrderType order = entity.Navigation?.OrderType ?? EntityNavigationOrderType.None;
        if (order == EntityNavigationOrderType.Move)
            return false;

        return true;
    }

    private EntityRuntimeState FindNearestHostile(EntityRuntimeState source)
    {
        float bestDistance = acquisitionRange * acquisitionRange;
        EntityRuntimeState best = null;
        foreach (EntityRuntimeState candidate in world.Values)
        {
            if (candidate == null || candidate.UnitId == source.UnitId ||
                candidate.Life == null || !candidate.Life.CanReceiveDamage ||
                candidate.TeamId <= 0 ||
                diplomacy.GetStance(source.TeamId, candidate.TeamId) != DiplomacyStance.Enemy ||
                EntityInteractionRules.BlocksContextualInteraction(candidate.Attributes) ||
                (candidate.Attributes != null && candidate.Attributes.Has(EntityAttributeIds.EntityArea)))
            {
                continue;
            }

            Vector2 delta = new(
                source.Position.x - candidate.Position.x,
                source.Position.z - candidate.Position.z);
            float distance = delta.sqrMagnitude;
            if (distance > bestDistance)
                continue;

            if (best == null || distance < bestDistance ||
                (Mathf.Approximately(distance, bestDistance) && candidate.UnitId < best.UnitId))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }
}
