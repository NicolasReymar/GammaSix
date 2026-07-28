/// <summary>
/// Centraliza la evaluación efectiva de las propiedades físicas de una entidad.
/// Las definiciones conservan el campo legacy "solid", mientras que los atributos
/// de instancia pueden modificarlo sin alterar el archivo base de la entidad.
/// </summary>
public static class EntityPhysicsRules
{
    /// <summary>
    /// physics.not_solid prevalece sobre cualquier declaración de solidez.
    /// Si el atributo está anulado por la partida, se vuelve a utilizar la solidez
    /// declarada por el JSON o por physics.solid.
    /// </summary>
    public static bool IsSolid(EntityDefinition definition, EntityAttributeSet attributes)
    {
        bool declaredSolid = (definition != null && definition.solid) ||
                             (attributes != null && attributes.Has(EntityAttributeIds.Solid));

        return declaredSolid && !IsNotSolid(attributes);
    }

    public static bool IsNotSolid(EntityAttributeSet attributes)
    {
        return EntityAttributeOverrideService.IsEffectivelyBlocked(
            attributes,
            EntityAttributeIds.NotSolid);
    }
}
