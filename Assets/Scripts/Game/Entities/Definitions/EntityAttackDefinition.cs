using System;

public static class EntityAttackDeliveryTypes
{
    public const string Melee = "melee";
    public const string Projectile = "projectile";
}

/// <summary>
/// Datos base de ataque de una entidad. baseAttackSpeed es un multiplicador:
/// 1 representa la velocidad declarada por attackTime + recoveryTime.
/// Modificadores futuros pueden alterar el multiplicador runtime sin cambiar
/// la definición original.
/// </summary>
[Serializable]
public sealed class EntityAttackDefinition
{
    public string delivery = EntityAttackDeliveryTypes.Melee;
    public string damageType = "physical";
    public int baseDamage = 10;
    public float baseAttackSpeed = 1f;
    public float attackTime = 0.35f;
    public float recoveryTime = 0.65f;
    public float attackRange = 0.65f;
    public bool chaseTarget = true;
}
