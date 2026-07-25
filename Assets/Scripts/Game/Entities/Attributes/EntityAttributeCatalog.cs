using System.Collections.Generic;

public static class EntityAttributeCatalog
{
    public static EntityAttributeSet Create(IEnumerable<string> definitionAttributes)
    {
        return Create(definitionAttributes, null);
    }

    public static EntityAttributeSet Create(
        IEnumerable<string> definitionAttributes,
        IEnumerable<string> instanceAttributes)
    {
        EntityAttributeSet result = new();
        result.AddRange(definitionAttributes);
        result.AddRange(instanceAttributes);

        // Heroic es una capacidad adicional del humanoide, no un tipo distinto.
        // Toda entidad heroica hereda automáticamente la cámara en tercera persona.
        if (result.Has(EntityAttributeIds.Heroic))
            result.Add(EntityAttributeIds.ThirdPersonCamera);

        return result;
    }
}
