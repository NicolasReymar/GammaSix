using System.Collections.Generic;

/// <summary>
/// Combina atributos de definición e instancia y aplica dependencias derivadas.
/// </summary>
public static class EntityAttributeResolver
{
    private static readonly Dictionary<string, string[]> Dependencies = new()
    {
        [EntityAttributeIds.Heroic] = new[] { EntityAttributeIds.ThirdPersonCamera }
    };

    public static EntityAttributeSet Resolve(IEnumerable<string> definitionAttributes)
    {
        return Resolve(definitionAttributes, null);
    }

    public static EntityAttributeSet Resolve(
        IEnumerable<string> definitionAttributes,
        IEnumerable<string> instanceAttributes)
    {
        EntityAttributeSet result = new();
        result.AddRange(definitionAttributes);
        result.AddRange(instanceAttributes);

        bool addedDependency;
        do
        {
            addedDependency = false;
            foreach (KeyValuePair<string, string[]> rule in Dependencies)
            {
                if (!result.Has(rule.Key))
                    continue;

                foreach (string dependency in rule.Value)
                {
                    if (result.Has(dependency))
                        continue;
                    result.Add(dependency);
                    addedDependency = true;
                }
            }
        }
        while (addedDependency);

        return result;
    }
}
