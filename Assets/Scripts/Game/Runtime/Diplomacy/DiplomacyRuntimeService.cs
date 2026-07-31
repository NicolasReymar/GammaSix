using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Matriz direccional de relaciones entre equipos. La postura Source -> Target
/// no modifica la dirección inversa salvo que el contenido lo solicite de forma
/// explícita. Equipo 0 permanece neutral y un equipo siempre es aliado de sí mismo.
/// </summary>
public sealed class DiplomacyRuntimeService
{
    private readonly Dictionary<long, DiplomacyStance> stances = new();
    private readonly HashSet<int> knownTeamIds = new();

    public IReadOnlyList<int> TeamIds => knownTeamIds.OrderBy(value => value).ToList();
    public event Action<DiplomacyStanceChangedEvent> StanceChanged;

    public DiplomacyRuntimeService(
        ScenarioDiplomacyDefinition[] definitions,
        IEnumerable<MatchTeamRuntimeState> teams)
    {
        if (teams != null)
        {
            foreach (MatchTeamRuntimeState team in teams)
            {
                if (team != null && team.TeamId > 0)
                    knownTeamIds.Add(team.TeamId);
            }
        }

        if (definitions == null)
            return;

        foreach (ScenarioDiplomacyDefinition definition in definitions)
        {
            if (definition == null ||
                definition.sourceTeamId <= 0 ||
                definition.targetTeamId <= 0 ||
                definition.sourceTeamId == definition.targetTeamId ||
                !TryParseStance(definition.stance, out DiplomacyStance stance))
            {
                continue;
            }

            knownTeamIds.Add(definition.sourceTeamId);
            knownTeamIds.Add(definition.targetTeamId);
            SetInitial(definition.sourceTeamId, definition.targetTeamId, stance);
            if (definition.bidirectional)
                SetInitial(definition.targetTeamId, definition.sourceTeamId, stance);
        }
    }

    public DiplomacyStance GetStance(int sourceTeamId, int targetTeamId)
    {
        if (sourceTeamId > 0 && sourceTeamId == targetTeamId)
            return DiplomacyStance.Ally;
        if (sourceTeamId <= 0 || targetTeamId <= 0)
            return DiplomacyStance.Neutral;

        return stances.TryGetValue(Key(sourceTeamId, targetTeamId), out DiplomacyStance stance)
            ? stance
            : DiplomacyStance.Neutral;
    }

    public bool IsEnemy(int sourceTeamId, int targetTeamId)
    {
        return GetStance(sourceTeamId, targetTeamId) == DiplomacyStance.Enemy;
    }

    public bool IsAlly(int sourceTeamId, int targetTeamId)
    {
        return GetStance(sourceTeamId, targetTeamId) == DiplomacyStance.Ally;
    }

    public bool TrySetStance(
        int sourceTeamId,
        int targetTeamId,
        DiplomacyStance stance,
        int changedByParticipantId,
        string reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (sourceTeamId <= 0 || targetTeamId <= 0)
        {
            rejectionReason = "La diplomacia solo puede configurarse entre equipos mayores que cero.";
            return false;
        }

        if (sourceTeamId == targetTeamId)
        {
            if (stance == DiplomacyStance.Ally)
                return true;

            rejectionReason = "Un equipo siempre es aliado de sí mismo.";
            return false;
        }

        DiplomacyStance previous = GetStance(sourceTeamId, targetTeamId);
        knownTeamIds.Add(sourceTeamId);
        knownTeamIds.Add(targetTeamId);
        stances[Key(sourceTeamId, targetTeamId)] = stance;
        if (previous == stance)
            return true;

        StanceChanged?.Invoke(new DiplomacyStanceChangedEvent(
            sourceTeamId,
            targetTeamId,
            previous,
            stance,
            changedByParticipantId,
            reason));
        return true;
    }

    public IReadOnlyList<DiplomacyStanceSnapshotData> CreateSnapshot()
    {
        List<int> teams = TeamIds.ToList();
        List<DiplomacyStanceSnapshotData> result = new();
        foreach (int source in teams)
        {
            foreach (int target in teams)
            {
                result.Add(new DiplomacyStanceSnapshotData
                {
                    SourceTeamId = source,
                    TargetTeamId = target,
                    Stance = GetStance(source, target).ToString()
                });
            }
        }
        return result;
    }

    public static bool TryParseStance(string value, out DiplomacyStance stance)
    {
        if (Enum.TryParse(value, true, out stance))
            return true;

        string normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('_', '-').ToLowerInvariant();
        switch (normalized)
        {
            case "ally":
            case "allied":
            case "aliado":
            case "aliada":
                stance = DiplomacyStance.Ally;
                return true;
            case "neutral":
                stance = DiplomacyStance.Neutral;
                return true;
            case "enemy":
            case "hostile":
            case "enemigo":
            case "enemiga":
                stance = DiplomacyStance.Enemy;
                return true;
            default:
                stance = DiplomacyStance.Neutral;
                return false;
        }
    }

    private void SetInitial(int sourceTeamId, int targetTeamId, DiplomacyStance stance)
    {
        stances[Key(sourceTeamId, targetTeamId)] = stance;
    }

    private static long Key(int sourceTeamId, int targetTeamId)
    {
        return ((long)sourceTeamId << 32) ^ (uint)targetTeamId;
    }
}
