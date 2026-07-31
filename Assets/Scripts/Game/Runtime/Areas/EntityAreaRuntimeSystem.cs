using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Evalúa áreas usando el estado autoritativo, no colliders de presentación.
/// Aura y trigger comparten esta detección y se distinguen por atributos/reglas.
/// </summary>
public sealed class EntityAreaRuntimeSystem
{
    private readonly EntityWorld world;
    private readonly RuntimeEventBus eventBus;
    private readonly DiplomacyRuntimeService diplomacy;
    private readonly Dictionary<int, HashSet<int>> occupantsByArea = new();
    private readonly Dictionary<long, float> nextStayByPair = new();

    public EntityAreaRuntimeSystem(
        EntityWorld world,
        RuntimeEventBus eventBus,
        DiplomacyRuntimeService diplomacy)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        this.diplomacy = diplomacy ?? throw new ArgumentNullException(nameof(diplomacy));
    }

    public void Update(float elapsedTime)
    {
        IReadOnlyList<EntityRuntimeState> snapshot = world.SnapshotValues();
        HashSet<int> activeAreaIds = new();

        foreach (EntityRuntimeState areaEntity in snapshot)
        {
            EntityAreaRuntimeState area = areaEntity.Area;
            if (area == null || areaEntity.Life?.State == EntityLifeState.Dead)
                continue;

            activeAreaIds.Add(areaEntity.UnitId);
            if (!occupantsByArea.TryGetValue(areaEntity.UnitId, out HashSet<int> previous))
            {
                previous = new HashSet<int>();
                occupantsByArea.Add(areaEntity.UnitId, previous);
            }

            HashSet<int> current = new();
            foreach (EntityRuntimeState candidate in snapshot)
            {
                if (candidate == null || candidate.UnitId == areaEntity.UnitId)
                    continue;
                if (!MatchesFilter(areaEntity, area, candidate))
                    continue;
                if (!Contains(areaEntity, area, candidate))
                    continue;

                current.Add(candidate.UnitId);
                if (!previous.Contains(candidate.UnitId) && area.EmitEnter)
                    Publish(RuntimeEventType.EntityEnteredArea, areaEntity, candidate, elapsedTime);

                if (area.EmitStay)
                {
                    long pairKey = PairKey(areaEntity.UnitId, candidate.UnitId);
                    if (!nextStayByPair.TryGetValue(pairKey, out float nextStay) || elapsedTime >= nextStay)
                    {
                        Publish(RuntimeEventType.EntityStayedInArea, areaEntity, candidate, elapsedTime);
                        nextStayByPair[pairKey] = elapsedTime + Mathf.Max(0.05f, area.StayInterval);
                    }
                }
            }

            if (area.EmitExit)
            {
                foreach (int previousEntityId in previous)
                {
                    if (current.Contains(previousEntityId))
                        continue;

                    world.TryGet(previousEntityId, out EntityRuntimeState previousEntity);
                    Publish(RuntimeEventType.EntityExitedArea, areaEntity, previousEntity, elapsedTime, previousEntityId);
                    nextStayByPair.Remove(PairKey(areaEntity.UnitId, previousEntityId));
                }
            }

            area.OccupantCount = current.Count;
            occupantsByArea[areaEntity.UnitId] = current;
        }

        foreach (int removedAreaId in occupantsByArea.Keys.Where(id => !activeAreaIds.Contains(id)).ToArray())
        {
            foreach (int occupantId in occupantsByArea[removedAreaId])
                nextStayByPair.Remove(PairKey(removedAreaId, occupantId));
            occupantsByArea.Remove(removedAreaId);
        }
    }

    public bool IsEntityInsideArea(int areaEntityId, int entityId)
    {
        return occupantsByArea.TryGetValue(areaEntityId, out HashSet<int> occupants) &&
               occupants.Contains(entityId);
    }

    private void Publish(
        RuntimeEventType type,
        EntityRuntimeState areaEntity,
        EntityRuntimeState candidate,
        float elapsedTime,
        int fallbackEntityId = -1)
    {
        eventBus.Publish(new RuntimeEventContext
        {
            Type = type,
            ElapsedTime = elapsedTime,
            EntityId = candidate?.UnitId ?? fallbackEntityId,
            Entity = candidate,
            AreaEntityId = areaEntity?.UnitId ?? -1,
            AreaEntity = areaEntity,
            ParticipantId = candidate?.OwnerParticipantId ?? -1
        });
    }

    private bool MatchesFilter(
        EntityRuntimeState areaEntity,
        EntityAreaRuntimeState area,
        EntityRuntimeState candidate)
    {
        if (candidate.Life?.State == EntityLifeState.Dead)
            return false;

        if (candidate.Attributes != null && candidate.Attributes.Has(EntityAttributeIds.EntityArea))
            return false;

        if (area.RequiredAttributes != null)
        {
            foreach (string required in area.RequiredAttributes)
            {
                if (!string.IsNullOrWhiteSpace(required) &&
                    (candidate.Attributes == null || !candidate.Attributes.Has(required)))
                    return false;
            }
        }

        if (area.ExcludedAttributes != null)
        {
            foreach (string excluded in area.ExcludedAttributes)
            {
                if (!string.IsNullOrWhiteSpace(excluded) &&
                    candidate.Attributes != null && candidate.Attributes.Has(excluded))
                    return false;
            }
        }

        string relationship = string.IsNullOrWhiteSpace(area.Relationship)
            ? EntityAreaRelationships.All
            : area.Relationship.Trim().ToLowerInvariant();
        if (relationship == EntityAreaRelationships.All)
            return true;
        if (relationship == EntityAreaRelationships.Owner)
            return areaEntity.OwnerParticipantId > 0 && candidate.OwnerParticipantId == areaEntity.OwnerParticipantId;
        if (relationship == EntityAreaRelationships.Neutral)
            return candidate.TeamId == 0;
        if (relationship == EntityAreaRelationships.Ally)
            return diplomacy.GetStance(areaEntity.TeamId, candidate.TeamId) == DiplomacyStance.Ally;
        if (relationship == EntityAreaRelationships.Enemy)
            return diplomacy.GetStance(areaEntity.TeamId, candidate.TeamId) == DiplomacyStance.Enemy;
        return true;
    }

    private static bool Contains(
        EntityRuntimeState areaEntity,
        EntityAreaRuntimeState area,
        EntityRuntimeState candidate)
    {
        Vector2 delta = new(
            candidate.Position.x - areaEntity.Position.x,
            candidate.Position.z - areaEntity.Position.z);
        float candidateRadius = Mathf.Max(candidate.BoundsSize.x, candidate.BoundsSize.z) * 0.5f;

        if (string.Equals(area.Shape, EntityAreaShapes.Rectangle, StringComparison.OrdinalIgnoreCase))
        {
            Vector3 size = area.Size;
            float halfX = Mathf.Max(0.01f, size.x * 0.5f) + candidateRadius;
            float halfZ = Mathf.Max(0.01f, size.z * 0.5f) + candidateRadius;
            return Mathf.Abs(delta.x) <= halfX && Mathf.Abs(delta.y) <= halfZ;
        }

        float effectiveRadius = Mathf.Max(0.01f, area.Radius) + candidateRadius;
        return delta.sqrMagnitude <= effectiveRadius * effectiveRadius;
    }

    private static long PairKey(int areaId, int entityId)
    {
        return ((long)areaId << 32) ^ (uint)entityId;
    }
}
