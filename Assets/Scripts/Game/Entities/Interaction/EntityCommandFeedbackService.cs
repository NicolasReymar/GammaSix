/// <summary>
/// Feedback visual local para cualquier orden contextual enviada por el jugador.
/// El color representa la relación del objetivo con el jugador local.
/// </summary>
public static class EntityCommandFeedbackService
{
    public static void AcknowledgeTarget(NetworkEntityView target)
    {
        if (target == null)
            return;

        target.PlayInteractionPulse(EntityRelationshipVisuals.GetColor(target));
    }
}
