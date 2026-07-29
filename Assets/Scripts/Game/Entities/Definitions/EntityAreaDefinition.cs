using System;

public static class EntityAreaShapes
{
    public const string Circle = "circle";
    public const string Rectangle = "rectangle";
}

public static class EntityAreaRelationships
{
    public const string All = "all";
    public const string Owner = "owner";
    public const string Ally = "ally";
    public const string Enemy = "enemy";
    public const string Neutral = "neutral";
}

/// <summary>
/// Configuración declarativa de una entidad de área. El visual es opcional y
/// puede cambiarse después sin modificar la lógica de aura o trigger.
/// </summary>
[Serializable]
public sealed class EntityAreaDefinition
{
    public string shape = EntityAreaShapes.Circle;
    public float radius = 1f;
    public ScenarioVector3 size;
    public string relationship = EntityAreaRelationships.All;
    public string[] requiredAttributes;
    public string[] excludedAttributes;
    public bool emitEnter = true;
    public bool emitStay;
    public bool emitExit = true;
    public float stayInterval = 1f;
    public bool visible = true;
}
