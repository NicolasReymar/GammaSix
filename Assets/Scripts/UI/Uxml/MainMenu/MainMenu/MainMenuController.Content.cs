using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class MainMenuController
{
    private void LoadMapList(VisualElement root, string foldoutName)
    {
        Foldout mapBrowserPanel = root.Q<Foldout>(foldoutName);
        if (mapBrowserPanel == null) return;

        mapBrowserPanel.Clear();
        IReadOnlyList<GameContentEntry> content = GameContentRepository.LoadAllContent();
        if (content.Count == 0)
        {
            mapBrowserPanel.Add(new Label("No hay escenarios ni campañas guardados. Se usará test_scenario_01."));
            pendingSelectedScenarioId = "test_scenario_01";
            pendingContentType = GameContentType.Scenario;
            return;
        }

        foreach (IGrouping<GameContentType, GameContentEntry> group in content.GroupBy(item => item.ContentType))
        {
            Label section = new(group.Key == GameContentType.Campaign ? "CAMPAÑAS" : "ESCENARIOS");
            section.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.style.marginTop = 8;
            section.style.marginBottom = 4;
            mapBrowserPanel.Add(section);

            foreach (GameContentEntry item in group)
            {
                string sourceSuffix = item.IsPackaged ? $"  [Paquete {item.PackageVersion}]" : string.Empty;
                Button contentButton = new() { text = item.DisplayName + sourceSuffix };
                contentButton.tooltip = item.IsPackaged
                    ? $"{item.Description}\nPaquete: {item.PackageId}\nHash: {item.ContentHash}"
                    : item.Description;
                contentButton.clicked += () =>
                {
                    pendingSelectedScenarioId = item.ContentId;
                    pendingContentType = item.ContentType;
                    SetStatus($"{(item.ContentType == GameContentType.Campaign ? "Campaña" : "Escenario")} pendiente: {item.DisplayName}");
                    if (isHostSetupScreen)
                        RefreshHostContentPreview(item.ContentId, item.ContentType);
                };
                mapBrowserPanel.Add(contentButton);
            }
        }
    }

    private void ConfirmSelectedMap()
    {
        if (string.IsNullOrEmpty(pendingSelectedScenarioId))
        {
            SetStatus("Debes seleccionar un contenido primero.");
            return;
        }

        confirmedScenarioId = pendingSelectedScenarioId;
        confirmedContentType = pendingContentType;

        if (isMultiplayerGameScreen)
        {
            if (NetworkSessionManager.Instance == null || !NetworkSessionManager.Instance.SelectGameContent(confirmedScenarioId, confirmedContentType))
                return;
        }

        if (selectedMapLabel != null)
            selectedMapLabel.text = $"{(confirmedContentType == GameContentType.Campaign ? "Campaña" : "Escenario")}: {confirmedScenarioId}";

        if (isHostSetupScreen)
            RefreshHostContentPreview(confirmedScenarioId, confirmedContentType);

        SetStatus($"Contenido confirmado: {confirmedScenarioId}");
    }

    private void RefreshContentOverrides()
    {
        // Los overrides siguen existiendo en el estado de la sesión y se aplican al cargar
        // el contenido, pero ya no se muestran como una segunda tabla editable.
        // Las opciones superiores son la única interfaz de configuración del lobby.
        if (uiDocument == null)
            return;

        VisualElement panel = uiDocument.rootVisualElement.Q<VisualElement>("mp-content-overrides-panel");
        if (panel != null)
        {
            panel.Clear();
            panel.style.display = DisplayStyle.None;
        }
    }

    private void HandleNetworkScenarioChanged(string scenarioId)
    {
        if (!isMultiplayerGameScreen)
            return;

        confirmedScenarioId = string.IsNullOrWhiteSpace(scenarioId)
            ? "test_scenario_01"
            : scenarioId;

        if (selectedMapLabel != null)
            selectedMapLabel.text = confirmedScenarioId;
    }

    private void RefreshHostContentPreview(string contentId, GameContentType contentType)
    {
        VisualElement panel = uiDocument.rootVisualElement.Q<VisualElement>("mp-host-preview-panel");
        if (panel == null) return;
        panel.Clear();

        GameContentEntry entry = GameContentRepository.LoadAllContent().FirstOrDefault(item => item.ContentId == contentId && item.ContentType == contentType);
        ScenarioDefinition scenario = entry != null ? GameContentRepository.ResolveFirstScenario(entry) : GameContentRepository.LoadScenario(contentId);
        if (scenario == null)
        {
            panel.style.display = DisplayStyle.None;
            return;
        }

        panel.style.display = DisplayStyle.Flex;
        Label title = new("Previsualización de configuración");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 6;
        panel.Add(title);
        panel.Add(new Label($"Máximo de jugadores: {Mathf.Clamp(scenario.maxPlayers, 1, 8)}"));
        panel.Add(new Label($"Máximo de equipos: {Mathf.Clamp(scenario.maxTeams, 1, 4)}"));
        panel.Add(new Label($"Equipos fijos: {(scenario.fixedTeams ? "Sí" : "No")}"));
        if (!string.IsNullOrWhiteSpace(scenario.sourcePackageId))
        {
            panel.Add(new Label($"Paquete: {scenario.sourcePackageId} {scenario.sourcePackageVersion}"));
            panel.Add(new Label($"Hash: {scenario.sourceContentHash}"));
        }

        if (scenario.settingOverrides == null || scenario.settingOverrides.Length == 0)
        {
            panel.Add(new Label("Sin configuraciones prioritarias."));
            return;
        }

        foreach (ScenarioSettingOverride item in scenario.settingOverrides)
            panel.Add(new Label($"• {item.displayName}: {item.value}"));
    }
}
