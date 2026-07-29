using UnityEngine;

/// <summary>
/// Aplica el resultado configurado después de una muerte confirmada.
/// La muerte ya ocurrió antes de este servicio y publicó entity-died.
/// Este servicio no confunde esa transición con un despawn directo.
/// </summary>
public static class DeathRuntimeService
{
    public static void Update(
        EntityWorld world,
        EntityLifecycleService lifecycle,
        float deltaTime)
    {
        if (world == null || lifecycle == null)
            return;

        float safeDelta = Mathf.Max(0f, deltaTime);
        foreach (EntityRuntimeState entity in world.SnapshotValues())
        {
            if (entity.Life == null ||
                entity.Life.State != EntityLifeState.Dead ||
                entity.Life.DeathOutcomeQueued)
            {
                continue;
            }

            entity.Life.DeathElapsed += safeDelta;
            if (entity.Life.DeathElapsed < Mathf.Max(0f, entity.Life.DeathOutcomeDelay))
                continue;

            ApplyOutcome(entity, lifecycle);
        }
    }

    private static void ApplyOutcome(
        EntityRuntimeState deadEntity,
        EntityLifecycleService lifecycle)
    {
        EntityLifeRuntimeState life = deadEntity.Life;
        switch (life.DeathOutcome)
        {
            case EntityDeathOutcome.Remain:
                life.DeathOutcomeQueued = true;
                return;

            case EntityDeathOutcome.Replace:
                QueueReplacement(deadEntity, lifecycle);
                return;

            default:
                if (lifecycle.QueueDespawn(
                        deadEntity.UnitId,
                        EntityLifecycleReason.DeathCleanup,
                        out string despawnRejection))
                {
                    life.DeathOutcomeQueued = true;
                }
                else
                {
                    Debug.LogWarning(
                        $"[DeathRuntimeService] No se pudo retirar la entidad muerta " +
                        $"{deadEntity.UnitId}: {despawnRejection}");
                    life.DeathOutcomeQueued = true;
                }
                return;
        }
    }

    private static void QueueReplacement(
        EntityRuntimeState deadEntity,
        EntityLifecycleService lifecycle)
    {
        EntityLifeRuntimeState life = deadEntity.Life;
        if (string.IsNullOrWhiteSpace(life.DeathReplacementEntityId))
        {
            Debug.LogError(
                $"[DeathRuntimeService] La entidad '{deadEntity.EntityDefinitionId}' " +
                "usa DeathOutcome.Replace sin declarar DeathReplacementEntityId. " +
                "La entidad muerta permanecerá en el mundo.");
            life.DeathOutcomeQueued = true;
            return;
        }

        bool inheritOwner = life.DeathReplacementInheritsOwner;
        EntitySpawnRequest replacement = new()
        {
            EntityDefinitionId = life.DeathReplacementEntityId,
            ScenarioInstanceId = string.IsNullOrWhiteSpace(deadEntity.ScenarioInstanceId)
                ? $"death-replacement.{deadEntity.UnitId}"
                : $"{deadEntity.ScenarioInstanceId}.death-replacement",
            OwnerParticipantId = inheritOwner ? deadEntity.OwnerParticipantId : -1,
            TeamId = inheritOwner ? deadEntity.TeamId : 0,
            ColorId = inheritOwner ? deadEntity.ColorId : PlayerColorPalette.Neutral,
            Position = deadEntity.Position,
            AlignToDefinitionGround = true,
            Reason = EntityLifecycleReason.DeathReplacement
        };

        if (lifecycle.QueueReplacement(
                deadEntity.UnitId,
                replacement,
                EntityLifecycleReason.DeathReplacement,
                out string rejection))
        {
            life.DeathOutcomeQueued = true;
            return;
        }

        Debug.LogError(
            $"[DeathRuntimeService] No se pudo transformar la entidad muerta " +
            $"{deadEntity.UnitId} en '{life.DeathReplacementEntityId}': {rejection}. " +
            "La entidad muerta permanecerá en el mundo.");
        life.DeathOutcomeQueued = true;
    }
}
