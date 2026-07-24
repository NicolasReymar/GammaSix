using System;
using System.Collections.Generic;
using System.Linq;

[Serializable]
public class EntityAttributeSet
{
    private readonly HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Values => values;

    public EntityAttributeSet() { }

    public EntityAttributeSet(IEnumerable<string> attributes)
    {
        AddRange(attributes);
    }

    public bool Has(string attributeId)
    {
        return !string.IsNullOrWhiteSpace(attributeId) && values.Contains(attributeId.Trim());
    }

    public bool Add(string attributeId)
    {
        return !string.IsNullOrWhiteSpace(attributeId) && values.Add(attributeId.Trim());
    }

    public void AddRange(IEnumerable<string> attributes)
    {
        if (attributes == null) return;
        foreach (string attribute in attributes)
            Add(attribute);
    }

    public bool Remove(string attributeId)
    {
        return !string.IsNullOrWhiteSpace(attributeId) && values.Remove(attributeId.Trim());
    }

    public string[] ToArray()
    {
        return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
