using System;
using System.Collections.Generic;

/// <summary>
/// Capacidades de formato/runtime disponibles en esta versión del ejecutable.
/// Los paquetes solo pueden exigir identificadores registrados aquí.
/// </summary>
public static class GameContentPackageFeatureCatalog
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "content.packages.v1",
        "runtime.participants.v1",
        "runtime.command-bus.v1",
        "runtime.dynamic-entities.v1",
        "runtime.match-state.v1",
        "runtime.entity-areas.v1",
        "runtime.rules.v1",
        "runtime.entity-life.v1",
        "runtime.combat.v1",
        "runtime.death-outcomes.v1",
        "runtime.declarative-actions.v2",
        "runtime.participant-variables.v1",
        "runtime.event-snapshots.v1",
        "runtime.channels.v1",
        "runtime.wave-mode.v1",
        "runtime.headless-controllers.v1",
        "runtime.diplomacy.v1",
        "ui.diplomacy.v1"
    };

    public static bool IsSupported(string featureId)
    {
        return !string.IsNullOrWhiteSpace(featureId) && Supported.Contains(featureId.Trim());
    }
}
