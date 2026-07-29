using System;
using UnityEngine;

/// <summary>
/// Ejecuta ataques declarados por las entidades. La entrega melee aplica daño
/// en el instante de impacto; otros delivery types podrán crear proyectiles sin
/// alterar el ciclo windup/recovery ni el comando de ataque.
/// </summary>
public sealed class CombatRuntimeSystem
{
    private readonly EntityWorld world;
    private readonly DamageRuntimeService damage;

    public CombatRuntimeSystem(EntityWorld world, DamageRuntimeService damage)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.damage = damage ?? throw new ArgumentNullException(nameof(damage));
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

        if (!EntityCombatRules.CanAttack(source, target, issuerParticipantId, out rejectionReason))
            return false;

        source.Attack.AssignTarget(target.UnitId);
        source.InteractionTargetUnitId = -1;
        ClearWorkerActivity(source);
        return true;
    }

    public void Update(float deltaTime, float elapsedTime)
    {
        float safeDelta = Mathf.Max(0f, deltaTime);
        foreach (EntityRuntimeState attacker in world.SnapshotValues())
        {
            EntityAttackRuntimeState attack = attacker.Attack;
            if (attack == null)
                continue;

            // Recovery pertenece a la entidad, no al objetivo. El temporizador
            // continúa aunque cambie la orden, pero no inmoviliza a la unidad:
            // puede desplazarse o acercarse al siguiente objetivo sin iniciar
            // otro windup hasta que finalice la recuperación.
            if (attack.Phase == EntityAttackPhase.Recovery)
            {
                UpdateRecovery(attacker, attack, safeDelta);
                continue;
            }

            if (attack.TargetEntityId <= 0)
                continue;

            if (!world.TryGet(attack.TargetEntityId, out EntityRuntimeState target) ||
                !EntityCombatRules.IsStillValidTarget(attacker, target))
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
                            attacker.Destination = new Vector3(target.Position.x, attacker.Position.y, target.Position.z);
                    }
                    else
                    {
                        attacker.Destination = attacker.Position;
                        BeginWindup(attacker, target, attack, elapsedTime);
                    }
                    break;
            }
        }
    }


    private void UpdateRecovery(
        EntityRuntimeState attacker,
        EntityAttackRuntimeState attack,
        float deltaTime)
    {
        attack.PhaseRemaining -= deltaTime;

        // Si durante Recovery se asignó otro objetivo de ataque, la unidad puede
        // aproximarse a él. La fase no cambia a Windup hasta que el temporizador
        // llegue a cero. Una orden de movimiento limpia TargetEntityId y conserva
        // la recuperación, por lo que su Destination manual no se sobrescribe.
        if (attack.TargetEntityId > 0)
        {
            if (!world.TryGet(attack.TargetEntityId, out EntityRuntimeState target) ||
                !EntityCombatRules.IsStillValidTarget(attacker, target))
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
                    attacker.Destination = new Vector3(
                        target.Position.x,
                        attacker.Position.y,
                        target.Position.z);
                }
                else if (inRange)
                {
                    attacker.Destination = attacker.Position;
                }
            }
        }

        if (attack.PhaseRemaining <= 0f)
        {
            attack.Phase = EntityAttackPhase.None;
            attack.PhaseRemaining = 0f;
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
        attacker.Destination = attacker.Position;
        if (!inRange && EntityCombatRules.NormalizeDelivery(attack.Delivery) == EntityAttackDeliveryTypes.Melee)
        {
            attack.Phase = EntityAttackPhase.Approaching;
            attack.PhaseRemaining = 0f;
            if (attack.ChaseTarget && attacker.MoveSpeed > 0f)
                attacker.Destination = new Vector3(target.Position.x, attacker.Position.y, target.Position.z);
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

    private static void ClearAttackTargetPreservingRecovery(EntityRuntimeState attacker)
    {
        if (attacker == null)
            return;
        attacker.Attack?.ClearTargetPreservingRecovery();
        attacker.Destination = attacker.Position;
    }

    private static void ClearAttack(EntityRuntimeState attacker)
    {
        if (attacker == null)
            return;
        attacker.Attack?.ClearTarget();
        attacker.Destination = attacker.Position;
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
