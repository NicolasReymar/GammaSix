using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class MainMenuController
{
    private void ShowMultiPlayerMenu()
    {
        LoadScreen(multiPlayerMenuUxml);
        VisualElement root = uiDocument.rootVisualElement;
        RegisterButton(root, "mp-menu-back-button", ShowMainMenu);
        RegisterButton(root, "mp-search-match-button", ShowMultiPlayerJoinMenu);
        RegisterButton(root, "mp-skirmish-button", ShowMultiPlayerHostSetupMenu);
        RegisterButton(root, "mp-change-name-button", () => ShowSettingsMultiPlayerMenu(ShowMultiPlayerMenu));
    }

    private void ShowMultiPlayerJoinMenu()
    {
        LoadScreen(multiPlayerJoinUxml);
        VisualElement root = uiDocument.rootVisualElement;
        statusLabel = root.Q<Label>("mp-network-status-label");

        TextField nameField = root.Q<TextField>("mp-player-name-field");
        TextField addressField = root.Q<TextField>("mp-address-field");
        IntegerField portField = root.Q<IntegerField>("mp-port-field");

        if (nameField != null)
        {
            nameField.value = GetSavedPlayerName();
            nameField.isReadOnly = true;
            nameField.SetEnabled(false);
        }
        if (addressField != null) addressField.value = "127.0.0.1";
        if (portField != null) portField.value = 7777;

        RegisterButton(root, "mp-join-menu-back-button", ShowMultiPlayerMenu);
        RegisterButton(root, "mp-join-button", () =>
        {
            ushort port = ParsePort(portField?.value ?? 7777);
            if (NetworkSessionManager.Instance.StartClient(addressField?.value, port, GetSavedPlayerName()))
                ShowMultiPlayerGameMenu();
        });
    }

    private void ShowMultiPlayerHostSetupMenu()
    {
        LoadScreen(multiPlayerGameMenuUxml);
        isHostSetupScreen = true;
        pendingSelectedScenarioId = null;
        confirmedScenarioId = null;
        pendingContentType = GameContentType.Scenario;
        confirmedContentType = GameContentType.Scenario;

        VisualElement root = uiDocument.rootVisualElement;
        RegisterMultiplayerResponsiveLayout(root);
        root.Q<VisualElement>("mp-create-session-view").style.display = DisplayStyle.Flex;
        root.Q<VisualElement>("mp-lobby-view").style.display = DisplayStyle.None;
        root.Q<Label>("mp-screen-subtitle").text = "Configura la sesión antes de abrir el lobby.";
        statusLabel = root.Q<Label>("mp-network-status-label");
        selectedMapLabel = root.Q<Label>("mp-selected-map-label");

        TextField hostNameField = root.Q<TextField>("mp-host-name-field");
        IntegerField portField = root.Q<IntegerField>("mp-host-port-field");
        if (hostNameField != null) hostNameField.value = GetSavedPlayerName();
        if (portField != null) portField.value = 7777;

        RegisterButton(root, "mp-menu-back-button", ShowMultiPlayerMenu);
        RegisterButton(root, "mp-select-skirmish-button", ConfirmSelectedMap);
        RegisterButton(root, "mp-host-button", () =>
        {
            if (string.IsNullOrWhiteSpace(confirmedScenarioId))
            {
                SetStatus("Debes seleccionar y confirmar un escenario o campaña.");
                return;
            }

            SavePlayerName(hostNameField?.value);
            bool started = NetworkSessionManager.Instance.StartHost(ParsePort(portField?.value ?? 7777), GetSavedPlayerName());
            if (!started) return;

            if (!NetworkSessionManager.Instance.SelectGameContent(confirmedScenarioId, confirmedContentType))
            {
                NetworkSessionManager.Instance.Shutdown();
                return;
            }

            ShowMultiPlayerGameMenu();
        });

        LoadMapList(root, "mp-map-browser-panel");
        SetStatus("Selecciona y confirma el contenido para crear la sesión.");
    }

    private void ShowMultiPlayerGameMenu()
    {
        LoadScreen(multiPlayerGameMenuUxml);
        isMultiplayerGameScreen = true;
        confirmedScenarioId = NetworkSessionManager.Instance?.SelectedContentId;
        confirmedContentType = NetworkSessionManager.Instance?.SelectedContentType ?? GameContentType.Scenario;

        VisualElement root = uiDocument.rootVisualElement;
        RegisterMultiplayerResponsiveLayout(root);
        root.Q<VisualElement>("mp-create-session-view").style.display = DisplayStyle.None;
        root.Q<VisualElement>("mp-lobby-view").style.display = DisplayStyle.Flex;
        root.Q<Label>("mp-screen-subtitle").text = "Sala de espera: organiza jugadores, equipos y configuraciones antes de iniciar.";
        root.Q<Button>("mp-menu-back-button").text = "Salir de la sesión";
        statusLabel = root.Q<Label>("mp-lobby-status-label");
        Label lobbyContentLabel = root.Q<Label>("mp-lobby-content-label");
        if (lobbyContentLabel != null)
            lobbyContentLabel.text = $"{(confirmedContentType == GameContentType.Campaign ? "Campaña" : "Escenario")}: {confirmedScenarioId ?? "Ninguno"}";

        RegisterButton(root, "mp-menu-back-button", () =>
        {
            NetworkSessionManager.Instance?.Shutdown();
            ShowMultiPlayerMenu();
        });
        RegisterButton(root, "mp-ready-button", () => NetworkSessionManager.Instance?.ToggleLocalReady());
        RegisterButton(root, "mp-menu-start-button", StartMultiplayerMatch);
        RegisterButton(root, "mp-headless-button", ToggleHeadlessPanel);

        Toggle fixedColorsToggle = root.Q<Toggle>("mp-fixed-colors-toggle");
        if (fixedColorsToggle != null)
        {
            fixedColorsToggle.SetValueWithoutNotify(NetworkSessionManager.Instance?.FixedColors ?? false);
            fixedColorsToggle.RegisterValueChangedCallback(evt =>
            {
                NetworkSessionManager session = NetworkSessionManager.Instance;
                if (session == null || !session.IsHost)
                    return;

                ActiveSettingOverride activeOverride = session.ActiveOverrides.FirstOrDefault(item =>
                    item.Enabled && string.Equals(item.Key, "fixedColors", StringComparison.OrdinalIgnoreCase));
                if (activeOverride != null)
                    session.SetOverrideEnabled("fixedColors", false);

                session.SetFixedColors(evt.newValue);
            });
        }

        Toggle fixedTeamsToggle = root.Q<Toggle>("mp-fixed-teams-toggle");
        if (fixedTeamsToggle != null)
        {
            fixedTeamsToggle.SetValueWithoutNotify(NetworkSessionManager.Instance?.FixedTeams ?? false);
            fixedTeamsToggle.RegisterValueChangedCallback(evt =>
            {
                NetworkSessionManager session = NetworkSessionManager.Instance;
                if (session == null || !session.IsHost)
                    return;

                ActiveSettingOverride activeOverride = session.ActiveOverrides.FirstOrDefault(item =>
                    item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
                if (activeOverride != null)
                    session.SetOverrideEnabled("fixedTeams", false);

                session.SetFixedTeams(evt.newValue);
            });
        }

        RefreshContentOverrides();
        RefreshMultiplayerPlayers();
        RefreshHeadlessPanel();
        RefreshNetworkControls();
    }

    private void StartMultiplayerMatch()
    {
        if (string.IsNullOrEmpty(confirmedScenarioId))
            confirmedScenarioId = "test_scenario_01";

        NetworkSessionManager.Instance?.StartNetworkMatch(confirmedScenarioId);
    }

    private void RefreshMultiplayerPlayers()
    {
        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        VisualElement list = root.Q<VisualElement>("mp-player-list");
        if (list == null)
            return;

        list.Clear();
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session == null)
            return;

        IReadOnlyList<NetworkPlayerInfo> participants = session.Players;
        int slotCount = Mathf.Clamp(session.SelectedScenarioMaxParticipants, 1, 16);
        int localParticipantId = session.GetLocalPlayer()?.ParticipantId ?? -1;

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            NetworkPlayerInfo participant = participants.FirstOrDefault(item => item.SlotIndex == slotIndex);
            VisualElement row = CreatePlayerSlotRow(slotIndex + 1, participant, session, localParticipantId);
            list.Add(row);
        }

        RefreshHeadlessPanel();
        RefreshNetworkControls();
    }

    private VisualElement CreatePlayerSlotRow(
        int slotNumber,
        NetworkPlayerInfo participant,
        NetworkSessionManager session,
        int localParticipantId)
    {
        VisualElement row = new();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        row.style.minHeight = 76;
        row.style.marginBottom = 10;
        row.style.paddingLeft = 14;
        row.style.paddingRight = 14;
        row.style.paddingTop = 10;
        row.style.paddingBottom = 10;
        row.style.backgroundColor = participant == null
            ? new Color(1f, 1f, 1f, 0.035f)
            : participant.IsHeadless
                ? new Color(0.18f, 0.55f, 0.42f, 0.16f)
                : new Color(1f, 1f, 1f, 0.075f);
        row.style.borderLeftWidth = 1;
        row.style.borderRightWidth = 1;
        row.style.borderTopWidth = 1;
        row.style.borderBottomWidth = 1;
        Color borderColor = participant != null && participant.IsHeadless
            ? new Color(0.25f, 0.90f, 0.64f, 0.42f)
            : new Color(0.35f, 0.55f, 0.46f, 0.28f);
        row.style.borderLeftColor = borderColor;
        row.style.borderRightColor = borderColor;
        row.style.borderTopColor = borderColor;
        row.style.borderBottomColor = borderColor;
        row.style.borderTopLeftRadius = 10;
        row.style.borderTopRightRadius = 10;
        row.style.borderBottomLeftRadius = 10;
        row.style.borderBottomRightRadius = 10;

        Label slotLabel = new($"Casilla {slotNumber}");
        slotLabel.style.width = 76;
        slotLabel.style.fontSize = 12;
        slotLabel.style.color = new Color(0.62f, 0.75f, 0.69f, 1f);
        row.Add(slotLabel);

        if (participant == null)
        {
            Label empty = new("Casilla vacía");
            empty.style.flexGrow = 1;
            empty.style.fontSize = 16;
            empty.style.unityFontStyleAndWeight = FontStyle.Italic;
            empty.style.color = new Color(0.52f, 0.65f, 0.59f, 1f);
            row.Add(empty);
            return row;
        }

        VisualElement identity = new();
        identity.style.flexGrow = 1;
        identity.style.minWidth = 170;
        string localSuffix = participant.IsHuman && participant.ParticipantId == localParticipantId
            ? " (Tú)"
            : string.Empty;
        Label name = new(participant.PlayerName + localSuffix);
        name.style.fontSize = 17;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = new Color(0.94f, 0.98f, 0.96f, 1f);
        identity.Add(name);

        string statusText;
        Color statusColor;
        if (participant.IsHeadless)
        {
            string source = string.Equals(participant.ControllerSourceId, "base", StringComparison.OrdinalIgnoreCase)
                ? "BASE"
                : "ESCENARIO";
            statusText = participant.ParticipantLocked
                ? $"HEADLESS · {source} · OBLIGATORIO"
                : $"HEADLESS · {source}";
            statusColor = new Color(0.34f, 0.93f, 0.66f);
        }
        else
        {
            statusText = participant.IsReady ? "LISTO" : "NO LISTO";
            statusColor = participant.IsReady
                ? new Color(0.32f, 0.90f, 0.55f)
                : new Color(1f, 0.43f, 0.43f);
        }

        Label status = new(statusText);
        status.style.fontSize = 12;
        status.style.color = statusColor;
        identity.Add(status);
        row.Add(identity);

        bool canEditOwn = participant.IsHuman &&
                          participant.ParticipantId == localParticipantId &&
                          !participant.IsReady;
        bool canHostEdit = session.IsHost;

        Button teamButton = new() { text = $"Equipo {participant.TeamId}" };
        teamButton.AddToClassList("gs-button");
        teamButton.style.width = 112;
        teamButton.style.height = 38;
        teamButton.style.marginRight = 8;
        bool canEditTeam = !session.FixedTeams &&
                           !participant.TeamLocked &&
                           (canHostEdit || canEditOwn);
        teamButton.SetEnabled(canEditTeam);
        teamButton.tooltip = participant.TeamLocked
            ? "El equipo está bloqueado por el escenario."
            : session.FixedTeams
                ? "Los equipos están fijados por la configuración de la partida."
                : string.Empty;
        teamButton.clicked += () => ShowTeamPicker(teamButton, participant);
        row.Add(teamButton);

        Button colorButton = new() { text = PlayerColorPalette.GetName(participant.ColorId) };
        colorButton.AddToClassList("gs-button");
        colorButton.style.width = 118;
        colorButton.style.height = 38;
        colorButton.style.marginRight = participant.IsHeadless && canHostEdit && !participant.ParticipantLocked ? 8 : 0;
        colorButton.style.backgroundColor = PlayerColorPalette.GetColor(participant.ColorId);
        colorButton.style.color = Color.white;
        bool canEditColor = !participant.ColorLocked &&
                            (canHostEdit || (canEditOwn && !session.FixedColors));
        colorButton.SetEnabled(canEditColor);
        colorButton.tooltip = participant.ColorLocked ? "El color está bloqueado por el escenario." : string.Empty;
        colorButton.clicked += () => ShowColorPicker(colorButton, participant);
        row.Add(colorButton);

        if (participant.IsHeadless && canHostEdit && !participant.ParticipantLocked)
        {
            Button removeButton = new() { text = "Quitar" };
            removeButton.AddToClassList("gs-button");
            removeButton.style.height = 38;
            removeButton.style.minWidth = 82;
            removeButton.clicked += () => session.RemoveHeadlessParticipant(participant.ParticipantId);
            row.Add(removeButton);
        }

        return row;
    }

    private void ToggleHeadlessPanel()
    {
        if (uiDocument == null)
            return;

        VisualElement panel = uiDocument.rootVisualElement.Q<VisualElement>("mp-headless-panel");
        if (panel == null)
            return;

        panel.style.display = panel.resolvedStyle.display == DisplayStyle.None
            ? DisplayStyle.Flex
            : DisplayStyle.None;
        RefreshHeadlessPanel();
    }

    private void RefreshHeadlessPanel()
    {
        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        VisualElement panel = root.Q<VisualElement>("mp-headless-panel");
        Button headlessButton = root.Q<Button>("mp-headless-button");
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (panel == null || session == null)
            return;

        if (headlessButton != null)
            headlessButton.text = $"Headless ({session.HeadlessParticipantCount})";

        DisplayStyle previousDisplay = panel.resolvedStyle.display;
        panel.Clear();
        panel.style.display = previousDisplay;

        Label title = new("Participantes headless disponibles");
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        title.style.marginBottom = 4;
        panel.Add(title);

        Label description = new("El host puede ocupar una casilla vacía con un controlador compatible con el modo activo.");
        description.style.fontSize = 12;
        description.style.color = new Color(0.68f, 0.78f, 0.73f);
        description.style.marginBottom = 10;
        panel.Add(description);

        IReadOnlyList<HeadlessProfileDefinition> profiles = session.AvailableHeadlessProfiles;
        if (profiles == null || profiles.Count == 0)
        {
            Label empty = new("Este contenido no registra perfiles headless compatibles.");
            empty.style.color = new Color(0.72f, 0.75f, 0.74f);
            panel.Add(empty);
            return;
        }

        foreach (HeadlessProfileDefinition profile in profiles)
        {
            VisualElement profileRow = new();
            profileRow.style.flexDirection = FlexDirection.Row;
            profileRow.style.flexWrap = Wrap.Wrap;
            profileRow.style.alignItems = Align.Center;
            profileRow.style.paddingTop = 8;
            profileRow.style.paddingBottom = 8;
            profileRow.style.borderTopWidth = 1;
            profileRow.style.borderTopColor = new Color(1f, 1f, 1f, 0.08f);

            VisualElement profileText = new();
            profileText.style.flexGrow = 1;
            profileText.style.minWidth = 230;
            Label profileName = new(profile.DisplayName);
            profileName.style.unityFontStyleAndWeight = FontStyle.Bold;
            profileName.style.color = Color.white;
            profileText.Add(profileName);

            int currentInstances = session.Players.Count(item =>
                item.IsHeadless &&
                string.Equals(item.ControllerProfileId, profile.Id, StringComparison.OrdinalIgnoreCase));
            Label metadata = new($"{profile.SourceLabel} · {currentInstances}/{Mathf.Max(1, profile.MaximumInstances)}");
            metadata.style.fontSize = 11;
            metadata.style.color = new Color(0.34f, 0.93f, 0.66f);
            profileText.Add(metadata);

            Label profileDescription = new(profile.Description);
            profileDescription.style.fontSize = 12;
            profileDescription.style.color = new Color(0.68f, 0.78f, 0.73f);
            profileText.Add(profileDescription);

            if (!profile.RuntimeImplemented)
            {
                Label pending = new("Matchmaking listo · controlador de gameplay pendiente");
                pending.style.fontSize = 11;
                pending.style.color = new Color(1f, 0.72f, 0.32f);
                profileText.Add(pending);
            }

            profileRow.Add(profileText);

            Button addButton = new() { text = "Agregar" };
            addButton.AddToClassList("gs-button");
            addButton.style.minWidth = 96;
            addButton.style.height = 38;
            bool reachedMaximum = currentInstances >= Mathf.Max(1, profile.MaximumInstances);
            bool canAdd = session.IsHost && session.HasFreeLobbySlot() && !reachedMaximum;
            addButton.SetEnabled(canAdd);
            addButton.text = reachedMaximum ? "Máximo" : "Agregar";
            addButton.tooltip = !session.IsHost
                ? "Solo el host puede agregar participantes headless."
                : !session.HasFreeLobbySlot()
                    ? "No quedan casillas disponibles."
                    : string.Empty;
            string selectedProfileId = profile.Id;
            addButton.clicked += () => session.AddHeadlessParticipant(selectedProfileId);
            profileRow.Add(addButton);
            panel.Add(profileRow);
        }
    }

    private void ShowTeamPicker(VisualElement anchor, NetworkPlayerInfo player)
    {
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session == null) return;

        bool canEdit = !session.FixedTeams && !player.TeamLocked && (session.IsHost || (session.GetLocalPlayer()?.ParticipantId == player.ParticipantId && player.IsHuman && !player.IsReady));
        if (!canEdit) return;

        VisualElement root = uiDocument.rootVisualElement;
        VisualElement existing = root.Q<VisualElement>("mp-team-popup");
        if (existing != null)
        {
            bool sameAnchor = ReferenceEquals(existing.userData, anchor);
            existing.RemoveFromHierarchy();
            if (sameAnchor)
                return;
        }

        root.Q<VisualElement>("mp-color-popup")?.RemoveFromHierarchy();

        int maxTeams = Mathf.Clamp(session.SelectedScenarioMaxTeams, 1, 4);
        const float optionWidth = 104f;
        const float optionGap = 6f;
        int columns = Mathf.CeilToInt(maxTeams / 2f);
        float popupWidth = Mathf.Max(220f, columns * (optionWidth + optionGap) + 24f);

        VisualElement popup = CreatePopupNearAnchor(root, anchor, "mp-team-popup", popupWidth, 180);
        popup.userData = anchor;

        Label title = new($"Equipo de {player.PlayerName}");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        title.style.marginBottom = 8;
        popup.Add(title);

        VisualElement grid = new();
        grid.style.flexDirection = FlexDirection.Row;
        grid.style.flexWrap = Wrap.Wrap;
        grid.style.width = columns * (optionWidth + optionGap);
        popup.Add(grid);

        for (int teamId = 1; teamId <= maxTeams; teamId++)
        {
            int selectedTeam = teamId;
            Button option = new() { text = $"Equipo {teamId}" };
            option.style.width = optionWidth;
            option.style.height = 38;
            option.style.marginRight = optionGap;
            option.style.marginBottom = 6;
            option.clicked += () =>
            {
                session.RequestTeamChange(player.ParticipantId, selectedTeam);
                popup.RemoveFromHierarchy();
            };
            grid.Add(option);
        }

        root.Add(popup);
    }

    private void ShowColorPicker(VisualElement anchor, NetworkPlayerInfo player)
    {
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session == null)
            return;

        bool canEdit = !player.ColorLocked && (session.IsHost || (session.GetLocalPlayer()?.ParticipantId == player.ParticipantId && player.IsHuman && !session.FixedColors && !player.IsReady));
        if (!canEdit)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        VisualElement existing = root.Q<VisualElement>("mp-color-popup");
        if (existing != null)
        {
            bool sameAnchor = ReferenceEquals(existing.userData, anchor);
            existing.RemoveFromHierarchy();
            if (sameAnchor)
                return;
        }

        root.Q<VisualElement>("mp-team-popup")?.RemoveFromHierarchy();

        const float optionWidth = 96f;
        const float optionGap = 6f;
        int columns = Mathf.CeilToInt(PlayerColorPalette.Count / 2f);
        float popupWidth = columns * (optionWidth + optionGap) + 24f;

        VisualElement popup = new() { name = "mp-color-popup" };
        popup.userData = anchor;
        popup.style.position = Position.Absolute;
        popup.style.width = popupWidth;
        popup.style.maxWidth = popupWidth;
        popup.style.paddingLeft = 12;
        popup.style.paddingRight = 12;
        popup.style.paddingTop = 12;
        popup.style.paddingBottom = 12;
        popup.style.backgroundColor = new Color(0.10f, 0.14f, 0.13f, 0.98f);
        popup.style.borderTopLeftRadius = 10;
        popup.style.borderTopRightRadius = 10;
        popup.style.borderBottomLeftRadius = 10;
        popup.style.borderBottomRightRadius = 10;

        Rect anchorRect = anchor.worldBound;
        float popupLeft = Mathf.Clamp(anchorRect.xMax - popupWidth, 12f, Mathf.Max(12f, root.resolvedStyle.width - popupWidth - 12f));
        float popupTop = Mathf.Clamp(anchorRect.yMax + 8f, 12f, Mathf.Max(12f, root.resolvedStyle.height - 180f));
        popup.style.left = popupLeft;
        popup.style.top = popupTop;

        Label title = new($"Color de {player.PlayerName}");
        title.style.marginBottom = 8;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        popup.Add(title);

        VisualElement grid = new();
        grid.style.flexDirection = FlexDirection.Row;
        grid.style.flexWrap = Wrap.Wrap;
        grid.style.width = columns * (optionWidth + optionGap);
        popup.Add(grid);

        for (int colorId = 0; colorId < PlayerColorPalette.Count; colorId++)
        {
            int selectedColorId = colorId;
            Button option = new() { text = PlayerColorPalette.GetName(colorId) };
            option.style.width = optionWidth;
            option.style.height = 38;
            option.style.marginRight = optionGap;
            option.style.marginBottom = 6;
            option.style.backgroundColor = PlayerColorPalette.GetColor(colorId);
            option.style.color = Color.white;
            option.style.unityFontStyleAndWeight = FontStyle.Bold;
            option.style.borderTopLeftRadius = 8;
            option.style.borderTopRightRadius = 8;
            option.style.borderBottomLeftRadius = 8;
            option.style.borderBottomRightRadius = 8;
            bool occupiedByAnother = session.Players != null && session.Players.Any(p => p.ColorId == selectedColorId && p.ParticipantId != player.ParticipantId);
            option.SetEnabled(session.IsHost || !occupiedByAnother);
            option.clicked += () =>
            {
                session.RequestColorChange(player.ParticipantId, selectedColorId);
                popup.RemoveFromHierarchy();
            };
            grid.Add(option);
        }

        root.Add(popup);
    }

    private void HandleLobbySettingsChanged()
    {
        RefreshMultiplayerPlayers();
        RefreshNetworkControls();
    }

    private void RefreshNetworkControls()
    {
        if (uiDocument == null || NetworkSessionManager.Instance == null) return;
        NetworkSessionManager session = NetworkSessionManager.Instance;
        VisualElement root = uiDocument.rootVisualElement;
        Button startButton = root.Q<Button>("mp-menu-start-button");
        Button readyButton = root.Q<Button>("mp-ready-button");
        VisualElement allReadyLight = root.Q<VisualElement>("mp-all-ready-light");
        Label allReadyLabel = root.Q<Label>("mp-all-ready-label");
        Toggle fixedColorsToggle = root.Q<Toggle>("mp-fixed-colors-toggle");
        Toggle fixedTeamsToggle = root.Q<Toggle>("mp-fixed-teams-toggle");
        Label contentLabel = root.Q<Label>("mp-lobby-content-label");

        bool isHost = session.IsHost;
        bool allReady = session.AllPlayersReady;
        bool contentCompatible = session.AllRemoteClientsContentCompatible;
        bool canStart = allReady && contentCompatible;

        if (startButton != null)
        {
            startButton.SetEnabled(isHost && canStart);
            startButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
        }
        if (readyButton != null)
        {
            readyButton.SetEnabled(session.IsConnectedClient);
            NetworkPlayerInfo local = session.GetLocalPlayer();
            readyButton.text = local != null && local.IsReady ? "Marcar No listo" : "Marcar Listo";
        }
        if (fixedColorsToggle != null)
        {
            bool overridden = session.ActiveOverrides.Any(item => item.Enabled && string.Equals(item.Key, "fixedColors", StringComparison.OrdinalIgnoreCase));
            fixedColorsToggle.SetValueWithoutNotify(session.FixedColors);
            fixedColorsToggle.SetEnabled(isHost);
            fixedColorsToggle.tooltip = overridden
                ? "Valor inicial precargado por el contenido. La selección del host tiene prioridad."
                : "La configuración elegida por el host se aplicará sobre la del mapa.";
        }
        if (fixedTeamsToggle != null)
        {
            bool overridden = session.ActiveOverrides.Any(item => item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
            fixedTeamsToggle.SetValueWithoutNotify(session.FixedTeams);
            fixedTeamsToggle.SetEnabled(isHost);
            fixedTeamsToggle.tooltip = overridden
                ? "Valor inicial precargado por el contenido. La selección del host tiene prioridad."
                : "La configuración elegida por el host se aplicará sobre la del mapa.";
        }
        if (contentLabel != null)
        {
            string modeName = string.Equals(session.SelectedGameModeId, HeadlessProfileCatalog.NormalGameModeId, StringComparison.OrdinalIgnoreCase)
                ? "Normal"
                : session.SelectedGameModeId;
            string packageLine = string.IsNullOrWhiteSpace(session.SelectedPackageId)
                ? string.Empty
                : $"\nPaquete: {session.SelectedPackageId} {session.SelectedPackageVersion}";
            contentLabel.text = $"{(session.SelectedContentType == GameContentType.Campaign ? "Campaña" : "Escenario")}: {session.SelectedContentId}\nModo: {modeName}{packageLine}";
            contentLabel.tooltip = string.IsNullOrWhiteSpace(session.SelectedContentHash)
                ? string.Empty
                : $"Hash del contenido: {session.SelectedContentHash}";
        }
        if (allReadyLight != null)
            allReadyLight.style.backgroundColor = canStart ? new Color(0.12f, 0.75f, 0.25f) : new Color(0.85f, 0.12f, 0.12f);
        if (allReadyLabel != null)
        {
            allReadyLabel.text = !contentCompatible
                ? "Hay jugadores con contenido ausente o incompatible"
                : allReady
                    ? "Todos los jugadores humanos listos"
                    : "Faltan jugadores humanos por confirmar";
        }

        RefreshContentOverrides();
    }

    private void RegisterMultiplayerResponsiveLayout(VisualElement root)
    {
        if (multiplayerGeometryChangedHandler != null)
            root.UnregisterCallback<GeometryChangedEvent>(multiplayerGeometryChangedHandler);

        multiplayerGeometryChangedHandler = _ => ApplyMultiplayerResponsiveLayout(root);
        root.RegisterCallback<GeometryChangedEvent>(multiplayerGeometryChangedHandler);
        ApplyMultiplayerResponsiveLayout(root);
    }

    private void ApplyMultiplayerResponsiveLayout(VisualElement root)
    {
        if (root == null)
            return;

        VisualElement header = root.Q<VisualElement>("mp-header");
        VisualElement content = root.Q<VisualElement>("mp-lobby-view");
        VisualElement createContent = root.Q<VisualElement>("mp-create-session-view");
        ScrollView createLeft = root.Q<ScrollView>("mp-create-session-left");
        ScrollView createRight = root.Q<ScrollView>("mp-create-session-right");
        VisualElement playersCard = root.Q<VisualElement>("mp-players-card");
        ScrollView settingsCard = root.Q<ScrollView>("mp-settings-card");

        float width = root.resolvedStyle.width;
        bool stacked = width > 0f && width < 1220f;
        bool compact = width > 0f && width < 920f;

        if (header != null)
            header.style.flexDirection = compact ? FlexDirection.Column : FlexDirection.Row;

        if (content != null)
            content.style.flexDirection = stacked ? FlexDirection.Column : FlexDirection.Row;

        if (createContent != null)
            createContent.style.flexDirection = stacked ? FlexDirection.Column : FlexDirection.Row;
        if (createLeft != null)
        {
            createLeft.style.width = stacked ? new StyleLength(new Length(100, LengthUnit.Percent)) : new StyleLength(StyleKeyword.Auto);
            createLeft.style.minWidth = stacked ? 0 : 420;
            createLeft.style.marginRight = stacked ? 0 : 18;
        }
        if (createRight != null)
        {
            createRight.style.width = stacked ? new StyleLength(new Length(100, LengthUnit.Percent)) : new StyleLength(new Length(620, LengthUnit.Pixel));
            createRight.style.minWidth = stacked ? 0 : 420;
        }

        if (playersCard != null)
        {
            playersCard.style.minWidth = stacked ? 0 : 520;
            playersCard.style.width = stacked ? new StyleLength(new Length(100, LengthUnit.Percent)) : new StyleLength(StyleKeyword.Auto);
            playersCard.style.marginRight = stacked ? 0 : 18;
        }

        if (settingsCard != null)
        {
            settingsCard.style.width = stacked ? new StyleLength(new Length(100, LengthUnit.Percent)) : new StyleLength(new Length(460, LengthUnit.Pixel));
            settingsCard.style.minWidth = stacked ? 0 : 400;
        }
    }

    private VisualElement CreatePopupNearAnchor(VisualElement root, VisualElement anchor, string name, float width, float estimatedHeight)
    {
        VisualElement popup = new() { name = name };
        popup.style.position = Position.Absolute;
        popup.style.width = width;
        popup.style.paddingLeft = 12;
        popup.style.paddingRight = 12;
        popup.style.paddingTop = 12;
        popup.style.paddingBottom = 12;
        popup.style.backgroundColor = new Color(0.10f, 0.14f, 0.13f, 0.98f);
        popup.style.borderTopLeftRadius = 10;
        popup.style.borderTopRightRadius = 10;
        popup.style.borderBottomLeftRadius = 10;
        popup.style.borderBottomRightRadius = 10;
        Rect bounds = anchor.worldBound;
        popup.style.left = Mathf.Clamp(bounds.xMax - width, 12f, Mathf.Max(12f, root.resolvedStyle.width - width - 12f));
        popup.style.top = Mathf.Clamp(bounds.yMax + 8f, 12f, Mathf.Max(12f, root.resolvedStyle.height - estimatedHeight - 12f));
        return popup;
    }
}
