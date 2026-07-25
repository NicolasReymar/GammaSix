using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Almacena los identificadores de los grupos Ctrl+1/2/3 sin depender de input
/// ni de la implementación concreta de selección.
/// </summary>
public sealed class ControlGroupService
{
    private readonly int maxEntities;
    private readonly Dictionary<int, List<int>> groups = new();

    public ControlGroupService(int maxEntities)
    {
        this.maxEntities = maxEntities;
    }

    public int Store(int groupNumber, IEnumerable<NetworkEntityView> entities)
    {
        List<int> ids = entities
            .Where(entity => entity != null)
            .Select(entity => entity.UnitId)
            .Take(maxEntities)
            .ToList();
        groups[groupNumber] = ids;
        return ids.Count;
    }

    public IReadOnlyList<int> Recall(int groupNumber)
    {
        return groups.TryGetValue(groupNumber, out List<int> ids)
            ? ids
            : System.Array.Empty<int>();
    }
}
