using System;
using System.Collections.Generic;
using System.Linq;

public enum ParticipantLifeState
{
    Active,
    Captured,
    Eliminated,
    Disconnected,
    Victorious,
    Defeated
}

public sealed class ParticipantStateChangedEvent
{
    public MatchParticipantRuntimeState Participant { get; }
    public ParticipantLifeState PreviousState { get; }
    public ParticipantLifeState CurrentState { get; }
    public string Reason { get; }

    public ParticipantStateChangedEvent(
        MatchParticipantRuntimeState participant,
        ParticipantLifeState previousState,
        ParticipantLifeState currentState,
        string reason)
    {
        Participant = participant;
        PreviousState = previousState;
        CurrentState = currentState;
        Reason = reason;
    }
}

public sealed class ParticipantControlChangedEvent
{
    public MatchParticipantRuntimeState Participant { get; }
    public bool PreviousValue { get; }
    public bool CurrentValue { get; }
    public string Reason { get; }

    public ParticipantControlChangedEvent(
        MatchParticipantRuntimeState participant,
        bool previousValue,
        bool currentValue,
        string reason)
    {
        Participant = participant;
        PreviousValue = previousValue;
        CurrentValue = currentValue;
        Reason = reason;
    }
}

public sealed class ParticipantAttributeChangedEvent
{
    public MatchParticipantRuntimeState Participant { get; }
    public string Attribute { get; }
    public bool Added { get; }
    public string Reason { get; }

    public ParticipantAttributeChangedEvent(
        MatchParticipantRuntimeState participant,
        string attribute,
        bool added,
        string reason)
    {
        Participant = participant;
        Attribute = attribute;
        Added = added;
        Reason = reason;
    }
}

public sealed class ParticipantVariableChangedEvent
{
    public MatchParticipantRuntimeState Participant { get; }
    public string VariableName { get; }
    public string PreviousValue { get; }
    public string CurrentValue { get; }
    public string Reason { get; }

    public ParticipantVariableChangedEvent(
        MatchParticipantRuntimeState participant,
        string variableName,
        string previousValue,
        string currentValue,
        string reason)
    {
        Participant = participant;
        VariableName = variableName;
        PreviousValue = previousValue;
        CurrentValue = currentValue;
        Reason = reason;
    }
}

/// <summary>
/// Participante utilizado por la simulación. Sus atributos, variables y permiso
/// de control son capacidades genéricas que los escenarios pueden combinar.
/// El runtime no interpreta atributos como "capturado" o "rescatado".
/// </summary>
public sealed class MatchParticipantRuntimeState
{
    private readonly Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);

    public int ParticipantId { get; }
    public string SlotId { get; }
    public int SlotIndex { get; }
    public string DisplayName { get; }
    public int TeamId { get; }
    public int ColorId { get; }
    public ParticipantControllerKind ControllerKind { get; }
    public string ControllerProfileId { get; }
    public ulong ClientId { get; }
    public ParticipantLifeState LifeState { get; private set; } = ParticipantLifeState.Active;
    public bool ControlEnabled { get; private set; } = true;
    public RuntimeResourceCollection Resources { get; }
    public EntityAttributeSet Attributes { get; } = new();

    public bool IsHuman => ControllerKind == ParticipantControllerKind.Human;
    public bool IsHeadless => ControllerKind == ParticipantControllerKind.Headless;
    public bool CanIssueCommands => ControlEnabled &&
                                    LifeState != ParticipantLifeState.Eliminated &&
                                    LifeState != ParticipantLifeState.Disconnected &&
                                    LifeState != ParticipantLifeState.Defeated;

    public event Action<ParticipantStateChangedEvent> StateChanged;
    public event Action<ParticipantControlChangedEvent> ControlChanged;
    public event Action<ParticipantAttributeChangedEvent> AttributeChanged;
    public event Action<ParticipantVariableChangedEvent> VariableChanged;

    public MatchParticipantRuntimeState(
        int participantId,
        string slotId,
        int slotIndex,
        string displayName,
        int teamId,
        int colorId,
        ParticipantControllerKind controllerKind,
        string controllerProfileId,
        ulong clientId)
    {
        ParticipantId = participantId;
        SlotId = slotId;
        SlotIndex = slotIndex;
        DisplayName = displayName;
        TeamId = teamId;
        ColorId = colorId;
        ControllerKind = controllerKind;
        ControllerProfileId = controllerProfileId;
        ClientId = clientId;
        Resources = new RuntimeResourceCollection("participant", participantId);
    }

    public bool SetLifeState(ParticipantLifeState nextState, string reason = null)
    {
        if (LifeState == nextState)
            return false;

        ParticipantLifeState previous = LifeState;
        LifeState = nextState;
        StateChanged?.Invoke(new ParticipantStateChangedEvent(this, previous, nextState, reason));
        return true;
    }

    public bool SetControlEnabled(bool enabled, string reason = null)
    {
        if (ControlEnabled == enabled)
            return false;

        bool previous = ControlEnabled;
        ControlEnabled = enabled;
        ControlChanged?.Invoke(new ParticipantControlChangedEvent(this, previous, enabled, reason));
        return true;
    }

    public bool AddAttribute(string attribute, string reason = null)
    {
        if (!Attributes.Add(attribute))
            return false;

        AttributeChanged?.Invoke(new ParticipantAttributeChangedEvent(this, attribute.Trim(), true, reason));
        return true;
    }

    public bool RemoveAttribute(string attribute, string reason = null)
    {
        if (!Attributes.Remove(attribute))
            return false;

        AttributeChanged?.Invoke(new ParticipantAttributeChangedEvent(this, attribute.Trim(), false, reason));
        return true;
    }

    public bool SetVariable(string variableName, string value, string reason = null)
    {
        if (string.IsNullOrWhiteSpace(variableName))
            return false;

        string key = variableName.Trim();
        variables.TryGetValue(key, out string previous);
        string next = value ?? string.Empty;
        if (string.Equals(previous, next, StringComparison.Ordinal))
            return false;

        variables[key] = next;
        VariableChanged?.Invoke(new ParticipantVariableChangedEvent(this, key, previous, next, reason));
        return true;
    }

    public bool TryGetVariable(string variableName, out string value)
    {
        value = null;
        return !string.IsNullOrWhiteSpace(variableName) &&
               variables.TryGetValue(variableName.Trim(), out value);
    }

    public IReadOnlyDictionary<string, string> SnapshotVariables()
    {
        return variables.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static MatchParticipantRuntimeState FromLobbyParticipant(NetworkPlayerInfo participant)
    {
        if (participant == null)
            throw new ArgumentNullException(nameof(participant));

        return new MatchParticipantRuntimeState(
            participant.ParticipantId,
            participant.SlotId,
            participant.SlotIndex,
            participant.PlayerName,
            participant.TeamId,
            participant.ColorId,
            participant.ControllerKind,
            participant.ControllerProfileId,
            participant.ClientId);
    }

    public static MatchParticipantRuntimeState CreateOfflineHuman(
        int participantId,
        string displayName,
        int teamId,
        int colorId)
    {
        return new MatchParticipantRuntimeState(
            participantId,
            "player.1",
            0,
            displayName,
            teamId,
            colorId,
            ParticipantControllerKind.Human,
            null,
            0UL);
    }
}
