using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSessionManager : MonoBehaviour
{
    private const int MaxPlayers = 8;
    private const int MaxTeams = 4;
    private const string ProfileMessage = "GammaSix.PlayerProfile";
    private const string RosterMessage = "GammaSix.PlayerRoster";
    private const string ReadyMessage = "GammaSix.PlayerReady";
    private const string ColorRequestMessage = "GammaSix.PlayerColorRequest";
    private const string TeamRequestMessage = "GammaSix.PlayerTeamRequest";
    private const string LobbySettingsMessage = "GammaSix.LobbySettings";
    private const string MatchConfigMessage = "GammaSix.MatchConfig";
    private const string ScenarioMessage = "GammaSix.SelectedScenario";
    private const string SessionClosedMessage = "GammaSix.SessionClosed";

    public static NetworkSessionManager Instance { get; private set; }

    public event Action ConnectionStateChanged;
    public event Action PlayersChanged;
    public event Action<string> StatusChanged;
    public event Action<string> ScenarioChanged;
    public event Action LobbySettingsChanged;
    public event Action ContentOverridesChanged;

    public IReadOnlyList<NetworkPlayerInfo> Players => players;
    public bool IsListening => Manager != null && Manager.IsListening;
    public bool IsHost => Manager != null && Manager.IsHost;
    public bool IsClient => Manager != null && Manager.IsClient;
    public bool IsConnectedClient => Manager != null && Manager.IsConnectedClient;
    public bool FixedColors { get; private set; }
    public bool FixedTeams { get; private set; }
    public bool FixedTeamsForcedByScenario { get; private set; }
    public bool AllPlayersReady => players.Count > 0 && players.All(p => p.IsReady);
    public string SelectedScenarioId { get; private set; } = "test_scenario_01";
    public string SelectedContentId { get; private set; } = "test_scenario_01";
    public GameContentType SelectedContentType { get; private set; } = GameContentType.Scenario;
    public int SelectedScenarioMaxPlayers { get; private set; } = MaxPlayers;
    public int SelectedScenarioMaxTeams { get; private set; } = MaxTeams;
    public IReadOnlyList<ActiveSettingOverride> ActiveOverrides => activeOverrides;

    private readonly List<NetworkPlayerInfo> players = new();
    private readonly List<ActiveSettingOverride> activeOverrides = new();
    private bool callbacksRegistered;
    private bool messageHandlersRegistered;
    private bool isShuttingDown;
    private bool hostFixedTeamsSetting;

    private NetworkManager Manager => NetworkRuntimeBootstrap.Instance != null
        ? NetworkRuntimeBootstrap.Instance.NetworkManager
        : null;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        RegisterNetworkCallbacks();
    }

    private void OnDestroy()
    {
        UnregisterNetworkCallbacks();
        if (Instance == this)
            Instance = null;
    }

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

    public bool SelectScenario(string scenarioId)
    {
        return SelectGameContent(scenarioId, GameContentType.Scenario);
    }

    public bool SelectGameContent(string contentId, GameContentType contentType)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede seleccionar el contenido de la partida.");
            return false;
        }

        SelectedContentId = string.IsNullOrWhiteSpace(contentId) ? "test_scenario_01" : contentId.Trim();
        SelectedContentType = contentType;

        GameContentEntry entry = GameContentRepository.LoadAllContent()
            .FirstOrDefault(item => item.ContentId == SelectedContentId && item.ContentType == SelectedContentType);
        ScenarioDefinition scenario = entry != null
            ? GameContentRepository.ResolveFirstScenario(entry)
            : GameContentRepository.LoadScenario(SelectedContentId);

        SelectedScenarioId = string.IsNullOrWhiteSpace(scenario?.id)
            ? (contentType == GameContentType.Scenario ? SelectedContentId : "test_scenario_01")
            : scenario.id;
        SelectedScenarioMaxPlayers = scenario != null && scenario.maxPlayers > 0 ? scenario.maxPlayers : MaxPlayers;
        SelectedScenarioMaxTeams = scenario != null && scenario.maxTeams > 0 ? scenario.maxTeams : MaxTeams;
        FixedTeamsForcedByScenario = scenario != null && scenario.fixedTeams;
        hostFixedTeamsSetting = false;

        LoadOverridesFromScenario(scenario);
        ApplyEnabledOverrides();
        BroadcastRoster();
        BroadcastScenario();
        BroadcastLobbySettings();
        ScenarioChanged?.Invoke(SelectedContentId);
        ContentOverridesChanged?.Invoke();
        SetStatus($"{(contentType == GameContentType.Campaign ? "Campaña" : "Escenario")} seleccionado: {SelectedContentId}");
        return true;
    }

    public bool SetOverrideEnabled(string key, bool enabled)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede modificar los overrides del contenido.");
            return false;
        }

        ActiveSettingOverride item = activeOverrides.FirstOrDefault(value => value.Key == key);
        if (item == null) return false;
        item.Enabled = enabled;
        ApplyEnabledOverrides();
        BroadcastRoster();
        BroadcastLobbySettings();
        ContentOverridesChanged?.Invoke();
        SetStatus(enabled ? $"Override activado: {item.DisplayName}." : $"Override cancelado: {item.DisplayName}.");
        return true;
    }

    public void ToggleLocalReady()
    {
        if (!IsConnectedClient) return;

        NetworkPlayerInfo local = GetLocalPlayer();
        bool nextReady = local == null || !local.IsReady;

        if (IsHost)
        {
            if (local != null)
                UpsertPlayer(local.ClientId, local.PlayerName, local.TeamId, local.ColorId, nextReady);
            BroadcastRoster();
            return;
        }

        using FastBufferWriter writer = new(sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(nextReady);
        Manager.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer);
    }

    public bool RequestColorChange(ulong targetClientId, int colorId)
    {
        colorId = PlayerColorPalette.Normalize(colorId);
        NetworkPlayerInfo local = GetLocalPlayer();
        if (local == null)
            return false;

        if (!IsHost && targetClientId != local.ClientId)
        {
            SetStatus("Solo el host puede cambiar el color de otro jugador.");
            return false;
        }

        if (!IsHost && FixedColors)
        {
            SetStatus("Los colores están fijados por el host.");
            return false;
        }

        if (IsHost)
            return ApplyColorChange(targetClientId, colorId, Manager.LocalClientId);

        ColorRequestPayload request = new() { TargetClientId = targetClientId, ColorId = colorId };
        string json = JsonUtility.ToJson(request);
        FixedString512Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(ColorRequestMessage, NetworkManager.ServerClientId, writer);
        return true;
    }

    public bool RequestTeamChange(ulong targetClientId, int teamId)
    {
        NetworkPlayerInfo local = GetLocalPlayer();
        if (local == null)
            return false;

        teamId = Mathf.Clamp(teamId, 1, Mathf.Max(1, SelectedScenarioMaxTeams));

        if (FixedTeams)
        {
            SetStatus("Los equipos están fijados por la configuración de la partida.");
            return false;
        }

        if (!IsHost && targetClientId != local.ClientId)
        {
            SetStatus("Solo el host puede cambiar el equipo de otro jugador.");
            return false;
        }

        if (!IsHost && local.IsReady)
        {
            SetStatus("Debes marcarte como no listo antes de cambiar de equipo.");
            return false;
        }

        if (IsHost)
            return ApplyTeamChange(targetClientId, teamId, Manager.LocalClientId);

        TeamRequestPayload request = new() { TargetClientId = targetClientId, TeamId = teamId };
        FixedString512Bytes payload = JsonUtility.ToJson(request);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(TeamRequestMessage, NetworkManager.ServerClientId, writer);
        return true;
    }

    public bool SetFixedColors(bool fixedColors)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede fijar los colores.");
            return false;
        }

        ActiveSettingOverride activeOverride = activeOverrides.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Key, "fixedColors", StringComparison.OrdinalIgnoreCase));
        if (activeOverride != null)
        {
            SetStatus("Colores fijos está controlado por un override del contenido. Desactiva el override para modificarlo.");
            ApplyEnabledOverrides();
            BroadcastLobbySettings();
            return false;
        }

        FixedColors = fixedColors;
        BroadcastLobbySettings();
        LobbySettingsChanged?.Invoke();
        SetStatus(FixedColors ? "Colores fijos activados." : "Colores fijos desactivados.");
        return true;
    }

    public bool SetFixedTeams(bool fixedTeams)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede fijar los equipos.");
            return false;
        }

        if (FixedTeamsForcedByScenario)
        {
            SetStatus("Los equipos están fijados por el escenario seleccionado.");
            ApplyEnabledOverrides();
            BroadcastLobbySettings();
            return false;
        }

        ActiveSettingOverride activeOverride = activeOverrides.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
        if (activeOverride != null)
        {
            SetStatus("Equipos fijos está controlado por un override del contenido. Desactiva el override para modificarlo.");
            ApplyEnabledOverrides();
            BroadcastLobbySettings();
            return false;
        }

        hostFixedTeamsSetting = fixedTeams;
        FixedTeams = fixedTeams;
        BroadcastLobbySettings();
        LobbySettingsChanged?.Invoke();
        SetStatus(FixedTeams ? "Equipos fijos activados." : "Equipos fijos desactivados.");
        return true;
    }

    public bool StartNetworkMatch(string scenarioId)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede iniciar la partida.");
            return false;
        }

        if (players.Count == 0)
        {
            SetStatus("No hay jugadores conectados.");
            return false;
        }

        if (!AllPlayersReady)
        {
            SetStatus("Todos los jugadores deben estar listos antes de iniciar.");
            return false;
        }

        if (players.Count > SelectedScenarioMaxPlayers)
        {
            SetStatus($"El escenario admite un máximo de {SelectedScenarioMaxPlayers} jugadores y hay {players.Count} conectados.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedScenarioId))
            SelectedScenarioId = string.IsNullOrWhiteSpace(scenarioId) ? "test_scenario_01" : scenarioId;
        ApplyEnabledOverrides();
        BroadcastMatchConfig();

        MatchConfig config = MatchConfigFactory.CreateMultiplayerDefault(SelectedScenarioId);
        MatchManager.Instance.CreateMatch(config);
        GameManager.Instance?.SetState(GameState.Loading);
        Manager.SceneManager.LoadScene(SceneLoader.GameScene, LoadSceneMode.Single);
        return true;
    }

    public NetworkPlayerInfo GetLocalPlayer()
    {
        return Manager == null ? null : players.FirstOrDefault(p => p.ClientId == Manager.LocalClientId);
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

    private void HandleReadyMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost) return;

        reader.ReadValueSafe(out bool isReady);
        NetworkPlayerInfo current = players.FirstOrDefault(p => p.ClientId == senderClientId);
        if (current == null) return;

        UpsertPlayer(senderClientId, current.PlayerName, current.TeamId, current.ColorId, isReady);
        BroadcastRoster();
    }

    private void HandleColorRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost) return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        ColorRequestPayload request = JsonUtility.FromJson<ColorRequestPayload>(payload.ToString());
        ApplyColorChange(request.TargetClientId, request.ColorId, senderClientId);
    }

    private void HandleTeamRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost) return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        TeamRequestPayload request = JsonUtility.FromJson<TeamRequestPayload>(payload.ToString());
        ApplyTeamChange(request.TargetClientId, request.TeamId, senderClientId);
    }

    private bool ApplyTeamChange(ulong targetClientId, int requestedTeamId, ulong requesterClientId)
    {
        NetworkPlayerInfo requester = players.FirstOrDefault(p => p.ClientId == requesterClientId);
        NetworkPlayerInfo target = players.FirstOrDefault(p => p.ClientId == targetClientId);
        if (requester == null || target == null)
            return false;

        bool requesterIsHost = requesterClientId == Manager.LocalClientId;

        if (FixedTeams)
        {
            SetStatus("Los equipos están fijados por la configuración de la partida.");
            return false;
        }

        if (!requesterIsHost && targetClientId != requesterClientId)
        {
            SetStatus("Un jugador no puede cambiar el equipo de otro jugador.");
            return false;
        }

        if (!requesterIsHost && target.IsReady)
        {
            SetStatus("Debes marcarte como no listo antes de cambiar de equipo.");
            return false;
        }

        requestedTeamId = Mathf.Clamp(requestedTeamId, 1, Mathf.Max(1, SelectedScenarioMaxTeams));
        if (target.TeamId == requestedTeamId)
            return true;

        target.TeamId = requestedTeamId;
        target.IsReady = false;

        if (FixedColors)
        {
            int configuredColor = GetConfiguredTeamColorId(requestedTeamId);
            if (configuredColor >= 0)
                target.ColorId = configuredColor;
        }

        BroadcastRoster();
        SetStatus($"Equipo de {target.PlayerName}: Equipo {target.TeamId}.");
        return true;
    }

    private bool ApplyColorChange(ulong targetClientId, int requestedColorId, ulong requesterClientId)
    {
        NetworkPlayerInfo requester = players.FirstOrDefault(p => p.ClientId == requesterClientId);
        NetworkPlayerInfo target = players.FirstOrDefault(p => p.ClientId == targetClientId);
        if (requester == null || target == null)
            return false;

        bool requesterIsHost = requesterClientId == Manager.LocalClientId;
        if (!requesterIsHost && targetClientId != requesterClientId)
        {
            SetStatus("Un jugador no puede cambiar el color de otro jugador.");
            return false;
        }

        if (!requesterIsHost && FixedColors)
        {
            SetStatus("Los colores están fijados por el host.");
            return false;
        }

        if (!requesterIsHost && target.IsReady)
        {
            SetStatus("Debes marcarte como no listo antes de cambiar de color.");
            return false;
        }

        requestedColorId = PlayerColorPalette.Normalize(requestedColorId);
        if (target.ColorId == requestedColorId)
            return true;

        NetworkPlayerInfo occupied = players.FirstOrDefault(p => p.ColorId == requestedColorId);
        if (!requesterIsHost && occupied != null && occupied.ClientId != target.ClientId)
        {
            SetStatus("Ese color ya está ocupado.");
            return false;
        }

        int previousTargetColor = target.ColorId;
        target.ColorId = requestedColorId;
        target.IsReady = false;

        if (requesterIsHost && occupied != null && occupied.ClientId != target.ClientId)
        {
            occupied.ColorId = previousTargetColor;
            occupied.IsReady = false;
        }

        BroadcastRoster();
        SetStatus($"Color de {target.PlayerName}: {PlayerColorPalette.GetName(target.ColorId)}.");
        return true;
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

    private void UpsertPlayer(ulong clientId, string playerName, int teamId, int colorId, bool isReady)
    {
        NetworkPlayerInfo existing = players.FirstOrDefault(p => p.ClientId == clientId);
        if (existing == null)
            players.Add(new NetworkPlayerInfo(clientId, playerName, teamId, colorId, isReady));
        else
        {
            existing.PlayerName = playerName;
            existing.TeamId = teamId;
            existing.ColorId = colorId;
            existing.IsReady = isReady;
        }

        players.Sort((a, b) => a.TeamId.CompareTo(b.TeamId));
        PlayersChanged?.Invoke();
    }

    private int GetNextTeamId()
    {
        // Distribuye a los jugadores entre los equipos 1 a 4.
        // En caso de empate, asigna primero el equipo con el ID más bajo:
        // 1, 2, 3, 4, 1, 2, 3, 4.
        return Enumerable.Range(1, MaxTeams)
            .OrderBy(teamId => players.Count(player => player.TeamId == teamId))
            .ThenBy(teamId => teamId)
            .First();
    }

    private int GetNextAvailableColorId()
    {
        for (int colorId = 0; colorId < PlayerColorPalette.Count; colorId++)
        {
            if (players.All(p => p.ColorId != colorId))
                return colorId;
        }
        return PlayerColorPalette.Red;
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

    private void LoadOverridesFromScenario(ScenarioDefinition scenario)
    {
        activeOverrides.Clear();
        if (scenario?.settingOverrides == null) return;
        foreach (ScenarioSettingOverride item in scenario.settingOverrides)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.key)) continue;
            activeOverrides.Add(new ActiveSettingOverride(item));
        }
    }

    private void ApplyEnabledOverrides()
    {
        ActiveSettingOverride fixedColorsOverride = activeOverrides
            .FirstOrDefault(item => item.Enabled && string.Equals(item.Key, "fixedColors", StringComparison.OrdinalIgnoreCase));
        if (fixedColorsOverride != null && bool.TryParse(fixedColorsOverride.Value, out bool fixedColors))
            FixedColors = fixedColors;

        FixedTeams = FixedTeamsForcedByScenario || hostFixedTeamsSetting;
        ActiveSettingOverride fixedTeamsOverride = activeOverrides
            .FirstOrDefault(item => item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
        if (!FixedTeamsForcedByScenario && fixedTeamsOverride != null &&
            bool.TryParse(fixedTeamsOverride.Value, out bool fixedTeams))
        {
            FixedTeams = fixedTeams;
        }

        if (!FixedColors)
            return;

        foreach (NetworkPlayerInfo player in players)
        {
            int configuredColor = GetConfiguredTeamColorId(player.TeamId);
            if (configuredColor < 0 || player.ColorId == configuredColor)
                continue;

            player.ColorId = configuredColor;
            player.IsReady = false;
        }
    }

    private int GetInitialColorForTeam(int teamId)
    {
        int configured = GetConfiguredTeamColorId(teamId);
        return configured >= 0 ? configured : GetNextAvailableColorId();
    }

    private int GetConfiguredTeamColorId(int teamId)
    {
        ActiveSettingOverride colorOverride = activeOverrides.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Key, $"team{teamId}Color", StringComparison.OrdinalIgnoreCase));
        if (colorOverride == null)
            return -1;

        return ParseColorId(colorOverride.Value);
    }

    private static int ParseColorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return -1;
        return value.Trim().ToLowerInvariant() switch
        {
            "red" or "rojo" => PlayerColorPalette.Red,
            "blue" or "azul" => PlayerColorPalette.Blue,
            "yellow" or "amarillo" => PlayerColorPalette.Yellow,
            "green" or "verde" => PlayerColorPalette.Green,
            "purple" or "morado" => PlayerColorPalette.Purple,
            "orange" or "naranja" or "naranjo" => PlayerColorPalette.Orange,
            "brown" or "cafe" or "café" => PlayerColorPalette.Brown,
            "cyan" or "celeste" => PlayerColorPalette.Cyan,
            "pink" or "rosa" => PlayerColorPalette.Pink,
            _ => int.TryParse(value, out int numeric) ? PlayerColorPalette.Normalize(numeric) : -1
        };
    }

    private void SetStatus(string message)
    {
        Debug.Log($"[NetworkSession] {message}");
        StatusChanged?.Invoke(message);
    }

    [Serializable]
    private class PlayerRosterPayload
    {
        public List<NetworkPlayerInfo> Players;
        public PlayerRosterPayload(List<NetworkPlayerInfo> players) => Players = players;
    }

    [Serializable]
    private class ColorRequestPayload
    {
        public ulong TargetClientId;
        public int ColorId;
    }

    [Serializable]
    private class TeamRequestPayload
    {
        public ulong TargetClientId;
        public int TeamId;
    }

    [Serializable]
    private class LobbySettingsPayload
    {
        public bool FixedColors;
        public bool FixedTeams;
        public bool FixedTeamsForcedByScenario;
        public string SelectedContentId;
        public GameContentType SelectedContentType;
        public int ScenarioMaxPlayers;
        public int ScenarioMaxTeams;
        public List<ActiveSettingOverride> Overrides;
    }

    [Serializable]
    private class ScenarioSelectionPayload
    {
        public string ContentId;
        public GameContentType ContentType;
        public string ScenarioId;
    }
}
