using System;
using UnityEngine;

public static class EntityKinds
{
    public const string Unit = "unit";
    public const string Building = "building";
}

[Serializable]
public class EntityDefinition
{
    public string id;
    public string name;
    public string kind = EntityKinds.Unit;
    public int maxHealth = 100;
    public float moveSpeed = 0f;
    public bool solid;
    public string visual = "capsule";
    public ScenarioVector3 scale;
    public string[] attributes;

    public Vector3 GetScale(Vector3 fallback)
    {
        if (scale == null || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            return fallback;
        return scale.ToVector3();
    }
}
