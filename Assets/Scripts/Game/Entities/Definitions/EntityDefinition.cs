using System;
using UnityEngine;

public static class EntityKinds
{
    public const string Unit = "unit";
    public const string Building = "building";
    public const string Environment = "environment";
}

[Serializable]
public class EntityDefinition
{
    public string id;
    public string name;
    public string description;
    public string kind = EntityKinds.Unit;
    public string entityType;
    public int maxHealth = 100;
    public float moveSpeed = 0f;
    public bool solid;
    public string visual = "capsule";
    public string prefabResource;
    public ScenarioVector3 scale;
    public ScenarioVector3 visualSize;
    public ScenarioVector3 collisionSize;
    public float groundOffset = -1f;
    public string[] attributes;
    public ResourceEntityDefinition resource;
    public WorkerEntityDefinition worker;

    public Vector3 GetScale(Vector3 fallback)
    {
        if (scale == null || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            return fallback;
        return scale.ToVector3();
    }

    public Vector3 GetCollisionSize(Vector3 fallback)
    {
        if (collisionSize == null || collisionSize.x <= 0f || collisionSize.y <= 0f || collisionSize.z <= 0f)
            return GetScale(fallback);
        return collisionSize.ToVector3();
    }

    public Vector3 GetPrefabTargetSize(Vector3 fallback)
    {
        if (visualSize != null && visualSize.x > 0f && visualSize.y > 0f && visualSize.z > 0f)
            return visualSize.ToVector3();

        // Las definiciones anteriores que ya tengan collisionSize siguen
        // normalizándose correctamente aunque aún no incluyan visualSize.
        if (collisionSize != null && collisionSize.x > 0f && collisionSize.y > 0f && collisionSize.z > 0f)
            return collisionSize.ToVector3();

        return GetScale(fallback);
    }
}
