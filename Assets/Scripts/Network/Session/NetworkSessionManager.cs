using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class NetworkSessionManager : MonoBehaviour
{
    private const int MaxPlayers = 8;
    private const int MaxParticipants = 16;
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
    private const string ContentCompatibilityMessage = "GammaSix.ContentCompatibility";

    public static NetworkSessionManager Instance { get; private set; }

    public event Action ConnectionStateChanged;
    public event Action PlayersChanged;
    public event Action<string> StatusChanged;
    public event Action<string> ScenarioChanged;
    public event Action LobbySettingsChanged;
    public event Action ContentOverridesChanged;

    public IReadOnlyList<NetworkPlayerInfo> Players => players;
    public IReadOnlyList<HeadlessProfileDefinition> AvailableHeadlessProfiles => availableHeadlessProfiles;
    public bool IsListening => Manager != null && Manager.IsListening;
    public bool IsHost => Manager != null && Manager.IsHost;
    public bool IsClient => Manager != null && Manager.IsClient;
    public bool IsConnectedClient => Manager != null && Manager.IsConnectedClient;
    public bool FixedColors { get; private set; }
    public bool FixedTeams { get; private set; }
    public bool FixedTeamsForcedByScenario { get; private set; }
    public int HumanPlayerCount => players.Count(p => p.IsHuman);
    public int HeadlessParticipantCount => players.Count(p => p.IsHeadless);
    public bool AllPlayersReady
    {
        get
        {
            List<NetworkPlayerInfo> humans = players.Where(p => p.IsHuman).ToList();
            return humans.Count > 0 && humans.All(p => p.IsReady);
        }
    }
    public string SelectedScenarioId { get; private set; } = "test_scenario_01";
    public string SelectedContentId { get; private set; } = "test_scenario_01";
    public GameContentType SelectedContentType { get; private set; } = GameContentType.Scenario;
    public int SelectedScenarioMaxPlayers { get; private set; } = MaxPlayers;
    public int SelectedScenarioMaxParticipants { get; private set; } = MaxPlayers;
    public int SelectedScenarioMaxTeams { get; private set; } = MaxTeams;
    public string SelectedGameModeId { get; private set; } = HeadlessProfileCatalog.NormalGameModeId;
    public string SelectedPackageId { get; private set; }
    public string SelectedPackageVersion { get; private set; }
    public string SelectedContentHash { get; private set; }
    public bool AllRemoteClientsContentCompatible => !IsHost || players
        .Where(item => item.IsHuman && Manager != null && item.ClientId != Manager.LocalClientId)
        .All(item => contentCompatibilityByClient.TryGetValue(item.ClientId, out bool compatible) && compatible);
    public IReadOnlyList<ActiveSettingOverride> ActiveOverrides => activeOverrides;

    private readonly List<NetworkPlayerInfo> players = new();
    private readonly List<HeadlessProfileDefinition> availableHeadlessProfiles = new();
    private readonly List<ActiveSettingOverride> activeOverrides = new();
    private readonly Dictionary<ulong, bool> contentCompatibilityByClient = new();
    private ScenarioDefinition selectedScenarioDefinition;
    private int nextParticipantId = 1;
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

    public NetworkPlayerInfo GetLocalPlayer()
    {
        return Manager == null
            ? null
            : players.FirstOrDefault(p => p.IsHuman && p.ClientId == Manager.LocalClientId);
    }

    public NetworkPlayerInfo GetParticipant(int participantId)
    {
        return players.FirstOrDefault(p => p.ParticipantId == participantId);
    }

    public bool HasFreeLobbySlot()
    {
        return FindFirstFreeSlotIndex(0, SelectedScenarioMaxParticipants) >= 0;
    }

    private void SetStatus(string message)
    {
        Debug.Log($"[NetworkSession] {message}");
        StatusChanged?.Invoke(message);
    }

}
