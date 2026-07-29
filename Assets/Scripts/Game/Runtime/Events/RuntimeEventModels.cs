using System;
using UnityEngine;

public enum RuntimeEventType
{
    None,
    MatchStarted,
    EntitySpawned,
    EntityDespawned,
    EntityEnteredArea,
    EntityStayedInArea,
    EntityExitedArea,
    EntityDamaged,
    EntityFatalDamage,
    EntityDied,
    ChannelStarted,
    ChannelCompleted,
    ChannelCancelled,
    ParticipantStateChanged,
    ParticipantControlChanged,
    ParticipantAttributeChanged,
    ParticipantVariableChanged,
    ResourceChanged,
    MatchResultDeclared
}

/// <summary>
/// Fotografía inmutable de una entidad al publicar un evento. Permite que las
/// reglas sigan usando propietario, posición, definición y atributos aunque la
/// entidad muera, sea reemplazada o se despawnee antes de procesar la cola.
/// </summary>
public sealed class RuntimeEntityEventSnapshot
{
    public int EntityId = -1;
    public string EntityDefinitionId;
    public string ScenarioInstanceId;
    public string UnitName;
    public int OwnerParticipantId = -1;
    public int TeamId;
    public int ColorId = -1;
    public Vector3 Position;
    public int Health;
    public int MaxHealth;
    public EntityLifeState LifeState = EntityLifeState.Alive;
    public string[] Attributes = Array.Empty<string>();

    public bool HasAttribute(string attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute) || Attributes == null)
            return false;

        foreach (string value in Attributes)
        {
            if (string.Equals(value, attribute, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static RuntimeEntityEventSnapshot Capture(EntityRuntimeState entity)
    {
        if (entity == null)
            return null;

        return new RuntimeEntityEventSnapshot
        {
            EntityId = entity.UnitId,
            EntityDefinitionId = entity.EntityDefinitionId,
            ScenarioInstanceId = entity.ScenarioInstanceId,
            UnitName = entity.UnitName,
            OwnerParticipantId = entity.OwnerParticipantId,
            TeamId = entity.TeamId,
            ColorId = entity.ColorId,
            Position = entity.Position,
            Health = entity.Health,
            MaxHealth = entity.MaxHealth,
            LifeState = entity.Life?.State ?? EntityLifeState.Alive,
            Attributes = entity.Attributes?.ToArray() ?? Array.Empty<string>()
        };
    }
}

public sealed class RuntimeEventContext
{
    public RuntimeEventType Type;
    public float ElapsedTime;

    public int EntityId = -1;
    public EntityRuntimeState Entity;
    public RuntimeEntityEventSnapshot EntitySnapshot;

    public int AreaEntityId = -1;
    public EntityRuntimeState AreaEntity;
    public RuntimeEntityEventSnapshot AreaEntitySnapshot;

    public int SourceEntityId = -1;
    public EntityRuntimeState SourceEntity;
    public RuntimeEntityEventSnapshot SourceEntitySnapshot;
    public int DamageAmount;
    public int PreviousHealth;
    public int CurrentHealth;
    public string DamageType;
    public FatalDamageResolution FatalResolution;

    public string ChannelId;
    public float ChannelDuration;
    public float ChannelProgress;

    public int ParticipantId = -1;
    public MatchParticipantRuntimeState Participant;
    public ParticipantLifeState PreviousParticipantState;
    public ParticipantLifeState CurrentParticipantState;
    public bool PreviousControlEnabled;
    public bool CurrentControlEnabled;
    public string ParticipantAttribute;
    public bool ParticipantAttributeAdded;
    public string VariableName;
    public string PreviousVariableValue;
    public string CurrentVariableValue;

    public string ResourceScope;
    public int ResourceOwnerId = -1;
    public string ResourceId;
    public int PreviousResourceAmount;
    public int CurrentResourceAmount;

    public MatchResultState MatchResult;
    public int ResultTeamId;
    public string Reason;

    public void CaptureEntitySnapshots()
    {
        EntitySnapshot ??= RuntimeEntityEventSnapshot.Capture(Entity);
        AreaEntitySnapshot ??= RuntimeEntityEventSnapshot.Capture(AreaEntity);
        SourceEntitySnapshot ??= RuntimeEntityEventSnapshot.Capture(SourceEntity);
    }

    public static RuntimeEventContext MatchStarted(float elapsedTime)
    {
        return new RuntimeEventContext
        {
            Type = RuntimeEventType.MatchStarted,
            ElapsedTime = elapsedTime
        };
    }
}
