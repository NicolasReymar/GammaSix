using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class EntityRuntimeFactory
{
    public static EntityRuntimeState Create(
        int runtimeId,
        EntityDefinition definition,
        IEnumerable<string> instanceAttributes,
        ulong ownerClientId,
        int teamId,
        int colorId,
        Vector3 position)
    {
        int maxHealth = definition.maxHealth > 0 ? definition.maxHealth : 1;
        EntityAttributeSet attributes = EntityAttributeResolver.Resolve(
            definition.attributes,
            instanceAttributes);

        EntityRuntimeState state = new()
        {
            UnitId = runtimeId,
            EntityDefinitionId = definition.id,
            UnitName = string.IsNullOrWhiteSpace(definition.name) ? definition.id : definition.name,
            UnitTypeId = definition.kind,
            Attributes = attributes,
            OwnerClientId = ownerClientId,
            TeamId = teamId,
            ColorId = colorId,
            Position = position,
            Destination = position,
            Health = maxHealth,
            MaxHealth = maxHealth,
            MoveSpeed = definition.moveSpeed,
            Solid = EntityPhysicsRules.IsSolid(definition, attributes),
            BoundsSize = definition.GetCollisionSize(new Vector3(0.8f, 1f, 0.8f))
        };

        ApplySpecializedState(state, definition);
        return state;
    }

    public static void Reconfigure(EntityRuntimeState state, EntityDefinition definition)
    {
        EntityAttributeSet attributes = EntityAttributeResolver.Resolve(definition.attributes);

        state.EntityDefinitionId = definition.id;
        state.UnitName = string.IsNullOrWhiteSpace(definition.name) ? definition.id : definition.name;
        state.UnitTypeId = definition.kind;
        state.Attributes = attributes;
        state.MaxHealth = Mathf.Max(1, definition.maxHealth);
        state.Health = state.MaxHealth;
        state.MoveSpeed = definition.moveSpeed;
        state.Solid = EntityPhysicsRules.IsSolid(definition, attributes);
        state.BoundsSize = definition.GetCollisionSize(new Vector3(0.8f, 1f, 0.8f));
        state.Destination = state.Position;
        state.InteractionTargetUnitId = -1;
        ApplySpecializedState(state, definition);
    }

    private static void ApplySpecializedState(EntityRuntimeState state, EntityDefinition definition)
    {
        state.Resource = CreateResourceState(definition.resource);
        state.Worker = CreateWorkerState(definition.worker);
    }

    private static ResourceRuntimeState CreateResourceState(ResourceEntityDefinition definition)
    {
        if (definition == null)
            return null;

        ResourceRuntimeState state = new()
        {
            Infinite = definition.infinite,
            OnResourcesSpentEntityId = definition.onResourcesSpentEntityId,
            ResourceTier = Mathf.Max(0, definition.resourceTier),
            ExtractionTools = definition.extractionTools ?? Array.Empty<string>(),
            InteractionRange = Mathf.Max(0f, definition.interactionRange),
            AmountPerExtraction = Mathf.Max(1, definition.amountPerExtraction)
        };

        if (definition.resources != null)
        {
            state.Resources = definition.resources
                .Where(resource => resource != null && !string.IsNullOrWhiteSpace(resource.resourceId))
                .Select(resource => new ResourceAmountRuntimeState
                {
                    ResourceId = resource.resourceId,
                    Amount = Mathf.Max(0, resource.amount)
                })
                .ToList();
        }

        return state;
    }

    private static WorkerRuntimeState CreateWorkerState(WorkerEntityDefinition definition)
    {
        if (definition == null)
            return null;

        return new WorkerRuntimeState
        {
            ExtractionTime = Mathf.Max(0.05f, definition.extractionTime),
            RepeatExtraction = definition.repeatExtraction,
            ResourceName = definition.resourceName,
            WorkerTier = Mathf.Max(0, definition.workerTier),
            Tools = definition.tools ?? Array.Empty<string>(),
            InteractionRange = Mathf.Max(0f, definition.interactionRange)
        };
    }
}
