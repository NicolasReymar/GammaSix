using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reglas puras de asignación del lobby. No envía mensajes ni modifica UI.
/// </summary>
public static class LobbyRuleService
{
    public static int GetNextTeamId(IReadOnlyCollection<NetworkPlayerInfo> players, int maxTeams)
    {
        int teamCount = System.Math.Max(1, maxTeams);
        return Enumerable.Range(1, teamCount)
            .OrderBy(teamId => players.Count(player => player.TeamId == teamId))
            .ThenBy(teamId => teamId)
            .First();
    }

    public static int GetNextAvailableColorId(IReadOnlyCollection<NetworkPlayerInfo> players)
    {
        for (int colorId = 0; colorId < PlayerColorPalette.Count; colorId++)
        {
            if (players.All(player => player.ColorId != colorId))
                return colorId;
        }

        return PlayerColorPalette.Red;
    }

    public static int ParseColorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return -1;

        return value.Trim().ToLowerInvariant() switch
        {
            "red" or "rojo" => PlayerColorPalette.Red,
            "blue" or "azul" => PlayerColorPalette.Blue,
            "yellow" or "amarillo" => PlayerColorPalette.Yellow,
            "green" or "verde" => PlayerColorPalette.Green,
            "purple" or "morado" => PlayerColorPalette.Purple,
            "orange" or "naranja" or "naranjo" => PlayerColorPalette.Orange,
            "brown" or "cafe" or "café" => PlayerColorPalette.Brown,
            "cyan" or "celeste" => PlayerColorPalette.Cyan,
            "pink" or "rosa" => PlayerColorPalette.Pink,
            _ => int.TryParse(value, out int numeric) ? PlayerColorPalette.Normalize(numeric) : -1
        };
    }
}
