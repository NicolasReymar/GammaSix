using System;
using System.Collections.Generic;
using System.Linq;
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

        selectedScenarioDefinition = scenario;
        SelectedScenarioId = string.IsNullOrWhiteSpace(scenario?.id)
            ? (contentType == GameContentType.Scenario ? SelectedContentId : "test_scenario_01")
            : scenario.id;
        SelectedPackageId = entry != null && entry.IsPackaged ? entry.PackageId : scenario?.sourcePackageId;
        SelectedPackageVersion = entry != null && entry.IsPackaged ? entry.PackageVersion : scenario?.sourcePackageVersion;
        SelectedContentHash = entry != null && entry.IsPackaged ? entry.ContentHash : scenario?.sourceContentHash;
        contentCompatibilityByClient.Clear();
        SelectedScenarioMaxTeams = scenario != null && scenario.maxTeams > 0 ? scenario.maxTeams : MaxTeams;

        ConfigureParticipantRules(scenario);

        // El contenido solo entrega el valor inicial del lobby. Desde este punto,
        // el host puede sobrescribirlo antes de iniciar la partida.
        FixedTeamsForcedByScenario = false;
        hostFixedTeamsSetting = scenario != null && scenario.fixedTeams;

        LoadOverridesFromScenario(scenario);
        ApplyEnabledOverrides();
        ResetHumanReadiness();
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
        if (item == null)
            return false;

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

        if (HumanPlayerCount == 0)
        {
            SetStatus("No hay jugadores humanos conectados.");
            return false;
        }

        if (!AllPlayersReady)
        {
            SetStatus("Todos los jugadores humanos deben estar listos antes de iniciar.");
            return false;
        }

        if (HumanPlayerCount > SelectedScenarioMaxPlayers)
        {
            SetStatus($"El escenario admite un máximo de {SelectedScenarioMaxPlayers} jugadores humanos y hay {HumanPlayerCount} conectados.");
            return false;
        }

        if (players.Count > SelectedScenarioMaxParticipants)
        {
            SetStatus($"El escenario admite un máximo de {SelectedScenarioMaxParticipants} participantes y hay {players.Count} configurados.");
            return false;
        }

        if (!AllRemoteClientsContentCompatible)
        {
            SetStatus("Uno o más clientes no poseen el mismo paquete, versión o hash del escenario seleccionado.");
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

    private void ConfigureParticipantRules(ScenarioDefinition scenario)
    {
        ScenarioParticipantConfiguration configuration = scenario?.participantConfiguration;
        int fallbackHumans = scenario != null && scenario.maxPlayers > 0 ? scenario.maxPlayers : MaxPlayers;
        SelectedScenarioMaxPlayers = Mathf.Clamp(
            configuration != null && configuration.maximumHumanPlayers > 0
                ? configuration.maximumHumanPlayers
                : fallbackHumans,
            1,
            MaxPlayers);

        int totalParticipants = configuration != null && configuration.maximumParticipants > 0
            ? configuration.maximumParticipants
            : SelectedScenarioMaxPlayers;

        ScenarioRequiredParticipantDefinition[] required = configuration?.requiredParticipants;
        if (required != null)
        {
            int implicitRequiredCount = required.Count(item => item != null && item.slotIndex < 0);
            int highestExplicitSlot = required
                .Where(item => item != null && item.slotIndex >= 0)
                .Select(item => item.slotIndex + 1)
                .DefaultIfEmpty(0)
                .Max();
            totalParticipants = Mathf.Max(totalParticipants, SelectedScenarioMaxPlayers + implicitRequiredCount, highestExplicitSlot);
        }

        SelectedScenarioMaxParticipants = Mathf.Clamp(
            Mathf.Max(SelectedScenarioMaxPlayers, totalParticipants),
            1,
            MaxParticipants);
        SelectedGameModeId = HeadlessProfileCatalog.ResolveGameModeId(scenario);

        availableHeadlessProfiles.Clear();
        availableHeadlessProfiles.AddRange(HeadlessProfileCatalog.GetAvailableProfiles(scenario));

        bool rosterChanged = RemoveIncompatibleOptionalHeadless();
        rosterChanged |= NormalizeParticipantSlots();
        rosterChanged |= EnsureRequiredHeadlessParticipants(required);

        if (rosterChanged)
            ResetHumanReadiness();
    }

    private bool RemoveIncompatibleOptionalHeadless()
    {
        HashSet<string> availableIds = new(
            availableHeadlessProfiles.Select(item => item.Id),
            StringComparer.OrdinalIgnoreCase);

        int removed = players.RemoveAll(item =>
            item.IsHeadless &&
            !item.ParticipantLocked &&
            !availableIds.Contains(item.ControllerProfileId));
        return removed > 0;
    }

    private bool NormalizeParticipantSlots()
    {
        bool changed = false;

        while (players.Count > SelectedScenarioMaxParticipants)
        {
            NetworkPlayerInfo removable = players
                .Where(item => item.IsHeadless && !item.ParticipantLocked)
                .OrderByDescending(item => item.SlotIndex)
                .FirstOrDefault();
            if (removable == null)
                break;
            players.Remove(removable);
            changed = true;
        }

        HashSet<int> occupied = new();
        foreach (NetworkPlayerInfo participant in players.OrderBy(item => item.SlotIndex).ThenBy(item => item.ParticipantId))
        {
            bool invalid = participant.SlotIndex < 0 ||
                           participant.SlotIndex >= SelectedScenarioMaxParticipants ||
                           !occupied.Add(participant.SlotIndex);
            if (!invalid)
                continue;

            int replacement = -1;
            for (int candidate = 0; candidate < SelectedScenarioMaxParticipants; candidate++)
            {
                if (occupied.Contains(candidate))
                    continue;
                replacement = candidate;
                break;
            }
            if (replacement < 0)
                continue;

            participant.SlotIndex = replacement;
            participant.SlotId = GetDefaultSlotId(replacement);
            occupied.Add(replacement);
            changed = true;
        }

        SortParticipants();
        return changed;
    }

    private bool EnsureRequiredHeadlessParticipants(ScenarioRequiredParticipantDefinition[] requiredDefinitions)
    {
        if (requiredDefinitions == null || requiredDefinitions.Length == 0)
            return false;

        bool changed = false;
        foreach (ScenarioRequiredParticipantDefinition required in requiredDefinitions)
        {
            if (required == null || string.IsNullOrWhiteSpace(required.controllerProfileId))
                continue;

            string slotId = string.IsNullOrWhiteSpace(required.slotId)
                ? null
                : required.slotId.Trim();
            NetworkPlayerInfo existing = players.FirstOrDefault(item =>
                item.IsHeadless &&
                ((!string.IsNullOrWhiteSpace(slotId) && string.Equals(item.SlotId, slotId, StringComparison.OrdinalIgnoreCase)) ||
                 (item.ParticipantLocked && string.Equals(item.ControllerProfileId, required.controllerProfileId, StringComparison.OrdinalIgnoreCase))));

            if (existing != null)
            {
                existing.ParticipantLocked = required.participantLocked;
                existing.TeamLocked = required.teamLocked;
                existing.ColorLocked = required.colorLocked;
                continue;
            }

            HeadlessProfileDefinition profile = availableHeadlessProfiles.FirstOrDefault(item =>
                string.Equals(item.Id, required.controllerProfileId, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                Debug.LogWarning($"[NetworkSession] El participante obligatorio usa un perfil no registrado: {required.controllerProfileId}");
                continue;
            }

            int slotIndex = required.slotIndex;
            if (slotIndex < 0 || slotIndex >= SelectedScenarioMaxParticipants || players.Any(item => item.SlotIndex == slotIndex))
                slotIndex = FindFirstFreeSlotIndex(SelectedScenarioMaxPlayers, SelectedScenarioMaxParticipants);
            if (slotIndex < 0)
                slotIndex = FindFirstFreeSlotIndex(0, SelectedScenarioMaxParticipants);
            if (slotIndex < 0)
            {
                Debug.LogError($"[NetworkSession] No existe una casilla libre para el participante obligatorio {profile.DisplayName}.");
                continue;
            }

            int teamId = Mathf.Clamp(required.teamId, 1, Mathf.Max(1, SelectedScenarioMaxTeams));
            int colorId = required.colorId >= 0
                ? PlayerColorPalette.Normalize(required.colorId)
                : GetInitialColorForTeam(teamId);
            int participantId = AllocateParticipantId();
            NetworkPlayerInfo participant = NetworkPlayerInfo.CreateHeadless(
                participantId,
                string.IsNullOrWhiteSpace(slotId) ? GetDefaultSlotId(slotIndex) : slotId,
                slotIndex,
                string.IsNullOrWhiteSpace(required.displayName) ? profile.DisplayName : required.displayName.Trim(),
                teamId,
                colorId,
                profile.Id,
                profile.SourceId,
                required.participantLocked,
                required.teamLocked,
                required.colorLocked);
            players.Add(participant);
            changed = true;
        }

        SortParticipants();
        return changed;
    }

    private void LoadOverridesFromScenario(ScenarioDefinition scenario)
    {
        activeOverrides.Clear();
        if (scenario?.settingOverrides == null)
            return;

        foreach (ScenarioSettingOverride item in scenario.settingOverrides)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.key))
                continue;
            activeOverrides.Add(new ActiveSettingOverride(item));
        }
    }

    private void ApplyEnabledOverrides()
    {
        ActiveSettingOverride fixedColorsOverride = activeOverrides
            .FirstOrDefault(item => item.Enabled && string.Equals(item.Key, "fixedColors", StringComparison.OrdinalIgnoreCase));
        if (fixedColorsOverride != null && bool.TryParse(fixedColorsOverride.Value, out bool fixedColors))
            FixedColors = fixedColors;

        FixedTeams = hostFixedTeamsSetting;
        ActiveSettingOverride fixedTeamsOverride = activeOverrides
            .FirstOrDefault(item => item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
        if (fixedTeamsOverride != null && bool.TryParse(fixedTeamsOverride.Value, out bool fixedTeams))
            FixedTeams = fixedTeams;

        if (!FixedColors)
            return;

        foreach (NetworkPlayerInfo player in players)
        {
            int configuredColor = GetConfiguredTeamColorId(player.TeamId);
            if (configuredColor < 0 || player.ColorId == configuredColor || player.ColorLocked)
                continue;

            player.ColorId = configuredColor;
            if (player.IsHuman)
                player.IsReady = false;
        }
    }
}
