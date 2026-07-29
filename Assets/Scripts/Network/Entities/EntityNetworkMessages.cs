using System;
using System.Collections.Generic;

public static class EntityNetworkMessageNames
{
    public const string Snapshot = "GammaSix.UnitSnapshot";
    public const string EntitySpawned = "GammaSix.EntitySpawned";
    public const string EntityDespawned = "GammaSix.EntityDespawned";
    public const string MoveCommand = "GammaSix.UnitMoveCommand";
    public const string ResourceInteractionCommand = "GammaSix.ResourceInteractionCommand";
    public const string EntityInteractionCommand = "GammaSix.EntityInteractionCommand";
    public const string AttackCommand = "GammaSix.EntityAttackCommand";
}

[Serializable]
public sealed class EntityMoveCommand
{
    public int UnitId;
    public float X;
    public float Y;
    public float Z;
}

[Serializable]
public sealed class ResourceInteractionCommand
{
    public int WorkerUnitId;
    public int ResourceUnitId;
}

[Serializable]
public sealed class EntityInteractionCommand
{
    public int SourceUnitId;
    public int TargetUnitId;
}

[Serializable]
public sealed class EntityAttackCommand
{
    public int SourceUnitId;
    public int TargetUnitId;
}

[Serializable]
public sealed class EntitySnapshotPayload
{
    public List<EntitySnapshotData> Units = new();
}

[Serializable]
public sealed class EntitySnapshotData
{
    public int UnitId;
    public string EntityDefinitionId;
    public string ScenarioInstanceId;
    public string UnitName;
    public string UnitTypeId;
    public int OwnerParticipantId;
    public ulong OwnerClientId;
    public int TeamId;
    public int ColorId;
    public float X;
    public float Y;
    public float Z;
    public int Health;
    public int MaxHealth;
    public bool Solid;
    public string[] Attributes;
    public bool ResourceInfinite;
    public int ResourceTier;
    public ResourceSnapshotData[] Resources;
    public string WorkerResourceName;
    public int WorkerCarriedAmount;
    public bool WorkerIsExtracting;
    public int AreaOccupantCount;
    public string LifeState;
    public string ActivityState;
    public bool InCombat;
    public bool IsUnderAttack;
    public string ActivityDetail;
    public bool HasAttack;
    public int AttackBaseDamage;
    public float BaseAttackSpeed;
    public float AttackSpeedMultiplier;
    public float AttackTime;
    public float RecoveryTime;
    public float AttackRange;
    public string AttackDelivery;
    public string AttackDamageType;
    public int AttackTargetEntityId;
    public string AttackPhase;
}

[Serializable]
public sealed class ResourceSnapshotData
{
    public string ResourceId;
    public int Amount;
}

[Serializable]
public sealed class EntitySpawnEventPayload
{
    public EntitySnapshotData Entity;
    public EntityLifecycleReason Reason;
}

[Serializable]
public sealed class EntityDespawnEventPayload
{
    public int EntityId;
    public EntityLifecycleReason Reason;
}
