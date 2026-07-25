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

    public NetworkPlayerInfo GetLocalPlayer()
    {
        return Manager == null ? null : players.FirstOrDefault(p => p.ClientId == Manager.LocalClientId);
    }

    private void SetStatus(string message)
    {
        Debug.Log($"[NetworkSession] {message}");
        StatusChanged?.Invoke(message);
    }

}
