using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Menú modal de partida. Escape abre/cierra el menú principal y vuelve desde
/// Ajustes. No detiene Time.timeScale para no congelar una partida multijugador;
/// solo bloquea el input local mediante GameUiModalService.
/// </summary>
public sealed class GamePauseMenuController : MonoBehaviour
{
    private enum MenuPage
    {
        Closed,
        Match,
        Settings
    }

    private enum SettingsCategory
    {
        Video,
        Sound,
        Interface
    }

    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private VisualElement overlay;
    private VisualElement matchPage;
    private VisualElement settingsPage;
    private VisualElement videoContent;
    private VisualElement soundContent;
    private VisualElement interfaceContent;
    private Button videoCategoryButton;
    private Button soundCategoryButton;
    private Button interfaceCategoryButton;
    private Label saveStatusLabel;
    private Label settingsStatusLabel;
    private MenuPage currentPage = MenuPage.Closed;

    private void Awake()
    {
        VisualTreeAsset pauseTree = Resources.Load<VisualTreeAsset>("UI/GameHud/GamePauseMenu");
        if (pauseTree == null)
        {
            Debug.LogError("[GamePauseMenuController] No se encontró GamePauseMenu.uxml.");
            enabled = false;
            return;
        }

        uiDocument = HudDocumentFactory.Create(gameObject, pauseTree, 1000, out runtimePanelSettings);
        if (uiDocument == null)
        {
            enabled = false;
            return;
        }

        BindElements();
        InstallInterfaceSettings();
        RegisterCallbacks();
        ShowClosed();
    }

    private void Update()
    {
        if (!GameInputReader.EscapePressedThisFrame)
            return;

        switch (currentPage)
        {
            case MenuPage.Closed:
                // Escape primero debe cerrar interfaces como chat/consola.
                // El frame de protección de GameUiModalService evita que el
                // mismo Escape atraviese esa interfaz y abra también la pausa.
                if (GameUiModalService.BlocksGameplayInput)
                    return;
                ShowMatchMenu();
                break;
            case MenuPage.Match:
                ShowClosed();
                break;
            case MenuPage.Settings:
                ShowMatchMenu();
                break;
        }
    }

