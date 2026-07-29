using System;

/// <summary>
/// Configuración del resultado posterior a una muerte confirmada.
/// Despawn directo y muerte son conceptos distintos:
/// - un despawn retira una entidad por una regla/sistema;
/// - una muerte cambia su estado a Dead y después aplica este resultado.
/// </summary>
[Serializable]
public sealed class EntityLifeDefinition
{
    /// <summary>
    /// remain: conserva la entidad muerta.
    /// despawn: retira la entidad muerta tras el retraso.
    /// replace: sustituye la entidad muerta por otra definición registrada.
    /// Si está vacío, se usan removeOnDeath/deathRemovalDelay por compatibilidad.
    /// </summary>
    public string deathOutcome;

    /// <summary>
    /// Retraso antes de aplicar deathOutcome. -1 usa deathRemovalDelay legado.
    /// </summary>
    public float deathOutcomeDelay = -1f;

    /// <summary>
    /// Entidad que aparecerá cuando deathOutcome sea replace.
    /// </summary>
    public string deathReplacementEntityId;

    /// <summary>
    /// Conserva participante, equipo y color en la entidad resultante.
    /// </summary>
    public bool deathReplacementInheritsOwner = true;

    // Compatibilidad con definiciones anteriores de la fase 6.
    public bool removeOnDeath = true;
    public float deathRemovalDelay = 0.75f;
}
