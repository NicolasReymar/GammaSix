using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public partial class NetworkSessionManager
{
    private void RegisterMessageHandlers()
    {
        if (messageHandlersRegistered)
            return;

        if (Manager?.CustomMessagingManager == null)
        {
            Debug.LogError("[NetworkSession] CustomMessagingManager aún no está disponible.");
            return;
        }

        Manager.CustomMessagingManager.RegisterNamedMessageHandler(ProfileMessage, HandleProfileMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(RosterMessage, HandleRosterMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, HandleReadyMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(ColorRequestMessage, HandleColorRequestMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(TeamRequestMessage, HandleTeamRequestMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(LobbySettingsMessage, HandleLobbySettingsMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(MatchConfigMessage, HandleMatchConfigMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(ScenarioMessage, HandleScenarioMessage);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(SessionClosedMessage, HandleSessionClosedMessage);
        messageHandlersRegistered = true;
    }

    private void UnregisterMessageHandlers()
    {
        if (!messageHandlersRegistered)
            return;

        if (Manager?.CustomMessagingManager != null)
        {
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(ProfileMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(RosterMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(ColorRequestMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(TeamRequestMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbySettingsMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(MatchConfigMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(ScenarioMessage);
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(SessionClosedMessage);
        }

        messageHandlersRegistered = false;
    }

    private void BroadcastRoster()
    {
        PlayersChanged?.Invoke();
        if (!IsHost || Manager.ConnectedClientsIds.Count <= 1) return;

        string json = JsonUtility.ToJson(new PlayerRosterPayload(players));
        FixedString4096Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessageToAll(RosterMessage, writer);
    }

    private void HandleRosterMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost) return;

        reader.ReadValueSafe(out FixedString4096Bytes payload);
        PlayerRosterPayload roster = JsonUtility.FromJson<PlayerRosterPayload>(payload.ToString());
        players.Clear();
        if (roster?.Players != null)
            players.AddRange(roster.Players);
        PlayersChanged?.Invoke();
    }

    private void BroadcastLobbySettings()
    {
        LobbySettingsChanged?.Invoke();
        if (!IsHost || Manager.CustomMessagingManager == null || Manager.ConnectedClientsIds.Count <= 1)
            return;

        LobbySettingsPayload settings = new() { FixedColors = FixedColors, FixedTeams = FixedTeams, FixedTeamsForcedByScenario = FixedTeamsForcedByScenario, SelectedContentId = SelectedContentId, SelectedContentType = SelectedContentType, ScenarioMaxPlayers = SelectedScenarioMaxPlayers, ScenarioMaxTeams = SelectedScenarioMaxTeams, Overrides = activeOverrides };
        string json = JsonUtility.ToJson(settings);
        FixedString512Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessageToAll(LobbySettingsMessage, writer);
    }

    private void SendLobbySettingsToClient(ulong clientId)
    {
        if (!IsHost || Manager.CustomMessagingManager == null)
            return;

        LobbySettingsPayload settings = new() { FixedColors = FixedColors, FixedTeams = FixedTeams, FixedTeamsForcedByScenario = FixedTeamsForcedByScenario, SelectedContentId = SelectedContentId, SelectedContentType = SelectedContentType, ScenarioMaxPlayers = SelectedScenarioMaxPlayers, ScenarioMaxTeams = SelectedScenarioMaxTeams, Overrides = activeOverrides };
        string json = JsonUtility.ToJson(settings);
        FixedString512Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(LobbySettingsMessage, clientId, writer);
    }

    private void HandleLobbySettingsMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost) return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        LobbySettingsPayload settings = JsonUtility.FromJson<LobbySettingsPayload>(payload.ToString());
        FixedColors = settings != null && settings.FixedColors;
        FixedTeams = settings != null && settings.FixedTeams;
        FixedTeamsForcedByScenario = settings != null && settings.FixedTeamsForcedByScenario;
        if (settings != null)
        {
            SelectedContentId = string.IsNullOrWhiteSpace(settings.SelectedContentId) ? SelectedScenarioId : settings.SelectedContentId;
            SelectedContentType = settings.SelectedContentType;
            SelectedScenarioMaxPlayers = settings.ScenarioMaxPlayers > 0 ? settings.ScenarioMaxPlayers : MaxPlayers;
            SelectedScenarioMaxTeams = settings.ScenarioMaxTeams > 0 ? settings.ScenarioMaxTeams : MaxTeams;
            activeOverrides.Clear();
            if (settings.Overrides != null) activeOverrides.AddRange(settings.Overrides);
        }
        LobbySettingsChanged?.Invoke();
        ContentOverridesChanged?.Invoke();
    }

    private void BroadcastScenario()
    {
        if (!IsHost || Manager.CustomMessagingManager == null)
            return;

        ScenarioSelectionPayload selection = new()
        {
            ContentId = SelectedContentId,
            ContentType = SelectedContentType,
            ScenarioId = SelectedScenarioId
        };
        FixedString512Bytes payload = JsonUtility.ToJson(selection);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessageToAll(ScenarioMessage, writer);
    }

    private void SendScenarioToClient(ulong clientId)
    {
        if (!IsHost || Manager.CustomMessagingManager == null)
            return;

        ScenarioSelectionPayload selection = new()
        {
            ContentId = SelectedContentId,
            ContentType = SelectedContentType,
            ScenarioId = SelectedScenarioId
        };
        FixedString512Bytes payload = JsonUtility.ToJson(selection);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(ScenarioMessage, clientId, writer);
    }

    private void HandleScenarioMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost) return;
        reader.ReadValueSafe(out FixedString512Bytes payload);
        ScenarioSelectionPayload selection = JsonUtility.FromJson<ScenarioSelectionPayload>(payload.ToString());
        if (selection != null)
        {
            SelectedContentId = selection.ContentId;
            SelectedContentType = selection.ContentType;
            SelectedScenarioId = selection.ScenarioId;
        }
        ScenarioChanged?.Invoke(SelectedContentId);
        SetStatus($"Contenido seleccionado por el host: {SelectedContentId}");
    }

    private void HandleSessionClosedMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost) return;

        if (Manager != null && Manager.IsListening)
        {
            isShuttingDown = true;
            Manager.Shutdown();
            isShuttingDown = false;
        }

        UnregisterMessageHandlers();
        ResetSessionState("El host cerró la sesión.");
    }

    private void BroadcastMatchConfig()
    {
        FixedString128Bytes scenario = SelectedScenarioId;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(scenario), Allocator.Temp);
        writer.WriteValueSafe(scenario);
        Manager.CustomMessagingManager.SendNamedMessageToAll(MatchConfigMessage, writer);
    }

    private void HandleMatchConfigMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost) return;
        reader.ReadValueSafe(out FixedString128Bytes scenario);
        SelectedScenarioId = scenario.ToString();
        MatchManager.Instance?.CreateMatch(MatchConfigFactory.CreateMultiplayerDefault(SelectedScenarioId));
        GameManager.Instance?.SetState(GameState.Loading);
    }
}
