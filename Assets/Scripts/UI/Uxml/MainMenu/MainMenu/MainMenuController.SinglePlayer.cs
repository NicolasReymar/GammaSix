using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class MainMenuController
{
    private void ShowSinglePlayerMenu()
    {
        LoadScreen(singlePlayerMenuUxml);
        VisualElement root = uiDocument.rootVisualElement;
        RegisterButton(root, "single-player-back-button", ShowMainMenu);
        RegisterButton(root, "sp-campaign-button", ShowSinglePlayerCampaignMenu);
        RegisterButton(root, "sp-skirmish-button", ShowSinglePlayerSkirmishMenu);
    }

    private void ShowSinglePlayerCampaignMenu()
    {
        LoadScreen(singlePlayerCampaignUxml);
        VisualElement root = uiDocument.rootVisualElement;
        RegisterButton(root, "sp-campaign-back-button", ShowSinglePlayerMenu);
        RegisterButton(root, "sp-campaign-select-button", () => Debug.Log("[Campaña] Selección pendiente."));
        RegisterButton(root, "sp-campaign-start-button", () => Debug.Log("[Campaña] Inicio pendiente."));
    }

    private void ShowSinglePlayerSkirmishMenu()
    {
        LoadScreen(singlePlayerSkirmishUxml);
        pendingSelectedScenarioId = null;
        confirmedScenarioId = null;

        VisualElement root = uiDocument.rootVisualElement;
        selectedMapLabel = root.Q<Label>("sp-selected-map-label");
        statusLabel = root.Q<Label>("sp-status-label");
        if (selectedMapLabel != null) selectedMapLabel.text = "Ninguna";
        SetStatus("Selecciona una escaramuza para continuar.");

        RegisterButton(root, "sp-menu-back-button", ShowSinglePlayerMenu);
        RegisterButton(root, "sp-select-skirmish-button", ConfirmSelectedMap);
        RegisterButton(root, "sp-menu-start-button", StartSinglePlayerMatch);
        LoadMapList(root, "sp-map-browser-panel");
    }

    private void StartSinglePlayerMatch()
    {
        if (string.IsNullOrEmpty(confirmedScenarioId))
        {
            SetStatus("Debes confirmar un mapa antes de comenzar.");
            return;
        }

        MatchManager.Instance.CreateMatch(MatchConfigFactory.CreateSinglePlayerDefault(confirmedScenarioId));
        SceneLoader.LoadGameScene();
    }
}
