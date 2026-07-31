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
        int ownerParticipantId,
        ulong ownerClientId,
        int teamId,
        int colorId,
        Vector3 position)
    {
        int maxHealth = definition.maxHealth > 0 ? definition.maxHealth : 1;
        EntityAttributeSet attributes = EntityAttributeResolver.Resolve(
            definition.attributes,
            instanceAttributes);
        ApplyDefinitionDerivedAttributes(definition, attributes);

        EntityRuntimeState state = new()
        {
            UnitId = runtimeId,
            EntityDefinitionId = definition.id,
            UnitName = string.IsNullOrWhiteSpace(definition.name) ? definition.id : definition.name,
            UnitTypeId = definition.kind,
            Attributes = attributes,
            OwnerParticipantId = ownerParticipantId,
            OwnerClientId = ownerClientId,
            TeamId = teamId,
            ColorId = colorId,
            Position = position,
            Destination = position,
            Health = maxHealth,
            MaxHealth = maxHealth,
            MoveSpeed = definition.moveSpeed,
            Solid = EntityPhysicsRules.IsSolid(definition, attributes),
            BoundsSize = definition.GetCollisionSize(new Vector3(0.8f, 1f, 0.8f)),
            Status = new EntityStatusRuntimeState(),
            Navigation = new EntityNavigationRuntimeState()
        };

        ApplySpecializedState(state, definition);
        return state;
    }

    public static void Reconfigure(EntityRuntimeState state, EntityDefinition definition)
    {
        EntityAttributeSet attributes = EntityAttributeResolver.Resolve(definition.attributes);
        ApplyDefinitionDerivedAttributes(definition, attributes);

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
        state.Status = new EntityStatusRuntimeState();
        state.Navigation = new EntityNavigationRuntimeState();
        ApplySpecializedState(state, definition);
    }

    private static void ApplyDefinitionDerivedAttributes(
        EntityDefinition definition,
        EntityAttributeSet attributes)
    {
        if (definition == null || attributes == null)
            return;

        // Un bloque area deserializado no basta para clasificar una entidad como área.
        // La clasificación debe venir de sus atributos (o del visual aura legado).
        bool declaredArea = attributes.Has(EntityAttributeIds.EntityArea) ||
                            string.Equals(definition.visual, "aura", StringComparison.OrdinalIgnoreCase);
        if (declaredArea)
        {
            attributes.Add(EntityAttributeIds.EntityArea);

            if (!attributes.Has(EntityAttributeIds.AreaRectangle) &&
                !attributes.Has(EntityAttributeIds.AreaCircular))
            {
                if (definition.area != null &&
                    string.Equals(definition.area.shape, EntityAreaShapes.Rectangle, StringComparison.OrdinalIgnoreCase))
                {
                    attributes.Add(EntityAttributeIds.AreaRectangle);
                }
                else
                {
                    attributes.Add(EntityAttributeIds.AreaCircular);
                }
            }
        }

        if (definition.attack != null &&
            string.Equals(
                EntityCombatRules.NormalizeDelivery(definition.attack.delivery),
                EntityAttackDeliveryTypes.Melee,
                StringComparison.OrdinalIgnoreCase))
        {
            attributes.Add(EntityAttributeIds.Melee);
        }
    }

    private static void ApplySpecializedState(EntityRuntimeState state, EntityDefinition definition)
    {
        state.Resource = CreateResourceState(definition.resource);
        state.Worker = CreateWorkerState(definition.worker);
        state.Area = CreateAreaState(definition, state.Attributes);
        state.Attack = CreateAttackState(definition.attack);
        state.Life = CreateLifeState(definition.life);
    }

    private static EntityAreaRuntimeState CreateAreaState(
        EntityDefinition definition,
        EntityAttributeSet attributes)
    {
        EntityAreaDefinition source = definition.area;
        bool legacyAura = source == null &&
                          (string.Equals(definition.visual, "aura", StringComparison.OrdinalIgnoreCase) ||
                           (attributes != null && attributes.Has(EntityAttributeIds.AuraTrigger)));
        bool declaredArea = attributes != null && attributes.Has(EntityAttributeIds.EntityArea);
        if (!legacyAura && !declaredArea)
            return null;

        Vector3 scale = definition.GetScale(new Vector3(2f, 0.04f, 2f));
        EntityAreaRuntimeState state = new();
        if (source == null)
        {
            state.Shape = EntityAreaShapes.Circle;
            state.Radius = Mathf.Max(0.05f, Mathf.Max(scale.x, scale.z) * 0.5f);
            state.Size = new Vector3(scale.x, 1f, scale.z);
            state.Relationship = EntityAreaRelationships.All;
            state.RequiredAttributes = new[] { EntityAttributeIds.Humanoid };
            state.ExcludedAttributes = new[] { EntityAttributeIds.EntityArea };
            state.EmitEnter = true;
            state.EmitStay = false;
            state.EmitExit = true;
            state.StayInterval = 1f;
            state.Visible = true;
            return state;
        }

        state.Shape = string.IsNullOrWhiteSpace(source.shape)
            ? EntityAreaShapes.Circle
            : source.shape.Trim();
        state.Radius = source.radius > 0f
            ? source.radius
            : Mathf.Max(0.05f, Mathf.Max(scale.x, scale.z) * 0.5f);
        state.Size = source.size != null
            ? source.size.ToVector3()
            : new Vector3(scale.x, 1f, scale.z);
        if (state.Size.x <= 0f || state.Size.z <= 0f)
            state.Size = new Vector3(scale.x, 1f, scale.z);
        state.Relationship = string.IsNullOrWhiteSpace(source.relationship)
            ? EntityAreaRelationships.All
            : source.relationship.Trim();
        state.RequiredAttributes = source.requiredAttributes ?? Array.Empty<string>();
        state.ExcludedAttributes = source.excludedAttributes ?? Array.Empty<string>();
        state.EmitEnter = source.emitEnter;
        state.EmitStay = source.emitStay;
        state.EmitExit = source.emitExit;
        state.StayInterval = Mathf.Max(0.05f, source.stayInterval);
        state.Visible = source.visible;
        return state;
    }


    private static EntityAttackRuntimeState CreateAttackState(EntityAttackDefinition definition)
    {
        if (definition == null)
            return null;

        return new EntityAttackRuntimeState
        {
            Delivery = string.IsNullOrWhiteSpace(definition.delivery)
                ? EntityAttackDeliveryTypes.Melee
                : definition.delivery.Trim().ToLowerInvariant(),
            DamageType = string.IsNullOrWhiteSpace(definition.damageType)
                ? "physical"
                : definition.damageType.Trim().ToLowerInvariant(),
            BaseDamage = Mathf.Max(0, definition.baseDamage),
            BaseAttackSpeed = Mathf.Max(0.05f, definition.baseAttackSpeed),
            AttackSpeedMultiplier = 1f,
            AttackTime = Mathf.Max(0f, definition.attackTime),
            RecoveryTime = Mathf.Max(0f, definition.recoveryTime),
            AttackRange = Mathf.Max(0.05f, definition.attackRange),
            ChaseTarget = definition.chaseTarget
        };
    }

    private static EntityLifeRuntimeState CreateLifeState(EntityLifeDefinition definition)
    {
        EntityDeathOutcome outcome = ResolveDeathOutcome(definition);
        float configuredDelay = definition?.deathOutcomeDelay ?? -1f;
        float legacyDelay = definition?.deathRemovalDelay ?? 0.75f;

        return new EntityLifeRuntimeState
        {
            State = EntityLifeState.Alive,
            DeathOutcome = outcome,
            DeathOutcomeDelay = Mathf.Max(0f, configuredDelay >= 0f ? configuredDelay : legacyDelay),
            DeathReplacementEntityId = string.IsNullOrWhiteSpace(definition?.deathReplacementEntityId)
                ? null
                : definition.deathReplacementEntityId.Trim(),
            DeathReplacementInheritsOwner = definition?.deathReplacementInheritsOwner ?? true
        };
    }

    private static EntityDeathOutcome ResolveDeathOutcome(EntityLifeDefinition definition)
    {
        if (definition != null &&
            Enum.TryParse(definition.deathOutcome, true, out EntityDeathOutcome configured))
        {
            return configured;
        }

        // Compatibilidad con la primera versión de la fase 6.
        return definition == null || definition.removeOnDeath
            ? EntityDeathOutcome.Despawn
            : EntityDeathOutcome.Remain;
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
