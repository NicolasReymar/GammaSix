using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Contenedor autoritativo de entidades. Las altas y bajas durante el tick deben
/// pasar por EntityLifecycleService; los métodos Immediate son usados solo por él.
/// </summary>
public sealed class EntityWorld
{
    private readonly Dictionary<int, EntityRuntimeState> entities = new();

    public int Count => entities.Count;
    public IEnumerable<EntityRuntimeState> Values => entities.Values;

    public bool Contains(int entityId) => entities.ContainsKey(entityId);

    public bool TryGet(int entityId, out EntityRuntimeState entity)
    {
        return entities.TryGetValue(entityId, out entity);
    }

    public void AddImmediate(EntityRuntimeState entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (entity.UnitId <= 0)
            throw new InvalidOperationException("El ID runtime debe ser mayor que cero.");
        if (entities.ContainsKey(entity.UnitId))
            throw new InvalidOperationException($"Ya existe la entidad runtime {entity.UnitId}.");

        entities.Add(entity.UnitId, entity);
    }

    public bool RemoveImmediate(int entityId)
    {
        return entities.Remove(entityId);
    }

    /// <summary>Compatibilidad para inicializadores antiguos. No usar durante Update.</summary>
    public void Add(EntityRuntimeState entity) => AddImmediate(entity);

    /// <summary>Compatibilidad temporal. Los sistemas nuevos deben encolar despawn.</summary>
    public bool Remove(int entityId) => RemoveImmediate(entityId);

    public void Clear()
    {
        entities.Clear();
    }

    public IReadOnlyList<EntityRuntimeState> SnapshotValues()
    {
        return entities.Values.ToList();
    }
}
