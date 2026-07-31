using UnityEngine;

/// <summary>
/// Deriva un estado presentable desde los sistemas reales. No reemplaza los
/// datos de combate, movimiento o trabajo; los resume sin que esos sistemas
/// compitan escribiendo una única variable manualmente.
/// </summary>
public static class EntityStatusRuntimeSystem
{
    public static void Update(EntityWorld world, float elapsedTime)
    {
        if (world == null)
            return;

        foreach (EntityRuntimeState entity in world.Values)
        {
            if (entity.Status == null)
                entity.Status = new EntityStatusRuntimeState();

            EntityStatusRuntimeState status = entity.Status;
            status.InCombat = (entity.Attack != null && entity.Attack.TargetEntityId > 0) ||
                              elapsedTime < status.CombatUntil;
            status.IsUnderAttack = elapsedTime < status.UnderAttackUntil;

            if (entity.Life == null || entity.Life.State == EntityLifeState.Dead)
            {
                status.Activity = EntityActivityState.Dead;
                status.ActivityDetail = "dead";
                status.InCombat = false;
                status.IsUnderAttack = false;
                continue;
            }

            if (entity.Life.State == EntityLifeState.Captured)
            {
                status.Activity = EntityActivityState.Captured;
                status.ActivityDetail = "captured";
                status.InCombat = false;
                status.IsUnderAttack = false;
                continue;
            }

            if (entity.Life.State == EntityLifeState.Downed)
            {
                status.Activity = EntityActivityState.Downed;
                status.ActivityDetail = "downed";
                status.InCombat = false;
                status.IsUnderAttack = false;
                continue;
            }

            if (entity.Attack != null)
            {
                if (entity.Attack.Phase == EntityAttackPhase.Windup)
                {
                    status.Activity = EntityActivityState.Attacking;
                    status.ActivityDetail = "attack-windup";
                    continue;
                }

                if (entity.Attack.Phase == EntityAttackPhase.Recovery)
                {
                    status.Activity = EntityActivityState.Recovering;
                    status.ActivityDetail = "attack-recovery";
                    continue;
                }
            }

            if (entity.Worker?.IsExtracting == true)
            {
                status.Activity = EntityActivityState.Performing;
                status.ActivityDetail = "resource-extraction";
                continue;
            }

            Vector3 difference = entity.Destination - entity.Position;
            difference.y = 0f;
            if (difference.sqrMagnitude > 0.01f)
            {
                status.Activity = EntityActivityState.Moving;
                if (entity.Attack?.Phase == EntityAttackPhase.Approaching)
                    status.ActivityDetail = "approaching-attack-target";
                else if (entity.Navigation?.OrderType == EntityNavigationOrderType.AttackMove)
                    status.ActivityDetail = "attack-move";
                else if (entity.Navigation?.OrderType == EntityNavigationOrderType.Patrol)
                    status.ActivityDetail = "patrol";
                else
                    status.ActivityDetail = "moving";
                continue;
            }

            status.Activity = EntityActivityState.Idle;
            status.ActivityDetail = "idle";
        }
    }
}
