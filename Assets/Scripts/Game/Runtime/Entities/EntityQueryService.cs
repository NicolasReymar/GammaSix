using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Consulta inmutable del mundo para reglas, oleadas, bots y objetivos futuros.
/// </summary>
public sealed class EntityQuery
{
    public int? OwnerParticipantId;
    public int? TeamId;
    public string EntityDefinitionId;
    public string ScenarioInstanceId;
    public string[] RequiredAttributes;
    public string[] ExcludedAttributes;
}

public sealed class EntityQueryService
{
    private readonly EntityWorld world;

    public EntityQueryService(EntityWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public IReadOnlyList<EntityRuntimeState> Execute(EntityQuery query)
    {
        if (query == null)
            return world.SnapshotValues();

        IEnumerable<EntityRuntimeState> result = world.Values;
        if (query.OwnerParticipantId.HasValue)
            result = result.Where(item => item.OwnerParticipantId == query.OwnerParticipantId.Value);
        if (query.TeamId.HasValue)
            result = result.Where(item => item.TeamId == query.TeamId.Value);
        if (!string.IsNullOrWhiteSpace(query.EntityDefinitionId))
        {
            result = result.Where(item => string.Equals(
                item.EntityDefinitionId,
                query.EntityDefinitionId,
                StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.ScenarioInstanceId))
        {
            result = result.Where(item => string.Equals(
                item.ScenarioInstanceId,
                query.ScenarioInstanceId,
                StringComparison.OrdinalIgnoreCase));
        }
        if (query.RequiredAttributes != null)
        {
            foreach (string attribute in query.RequiredAttributes.Where(value => !string.IsNullOrWhiteSpace(value)))
                result = result.Where(item => item.Attributes != null && item.Attributes.Has(attribute));
        }
        if (query.ExcludedAttributes != null)
        {
            foreach (string attribute in query.ExcludedAttributes.Where(value => !string.IsNullOrWhiteSpace(value)))
                result = result.Where(item => item.Attributes == null || !item.Attributes.Has(attribute));
        }

        return result.OrderBy(item => item.UnitId).ToList();
    }

    public EntityRuntimeState FindScenarioInstance(string scenarioInstanceId)
    {
        return Execute(new EntityQuery { ScenarioInstanceId = scenarioInstanceId }).FirstOrDefault();
    }
}
