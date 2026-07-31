using System;
using UnityEngine;

/// <summary>
/// Punto único para aplicar daño autoritativo. Publica eventos normales y
/// consulta sincrónicamente al motor de reglas antes de confirmar una muerte.
/// </summary>
public sealed class DamageRuntimeService
{
    private const float DefaultCombatMemory = 2.5f;
    private const float DefaultUnderAttackDuration = 1.25f;

    private readonly EntityWorld world;
    private readonly RuntimeEventBus eventBus;
    private readonly RuleRuntimeSystem rules;

    public DamageRuntimeService(
        EntityWorld world,
        RuntimeEventBus eventBus,
        RuleRuntimeSystem rules)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public bool ApplyDamage(
        int sourceEntityId,
        int targetEntityId,
        int amount,
        string damageType,
        string reason,
        float elapsedTime,
        out EntityDamageResult result)
    {
        result = new EntityDamageResult();
        if (amount <= 0)
        {
            result.Message = "El daño debe ser mayor que cero.";
            return false;
        }

        if (!world.TryGet(targetEntityId, out EntityRuntimeState target))
        {
            result.Message = $"No existe la entidad objetivo {targetEntityId}.";
            return false;
        }

        world.TryGet(sourceEntityId, out EntityRuntimeState source);
        if (target.Life == null || !target.Life.CanReceiveDamage)
        {
            result.Message = "La entidad objetivo no puede recibir daño en su estado actual.";
            return false;
        }

        int previousHealth = target.Health;
        target.Health = Mathf.Max(0, target.Health - amount);
        MarkCombat(source, target, elapsedTime);

        result.Applied = true;
        result.PreviousHealth = previousHealth;
        result.CurrentHealth = target.Health;

        eventBus.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.EntityDamaged,
            ElapsedTime = elapsedTime,
            EntityId = target.UnitId,
            Entity = target,
            SourceEntityId = source?.UnitId ?? -1,
            SourceEntity = source,
            DamageAmount = amount,
            PreviousHealth = previousHealth,
            CurrentHealth = target.Health,
            DamageType = damageType,
            ParticipantId = target.OwnerParticipantId,
            Reason = reason
        });

        if (target.Health > 0)
            return true;

        FatalDamageContext fatal = new()
        {
            Source = source,
            Target = target,
            DamageAmount = amount,
            PreviousHealth = previousHealth,
            DamageType = damageType,
            Reason = reason,
            Resolution = FatalDamageResolution.Death,
            RestoredHealth = 1
        };

        rules.ResolveFatalDamage(fatal, elapsedTime);
        ApplyFatalResolution(fatal, elapsedTime);

        result.WasFatal = true;
        result.FatalResolution = fatal.Resolution;
        result.CurrentHealth = target.Health;
        return true;
    }

    /// <summary>
    /// Confirma una muerte causada por una regla sin ejecutar interceptores de
    /// daño fatal. Se usa para acciones declarativas de destrucción: la entidad
    /// muere, publica sus eventos y conserva su deathOutcome configurado.
    /// </summary>
    public bool ApplyForcedDeath(
        int sourceEntityId,
        int targetEntityId,
        string reason,
        float elapsedTime,
        out EntityDamageResult result)
    {
        result = new EntityDamageResult();
        if (!world.TryGet(targetEntityId, out EntityRuntimeState target))
        {
            result.Message = $"No existe la entidad objetivo {targetEntityId}.";
            return false;
        }
        if (target.Life == null || target.Life.State == EntityLifeState.Dead)
        {
            result.Message = "La entidad objetivo ya está muerta o no posee estado de vida.";
            return false;
        }

        world.TryGet(sourceEntityId, out EntityRuntimeState source);
        int previousHealth = target.Health;
        int forcedAmount = Mathf.Max(1, Mathf.Max(target.Health, target.MaxHealth) + 1);
        FatalDamageContext fatal = new()
        {
            Source = source,
            Target = target,
            DamageAmount = forcedAmount,
            PreviousHealth = previousHealth,
            DamageType = "rule-destroy",
            Reason = reason,
            Resolution = FatalDamageResolution.Death,
            RestoredHealth = 0
        };

        ApplyFatalResolution(fatal, elapsedTime);
        result.Applied = true;
        result.WasFatal = true;
        result.PreviousHealth = previousHealth;
        result.CurrentHealth = target.Health;
        result.FatalResolution = FatalDamageResolution.Death;
        return true;
    }

    private void ApplyFatalResolution(FatalDamageContext fatal, float elapsedTime)
    {
        EntityRuntimeState target = fatal.Target;
        if (target == null || target.Life == null)
            return;

        bool died = false;
        switch (fatal.Resolution)
        {
            case FatalDamageResolution.Prevented:
                target.Health = Mathf.Clamp(fatal.RestoredHealth, 1, target.MaxHealth);
                target.Life.State = EntityLifeState.Alive;
                break;

            case FatalDamageResolution.Downed:
                target.Health = Mathf.Clamp(fatal.RestoredHealth, 0, target.MaxHealth);
                target.Life.State = EntityLifeState.Downed;
                StopEntity(target);
                break;

            default:
                // Captured se conserva en el enum por compatibilidad de datos
                // antiguos, pero ya no es una resolución del motor. Cualquier
                // valor no soportado finaliza como muerte normal.
                fatal.Resolution = FatalDamageResolution.Death;
                target.Health = 0;
                target.Life.State = EntityLifeState.Dead;
                target.Life.DeathElapsed = 0f;
                target.Life.DeathOutcomeQueued = false;
                target.Life.LastFatalReason = fatal.Reason;
                target.Solid = false;
                StopEntity(target);
                died = true;
                break;
        }

        eventBus.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.EntityFatalDamage,
            ElapsedTime = elapsedTime,
            EntityId = target.UnitId,
            Entity = target,
            SourceEntityId = fatal.Source?.UnitId ?? -1,
            SourceEntity = fatal.Source,
            DamageAmount = fatal.DamageAmount,
            PreviousHealth = fatal.PreviousHealth,
            CurrentHealth = target.Health,
            DamageType = fatal.DamageType,
            FatalResolution = fatal.Resolution,
            ParticipantId = target.OwnerParticipantId,
            Reason = fatal.Reason
        });

        if (died)
        {
            eventBus.Publish(new RuntimeEventContext
            {
                Type = RuntimeEventType.EntityDied,
                ElapsedTime = elapsedTime,
                EntityId = target.UnitId,
                Entity = target,
                SourceEntityId = fatal.Source?.UnitId ?? -1,
                SourceEntity = fatal.Source,
                DamageAmount = fatal.DamageAmount,
                PreviousHealth = fatal.PreviousHealth,
                CurrentHealth = 0,
                DamageType = fatal.DamageType,
                FatalResolution = fatal.Resolution,
                ParticipantId = target.OwnerParticipantId,
                Reason = fatal.Reason
            });
        }
    }

    private static void MarkCombat(
        EntityRuntimeState source,
        EntityRuntimeState target,
        float elapsedTime)
    {
        if (target.Status != null)
        {
            target.Status.CombatUntil = Mathf.Max(target.Status.CombatUntil, elapsedTime + DefaultCombatMemory);
            target.Status.UnderAttackUntil = Mathf.Max(target.Status.UnderAttackUntil, elapsedTime + DefaultUnderAttackDuration);
        }

        if (source?.Status != null)
            source.Status.CombatUntil = Mathf.Max(source.Status.CombatUntil, elapsedTime + DefaultCombatMemory);
    }

    private static void StopEntity(EntityRuntimeState entity)
    {
        entity.Navigation?.ClearAll(entity.Position, "stopped-by-runtime");
        entity.Destination = entity.Position;
        entity.InteractionTargetUnitId = -1;
        entity.Attack?.ClearTarget();
        if (entity.Worker != null)
        {
            entity.Worker.TargetResourceUnitId = -1;
            entity.Worker.ExtractionTimer = 0f;
            entity.Worker.IsExtracting = false;
        }
    }
}
