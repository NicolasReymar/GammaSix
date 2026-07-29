using System;
using System.Collections.Generic;
using System.Linq;

public sealed class MatchParticipantRegistry
{
    private readonly Dictionary<int, MatchParticipantRuntimeState> byId = new();

    public IReadOnlyList<MatchParticipantRuntimeState> All => byId.Values
        .OrderBy(item => item.SlotIndex)
        .ThenBy(item => item.ParticipantId)
        .ToList();

    public int Count => byId.Count;

    public event Action<ParticipantStateChangedEvent> ParticipantStateChanged;
    public event Action<ParticipantControlChangedEvent> ParticipantControlChanged;
    public event Action<ParticipantAttributeChangedEvent> ParticipantAttributeChanged;
    public event Action<ParticipantVariableChangedEvent> ParticipantVariableChanged;
    public event Action<RuntimeResourceChangedEvent> ParticipantResourceChanged;

    public MatchParticipantRegistry(
        IEnumerable<MatchParticipantRuntimeState> participants,
        ScenarioDefinition scenario = null)
    {
        if (participants != null)
        {
            foreach (MatchParticipantRuntimeState participant in participants)
            {
                if (participant == null)
                    continue;
                if (participant.ParticipantId <= 0)
                    throw new InvalidOperationException("ParticipantId debe ser mayor que cero.");
                if (byId.ContainsKey(participant.ParticipantId))
                    throw new InvalidOperationException($"ParticipantId duplicado: {participant.ParticipantId}.");

                participant.StateChanged += OnParticipantStateChanged;
                participant.ControlChanged += OnParticipantControlChanged;
                participant.AttributeChanged += OnParticipantAttributeChanged;
                participant.VariableChanged += OnParticipantVariableChanged;
                participant.Resources.Changed += OnParticipantResourceChanged;
                byId.Add(participant.ParticipantId, participant);
            }
        }

        ApplyInitialResources(scenario);
    }

    public bool TryGet(int participantId, out MatchParticipantRuntimeState participant)
    {
        return byId.TryGetValue(participantId, out participant);
    }

    public bool TryGetBySlotId(string slotId, out MatchParticipantRuntimeState participant)
    {
        participant = byId.Values.FirstOrDefault(item =>
            string.Equals(item.SlotId, slotId, StringComparison.OrdinalIgnoreCase));
        return participant != null;
    }

    public bool TryGetHumanByClientId(ulong clientId, out MatchParticipantRuntimeState participant)
    {
        participant = byId.Values.FirstOrDefault(item => item.IsHuman && item.ClientId == clientId);
        return participant != null;
    }

    public bool SetLifeState(
        int participantId,
        ParticipantLifeState state,
        string reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(participantId, out MatchParticipantRuntimeState participant))
        {
            rejectionReason = $"No existe el participante {participantId}.";
            return false;
        }

