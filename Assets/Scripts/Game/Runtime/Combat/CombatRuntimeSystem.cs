using System;
using UnityEngine;

/// <summary>
/// Ejecuta ataques declarados por las entidades. La persecución utiliza el
/// sistema de navegación y puede suspender temporalmente una patrulla o
/// attack-move para reanudarla al terminar el objetivo.
/// </summary>
public sealed class CombatRuntimeSystem
{
    private readonly EntityWorld world;
    private readonly DamageRuntimeService damage;
    private readonly DiplomacyRuntimeService diplomacy;
    private readonly NavigationRuntimeSystem navigation;
    private float currentElapsedTime;

    public CombatRuntimeSystem(
        EntityWorld world,
        DamageRuntimeService damage,
        DiplomacyRuntimeService diplomacy,
        NavigationRuntimeSystem navigation)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.damage = damage ?? throw new ArgumentNullException(nameof(damage));
        this.diplomacy = diplomacy ?? throw new ArgumentNullException(nameof(diplomacy));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    public bool TryAssignAttack(
        int issuerParticipantId,
        EntityAttackCommand command,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (command == null ||
            !world.TryGet(command.SourceUnitId, out EntityRuntimeState source) ||
            !world.TryGet(command.TargetUnitId, out EntityRuntimeState target))
        {
            rejectionReason = "Entidad atacante u objetivo inexistente.";
            return false;
        }

        if (!EntityCombatRules.CanAttack(
                source,
                target,
                issuerParticipantId,
                command.ForceTarget,
                diplomacy,
                out rejectionReason))
        {
            return false;
        }

        if (!command.PreserveNavigationOrder)
            navigation.ClearOrders(source, "direct-attack");

        source.Attack.AssignTarget(
            target.UnitId,
            command.ForceTarget,
            command.PreserveNavigationOrder);
        source.InteractionTargetUnitId = -1;
        ClearWorkerActivity(source);
        return true;
    }

    public void Update(float deltaTime, float elapsedTime)
    {
        currentElapsedTime = elapsedTime;
        float safeDelta = Mathf.Max(0f, deltaTime);
        foreach (EntityRuntimeState attacker in world.SnapshotValues())
        {
            EntityAttackRuntimeState attack = attacker.Attack;
            if (attack == null)
                continue;

            if (attack.Phase == EntityAttackPhase.Recovery)
            {
                UpdateRecovery(attacker, attack, safeDelta);
                continue;
            }

            if (attack.TargetEntityId <= 0)
                continue;

            if (!world.TryGet(attack.TargetEntityId, out EntityRuntimeState target) ||
                !EntityCombatRules.IsStillValidTarget(attacker, target, diplomacy, attack.ForceTarget))
            {
                ClearAttack(attacker);
                continue;
            }

            float interactionDistance = EntityCombatRules.GetInteractionDistance(attacker, target);
            Vector2 delta = new(attacker.Position.x - target.Position.x, attacker.Position.z - target.Position.z);
            bool inRange = delta.sqrMagnitude <= interactionDistance * interactionDistance;

            switch (attack.Phase)
            {
                case EntityAttackPhase.Windup:
                    UpdateWindup(attacker, target, attack, inRange, safeDelta, elapsedTime);
                    break;

                default:
                    if (!inRange)
                    {
                        attack.Phase = EntityAttackPhase.Approaching;
                        if (attack.ChaseTarget && attacker.MoveSpeed > 0f)
                            navigation.SetChaseDestination(attacker, target.Position, elapsedTime);
                    }
                    else
                    {
                        navigation.HoldPosition(
                            attacker,
                            attack.ResumeNavigationOrderAfterTarget,
                            "attack-range");
                        BeginWindup(attacker, target, attack, elapsedTime);
                    }
                    break;
            }
        }
    }

    private void UpdateRecovery(EntityRuntimeState attacker, EntityAttackRuntimeState attack, float deltaTime)
    {
        attack.PhaseRemaining -= deltaTime;

        if (attack.TargetEntityId > 0)
        {
            if (!world.TryGet(attack.TargetEntityId, out EntityRuntimeState target) ||
                !EntityCombatRules.IsStillValidTarget(attacker, target, diplomacy, attack.ForceTarget))
            {
                ClearAttackTargetPreservingRecovery(attacker);
            }
            else
            {
                float interactionDistance = EntityCombatRules.GetInteractionDistance(attacker, target);
                Vector2 delta = new(
                    attacker.Position.x - target.Position.x,
                    attacker.Position.z - target.Position.z);
                bool inRange = delta.sqrMagnitude <= interactionDistance * interactionDistance;

                if (!inRange && attack.ChaseTarget && attacker.MoveSpeed > 0f)
                {
                    navigation.SetChaseDestination(attacker, target.Position, currentElapsedTime);
                }
                else if (inRange)
                {
                    navigation.HoldPosition(
                        attacker,
                        attack.ResumeNavigationOrderAfterTarget,
                        "attack-recovery-range");
                }
            }
        }

        if (attack.PhaseRemaining <= 0f)
        {
            attack.Phase = EntityAttackPhase.None;
            attack.PhaseRemaining = 0f;
        }
    }

