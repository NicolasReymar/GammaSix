using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public enum MatchTextCommandType
{
    None,
    Help,
    Spawn,
    Despawn,
    EntityDefinitions,
    RuntimeEntities,
    MatchState,
    Areas,
    Combat,
    Channels,
    Waves,
    WaveControl,
    HeadlessControllers,
    Navigation,
    PathVisualization,
    Diplomacy,
    DiplomacyStance,
    ChangeOwner,
    ChangeTeam,
    Attack,
    Damage
}

public sealed class MatchTextCommandParseResult
{
    public bool IsCommand;
    public bool Success;
    public MatchTextCommandType CommandType;
    public string EntityDefinitionId;
    public int RuntimeEntityId;
    public int SourceRuntimeEntityId;
    public int Amount;
    public bool UseLastRuntimeEntity;
    public Vector3 Position;
    public string Filter;
    public string Error;
    public string WaveControllerId;
    public string WaveOperation;
    public int SourceTeamId;
    public int TargetTeamId;
    public string DiplomacyStance;
    public string OwnerSelector;
    public int OwnerParticipantId;
    public string OwnerSlotId;
    public int OwnerTeamId = -1;
    public bool ToggleEnabled;
}

/// <summary>
/// Parser declarativo para el canal de partida. Acepta comandos con '/' y
/// mantiene los alias spawn/despawn sin prefijo para la interfaz de administración.
/// </summary>
public static class MatchTextCommandParser
{
    private static readonly Regex PositionExpression = new(
        @"posici(?:o|ó)n\s*\(\s*(?<x>[-+]?\d+(?:[\.,]\d+)?)\s*,\s*(?<y>[-+]?\d+(?:[\.,]\d+)?)\s*,\s*(?<z>[-+]?\d+(?:[\.,]\d+)?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static MatchTextCommandParseResult Parse(string rawText)
    {
        string text = rawText?.Trim();
        if (string.IsNullOrEmpty(text))
            return new MatchTextCommandParseResult { IsCommand = false, Success = false };

        bool explicitCommand = text.StartsWith("/", StringComparison.Ordinal);
        string normalized = explicitCommand ? text.Substring(1).Trim() : text;
        string commandWord = normalized
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        bool knownWithoutPrefix = commandWord is "spawn" or "despawn";
        if (!explicitCommand && !knownWithoutPrefix)
            return new MatchTextCommandParseResult { IsCommand = false, Success = true };

        if (string.IsNullOrEmpty(commandWord))
            return Error("Escribe un comando después de '/'.");

        string arguments = normalized.Length > commandWord.Length
            ? normalized.Substring(commandWord.Length).Trim()
            : string.Empty;

        return commandWord switch
        {
            "help" or "ayuda" => Success(MatchTextCommandType.Help),
            "spawn" => ParseSpawn(arguments),
            "despawn" => ParseDespawn(arguments),
            "entities" or "entidades" => ParseList(MatchTextCommandType.EntityDefinitions, arguments),
            "runtime" => ParseList(MatchTextCommandType.RuntimeEntities, arguments),
            "state" or "estado" or "match" => Success(MatchTextCommandType.MatchState),
            "areas" or "zonas" => Success(MatchTextCommandType.Areas),
            "combat" or "combate" => ParseList(MatchTextCommandType.Combat, arguments),
            "channels" or "canales" => Success(MatchTextCommandType.Channels),
            "waves" or "oleadas" => ParseList(MatchTextCommandType.Waves, arguments),
            "wave" or "oleada" => ParseWaveControl(arguments),
            "headless" or "bots" or "ia" => ParseList(MatchTextCommandType.HeadlessControllers, arguments),
            "navigation" or "navegacion" or "navegación" or "nav" => ParseList(MatchTextCommandType.Navigation, arguments),
            "path_visualization" or "path-visualization" or "visualizar_rutas" or "pathviz" => ParseOnOff(MatchTextCommandType.PathVisualization, arguments, "/path_visualization <on|off>"),
            "diplomacy" or "diplomacia" => Success(MatchTextCommandType.Diplomacy),
            "diplomacy_stance" or "diplomacy-stance" or "postura_diplomatica" => ParseDiplomacyStance(arguments),
            "change_owner" or "change-owner" or "cambiar_propietario" => ParseChangeOwner(arguments),
            "change_team" or "change-team" or "cambiar_equipo" => ParseChangeTeam(arguments),
            "attack" or "atacar" => ParseAttack(arguments),
            "damage" or "daño" or "dano" => ParseDamage(arguments),
            _ => Error($"Comando desconocido: {commandWord}. Usa /help.")
        };
    }

    private static MatchTextCommandParseResult ParseSpawn(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return Error(
                "Uso: /spawn <id-entidad> <x> <y> <z> [owner <participant-id>|slot <slot-id>|team <team-id>]");
        }

        string entityId;
        Vector3 position;
        string selectorTail = string.Empty;
        Match positionMatch = PositionExpression.Match(arguments);
        if (positionMatch.Success)
        {
            entityId = arguments.Substring(0, positionMatch.Index).Trim();
            selectorTail = arguments.Substring(positionMatch.Index + positionMatch.Length).Trim();
            if (!TryParsePositionMatch(positionMatch, out position))
                return Error("La posición debe contener números válidos.");
        }
        else
        {
            string[] tokens = arguments.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4 && tokens.Length != 6)
            {
                return Error(
                    "Uso: /spawn <id-entidad> <x> <y> <z> [owner <participant-id>|slot <slot-id>|team <team-id>]");
            }

            entityId = tokens[0];
            if (!TryParseFloat(tokens[1], out float x) ||
                !TryParseFloat(tokens[2], out float y) ||
                !TryParseFloat(tokens[3], out float z))
            {
                return Error("Las coordenadas x, y y z deben ser numéricas.");
            }

            position = new Vector3(x, y, z);
            if (tokens.Length == 6)
                selectorTail = $"{tokens[4]} {tokens[5]}";
        }

