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
            "attack" or "atacar" => ParseAttack(arguments),
            "damage" or "daño" or "dano" => ParseDamage(arguments),
            _ => Error($"Comando desconocido: {commandWord}. Usa /help.")
        };
    }

    private static MatchTextCommandParseResult ParseSpawn(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return Error("Uso: /spawn <id-entidad> posicion(x,y,z)");

        string entityId;
        Vector3 position;
        Match positionMatch = PositionExpression.Match(arguments);
        if (positionMatch.Success)
        {
            entityId = arguments.Substring(0, positionMatch.Index).Trim();
            if (!TryParsePositionMatch(positionMatch, out position))
                return Error("La posición debe contener números válidos.");
        }
        else
        {
            string[] tokens = arguments.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4)
                return Error("Uso: /spawn <id-entidad> <x> <y> <z> o posicion(x,y,z)");

            entityId = tokens[0];
            if (!TryParseFloat(tokens[1], out float x) ||
                !TryParseFloat(tokens[2], out float y) ||
                !TryParseFloat(tokens[3], out float z))
            {
                return Error("Las coordenadas x, y y z deben ser numéricas.");
            }

            position = new Vector3(x, y, z);
        }

        entityId = entityId.Trim().Trim('(', ')', '"', '\'');
        if (string.IsNullOrWhiteSpace(entityId))
            return Error("Debes indicar el ID de la entidad.");

        return new MatchTextCommandParseResult
        {
            IsCommand = true,
            Success = true,
            CommandType = MatchTextCommandType.Spawn,
            EntityDefinitionId = entityId,
            Position = position
        };
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
