using System;

[Serializable]
public sealed class ScenarioResourceAmount
{
    public string resourceId;
    public int amount;
}

[Serializable]
public sealed class ScenarioParticipantResourceDefinition
{
    public int participantId;
    public string slotId;
    public ScenarioResourceAmount[] resources;
}

[Serializable]
public sealed class ScenarioRuleDefinition
{
    public string id;
    public bool enabled = true;
    public string eventType;
    public int priority;
    public bool once;
    public float cooldown;
    public ScenarioRuleConditionDefinition[] conditions;
    public ScenarioRuleActionDefinition[] actions;
}

[Serializable]
public sealed class ScenarioRuleConditionDefinition
{
    public string type;
    public string attribute;
    public string state;
    public string participantSelector;
    public int participantId;
    public int teamId;
    public string value;
    public string entitySelector;
    public string variableName;
}

[Serializable]
public sealed class ScenarioRuleActionDefinition
{
    public string type;
    public string message;

    public string participantSelector;
    public int participantId;
    public string participantState;
    public bool controlEnabled;
    public string attribute;

    public string variableName;
    public string variableValue;
    public string valueSource;

    public string resourceScope;
    public string resourceId;
    public int amount;
    public int teamId;

    public string entityId;
    public string entityIdVariable;
    public string positionSelector;
    public ScenarioVector3 position;
    public bool inheritEventOwner = true;
    public string entitySelector;
    public string entityState;
    public string entityAttribute;
    public string excludeEntityAttribute;
    public string preserveAttribute;
    public bool excludeEventEntity;

    public string areaAttribute;
    public string channelId;
    public float duration;
    public string requiredParticipantState;
    public string requiredParticipantAttribute;

    public string result;
    public string value;
    public string reason;
}
