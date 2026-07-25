using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class NetworkSessionManager
{
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
}
