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

        GameObject entityObject;
        if (string.Equals(definition.visual, "prefab", StringComparison.OrdinalIgnoreCase))
        {
            entityObject = EntityPrefabView.Create(
                definition,
                state.UnitId,
                state.TeamId,
                state.Solid);
        }
        else if (string.Equals(definition.kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase))
        {
            entityObject = BuildingEntityView.Create(
                definition,
                state.UnitId,
                state.TeamId,
                state.Solid);
        }
        else
        {
            entityObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            entityObject.name = $"{definition.name} {state.UnitId} - Equipo {state.TeamId}";
            entityObject.transform.localScale = definition.GetScale(new Vector3(0.8f, 1f, 0.8f));
            ConfigureQueryCollider(entityObject, state.Solid);
        }

        NetworkEntityView view = entityObject.AddComponent<NetworkEntityView>();
        view.Initialize(
            state.UnitId,
            state.EntityDefinitionId,
            state.UnitName,
            state.UnitTypeId,
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
            state.OwnerClientId,
            state.TeamId,
            state.ColorId,
            state.Attributes);
        return view;
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