        entityId = entityId.Trim().Trim('(', ')', '"', '\'');
        if (string.IsNullOrWhiteSpace(entityId))
            return Error("Debes indicar el ID de la entidad.");

        MatchTextCommandParseResult result = new()
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.Spawn,
            EntityDefinitionId = entityId,
            Position = position
        };

        if (!string.IsNullOrWhiteSpace(selectorTail) &&
            !TryParseOwnerSelector(selectorTail, result, allowTeam: true, out string selectorError))
        {
            return Error(selectorError);
        }

        return result;
    }

    private static MatchTextCommandParseResult ParseDespawn(string arguments)
    {
        string value = arguments?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return Error("Uso: /despawn <runtime-id> o /despawn last");

        if (string.Equals(value, "last", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "ultimo", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "último", StringComparison.OrdinalIgnoreCase))
        {
            return new MatchTextCommandParseResult
            {
                IsCommand = true,
                Success = true,
                CommandType = MatchTextCommandType.Despawn,
                UseLastRuntimeEntity = true
            };
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int runtimeId) || runtimeId <= 0)
            return Error("El runtime-id debe ser un número mayor que cero.");

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.Despawn,
            RuntimeEntityId = runtimeId
        };
    }

    private static MatchTextCommandParseResult ParseWaveControl(string arguments)
    {
        string[] tokens = arguments?.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || tokens.Length != 2)
            return Error("Uso: /wave <start|pause|resume|stop|advance> <controller-id>");

        string operation = tokens[0].Trim().ToLowerInvariant();
        bool validOperation = operation == "start" || operation == "pause" ||
                              operation == "resume" || operation == "stop" ||
                              operation == "advance" || operation == "iniciar" ||
                              operation == "pausar" || operation == "reanudar" ||
                              operation == "detener" || operation == "avanzar";
        if (!validOperation)
        {
            return Error("Operación de oleada inválida. Usa start, pause, resume, stop o advance.");
        }

        operation = operation switch
        {
            "iniciar" => "start",
            "pausar" => "pause",
            "reanudar" => "resume",
            "detener" => "stop",
            "avanzar" => "advance",
            _ => operation
        };

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.WaveControl,
            WaveOperation = operation,
            WaveControllerId = tokens[1].Trim()
        };
    }

    private static MatchTextCommandParseResult ParseDiplomacyStance(string arguments)
    {
        string[] tokens = arguments?.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || tokens.Length != 3 ||
            !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceTeamId) ||
            !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetTeamId) ||
            sourceTeamId <= 0 || targetTeamId <= 0)
        {
            return Error("Uso: /diplomacy_stance <equipo-origen> <equipo-objetivo> <ally|neutral|enemy>");
        }

        if (!DiplomacyRuntimeService.TryParseStance(tokens[2], out DiplomacyStance stance))
            return Error("La postura debe ser ally, neutral o enemy.");

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.DiplomacyStance,
            SourceTeamId = sourceTeamId,
            TargetTeamId = targetTeamId,
            DiplomacyStance = stance.ToString()
        };
    }

    private static MatchTextCommandParseResult ParseChangeOwner(string arguments)
    {
        string[] tokens = arguments?.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || tokens.Length != 3 ||
            !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int runtimeEntityId) ||
            runtimeEntityId <= 0)
        {
            return Error(
                "Uso: /change_owner <runtime-id> participant <participant-id> o /change_owner <runtime-id> slot <slot-id>");
        }

        MatchTextCommandParseResult result = new()
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.ChangeOwner,
            RuntimeEntityId = runtimeEntityId
        };

        if (!TryParseOwnerSelector($"{tokens[1]} {tokens[2]}", result, allowTeam: false, out string error))
            return Error(error);

        return result;
    }

    private static MatchTextCommandParseResult ParseChangeTeam(string arguments)
    {
        string[] tokens = arguments?.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || tokens.Length != 2 ||
            !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int runtimeEntityId) ||
            !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int teamId) ||
            runtimeEntityId <= 0 || teamId < 0)
        {
            return Error("Uso: /change_team <runtime-id> <team-id>. El equipo neutral es 0.");
        }

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.ChangeTeam,
            RuntimeEntityId = runtimeEntityId,
            OwnerSelector = "team",
            OwnerTeamId = teamId
        };
    }

    private static bool TryParseOwnerSelector(
        string selectorText,
        MatchTextCommandParseResult result,
        bool allowTeam,
        out string error)
    {
        error = null;
        string[] tokens = selectorText?.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || tokens.Length != 2)
        {
            error = allowTeam
                ? "El propietario debe indicarse como owner <participant-id>, slot <slot-id> o team <team-id>."
                : "El propietario debe indicarse como participant <participant-id> o slot <slot-id>.";
            return false;
        }

        string selector = tokens[0].Trim().ToLowerInvariant();
        string value = tokens[1].Trim();
        if (selector is "owner" or "participant" or "participante" or "propietario")
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int participantId) ||
                participantId <= 0)
            {
                error = "El participant-id debe ser un número mayor que cero.";
                return false;
            }

            result.OwnerSelector = "participant";
            result.OwnerParticipantId = participantId;
            return true;
        }

        if (selector == "slot")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "El slot-id no puede estar vacío.";
                return false;
            }

            result.OwnerSelector = "slot";
            result.OwnerSlotId = value;
            return true;
        }

        if (allowTeam && (selector == "team" || selector == "equipo"))
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int teamId) || teamId < 0)
            {
                error = "El team-id debe ser cero o un número positivo.";
                return false;
            }

            result.OwnerSelector = "team";
            result.OwnerTeamId = teamId;
            return true;
        }

        error = allowTeam
            ? "Selector desconocido. Usa owner, participant, slot o team."
            : "Selector desconocido. Usa participant o slot.";
        return false;
    }

    private static MatchTextCommandParseResult ParseAttack(string arguments)
    {
        string[] tokens = arguments?.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || tokens.Length != 2 ||
            !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceId) ||
            !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId) ||
            sourceId <= 0 || targetId <= 0)
        {
            return Error("Uso: /attack <runtime-id-atacante> <runtime-id-objetivo>");
        }

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.Attack,
            SourceRuntimeEntityId = sourceId,
            RuntimeEntityId = targetId
        };
    }

    private static MatchTextCommandParseResult ParseDamage(string arguments)
    {
        string[] tokens = arguments?.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || (tokens.Length != 2 && tokens.Length != 3) ||
            !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId) ||
            !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) ||
            targetId <= 0 || amount <= 0)
        {
            return Error("Uso: /damage <runtime-id-objetivo> <cantidad> [runtime-id-origen]");
        }

        int sourceId = -1;
        if (tokens.Length == 3 &&
            (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out sourceId) || sourceId <= 0))
        {
            return Error("El runtime-id de origen debe ser mayor que cero.");
        }

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.Damage,
            RuntimeEntityId = targetId,
            SourceRuntimeEntityId = sourceId,
            Amount = amount
        };
    }

    private static MatchTextCommandParseResult ParseOnOff(
        MatchTextCommandType commandType,
        string arguments,
        string usage)
    {
        string value = arguments?.Trim().ToLowerInvariant();
        bool enabled;
        if (value is "on" or "true" or "1" or "activar" or "activo")
            enabled = true;
        else if (value is "off" or "false" or "0" or "desactivar" or "inactivo")
            enabled = false;
        else
            return Error($"Uso: {usage}");

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = commandType,
            ToggleEnabled = enabled
        };
    }

    private static MatchTextCommandParseResult ParseList(MatchTextCommandType commandType, string arguments)
    {
        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = commandType,
            Filter = arguments?.Trim()
        };
    }

    private static bool TryParsePositionMatch(Match match, out Vector3 position)
    {
        position = default;
        if (!TryParseFloat(match.Groups["x"].Value, out float x) ||
            !TryParseFloat(match.Groups["y"].Value, out float y) ||
            !TryParseFloat(match.Groups["z"].Value, out float z))
        {
            return false;
        }

        position = new Vector3(x, y, z);
        return true;
    }

    private static bool TryParseFloat(string value, out float result)
    {
        string normalized = value?.Trim().Replace(',', '.');
        return float.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static MatchTextCommandParseResult Success(MatchTextCommandType commandType)
    {
        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = commandType
        };
    }

    private static MatchTextCommandParseResult Error(string message)
    {
        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = false,
            Error = message
        };
    }
}
