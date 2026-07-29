using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Combina los perfiles incorporados en GammaSix con los perfiles declarados
/// por el escenario seleccionado. Por ahora el juego base registra el
/// comandante normal; los perfiles del escenario quedan preparados para el
/// futuro paquete importable de Kodo Tag.
/// </summary>
public static class HeadlessProfileCatalog
{
    public const string NormalGameModeId = "base:game-mode.normal";
    public const string NormalCommanderProfileId = "base:headless.commander.normal";

    public static IReadOnlyList<HeadlessProfileDefinition> GetAvailableProfiles(ScenarioDefinition scenario)
    {
        string gameModeId = ResolveGameModeId(scenario);
        List<HeadlessProfileDefinition> profiles = new();

        if (string.Equals(gameModeId, NormalGameModeId, StringComparison.OrdinalIgnoreCase))
            profiles.Add(CreateNormalCommander());

        if (scenario?.headlessProfiles != null)
        {
            foreach (ScenarioHeadlessProfileDefinition source in scenario.headlessProfiles)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.id))
                    continue;

                string supportedMode = string.IsNullOrWhiteSpace(source.gameModeId)
                    ? gameModeId
                    : source.gameModeId.Trim();
                if (!string.Equals(supportedMode, gameModeId, StringComparison.OrdinalIgnoreCase))
                    continue;

                profiles.RemoveAll(item => string.Equals(item.Id, source.id, StringComparison.OrdinalIgnoreCase));
                profiles.Add(new HeadlessProfileDefinition
                {
                    Id = source.id.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(source.displayName) ? source.id.Trim() : source.displayName.Trim(),
                    Description = source.description ?? string.Empty,
                    SourceId = string.IsNullOrWhiteSpace(source.sourceId) ? scenario.id : source.sourceId.Trim(),
                    SourceLabel = string.IsNullOrWhiteSpace(source.sourceLabel) ? "Escenario" : source.sourceLabel.Trim(),
                    GameModeId = supportedMode,
                    MaximumInstances = Math.Max(1, source.maximumInstances),
                    BuiltIn = false,
                    RuntimeImplemented = source.runtimeImplemented
                });
            }
        }

        string[] explicitlyAllowed = scenario?.participantConfiguration?.availableHeadlessProfiles;
        if (explicitlyAllowed != null && explicitlyAllowed.Length > 0)
        {
            HashSet<string> allowed = new(explicitlyAllowed.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
            profiles.RemoveAll(profile => !allowed.Contains(profile.Id));
        }

        return profiles;
    }

    public static string ResolveGameModeId(ScenarioDefinition scenario)
    {
        return string.IsNullOrWhiteSpace(scenario?.gameModeId)
            ? NormalGameModeId
            : scenario.gameModeId.Trim();
    }

    private static HeadlessProfileDefinition CreateNormalCommander()
    {
        return new HeadlessProfileDefinition
        {
            Id = NormalCommanderProfileId,
            DisplayName = "Comandante normal",
            Description = "Participante controlado por la IA base de una escaramuza normal.",
            SourceId = "base",
            SourceLabel = "Juego base",
            GameModeId = NormalGameModeId,
            MaximumInstances = 7,
            BuiltIn = true,
            // La representación y el matchmaking ya están disponibles. El
            // planificador RTS completo se conectará en la siguiente etapa.
            RuntimeImplemented = false
        };
    }
}
