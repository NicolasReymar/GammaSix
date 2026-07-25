using UnityEngine;

/// <summary>
/// Estado autoritativo de una entidad en el servidor.
/// </summary>
public sealed class EntityRuntimeState
{
    public int UnitId;
    public string EntityDefinitionId;
    public string UnitName;
    public string UnitTypeId;
    public EntityAttributeSet Attributes;
    public ulong OwnerClientId;
    public int TeamId;
    public int ColorId;
    public Vector3 Position;
    public Vector3 Destination;
    public int Health;
    public int MaxHealth;
    public float MoveSpeed;
    public bool Solid;
    public Vector3 BoundsSize;
}