    public void HandleDiplomacyChanged(int sourceTeamId, int targetTeamId, DiplomacyStance newStance)
    {
        if (newStance == DiplomacyStance.Enemy)
            return;

        foreach (EntityRuntimeState attacker in world.Values)
        {
            if (attacker?.TeamId != sourceTeamId ||
                attacker.Attack == null ||
                attacker.Attack.TargetEntityId <= 0 ||
                attacker.Attack.ForceTarget)
            {
                continue;
            }

            if (!world.TryGet(attacker.Attack.TargetEntityId, out EntityRuntimeState target) ||
                target.TeamId != targetTeamId)
            {
                continue;
            }

            if (attacker.Attack.Phase == EntityAttackPhase.Recovery)
                ClearAttackTargetPreservingRecovery(attacker);
            else
                ClearAttack(attacker);
        }
    }

    public void ClearReferencesToEntity(int entityId)
    {
        foreach (EntityRuntimeState entity in world.Values)
        {
            if (entity.Attack?.TargetEntityId == entityId)
                ClearAttackTargetPreservingRecovery(entity);
        }
    }

    private void UpdateWindup(
        EntityRuntimeState attacker,
        EntityRuntimeState target,
        EntityAttackRuntimeState attack,
        bool inRange,
        float deltaTime,
        float elapsedTime)
    {
        navigation.HoldPosition(attacker, attack.ResumeNavigationOrderAfterTarget, "attack-windup");
        if (!inRange && EntityCombatRules.NormalizeDelivery(attack.Delivery) == EntityAttackDeliveryTypes.Melee)
        {
            attack.Phase = EntityAttackPhase.Approaching;
            attack.PhaseRemaining = 0f;
            if (attack.ChaseTarget && attacker.MoveSpeed > 0f)
                navigation.SetChaseDestination(attacker, target.Position, elapsedTime);
            return;
        }

        attack.PhaseRemaining -= deltaTime;
        if (attack.PhaseRemaining > 0f)
            return;

        ResolveImpact(attacker, target, attack, elapsedTime);
        attack.Phase = EntityAttackPhase.Recovery;
        attack.PhaseRemaining = attack.EffectiveRecoveryTime;
    }

    private void BeginWindup(
        EntityRuntimeState attacker,
        EntityRuntimeState target,
        EntityAttackRuntimeState attack,
        float elapsedTime)
    {
        attack.Phase = EntityAttackPhase.Windup;
        attack.PhaseRemaining = attack.EffectiveAttackTime;
        if (attacker.Status != null)
            attacker.Status.CombatUntil = Mathf.Max(attacker.Status.CombatUntil, elapsedTime + 2.5f);

        if (attack.PhaseRemaining <= 0f)
        {
            ResolveImpact(attacker, target, attack, elapsedTime);
            attack.Phase = EntityAttackPhase.Recovery;
            attack.PhaseRemaining = attack.EffectiveRecoveryTime;
        }
    }

    private void ResolveImpact(
        EntityRuntimeState attacker,
        EntityRuntimeState target,
        EntityAttackRuntimeState attack,
        float elapsedTime)
    {
        string delivery = EntityCombatRules.NormalizeDelivery(attack.Delivery);
        if (delivery != EntityAttackDeliveryTypes.Melee)
        {
            Debug.LogWarning($"[CombatRuntimeSystem] Delivery '{attack.Delivery}' no implementado para {attacker.EntityDefinitionId}.");
            ClearAttack(attacker);
            return;
        }

        damage.ApplyDamage(
            attacker.UnitId,
            target.UnitId,
            attack.BaseDamage,
            attack.DamageType,
            $"basic-attack:{delivery}",
            elapsedTime,
            out _);
    }

    private void ClearAttackTargetPreservingRecovery(EntityRuntimeState attacker)
    {
        if (attacker == null)
            return;

        bool resume = attacker.Attack?.ResumeNavigationOrderAfterTarget == true;
        attacker.Attack?.ClearTargetPreservingRecovery();
        if (resume && attacker.Navigation?.HasBaseOrder == true)
            navigation.ResumeBaseOrder(attacker, currentElapsedTime);
        else
            navigation.HoldPosition(attacker, false, "attack-target-cleared");
    }

    private void ClearAttack(EntityRuntimeState attacker)
    {
        if (attacker == null)
            return;

        bool resume = attacker.Attack?.ResumeNavigationOrderAfterTarget == true;
        attacker.Attack?.ClearTarget();
        if (resume && attacker.Navigation?.HasBaseOrder == true)
            navigation.ResumeBaseOrder(attacker, currentElapsedTime);
        else
            navigation.HoldPosition(attacker, false, "attack-ended");
    }

    private static void ClearWorkerActivity(EntityRuntimeState source)
    {
        if (source.Worker == null)
            return;
        source.Worker.TargetResourceUnitId = -1;
        source.Worker.ExtractionTimer = 0f;
        source.Worker.IsExtracting = false;
    }
}
