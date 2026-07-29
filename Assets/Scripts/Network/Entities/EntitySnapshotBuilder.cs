using System.Linq;

/// <summary>
/// Convierte el mundo autoritativo en payloads de presentación/red.
/// </summary>
public static class EntitySnapshotBuilder
{
    public static EntitySnapshotPayload Build(EntityWorld world)
    {
        EntitySnapshotPayload snapshot = new();
        if (world == null)
            return snapshot;

        foreach (EntityRuntimeState unit in world.Values.OrderBy(item => item.UnitId))
            snapshot.Units.Add(BuildSingle(unit));

        return snapshot;
    }

    public static EntitySnapshotData BuildSingle(EntityRuntimeState unit)
    {
        if (unit == null)
            return null;

        return new EntitySnapshotData
        {
            UnitId = unit.UnitId,
            EntityDefinitionId = unit.EntityDefinitionId,
            ScenarioInstanceId = unit.ScenarioInstanceId,
            UnitName = unit.UnitName,
            UnitTypeId = unit.UnitTypeId,
            OwnerParticipantId = unit.OwnerParticipantId,
            OwnerClientId = unit.OwnerClientId,
            TeamId = unit.TeamId,
            ColorId = unit.ColorId,
            X = unit.Position.x,
            Y = unit.Position.y,
            Z = unit.Position.z,
            Health = unit.Health,
            MaxHealth = unit.MaxHealth,
            Solid = unit.Solid,
            Attributes = unit.Attributes?.ToArray(),
            ResourceInfinite = unit.Resource?.Infinite ?? false,
            ResourceTier = unit.Resource?.ResourceTier ?? 0,
            Resources = unit.Resource?.Resources?
                .Select(resource => new ResourceSnapshotData
                {
                    ResourceId = resource.ResourceId,
                    Amount = resource.Amount
                })
                .ToArray(),
            WorkerResourceName = unit.Worker?.CarriedResourceName,
            WorkerCarriedAmount = unit.Worker?.CarriedResourceAmount ?? 0,
            WorkerIsExtracting = unit.Worker?.IsExtracting ?? false,
            AreaOccupantCount = unit.Area?.OccupantCount ?? 0,
            LifeState = (unit.Life?.State ?? EntityLifeState.Alive).ToString(),
            ActivityState = (unit.Status?.Activity ?? EntityActivityState.Idle).ToString(),
            InCombat = unit.Status?.InCombat ?? false,
            IsUnderAttack = unit.Status?.IsUnderAttack ?? false,
            ActivityDetail = unit.Status?.ActivityDetail,
            HasAttack = unit.Attack != null,
            AttackBaseDamage = unit.Attack?.BaseDamage ?? 0,
            BaseAttackSpeed = unit.Attack?.BaseAttackSpeed ?? 0f,
            AttackSpeedMultiplier = unit.Attack?.AttackSpeedMultiplier ?? 1f,
            AttackTime = unit.Attack?.AttackTime ?? 0f,
            RecoveryTime = unit.Attack?.RecoveryTime ?? 0f,
            AttackRange = unit.Attack?.AttackRange ?? 0f,
            AttackDelivery = unit.Attack?.Delivery,
            AttackDamageType = unit.Attack?.DamageType,
            AttackTargetEntityId = unit.Attack?.TargetEntityId ?? -1,
            AttackPhase = (unit.Attack?.Phase ?? EntityAttackPhase.None).ToString()
        };
    }
}
