using System;
using System.Collections.Generic;

public static class EntityNetworkMessageNames
{
    public const string Snapshot = "GammaSix.UnitSnapshot";
    public const string MoveCommand = "GammaSix.UnitMoveCommand";
    public const string ResourceInteractionCommand = "GammaSix.ResourceInteractionCommand";
    public const string EntityInteractionCommand = "GammaSix.EntityInteractionCommand";
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
public sealed class EntitySnapshotPayload
{
    public List<EntitySnapshotData> Units = new();
}

[Serializable]
public sealed class EntitySnapshotData
{
    public int UnitId;
    public string EntityDefinitionId;
    public string UnitName;
    public string UnitTypeId;
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
}

[Serializable]
public sealed class ResourceSnapshotData
{
    public string ResourceId;
    public int Amount;
}
