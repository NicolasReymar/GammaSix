using System;
using UnityEngine;

public enum EntityLifecycleReason
{
    ScenarioInitialization,
    RuntimeRule,
    Wave,
    Construction,
    ResourceDepleted,
    Replacement,
    Rescue,
    Death,
    DeathCleanup,
    DeathReplacement,
    MatchCommand,
    Debug
}

[Serializable]
public sealed class EntitySpawnRequest
{
    public string EntityDefinitionId;
    public string ScenarioInstanceId;
    public string[] InstanceAttributes;
    public int OwnerParticipantId = -1;
    public int TeamId;
    public int ColorId = -1;
    public Vector3 Position;
    public bool AlignToDefinitionGround;
    public EntityLifecycleReason Reason = EntityLifecycleReason.RuntimeRule;
}

[Serializable]
public sealed class EntityDespawnRequest
{
    public int EntityId;
    public EntityLifecycleReason Reason = EntityLifecycleReason.RuntimeRule;
}


[Serializable]
public sealed class EntityReplacementRequest
{
    public int SourceEntityId;
    public EntitySpawnRequest Replacement;
    public EntityLifecycleReason Reason = EntityLifecycleReason.Replacement;
}

public sealed class EntitySpawnedEvent
{
    public EntityRuntimeState Entity { get; }
    public EntityLifecycleReason Reason { get; }

    public EntitySpawnedEvent(EntityRuntimeState entity, EntityLifecycleReason reason)
    {
        Entity = entity;
        Reason = reason;
    }
}

public sealed class EntityDespawnedEvent
{
    public EntityRuntimeState Entity { get; }
    public int EntityId { get; }
    public string EntityDefinitionId { get; }
    public int OwnerParticipantId { get; }
    public int TeamId { get; }
    public EntityLifecycleReason Reason { get; }

    public EntityDespawnedEvent(EntityRuntimeState entity, EntityLifecycleReason reason)
    {
        Entity = entity;
        EntityId = entity?.UnitId ?? -1;
        EntityDefinitionId = entity?.EntityDefinitionId;
        OwnerParticipantId = entity?.OwnerParticipantId ?? -1;
        TeamId = entity?.TeamId ?? 0;
        Reason = reason;
    }
}
