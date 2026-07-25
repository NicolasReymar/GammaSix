using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Construye la representación agrupada de una selección sin depender de input,
/// red ni UI. Los heroicos se mantienen individuales; el resto se agrupa por
/// definición y usa como representante la entidad con mayor vida.
/// </summary>
public static class SelectionGroupBuilder
{
    public static IReadOnlyList<SelectionInspectionGroup> Build(
        IEnumerable<NetworkEntityView> selection,
        Func<NetworkEntityView, bool> filter = null)
    {
        IEnumerable<NetworkEntityView> source = selection?.Where(view => view != null)
            ?? Enumerable.Empty<NetworkEntityView>();

        if (filter != null)
            source = source.Where(filter);

        List<NetworkEntityView> entities = source.ToList();
        if (entities.Count == 0)
            return Array.Empty<SelectionInspectionGroup>();

        List<SelectionInspectionGroup> groups = new();

        foreach (NetworkEntityView heroic in entities
                     .Where(view => view.HasAttribute(EntityAttributeIds.Heroic))
                     .OrderBy(view => view.UnitId))
        {
            groups.Add(new SelectionInspectionGroup(
                $"heroic:{heroic.UnitId}",
                heroic.UnitName,
                true,
                new[] { heroic },
                heroic));
        }

        foreach (IGrouping<string, NetworkEntityView> group in entities
                     .Where(view => !view.HasAttribute(EntityAttributeIds.Heroic))
                     .GroupBy(GetGroupingKey)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<NetworkEntityView> members = group.OrderBy(view => view.UnitId).ToList();
            NetworkEntityView representative = members
                .OrderByDescending(view => view.Health)
                .ThenByDescending(view => view.MaxHealth)
                .ThenBy(view => view.UnitId)
                .First();

            groups.Add(new SelectionInspectionGroup(
                $"group:{group.Key}",
                representative.UnitName,
                false,
                members,
                representative));
        }

        return groups;
    }

    private static string GetGroupingKey(NetworkEntityView view)
    {
        return string.IsNullOrWhiteSpace(view.EntityDefinitionId)
            ? view.UnitTypeId
            : view.EntityDefinitionId;
    }
}
