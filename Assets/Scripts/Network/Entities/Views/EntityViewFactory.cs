using System;
using UnityEngine;

/// <summary>
/// Crea la representación visual local a partir de un snapshot de entidad.
/// </summary>
public static class EntityViewFactory
{
    public static NetworkEntityView Create(EntitySnapshotData state)
    {
        EntityDefinition definition = EntityDefinitionRepository.Load(state.EntityDefinitionId);
        if (definition == null)
            return CreateMissing(state);

        GameObject entityObject;
        if (string.Equals(definition.kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase))
        {
            entityObject = BuildingEntityView.Create(definition, state.UnitId, state.TeamId);
        }
        else
        {
            entityObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            entityObject.name = $"{definition.name} {state.UnitId} - Equipo {state.TeamId}";
            entityObject.transform.localScale = definition.GetScale(new Vector3(0.8f, 1f, 0.8f));
            Collider collider = entityObject.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = definition.solid;
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
}
