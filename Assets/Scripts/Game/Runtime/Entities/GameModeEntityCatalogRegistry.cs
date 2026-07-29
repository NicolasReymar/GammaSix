using System;
using System.Collections.Generic;

/// <summary>
/// Catálogos base registrados por los modos incluidos en GammaSix.
/// Cumple el mismo propósito que los perfiles Headless base: un escenario
/// normal hereda contenido clásico sin repetir la lista en cada mapa, mientras
/// que un modo importado declara su propio catálogo dentro del paquete.
/// </summary>
public static class GameModeEntityCatalogRegistry
{
    public const string NormalGameModeId = "base:game-mode.normal";

    private static readonly string[] NormalSpawnableEntityIds =
    {
        "unit.humanoid.default",
        "unit.humanoid.worker",
        "building.mercenary"
    };

    public static IReadOnlyList<string> GetDefaultSpawnableEntityIds(string gameModeId)
    {
        if (string.Equals(
                ContentReference.Parse(gameModeId).ToString(),
                NormalGameModeId,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(gameModeId, "normal", StringComparison.OrdinalIgnoreCase))
        {
            return NormalSpawnableEntityIds;
        }

        return Array.Empty<string>();
    }
}
