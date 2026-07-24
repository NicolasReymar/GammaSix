using System.Collections.Generic;

public static class EntityAttributeCatalog
{
    public static EntityAttributeSet Create(IEnumerable<string> definitionAttributes)
    {
        EntityAttributeSet result = new();
        result.AddRange(definitionAttributes);

        if (result.Has(EntityAttributeIds.Heroic))
            result.Add(EntityAttributeIds.ThirdPersonCamera);

        return result;
    }
}
