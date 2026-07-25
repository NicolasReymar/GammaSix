using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public partial class NetworkSessionManager
{
    public bool StartHost(ushort port, string playerName)
    {
        if (!CanStartConnection()) return false;

        UnityTransport transport = NetworkRuntimeBootstrap.Instance.Transport;
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");

        bool started = Manager.StartHost();
        if (!started)
        {
            SetStatus("No se pudo iniciar el host.");
            return false;
        }

        RegisterMessageHandlers();
        FixedColors = false;
        FixedTeams = false;
        FixedTeamsForcedByScenario = false;
        hostFixedTeamsSetting = false;
        UpsertPlayer(Manager.LocalClientId, SanitizeName(playerName), 1, PlayerColorPalette.Red, false);
        BroadcastRoster();
        BroadcastLobbySettings();
        SetStatus($"Host iniciado en el puerto {port}.");
        ConnectionStateChanged?.Invoke();
        return true;
    }

    public bool StartClient(string address, ushort port, string playerName)
    {
        if (!CanStartConnection()) return false;

        if (string.IsNullOrWhiteSpace(address))
            address = "127.0.0.1";

        NetworkRuntimeBootstrap.Instance.Transport.SetConnectionData(address.Trim(), port);

        bool started = Manager.StartClient();
        if (!started)
        {
            SetStatus("No se pudo iniciar el cliente.");
            return false;
        }

        RegisterMessageHandlers();
        PlayerPrefs.SetString("MultiplayerPlayerName", SanitizeName(playerName));
        PlayerPrefs.Save();
        SetStatus($"Conectando a {address}:{port}...");
        ConnectionStateChanged?.Invoke();
        return true;
    }

    public void Shutdown()
    {
        if (isShuttingDown)
            return;

        isShuttingDown = true;

        if (Manager != null && Manager.IsListening)
        {
            if (Manager.IsHost && Manager.CustomMessagingManager != null && Manager.ConnectedClientsIds.Count > 1)
            {
                using FastBufferWriter writer = new(sizeof(byte), Allocator.Temp);
                writer.WriteValueSafe((byte)1);
                Manager.CustomMessagingManager.SendNamedMessageToAll(SessionClosedMessage, writer);
            }

            UnregisterMessageHandlers();
            Manager.Shutdown();
        }
        else
        {
            UnregisterMessageHandlers();
        }

        ResetSessionState("Conexión cerrada.");
        isShuttingDown = false;
    }

    private bool CanStartConnection()
    {
        if (Manager == null)
        {
            SetStatus("NetworkManager no está disponible. Inicia desde Bootstrap.");
            return false;
        }

        if (isShuttingDown)
        {
            SetStatus("La sesión anterior todavía se está cerrando.");
            return false;
        }

        if (Manager.IsListening)
        {
            SetStatus("Ya existe una conexión activa.");
            return false;
        }

        messageHandlersRegistered = false;
        return true;
    }

    private void RegisterNetworkCallbacks()
    {
        if (Manager == null)
        {
            Debug.LogError("[NetworkSession] NetworkManager no disponible.");
            return;
        }

        if (callbacksRegistered)
            return;

        Manager.OnClientConnectedCallback += HandleClientConnected;
        Manager.OnClientDisconnectCallback += HandleClientDisconnected;
        Manager.OnServerStarted += HandleServerStarted;
        callbacksRegistered = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (Manager == null) return;

        if (callbacksRegistered)
        {
            Manager.OnClientConnectedCallback -= HandleClientConnected;
            Manager.OnClientDisconnectCallback -= HandleClientDisconnected;
            Manager.OnServerStarted -= HandleServerStarted;
            callbacksRegistered = false;
        }

        UnregisterMessageHandlers();
    }

    private void HandleServerStarted()
    {
        SetStatus("Servidor iniciado correctamente.");
        ConnectionStateChanged?.Invoke();
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (clientId == Manager.LocalClientId && !IsHost)
            SendLocalProfile();

        if (IsHost && clientId != Manager.LocalClientId)
            SetStatus($"Cliente {clientId} conectado.");

        ConnectionStateChanged?.Invoke();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (IsHost)
        {
            players.RemoveAll(p => p.ClientId == clientId);
            BroadcastRoster();
            ConnectionStateChanged?.Invoke();
            return;
        }

        if (Manager != null && Manager.IsListening && !isShuttingDown)
        {
            isShuttingDown = true;
            Manager.Shutdown();
            isShuttingDown = false;
        }

        UnregisterMessageHandlers();
        ResetSessionState("El host cerró la sesión o se perdió la conexión.");
    }

    private void SendLocalProfile()
    {
        FixedString64Bytes playerName = GetSavedPlayerName();
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(playerName), Allocator.Temp);
        writer.WriteValueSafe(playerName);
        Manager.CustomMessagingManager.SendNamedMessage(ProfileMessage, NetworkManager.ServerClientId, writer);
    }

    private void HandleProfileMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost) return;

        reader.ReadValueSafe(out FixedString64Bytes playerName);
        if (players.Count >= Mathf.Clamp(SelectedScenarioMaxPlayers, 1, MaxPlayers))
        {
            SetStatus($"Sesión llena. Se rechazó al cliente {senderClientId}.");
            Manager.DisconnectClient(senderClientId);
            return;
        }

        int teamId = GetNextTeamId();
        int colorId = GetInitialColorForTeam(teamId);
        UpsertPlayer(senderClientId, SanitizeName(playerName.ToString()), teamId, colorId, false);
        BroadcastRoster();
        SendScenarioToClient(senderClientId);
        SendLobbySettingsToClient(senderClientId);
    }

    private string GetSavedPlayerName()
    {
        return SanitizeName(PlayerPrefs.GetString("MultiplayerPlayerName", $"Jugador {Manager.LocalClientId + 1}"));
    }

    private static string SanitizeName(string playerName)
    {
        string sanitized = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName.Trim();
        return sanitized.Length <= 24 ? sanitized : sanitized.Substring(0, 24);
    }

    private void ResetSessionState(string status)
    {
        players.Clear();
        FixedColors = false;
        FixedTeams = false;
        FixedTeamsForcedByScenario = false;
        hostFixedTeamsSetting = false;
        SelectedScenarioId = "test_scenario_01";
        SelectedContentId = "test_scenario_01";
        SelectedContentType = GameContentType.Scenario;
        SelectedScenarioMaxPlayers = MaxPlayers;
        SelectedScenarioMaxTeams = MaxTeams;
        activeOverrides.Clear();
        MatchManager.Instance?.ClearMatch();
        GameManager.Instance?.SetState(GameState.MainMenu);
        PlayersChanged?.Invoke();
        LobbySettingsChanged?.Invoke();
        ContentOverridesChanged?.Invoke();
        ScenarioChanged?.Invoke(SelectedContentId);
        ConnectionStateChanged?.Invoke();
        SetStatus(status);
    }
}
