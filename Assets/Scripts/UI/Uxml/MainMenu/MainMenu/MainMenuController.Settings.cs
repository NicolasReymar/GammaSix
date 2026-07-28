using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class MainMenuController
{
    private enum MainSettingsCategory
    {
        Video,
        Sound,
        Interface,
        Multiplayer
    }

    private void ShowSettingsMenu()
    {
        LoadScreen(settingsMenuUxml);
        VisualElement root = uiDocument.rootVisualElement;

        Button videoButton = root.Q<Button>("graphics-setting-button");
        Button soundButton = root.Q<Button>("sound-setting-button");
        Button interfaceButton = root.Q<Button>("interface-setting-button");
        Button multiplayerButton = root.Q<Button>("mp-settings-button");

        VisualElement videoContent = root.Q<VisualElement>("settings-video-content");
        VisualElement soundContent = root.Q<VisualElement>("settings-sound-content");
        VisualElement interfaceContent = root.Q<VisualElement>("settings-interface-content");
        VisualElement multiplayerContent = root.Q<VisualElement>("settings-multiplayer-content");
        Label interfaceStatus = root.Q<Label>("settings-interface-status-label");
        Label globalStatus = root.Q<Label>("settings-global-status-label");

        void ShowCategory(MainSettingsCategory category)
        {
            SetMainSettingsDisplay(videoContent, category == MainSettingsCategory.Video);
            SetMainSettingsDisplay(soundContent, category == MainSettingsCategory.Sound);
            SetMainSettingsDisplay(interfaceContent, category == MainSettingsCategory.Interface);
            SetMainSettingsDisplay(multiplayerContent, category == MainSettingsCategory.Multiplayer);

            SetMainSettingsCategorySelected(videoButton, category == MainSettingsCategory.Video);
            SetMainSettingsCategorySelected(soundButton, category == MainSettingsCategory.Sound);
            SetMainSettingsCategorySelected(interfaceButton, category == MainSettingsCategory.Interface);
            SetMainSettingsCategorySelected(multiplayerButton, category == MainSettingsCategory.Multiplayer);

            if (globalStatus != null)
                globalStatus.text = string.Empty;
        }

        if (videoButton != null)
            videoButton.clicked += () => ShowCategory(MainSettingsCategory.Video);
        if (soundButton != null)
            soundButton.clicked += () => ShowCategory(MainSettingsCategory.Sound);
        if (interfaceButton != null)
            interfaceButton.clicked += () => ShowCategory(MainSettingsCategory.Interface);
        if (multiplayerButton != null)
            multiplayerButton.clicked += () => ShowCategory(MainSettingsCategory.Multiplayer);

        RegisterButton(root, "mp-menu-back-button", ShowMainMenu);
        RegisterButton(root, "settings-save-back-button", () => SaveMainMenuSettingsAndReturn(globalStatus));
        RegisterButton(root, "settings-open-multiplayer-button", () => ShowSettingsMultiPlayerMenu(ShowSettingsMenu));
        RegisterButton(root, "settings-reset-interface-button", () => ResetInterfaceLayout(interfaceStatus));

        ShowCategory(MainSettingsCategory.Video);
    }

    private static void SetMainSettingsCategorySelected(Button button, bool selected)
    {
        if (button == null)
            return;

        button.EnableInClassList("gs-settings-nav-selected", selected);
    }

    private static void SetMainSettingsDisplay(VisualElement element, bool visible)
    {
        if (element != null)
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SaveMainMenuSettingsAndReturn(Label statusLabel)
    {
        GameSettingsPersistenceService.SaveResult result = GameSettingsPersistenceService.SaveAll();
        if (!result.Success)
        {
            if (statusLabel != null)
            {
                statusLabel.text = "No se pudieron guardar todos los ajustes. Revisa la consola.";
                statusLabel.AddToClassList("gs-status-error");
            }
            return;
        }

        ShowMainMenu();
    }

    private static void ResetInterfaceLayout(Label statusLabel)
    {
        try
        {
            int resetCount = HudLayoutPersistenceService.ResetToDefaults();
            if (statusLabel != null)
            {
                statusLabel.text = resetCount > 0
                    ? $"Interfaz restablecida: {resetCount} paneles volvieron a su posición inicial."
                    : "Distribución guardada eliminada. La próxima partida usará la interfaz predeterminada.";
                statusLabel.RemoveFromClassList("gs-status-error");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Settings] No se pudo restablecer la interfaz: {exception}");
            if (statusLabel != null)
            {
                statusLabel.text = "No se pudo restablecer la interfaz. Revisa la consola.";
                statusLabel.AddToClassList("gs-status-error");
            }
        }
    }

    private void ShowSettingsMultiPlayerMenu(Action backAction)
    {
        settingsBackAction = backAction;
        LoadScreen(settingsMultiPlayerUxml);
        VisualElement root = uiDocument.rootVisualElement;
        TextField nameField = root.Q<TextField>("mp-setting-name-field");
        Label successLabel = root.Q<Label>("success-change-label");
        if (nameField != null)
            nameField.value = GetSavedPlayerName();

        RegisterButton(root, "mp-menu-back-button", () => settingsBackAction?.Invoke());
        RegisterButton(root, "mp-change-name-button", () =>
        {
            SavePlayerName(nameField?.value);
            if (successLabel != null)
                successLabel.text = $"Nombre guardado: {GetSavedPlayerName()}";
        });
    }
}
