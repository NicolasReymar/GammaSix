using System;

public enum MatchCommandType
{
    Move,
    AttackMove,
    Patrol,
    ResourceInteraction,
    EntityInteraction,
    Attack,
    Stop,
    SetCombatStance,
    SetDiplomacyStance
}

public enum MatchCommandIssuerKind
{
    HumanClient,
    Headless,
    RuntimeRule
}

/// <summary>
/// Identifica quién emitió una orden dentro del runtime. El gameplay valida
/// propiedad mediante ParticipantId; ClientId solo autentica órdenes humanas
/// recibidas por red.
/// </summary>
public sealed class MatchCommandIssuer
{
    public int ParticipantId { get; }
    public MatchCommandIssuerKind Kind { get; }
    public ulong ClientId { get; }
    public string ControllerProfileId { get; }

    private MatchCommandIssuer(
        int participantId,
        MatchCommandIssuerKind kind,
        ulong clientId,
        string controllerProfileId)
    {
        ParticipantId = participantId;
        Kind = kind;
        ClientId = clientId;
        ControllerProfileId = controllerProfileId;
    }

    public static MatchCommandIssuer Human(int participantId, ulong clientId)
    {
        return new MatchCommandIssuer(
            participantId,
            MatchCommandIssuerKind.HumanClient,
            clientId,
            null);
    }

    public static MatchCommandIssuer Headless(int participantId, string controllerProfileId)
    {
        return new MatchCommandIssuer(
            participantId,
            MatchCommandIssuerKind.Headless,
            ulong.MaxValue,
            controllerProfileId);
    }

    public static MatchCommandIssuer RuntimeRule(int participantId = -1)
    {
        return new MatchCommandIssuer(
            participantId,
            MatchCommandIssuerKind.RuntimeRule,
            ulong.MaxValue,
            null);
    }
}

public sealed class MatchCommandEnvelope
{
    public long Sequence { get; }
    public MatchCommandIssuer Issuer { get; }
    public MatchCommandType CommandType { get; }
    public object Payload { get; }

    public MatchCommandEnvelope(
        long sequence,
        MatchCommandIssuer issuer,
        MatchCommandType commandType,
        object payload)
    {
        Sequence = sequence;
        Issuer = issuer;
        CommandType = commandType;
        Payload = payload;
    }
}

public sealed class MatchCommandResult
{
    public bool Accepted { get; }
    public string Message { get; }

    private MatchCommandResult(bool accepted, string message)
    {
        Accepted = accepted;
        Message = message;
    }

    public static MatchCommandResult Success()
    {
        return new MatchCommandResult(true, null);
    }

    public static MatchCommandResult Rejected(string message)
    {
        return new MatchCommandResult(false, message);
    }
}
