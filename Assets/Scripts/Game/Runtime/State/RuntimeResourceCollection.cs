using System;
using System.Collections.Generic;
using System.Linq;

public sealed class RuntimeResourceChangedEvent
{
    public string Scope { get; }
    public int OwnerId { get; }
    public string ResourceId { get; }
    public int PreviousAmount { get; }
    public int CurrentAmount { get; }
    public int Delta => CurrentAmount - PreviousAmount;

    public RuntimeResourceChangedEvent(
        string scope,
        int ownerId,
        string resourceId,
        int previousAmount,
        int currentAmount)
    {
        Scope = scope;
        OwnerId = ownerId;
        ResourceId = resourceId;
        PreviousAmount = previousAmount;
        CurrentAmount = currentAmount;
    }
}

/// <summary>
/// Colección mutable y extensible de recursos. No conoce oro, madera ni otros
/// nombres concretos: todos se identifican por string y se mantienen >= 0.
/// </summary>
public sealed class RuntimeResourceCollection
{
    private readonly Dictionary<string, int> values =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string scope;
    private readonly int ownerId;

    public event Action<RuntimeResourceChangedEvent> Changed;

    public RuntimeResourceCollection(string scope, int ownerId)
    {
        this.scope = string.IsNullOrWhiteSpace(scope) ? "unknown" : scope.Trim();
        this.ownerId = ownerId;
    }

    public int Get(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return 0;
        return values.TryGetValue(resourceId.Trim(), out int amount) ? amount : 0;
    }

    public bool Contains(string resourceId)
    {
        return !string.IsNullOrWhiteSpace(resourceId) && values.ContainsKey(resourceId.Trim());
    }

    public void Set(string resourceId, int amount, bool notify = true)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return;

        string key = resourceId.Trim();
        int safeAmount = Math.Max(0, amount);
        int previous = Get(key);
        values[key] = safeAmount;
        if (notify && previous != safeAmount)
        {
            Changed?.Invoke(new RuntimeResourceChangedEvent(
                scope,
                ownerId,
                key,
                previous,
                safeAmount));
        }
    }

    public int Add(string resourceId, int delta)
    {
        if (string.IsNullOrWhiteSpace(resourceId) || delta == 0)
            return Get(resourceId);

        int next = Math.Max(0, Get(resourceId) + delta);
        Set(resourceId, next);
        return next;
    }

    public bool TrySpend(string resourceId, int amount)
    {
        if (amount <= 0)
            return true;

        int current = Get(resourceId);
        if (current < amount)
            return false;

        Set(resourceId, current - amount);
        return true;
    }

    public IReadOnlyDictionary<string, int> Snapshot()
    {
        return values
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }
}
