using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class MainMenuController : MonoBehaviour
{
    [Header("UXML Screens")]
    [SerializeField] private VisualTreeAsset mainMenuUxml;
    [SerializeField] private VisualTreeAsset singlePlayerMenuUxml;
    [SerializeField] private VisualTreeAsset singlePlayerCampaignUxml;
    [SerializeField] private VisualTreeAsset singlePlayerSkirmishUxml;
    [SerializeField] private VisualTreeAsset multiPlayerMenuUxml;
    [SerializeField] private VisualTreeAsset multiPlayerJoinUxml;
    [SerializeField] private VisualTreeAsset multiPlayerGameMenuUxml;
    [SerializeField] private VisualTreeAsset settingsMenuUxml;
    [SerializeField] private VisualTreeAsset settingsMultiPlayerUxml;

    private UIDocument uiDocument;
    private string pendingSelectedScenarioId;
    private string confirmedScenarioId;
    private GameContentType pendingContentType = GameContentType.Scenario;
    private GameContentType confirmedContentType = GameContentType.Scenario;
    private Label selectedMapLabel;
    private Label statusLabel;
    private Action settingsBackAction;
    private bool isMultiplayerGameScreen;
    private bool isHostSetupScreen;
    private EventCallback<GeometryChangedEvent> multiplayerGeometryChangedHandler;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("[MainMenuController] No se encontró UIDocument.");
            return;
        }

        SubscribeNetworkEvents();
        ShowMainMenu();
    }

    private void OnDisable()
    {
        UnsubscribeNetworkEvents();
    }

    private void SubscribeNetworkEvents()
    {
        if (NetworkSessionManager.Instance == null) return;
        NetworkSessionManager.Instance.PlayersChanged += RefreshMultiplayerPlayers;
        NetworkSessionManager.Instance.ConnectionStateChanged += RefreshNetworkControls;
        NetworkSessionManager.Instance.StatusChanged += SetStatus;
        NetworkSessionManager.Instance.ScenarioChanged += HandleNetworkScenarioChanged;
        NetworkSessionManager.Instance.LobbySettingsChanged += HandleLobbySettingsChanged;
        NetworkSessionManager.Instance.ContentOverridesChanged += RefreshContentOverrides;
    }

    private void UnsubscribeNetworkEvents()
    {
        if (NetworkSessionManager.Instance == null) return;
        NetworkSessionManager.Instance.PlayersChanged -= RefreshMultiplayerPlayers;
        NetworkSessionManager.Instance.ConnectionStateChanged -= RefreshNetworkControls;
        NetworkSessionManager.Instance.StatusChanged -= SetStatus;
        NetworkSessionManager.Instance.ScenarioChanged -= HandleNetworkScenarioChanged;
        NetworkSessionManager.Instance.LobbySettingsChanged -= HandleLobbySettingsChanged;
        NetworkSessionManager.Instance.ContentOverridesChanged -= RefreshContentOverrides;
    }

    private void LoadScreen(VisualTreeAsset asset)
    {
        if (asset == null)
        {
            Debug.LogError("[MainMenuController] Falta asignar un UXML en el Inspector.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        root.Clear();
        asset.CloneTree(root);
        statusLabel = null;
        selectedMapLabel = null;
        isMultiplayerGameScreen = false;
        isHostSetupScreen = false;
    }

    private void ShowMainMenu()
    {
        LoadScreen(mainMenuUxml);
        VisualElement root = uiDocument.rootVisualElement;
        RegisterButton(root, "single-player-button", ShowSinglePlayerMenu);
        RegisterButton(root, "multi-player-button", ShowMultiPlayerMenu);
        RegisterButton(root, "settings-button", ShowSettingsMenu);
        RegisterButton(root, "main-menu-quit-button", QuitGame);
    }

    private void RegisterButton(VisualElement root, string buttonName, Action action)
    {
        Button button = root.Q<Button>(buttonName);
        if (button == null)
        {
            Debug.LogWarning($"[MainMenuController] No se encontró el botón: {buttonName}");
            return;
        }
        button.clicked += action;
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
        Debug.Log($"[MainMenuController] {message}");
    }

    private static ushort ParsePort(int port)
    {
        return (ushort)Mathf.Clamp(port, 1, 65535);
    }

    private static string GetSavedPlayerName()
    {
        return PlayerPrefs.GetString("MultiplayerPlayerName", "Jugador");
    }

    private static void SavePlayerName(string playerName)
    {
        string value = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName.Trim();
        PlayerPrefs.SetString("MultiplayerPlayerName", value);
        PlayerPrefs.Save();
    }

    private void QuitGame()
    {
        NetworkSessionManager.Instance?.Shutdown();
        Application.Quit();
    }
}