        participant.SetLifeState(state, reason);
        return true;
    }

    public bool SetControlEnabled(
        int participantId,
        bool enabled,
        string reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(participantId, out MatchParticipantRuntimeState participant))
        {
            rejectionReason = $"No existe el participante {participantId}.";
            return false;
        }

        participant.SetControlEnabled(enabled, reason);
        return true;
    }

    public bool AddAttribute(
        int participantId,
        string attribute,
        string reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(participantId, out MatchParticipantRuntimeState participant))
        {
            rejectionReason = $"No existe el participante {participantId}.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(attribute))
        {
            rejectionReason = "El atributo del participante está vacío.";
            return false;
        }

        participant.AddAttribute(attribute, reason);
        return true;
    }

    public bool RemoveAttribute(
        int participantId,
        string attribute,
        string reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(participantId, out MatchParticipantRuntimeState participant))
        {
            rejectionReason = $"No existe el participante {participantId}.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(attribute))
        {
            rejectionReason = "El atributo del participante está vacío.";
            return false;
        }

        participant.RemoveAttribute(attribute, reason);
        return true;
    }

    public bool SetVariable(
        int participantId,
        string variableName,
        string value,
        string reason,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(participantId, out MatchParticipantRuntimeState participant))
        {
            rejectionReason = $"No existe el participante {participantId}.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(variableName))
        {
            rejectionReason = "El nombre de la variable del participante está vacío.";
            return false;
        }

        participant.SetVariable(variableName, value, reason);
        return true;
    }

    public bool ValidateIssuer(MatchCommandIssuer issuer, out string rejectionReason)
    {
        rejectionReason = null;
        if (issuer == null)
        {
            rejectionReason = "El comando no tiene emisor.";
            return false;
        }

        if (issuer.Kind == MatchCommandIssuerKind.RuntimeRule)
            return true;

        if (!TryGet(issuer.ParticipantId, out MatchParticipantRuntimeState participant))
        {
            rejectionReason = $"Participante inexistente: {issuer.ParticipantId}.";
            return false;
        }

        if (!participant.CanIssueCommands)
        {
            rejectionReason = participant.ControlEnabled
                ? $"El participante {issuer.ParticipantId} está en estado {participant.LifeState} y no puede emitir órdenes."
                : $"El control del participante {issuer.ParticipantId} está deshabilitado por las reglas de la partida.";
            return false;
        }

        if (issuer.Kind == MatchCommandIssuerKind.HumanClient)
        {
            if (!participant.IsHuman)
            {
                rejectionReason = $"El participante {issuer.ParticipantId} no es humano.";
                return false;
            }

            if (participant.ClientId != issuer.ClientId)
            {
                rejectionReason = $"El ClientId {issuer.ClientId} no controla al participante {issuer.ParticipantId}.";
                return false;
            }

            return true;
        }

        if (!participant.IsHeadless)
        {
            rejectionReason = $"El participante {issuer.ParticipantId} no es headless.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(participant.ControllerProfileId) &&
            !string.Equals(
                participant.ControllerProfileId,
                issuer.ControllerProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = $"El perfil '{issuer.ControllerProfileId}' no controla al participante {issuer.ParticipantId}.";
            return false;
        }

        return true;
    }

    private void ApplyInitialResources(ScenarioDefinition scenario)
    {
        if (scenario?.participantResources == null)
            return;

        foreach (ScenarioParticipantResourceDefinition definition in scenario.participantResources)
        {
            if (definition == null)
                continue;

            MatchParticipantRuntimeState participant = null;
            if (definition.participantId > 0)
                TryGet(definition.participantId, out participant);
            if (participant == null && !string.IsNullOrWhiteSpace(definition.slotId))
                TryGetBySlotId(definition.slotId, out participant);
            if (participant == null || definition.resources == null)
                continue;

            foreach (ScenarioResourceAmount resource in definition.resources)
            {
                if (resource == null || string.IsNullOrWhiteSpace(resource.resourceId))
                    continue;
                participant.Resources.Set(resource.resourceId, resource.amount, false);
            }
        }
    }

    private void OnParticipantStateChanged(ParticipantStateChangedEvent stateEvent)
    {
        ParticipantStateChanged?.Invoke(stateEvent);
    }

    private void OnParticipantControlChanged(ParticipantControlChangedEvent controlEvent)
    {
        ParticipantControlChanged?.Invoke(controlEvent);
    }

    private void OnParticipantAttributeChanged(ParticipantAttributeChangedEvent attributeEvent)
    {
        ParticipantAttributeChanged?.Invoke(attributeEvent);
    }

    private void OnParticipantVariableChanged(ParticipantVariableChangedEvent variableEvent)
    {
        ParticipantVariableChanged?.Invoke(variableEvent);
    }

    private void OnParticipantResourceChanged(RuntimeResourceChangedEvent resourceEvent)
    {
        ParticipantResourceChanged?.Invoke(resourceEvent);
    }
}