    private void OnDestroy()
    {
        UnregisterCallbacks();
        GameUiModalService.Release(this);

        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void BindElements()
    {
        VisualElement root = uiDocument.rootVisualElement;
        overlay = root.Q<VisualElement>("game-pause-overlay");
        matchPage = root.Q<VisualElement>("game-pause-main-page");
        settingsPage = root.Q<VisualElement>("game-settings-page");
        videoContent = root.Q<VisualElement>("game-settings-video-content");
        soundContent = root.Q<VisualElement>("game-settings-sound-content");
        interfaceContent = root.Q<VisualElement>("game-settings-interface-content");
        videoCategoryButton = root.Q<Button>("game-settings-video-button");
        soundCategoryButton = root.Q<Button>("game-settings-sound-button");
        interfaceCategoryButton = root.Q<Button>("game-settings-interface-button");
        settingsStatusLabel = root.Q<Label>("game-settings-global-status");
        root.Q<Button>("game-pause-future-button")?.SetEnabled(false);
    }

    private void InstallInterfaceSettings()
    {
        if (interfaceContent == null)
            return;

        VisualTreeAsset interfaceTree = Resources.Load<VisualTreeAsset>("UI/GameHud/GameInterfaceSettings");
        if (interfaceTree == null)
        {
            Debug.LogError("[GamePauseMenuController] No se encontró GameInterfaceSettings.uxml.");
            return;
        }

        interfaceTree.CloneTree(interfaceContent);
        saveStatusLabel = interfaceContent.Q<Label>("save-ui-coordinates-status");
    }

    private void RegisterCallbacks()
    {
        VisualElement root = uiDocument.rootVisualElement;
        RegisterButton(root, "game-pause-return-button", ShowClosed);
        RegisterButton(root, "game-pause-settings-button", ShowSettings);
        RegisterButton(root, "game-pause-exit-button", ExitMatch);
        RegisterButton(root, "game-settings-back-button", ShowMatchMenu);
        RegisterButton(root, "game-settings-save-back-button", SaveSettingsAndReturn);
        RegisterButton(root, "save-ui-coordinates-button", SaveHudCoordinates);
        RegisterButton(root, "reset-ui-layout-button", ResetHudCoordinates);

        if (videoCategoryButton != null)
            videoCategoryButton.clicked += ShowVideoCategory;
        if (soundCategoryButton != null)
            soundCategoryButton.clicked += ShowSoundCategory;
        if (interfaceCategoryButton != null)
            interfaceCategoryButton.clicked += ShowInterfaceCategory;
    }

    private void UnregisterCallbacks()
    {
        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        UnregisterButton(root, "game-pause-return-button", ShowClosed);
        UnregisterButton(root, "game-pause-settings-button", ShowSettings);
        UnregisterButton(root, "game-pause-exit-button", ExitMatch);
        UnregisterButton(root, "game-settings-back-button", ShowMatchMenu);
        UnregisterButton(root, "game-settings-save-back-button", SaveSettingsAndReturn);
        UnregisterButton(root, "save-ui-coordinates-button", SaveHudCoordinates);
        UnregisterButton(root, "reset-ui-layout-button", ResetHudCoordinates);

        if (videoCategoryButton != null)
            videoCategoryButton.clicked -= ShowVideoCategory;
        if (soundCategoryButton != null)
            soundCategoryButton.clicked -= ShowSoundCategory;
        if (interfaceCategoryButton != null)
            interfaceCategoryButton.clicked -= ShowInterfaceCategory;
    }

    private static void RegisterButton(VisualElement root, string name, System.Action callback)
    {
        Button button = root?.Q<Button>(name);
        if (button != null)
            button.clicked += callback;
    }

    private static void UnregisterButton(VisualElement root, string name, System.Action callback)
    {
        Button button = root?.Q<Button>(name);
        if (button != null)
            button.clicked -= callback;
    }

    private void ShowClosed()
    {
        currentPage = MenuPage.Closed;
        SetDisplay(overlay, false);
        GameUiModalService.SetOpen(this, false);
    }

    private void ShowMatchMenu()
    {
        currentPage = MenuPage.Match;
        SetDisplay(overlay, true);
        SetDisplay(matchPage, true);
        SetDisplay(settingsPage, false);
        GameUiModalService.SetOpen(this, true);
    }

    private void ShowSettings()
    {
        currentPage = MenuPage.Settings;
        SetDisplay(overlay, true);
        SetDisplay(matchPage, false);
        SetDisplay(settingsPage, true);
        ShowVideoCategory();
        ClearSettingsStatus();
        GameUiModalService.SetOpen(this, true);
    }

    private void ShowVideoCategory()
    {
        ShowCategory(SettingsCategory.Video);
    }

    private void ShowSoundCategory()
    {
        ShowCategory(SettingsCategory.Sound);
    }

    private void ShowInterfaceCategory()
    {
        ShowCategory(SettingsCategory.Interface);
    }

    private void ShowCategory(SettingsCategory category)
    {
        SetDisplay(videoContent, category == SettingsCategory.Video);
        SetDisplay(soundContent, category == SettingsCategory.Sound);
        SetDisplay(interfaceContent, category == SettingsCategory.Interface);

        SetSelectedCategory(videoCategoryButton, category == SettingsCategory.Video);
        SetSelectedCategory(soundCategoryButton, category == SettingsCategory.Sound);
        SetSelectedCategory(interfaceCategoryButton, category == SettingsCategory.Interface);

        if (saveStatusLabel != null)
            saveStatusLabel.text = string.Empty;
    }

    private static void SetSelectedCategory(Button button, bool selected)
    {
        if (button == null)
            return;

        if (selected)
            button.AddToClassList("game-settings-category-selected");
        else
            button.RemoveFromClassList("game-settings-category-selected");
    }


    private void SaveSettingsAndReturn()
    {
        GameSettingsPersistenceService.SaveResult result = GameSettingsPersistenceService.SaveAll();
        if (!result.Success)
        {
            if (settingsStatusLabel != null)
            {
                settingsStatusLabel.text =
                    "No se pudieron guardar todos los ajustes. Revisa la consola antes de volver.";
                settingsStatusLabel.AddToClassList("game-settings-status-error");
            }

            return;
        }

        ShowMatchMenu();
    }

    private void ClearSettingsStatus()
    {
        if (settingsStatusLabel == null)
            return;

        settingsStatusLabel.text = string.Empty;
        settingsStatusLabel.RemoveFromClassList("game-settings-status-error");
    }

    private void SaveHudCoordinates()
    {
        try
        {
            int savedCount = HudLayoutPersistenceService.SaveAllRegisteredPositions();
            if (saveStatusLabel != null)
            {
                saveStatusLabel.text = savedCount > 0
                    ? $"Coordenadas guardadas: {savedCount} paneles."
                    : "No hay paneles de HUD disponibles para guardar.";
                saveStatusLabel.RemoveFromClassList("game-settings-status-error");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[GamePauseMenuController] No se pudieron guardar las coordenadas del HUD: {exception}");
            if (saveStatusLabel != null)
            {
                saveStatusLabel.text = "No se pudieron escribir las coordenadas en disco.";
                saveStatusLabel.AddToClassList("game-settings-status-error");
            }
        }
    }

    private void ResetHudCoordinates()
    {
        try
        {
            int resetCount = HudLayoutPersistenceService.ResetToDefaults();
            if (saveStatusLabel != null)
            {
                saveStatusLabel.text = resetCount > 0
                    ? $"Interfaz restablecida: {resetCount} paneles volvieron a su posición inicial."
                    : "Se eliminó la distribución guardada. La próxima partida usará las posiciones predeterminadas.";
                saveStatusLabel.RemoveFromClassList("game-settings-status-error");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[GamePauseMenuController] No se pudo restablecer la interfaz: {exception}");
            if (saveStatusLabel != null)
            {
                saveStatusLabel.text = "No se pudo restablecer la interfaz.";
                saveStatusLabel.AddToClassList("game-settings-status-error");
            }
        }
    }

    private void ExitMatch()
    {
        ShowClosed();
        NetworkSessionManager.Instance?.Shutdown();
        MatchManager.Instance?.ClearMatch();
        SceneLoader.LoadMainMenu();
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element != null)
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
