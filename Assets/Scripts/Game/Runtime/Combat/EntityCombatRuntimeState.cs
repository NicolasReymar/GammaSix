using System;
using UnityEngine;

public enum EntityAttackPhase
{
    None,
    Approaching,
    Windup,
    Recovery
}

public enum EntityCombatStance
{
    Aggressive,
    Passive
}

public enum EntityLifeState
{
    Alive,
    Downed,
    Captured,
    Dead
}

public enum EntityDeathOutcome
{
    Remain,
    Despawn,
    Replace
}

public enum EntityActivityState
{
    Idle,
    Moving,
    Performing,
    Attacking,
    Recovering,
    Downed,
    Captured,
    Dead
}

public enum FatalDamageResolution
{
    Death,
    Prevented,
    Downed,
    Captured
}

public sealed class EntityAttackRuntimeState
{
    public string Delivery = EntityAttackDeliveryTypes.Melee;
    public string DamageType = "physical";
    public int BaseDamage;
    public float BaseAttackSpeed = 1f;
    public float AttackSpeedMultiplier = 1f;
    public float AttackTime;
    public float RecoveryTime;
    public float AttackRange;
    public bool ChaseTarget = true;

    public int TargetEntityId = -1;
    public EntityAttackPhase Phase;
    public float PhaseRemaining;
    public bool ForceTarget;
    public bool ResumeNavigationOrderAfterTarget;
    public EntityCombatStance Stance = EntityCombatStance.Aggressive;

    public float EffectiveSpeed => Mathf.Max(0.05f, BaseAttackSpeed * AttackSpeedMultiplier);
    public float EffectiveAttackTime => Mathf.Max(0f, AttackTime) / EffectiveSpeed;
    public float EffectiveRecoveryTime => Mathf.Max(0f, RecoveryTime) / EffectiveSpeed;

    /// <summary>
    /// Cambia el objetivo sin saltarse una recuperación ya iniciada.
    /// Fuera de Recovery, una nueva orden reinicia la preparación del ataque.
    /// </summary>
    public void AssignTarget(int targetEntityId, bool forceTarget = false, bool resumeNavigationOrderAfterTarget = false)
    {
        TargetEntityId = targetEntityId;
        ForceTarget = forceTarget;
        ResumeNavigationOrderAfterTarget = resumeNavigationOrderAfterTarget;
        if (Phase == EntityAttackPhase.Recovery)
            return;

        Phase = EntityAttackPhase.None;
        PhaseRemaining = 0f;
    }

    /// <summary>
    /// Cancela el objetivo actual, pero conserva el temporizador de Recovery.
    /// Esto evita reiniciar el ataque mediante órdenes de movimiento, seguimiento
    /// o extracción durante la recuperación.
    /// </summary>
    public void ClearTargetPreservingRecovery()
    {
        TargetEntityId = -1;
        ForceTarget = false;
        ResumeNavigationOrderAfterTarget = false;
        if (Phase == EntityAttackPhase.Recovery)
            return;

        Phase = EntityAttackPhase.None;
        PhaseRemaining = 0f;
    }

    /// <summary>
    /// Reinicia por completo el ciclo de ataque. Se reserva para estados que
    /// invalidan definitivamente la acción, como muerte o una definición inválida.
    /// </summary>
    public void ClearTarget()
    {
        TargetEntityId = -1;
        ForceTarget = false;
        ResumeNavigationOrderAfterTarget = false;
        Phase = EntityAttackPhase.None;
        PhaseRemaining = 0f;
    }
}

public sealed class EntityLifeRuntimeState
{
    public EntityLifeState State = EntityLifeState.Alive;
    public EntityDeathOutcome DeathOutcome = EntityDeathOutcome.Despawn;
    public float DeathOutcomeDelay = 0.75f;
    public string DeathReplacementEntityId;
    public bool DeathReplacementInheritsOwner = true;
    public float DeathElapsed;
    public bool DeathOutcomeQueued;
    public string LastFatalReason;

    public bool CanAct => State == EntityLifeState.Alive;
    public bool CanReceiveDamage => State == EntityLifeState.Alive;
}

public sealed class EntityStatusRuntimeState
{
    public EntityActivityState Activity = EntityActivityState.Idle;
    public bool InCombat;
    public bool IsUnderAttack;
    public float CombatUntil;
    public float UnderAttackUntil;
    public string ActivityDetail;
}

public sealed class FatalDamageContext
{
    public EntityRuntimeState Source;
    public EntityRuntimeState Target;
    public int DamageAmount;
    public int PreviousHealth;
    public string DamageType;
    public string Reason;
    public FatalDamageResolution Resolution = FatalDamageResolution.Death;
    public int RestoredHealth = 1;
}

public sealed class EntityDamageResult
{
    public bool Applied;
    public int PreviousHealth;
    public int CurrentHealth;
    public bool WasFatal;
    public FatalDamageResolution FatalResolution;
    public string Message;
}
