using UnityEngine;

/// <summary>
/// Estado autoritativo de una entidad en el servidor.
/// </summary>
public sealed class EntityRuntimeState
{
    public int UnitId;
    public string EntityDefinitionId;
    public string ScenarioInstanceId;
    public string UnitName;
    public string UnitTypeId;
    public EntityAttributeSet Attributes;
    public int OwnerParticipantId;
    /// <summary>Identificador de red legado para vistas y compatibilidad temporal.</summary>
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
    public ResourceRuntimeState Resource;
    public WorkerRuntimeState Worker;
    public EntityAreaRuntimeState Area;
    public EntityAttackRuntimeState Attack;
    public EntityLifeRuntimeState Life;
    public EntityStatusRuntimeState Status;
    public int InteractionTargetUnitId = -1;
    public float InteractionRange = 0.65f;
}
