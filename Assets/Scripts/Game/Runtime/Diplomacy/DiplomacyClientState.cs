using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Copia de presentación sincronizada desde la autoridad. Se usa en input,
/// colores de selección y el tablero UXML; nunca modifica el gameplay autoritativo.
/// </summary>
public static class DiplomacyClientState
{
    private static readonly Dictionary<long, DiplomacyStance> Stances = new();
    private static readonly HashSet<int> Teams = new();

    public static event Action Changed;
    public static IReadOnlyList<int> TeamIds => Teams.OrderBy(value => value).ToList();

    public static DiplomacyStance GetStance(int sourceTeamId, int targetTeamId)
    {
        if (sourceTeamId > 0 && sourceTeamId == targetTeamId)
            return DiplomacyStance.Ally;
        if (sourceTeamId <= 0 || targetTeamId <= 0)
            return DiplomacyStance.Neutral;
        return Stances.TryGetValue(Key(sourceTeamId, targetTeamId), out DiplomacyStance stance)
            ? stance
            : DiplomacyStance.Neutral;
    }

    public static void Apply(
        IEnumerable<int> teamIds,
        IEnumerable<DiplomacyStanceSnapshotData> entries)
    {
        Dictionary<long, DiplomacyStance> next = new();
        HashSet<int> nextTeams = new();
        if (teamIds != null)
        {
            foreach (int teamId in teamIds)
            {
                if (teamId > 0)
                    nextTeams.Add(teamId);
            }
        }

        if (entries != null)
        {
            foreach (DiplomacyStanceSnapshotData entry in entries)
            {
                if (entry == null || entry.SourceTeamId <= 0 || entry.TargetTeamId <= 0)
                    continue;
                nextTeams.Add(entry.SourceTeamId);
                nextTeams.Add(entry.TargetTeamId);
                if (DiplomacyRuntimeService.TryParseStance(entry.Stance, out DiplomacyStance stance))
                    next[Key(entry.SourceTeamId, entry.TargetTeamId)] = stance;
            }
        }

        bool changed = Teams.Count != nextTeams.Count ||
                       !Teams.SetEquals(nextTeams) ||
                       Stances.Count != next.Count ||
                       next.Any(pair => !Stances.TryGetValue(pair.Key, out DiplomacyStance current) || current != pair.Value);
        if (!changed)
            return;

        Teams.Clear();
        foreach (int teamId in nextTeams)
            Teams.Add(teamId);
        Stances.Clear();
        foreach (KeyValuePair<long, DiplomacyStance> pair in next)
            Stances[pair.Key] = pair.Value;
        Changed?.Invoke();
    }

    public static void Reset()
    {
        if (Teams.Count == 0 && Stances.Count == 0)
            return;
        Teams.Clear();
        Stances.Clear();
        Changed?.Invoke();
    }

    private static long Key(int sourceTeamId, int targetTeamId)
    {
        return ((long)sourceTeamId << 32) ^ (uint)targetTeamId;
    }
}
