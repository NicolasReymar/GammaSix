using System;
using System.Collections.Generic;

public static class EntityNetworkMessageNames
{
    public const string Snapshot = "GammaSix.UnitSnapshot";
    public const string MoveCommand = "GammaSix.UnitMoveCommand";
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
}
