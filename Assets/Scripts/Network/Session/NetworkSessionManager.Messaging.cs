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
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(ContentCompatibilityMessage, HandleContentCompatibilityMessage);
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
            Manager.CustomMessagingManager.UnregisterNamedMessageHandler(ContentCompatibilityMessage);
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
        SortParticipants();
        nextParticipantId = players.Count == 0 ? 1 : players.Max(item => item.ParticipantId) + 1;
        PlayersChanged?.Invoke();
    }

    private void BroadcastLobbySettings()
    {
        LobbySettingsChanged?.Invoke();
        if (!IsHost || Manager.CustomMessagingManager == null || Manager.ConnectedClientsIds.Count <= 1)
            return;

        LobbySettingsPayload settings = new() { FixedColors = FixedColors, FixedTeams = FixedTeams, FixedTeamsForcedByScenario = FixedTeamsForcedByScenario, SelectedContentId = SelectedContentId, SelectedContentType = SelectedContentType, ScenarioMaxPlayers = SelectedScenarioMaxPlayers, ScenarioMaxParticipants = SelectedScenarioMaxParticipants, ScenarioMaxTeams = SelectedScenarioMaxTeams, GameModeId = SelectedGameModeId, AvailableHeadlessProfiles = availableHeadlessProfiles, Overrides = activeOverrides };
        string json = JsonUtility.ToJson(settings);
        FixedString4096Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessageToAll(
            LobbySettingsMessage,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void SendLobbySettingsToClient(ulong clientId)
    {
        if (!IsHost || Manager.CustomMessagingManager == null)
            return;

        LobbySettingsPayload settings = new() { FixedColors = FixedColors, FixedTeams = FixedTeams, FixedTeamsForcedByScenario = FixedTeamsForcedByScenario, SelectedContentId = SelectedContentId, SelectedContentType = SelectedContentType, ScenarioMaxPlayers = SelectedScenarioMaxPlayers, ScenarioMaxParticipants = SelectedScenarioMaxParticipants, ScenarioMaxTeams = SelectedScenarioMaxTeams, GameModeId = SelectedGameModeId, AvailableHeadlessProfiles = availableHeadlessProfiles, Overrides = activeOverrides };
        string json = JsonUtility.ToJson(settings);
        FixedString4096Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(
            LobbySettingsMessage,
            clientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void HandleLobbySettingsMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost) return;

        reader.ReadValueSafe(out FixedString4096Bytes payload);
        LobbySettingsPayload settings = JsonUtility.FromJson<LobbySettingsPayload>(payload.ToString());
        FixedColors = settings != null && settings.FixedColors;
        FixedTeams = settings != null && settings.FixedTeams;
        FixedTeamsForcedByScenario = settings != null && settings.FixedTeamsForcedByScenario;
        if (settings != null)
        {
            SelectedContentId = string.IsNullOrWhiteSpace(settings.SelectedContentId) ? SelectedScenarioId : settings.SelectedContentId;
            SelectedContentType = settings.SelectedContentType;
            SelectedScenarioMaxPlayers = settings.ScenarioMaxPlayers > 0 ? settings.ScenarioMaxPlayers : MaxPlayers;
            SelectedScenarioMaxParticipants = settings.ScenarioMaxParticipants > 0 ? settings.ScenarioMaxParticipants : SelectedScenarioMaxPlayers;
            SelectedScenarioMaxTeams = settings.ScenarioMaxTeams > 0 ? settings.ScenarioMaxTeams : MaxTeams;
            SelectedGameModeId = string.IsNullOrWhiteSpace(settings.GameModeId) ? HeadlessProfileCatalog.NormalGameModeId : settings.GameModeId;
            availableHeadlessProfiles.Clear();
            if (settings.AvailableHeadlessProfiles != null) availableHeadlessProfiles.AddRange(settings.AvailableHeadlessProfiles);
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

        ScenarioSelectionPayload selection = CreateScenarioSelectionPayload();
        FixedString4096Bytes payload = JsonUtility.ToJson(selection);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessageToAll(
            ScenarioMessage,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void SendScenarioToClient(ulong clientId)
    {
        if (!IsHost || Manager.CustomMessagingManager == null)
            return;

        contentCompatibilityByClient[clientId] = false;
        ScenarioSelectionPayload selection = CreateScenarioSelectionPayload();
        FixedString4096Bytes payload = JsonUtility.ToJson(selection);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(
            ScenarioMessage,
            clientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private ScenarioSelectionPayload CreateScenarioSelectionPayload()
    {
        return new ScenarioSelectionPayload
        {
            ContentId = SelectedContentId,
            ContentType = SelectedContentType,
            ScenarioId = SelectedScenarioId,
            PackageId = SelectedPackageId,
            PackageVersion = SelectedPackageVersion,
            ContentHash = SelectedContentHash
        };
    }

    private void HandleScenarioMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost)
            return;

        reader.ReadValueSafe(out FixedString4096Bytes payload);
        ScenarioSelectionPayload selection = JsonUtility.FromJson<ScenarioSelectionPayload>(payload.ToString());
        if (selection == null)
            return;

        SelectedContentId = selection.ContentId;
        SelectedContentType = selection.ContentType;
        SelectedScenarioId = selection.ScenarioId;
        SelectedPackageId = selection.PackageId;
        SelectedPackageVersion = selection.PackageVersion;
        SelectedContentHash = selection.ContentHash;

        ContentCompatibilityPayload compatibility = EvaluateLocalContentCompatibility(selection);
        SendContentCompatibilityToHost(compatibility);
        ScenarioChanged?.Invoke(SelectedContentId);
        SetStatus(compatibility.Compatible
            ? $"Contenido compatible seleccionado por el host: {SelectedContentId}"
            : compatibility.Status);
    }

    private ContentCompatibilityPayload EvaluateLocalContentCompatibility(
        ScenarioSelectionPayload selection)
    {
        ContentCompatibilityPayload result = new()
        {
            ScenarioId = selection.ScenarioId,
            PackageId = selection.PackageId,
            PackageVersion = selection.PackageVersion,
            ContentHash = selection.ContentHash,
            Compatible = true,
            Status = "Contenido base compatible."
        };

        if (string.IsNullOrWhiteSpace(selection.PackageId))
        {
            result.Compatible = GameContentRepository.LoadScenario(selection.ScenarioId) != null;
            result.Status = result.Compatible
                ? "Contenido base compatible."
                : $"No existe el escenario base '{selection.ScenarioId}'.";
            return result;
        }

        if (!PackageContentResolver.TryResolveInstalledPackage(
                selection.PackageId,
                out InstalledGameContentPackage installed,
                out _))
        {
            result.Compatible = false;
            result.Status = $"Falta el paquete '{selection.PackageId}'.";
            return result;
        }

        if (!string.Equals(installed.PackageVersion, selection.PackageVersion, StringComparison.OrdinalIgnoreCase))
        {
            result.Compatible = false;
            result.Status = $"Versión de paquete incompatible: local {installed.PackageVersion}, host {selection.PackageVersion}.";
            return result;
        }

        if (!string.Equals(installed.ContentHash, selection.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            result.Compatible = false;
            result.Status = $"El hash del paquete '{selection.PackageId}' no coincide con el host.";
            return result;
        }

        result.Compatible = GameContentRepository.LoadScenario(selection.ScenarioId) != null;
        result.Status = result.Compatible
            ? "Paquete, versión y hash compatibles."
            : $"El paquete no contiene el escenario '{selection.ScenarioId}'.";
        return result;
    }

    private void SendContentCompatibilityToHost(ContentCompatibilityPayload compatibility)
    {
        if (IsHost || Manager?.CustomMessagingManager == null || compatibility == null)
            return;

        FixedString4096Bytes payload = JsonUtility.ToJson(compatibility);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(
            ContentCompatibilityMessage,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void HandleContentCompatibilityMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost)
            return;

        reader.ReadValueSafe(out FixedString4096Bytes payload);
        ContentCompatibilityPayload compatibility =
            JsonUtility.FromJson<ContentCompatibilityPayload>(payload.ToString());
        bool matchesSelection = compatibility != null &&
            string.Equals(compatibility.ScenarioId, SelectedScenarioId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(compatibility.PackageId ?? string.Empty, SelectedPackageId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(compatibility.PackageVersion ?? string.Empty, SelectedPackageVersion ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(compatibility.ContentHash ?? string.Empty, SelectedContentHash ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        contentCompatibilityByClient[senderClientId] = matchesSelection && compatibility.Compatible;
        SetStatus(contentCompatibilityByClient[senderClientId]
            ? $"Cliente {senderClientId}: contenido compatible."
            : $"Cliente {senderClientId}: {compatibility?.Status ?? "respuesta de contenido inválida"}");
        LobbySettingsChanged?.Invoke();
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
        FixedString512Bytes scenario = SelectedScenarioId;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(scenario), Allocator.Temp);
        writer.WriteValueSafe(scenario);
        Manager.CustomMessagingManager.SendNamedMessageToAll(MatchConfigMessage, writer);
    }

    private void HandleMatchConfigMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (IsHost) return;
        reader.ReadValueSafe(out FixedString512Bytes scenario);
        SelectedScenarioId = scenario.ToString();
        MatchManager.Instance?.CreateMatch(MatchConfigFactory.CreateMultiplayerDefault(SelectedScenarioId));
        GameManager.Instance?.SetState(GameState.Loading);
    }
}
