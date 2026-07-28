using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Resuelve overrides de atributos definidos por la configuración de la partida.
/// El atributo permanece en la entidad; el override solo cambia su evaluación efectiva.
/// </summary>
public static class EntityAttributeOverrideService
{
    private static readonly Dictionary<string, string> OverrideKeysByAttribute = new(StringComparer.OrdinalIgnoreCase)
    {
        [EntityAttributeIds.NotSelectable] = "override_not_selectable",
        [EntityAttributeIds.NotSolid] = "override_not_solid"
    };

    private static string cachedScenarioId;
    private static ScenarioDefinition cachedScenario;

    public static bool IsAttributeOverridden(string attributeId)
    {
        if (string.IsNullOrWhiteSpace(attributeId) ||
            !OverrideKeysByAttribute.TryGetValue(attributeId, out string overrideKey))
        {
            return false;
        }

        NetworkSessionManager session = NetworkSessionManager.Instance;
        ActiveSettingOverride networkOverride = session?.ActiveOverrides?
            .FirstOrDefault(item =>
                item != null &&
                string.Equals(item.Key, overrideKey, StringComparison.OrdinalIgnoreCase));

        // Cuando existe una configuración sincronizada de sesión, esta es la fuente
        // autoritativa. Un override desactivado por el host no debe volver a activarse
        // leyendo el JSON local del escenario.
        if (networkOverride != null)
        {
            return networkOverride.Enabled &&
                   bool.TryParse(networkOverride.Value, out bool networkValue) &&
                   networkValue;
        }

        ScenarioSettingOverride scenarioOverride = GetCurrentScenario()?.settingOverrides?
            .FirstOrDefault(item =>
                item != null &&
                item.enabled &&
                string.Equals(item.key, overrideKey, StringComparison.OrdinalIgnoreCase));

        return scenarioOverride != null &&
               bool.TryParse(scenarioOverride.value, out bool scenarioValue) &&
               scenarioValue;
    }

    public static bool IsEffectivelyBlocked(EntityAttributeSet attributes, string blockingAttributeId)
    {
        return attributes != null &&
               attributes.Has(blockingAttributeId) &&
               !IsAttributeOverridden(blockingAttributeId);
    }

    private static ScenarioDefinition GetCurrentScenario()
    {
        string scenarioId = MatchManager.Instance?.CurrentMatchConfig?.ScenarioId;
        if (string.IsNullOrWhiteSpace(scenarioId))
            return null;

        if (cachedScenario != null && string.Equals(cachedScenarioId, scenarioId, StringComparison.Ordinal))
            return cachedScenario;

        cachedScenarioId = scenarioId;
        cachedScenario = GameContentRepository.LoadScenario(scenarioId);
        return cachedScenario;
    }
}
