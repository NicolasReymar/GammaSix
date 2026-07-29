using System;
using UnityEngine;

/// <summary>
/// Crea la representación visual local a partir de un snapshot de entidad.
/// La colisión visual usa Solid del snapshot, que ya incluye atributos y overrides.
/// </summary>
public static class EntityViewFactory
{
    public static NetworkEntityView Create(EntitySnapshotData state)
    {
        EntityDefinition definition = EntityDefinitionRepository.Load(state.EntityDefinitionId);
        if (definition == null)
            return CreateMissing(state);

        bool isAreaEntity = HasAttribute(state.Attributes, EntityAttributeIds.EntityArea);

        GameObject entityObject;
        if (string.Equals(definition.visual, "prefab", StringComparison.OrdinalIgnoreCase))
        {
            entityObject = EntityPrefabView.Create(
                definition,
                state.UnitId,
                state.TeamId,
                state.Solid);
        }
        else if (string.Equals(definition.visual, "aura", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(definition.kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase))
        {
            entityObject = BuildingEntityView.Create(
                definition,
                state.UnitId,
                state.TeamId,
                state.Solid,
                isAreaEntity);
        }
        else
        {
            PrimitiveType primitive = ResolvePrimitive(definition.visual);
            entityObject = GameObject.CreatePrimitive(primitive);
            entityObject.name = $"{definition.name} {state.UnitId} - Equipo {state.TeamId}";
            entityObject.transform.localScale = definition.GetScale(new Vector3(0.8f, 1f, 0.8f));
            ConfigureQueryCollider(entityObject, state.Solid);
        }

        if (isAreaEntity && entityObject.GetComponent<AreaEntityVisual>() == null)
        {
            AreaEntityVisual areaVisual = entityObject.AddComponent<AreaEntityVisual>();
            areaVisual.Configure(definition);
        }

        NetworkEntityView view = entityObject.AddComponent<NetworkEntityView>();
        view.Initialize(
            state.UnitId,
            state.EntityDefinitionId,
            state.UnitName,
            state.UnitTypeId,
            state.OwnerParticipantId,
            state.OwnerClientId,
            state.TeamId,
            state.ColorId,
            state.Attributes);
        return view;
    }

    private static NetworkEntityView CreateMissing(EntitySnapshotData state)
    {
        GameObject entityObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        entityObject.name = $"Missing Entity {state.EntityDefinitionId}";
        ConfigureQueryCollider(entityObject, state.Solid);

        NetworkEntityView view = entityObject.AddComponent<NetworkEntityView>();
        view.Initialize(
            state.UnitId,
            state.EntityDefinitionId,
            state.UnitName,
            state.UnitTypeId,
            state.OwnerParticipantId,
            state.OwnerClientId,
            state.TeamId,
            state.ColorId,
            state.Attributes);
        return view;
    }



    private static bool HasAttribute(string[] attributes, string attributeId)
    {
        if (attributes == null || string.IsNullOrWhiteSpace(attributeId))
            return false;

        foreach (string attribute in attributes)
        {
            if (string.Equals(attribute, attributeId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PrimitiveType ResolvePrimitive(string visual)
    {
        if (string.Equals(visual, "sphere", StringComparison.OrdinalIgnoreCase))
            return PrimitiveType.Sphere;
        if (string.Equals(visual, "cube", StringComparison.OrdinalIgnoreCase))
            return PrimitiveType.Cube;
        if (string.Equals(visual, "cylinder", StringComparison.OrdinalIgnoreCase))
            return PrimitiveType.Cylinder;
        if (string.Equals(visual, "plane", StringComparison.OrdinalIgnoreCase))
            return PrimitiveType.Plane;
        if (string.Equals(visual, "quad", StringComparison.OrdinalIgnoreCase))
            return PrimitiveType.Quad;
        return PrimitiveType.Capsule;
    }

    private static void ConfigureQueryCollider(GameObject entityObject, bool effectiveSolid)
    {
        Collider collider = entityObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = true;
            collider.isTrigger = !effectiveSolid;
        }

        if (!effectiveSolid && entityObject.GetComponent<Rigidbody>() == null)
        {
            Rigidbody body = entityObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }
}
