using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class MainMenuController
{
    private void ShowSettingsMenu()
    {
        LoadScreen(settingsMenuUxml);
        VisualElement root = uiDocument.rootVisualElement;
        RegisterButton(root, "mp-menu-back-button", ShowMainMenu);
        RegisterButton(root, "mp-settings-button", () => ShowSettingsMultiPlayerMenu(ShowSettingsMenu));
        RegisterButton(root, "graphics-setting-button", () => Debug.Log("[Settings] Gráficos pendiente."));
        RegisterButton(root, "sound-setting-button", () => Debug.Log("[Settings] Sonido pendiente."));
    }

    private void ShowSettingsMultiPlayerMenu(Action backAction)
    {
        settingsBackAction = backAction;
        LoadScreen(settingsMultiPlayerUxml);
        VisualElement root = uiDocument.rootVisualElement;
        TextField nameField = root.Q<TextField>("mp-setting-name-field");
        Label successLabel = root.Q<Label>("success-change-label");
        if (nameField != null) nameField.value = GetSavedPlayerName();

        RegisterButton(root, "mp-menu-back-button", () => settingsBackAction?.Invoke());
        RegisterButton(root, "mp-change-name-button", () =>
        {
            SavePlayerName(nameField?.value);
            if (successLabel != null) successLabel.text = $"Nombre guardado: {GetSavedPlayerName()}";
        });
    }
}
