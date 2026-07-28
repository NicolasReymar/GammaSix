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
                if (session == null || !session.IsHost || session.FixedTeamsForcedByScenario)
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
        if (uiDocument == null) return;
        VisualElement root = uiDocument.rootVisualElement;
        VisualElement list = root.Q<VisualElement>("mp-player-list");
        if (list == null) return;

        list.Clear();
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session == null) return;

        IReadOnlyList<NetworkPlayerInfo> players = session.Players;
        int slotCount = Mathf.Clamp(session.SelectedScenarioMaxPlayers, 1, 8);
        ulong localClientId = session.GetLocalPlayer()?.ClientId ?? ulong.MaxValue;

        for (int slot = 0; slot < slotCount; slot++)
        {
            NetworkPlayerInfo player = slot < players.Count ? players[slot] : null;
            VisualElement row = CreatePlayerSlotRow(slot + 1, player, session, localClientId);
            list.Add(row);
        }

        RefreshNetworkControls();
    }

    private VisualElement CreatePlayerSlotRow(int slotNumber, NetworkPlayerInfo player, NetworkSessionManager session, ulong localClientId)
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
        row.style.backgroundColor = player == null ? new Color(1f, 1f, 1f, 0.035f) : new Color(1f, 1f, 1f, 0.075f);
        row.style.borderLeftWidth = 1;
        row.style.borderRightWidth = 1;
        row.style.borderTopWidth = 1;
        row.style.borderBottomWidth = 1;
        row.style.borderLeftColor = new Color(0.35f, 0.55f, 0.46f, 0.28f);
        row.style.borderRightColor = new Color(0.35f, 0.55f, 0.46f, 0.28f);
        row.style.borderTopColor = new Color(0.35f, 0.55f, 0.46f, 0.28f);
        row.style.borderBottomColor = new Color(0.35f, 0.55f, 0.46f, 0.28f);
        row.style.borderTopLeftRadius = 10;
        row.style.borderTopRightRadius = 10;
        row.style.borderBottomLeftRadius = 10;
        row.style.borderBottomRightRadius = 10;

        Label slotLabel = new($"Casilla {slotNumber}");
        slotLabel.style.width = 76;
        slotLabel.style.fontSize = 12;
        slotLabel.style.color = new Color(0.62f, 0.75f, 0.69f, 1f);
        row.Add(slotLabel);

        if (player == null)
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
        identity.style.minWidth = 150;
        Label name = new(player.PlayerName + (player.ClientId == localClientId ? " (Tú)" : string.Empty));
        name.style.fontSize = 17;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = new Color(0.94f, 0.98f, 0.96f, 1f);
        identity.Add(name);
        Label ready = new(player.IsReady ? "LISTO" : "NO LISTO");
        ready.style.fontSize = 12;
        ready.style.color = player.IsReady ? new Color(0.32f, 0.90f, 0.55f) : new Color(1f, 0.43f, 0.43f);
        identity.Add(ready);
        row.Add(identity);

        bool canEditOwn = player.ClientId == localClientId && !player.IsReady;
        bool canHostEdit = session.IsHost;

        Button teamButton = new() { text = $"Equipo {player.TeamId}" };
        teamButton.AddToClassList("gs-button");
        teamButton.style.width = 112;
        teamButton.style.height = 38;
        teamButton.style.marginRight = 8;
        teamButton.SetEnabled(!session.FixedTeams && (canHostEdit || canEditOwn));
        teamButton.tooltip = session.FixedTeams ? "Los equipos están fijados por la configuración de la partida." : string.Empty;
        teamButton.clicked += () => ShowTeamPicker(teamButton, player);
        row.Add(teamButton);

        Button colorButton = new() { text = PlayerColorPalette.GetName(player.ColorId) };
        colorButton.AddToClassList("gs-button");
        colorButton.style.width = 118;
        colorButton.style.height = 38;
        colorButton.style.backgroundColor = PlayerColorPalette.GetColor(player.ColorId);
        colorButton.style.color = Color.white;
        bool canEditColor = canHostEdit || (canEditOwn && !session.FixedColors);
        colorButton.SetEnabled(canEditColor);
        colorButton.clicked += () => ShowColorPicker(colorButton, player);
        row.Add(colorButton);
        return row;
    }

    private void ShowTeamPicker(VisualElement anchor, NetworkPlayerInfo player)
    {
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session == null) return;

        bool canEdit = !session.FixedTeams && (session.IsHost || (session.GetLocalPlayer()?.ClientId == player.ClientId && !player.IsReady));
        if (!canEdit) return;

        VisualElement root = uiDocument.rootVisualElement;
        root.Q<VisualElement>("mp-team-popup")?.RemoveFromHierarchy();
        VisualElement popup = CreatePopupNearAnchor(root, anchor, "mp-team-popup", 220, 220);
        Label title = new($"Equipo de {player.PlayerName}");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        title.style.marginBottom = 8;
        popup.Add(title);

        int maxTeams = Mathf.Clamp(session.SelectedScenarioMaxTeams, 1, 4);
        for (int teamId = 1; teamId <= maxTeams; teamId++)
        {
            int selectedTeam = teamId;
            Button option = new() { text = $"Equipo {teamId}" };
            option.style.height = 38;
            option.style.marginBottom = 6;
            option.clicked += () =>
            {
                session.RequestTeamChange(player.ClientId, selectedTeam);
                popup.RemoveFromHierarchy();
            };
            popup.Add(option);
        }
        root.Add(popup);
    }

    private void ShowColorPicker(VisualElement anchor, NetworkPlayerInfo player)
    {
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session == null)
            return;

        bool canEdit = session.IsHost || (session.GetLocalPlayer()?.ClientId == player.ClientId && !session.FixedColors && !player.IsReady);
        if (!canEdit)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        root.Q<VisualElement>("mp-color-popup")?.RemoveFromHierarchy();

        VisualElement popup = new() { name = "mp-color-popup" };
        popup.style.position = Position.Absolute;
        popup.style.width = 272;
        popup.style.maxWidth = 272;
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
        float popupLeft = Mathf.Clamp(anchorRect.xMax - 272f, 12f, Mathf.Max(12f, root.resolvedStyle.width - 284f));
        float popupTop = Mathf.Clamp(anchorRect.yMax + 8f, 12f, Mathf.Max(12f, root.resolvedStyle.height - 260f));
        popup.style.left = popupLeft;
        popup.style.top = popupTop;

        Label title = new($"Color de {player.PlayerName}");
        title.style.marginBottom = 8;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        popup.Add(title);

        Label subtitle = new("Selecciona un color disponible para este jugador.");
        subtitle.style.fontSize = 12;
        subtitle.style.whiteSpace = WhiteSpace.Normal;
        subtitle.style.color = new Color(1f, 1f, 1f, 0.72f);
        subtitle.style.marginBottom = 10;
        popup.Add(subtitle);

        VisualElement grid = new();
        grid.style.flexDirection = FlexDirection.Row;
        grid.style.flexWrap = Wrap.Wrap;
        popup.Add(grid);

        for (int colorId = 0; colorId < PlayerColorPalette.Count; colorId++)
        {
            int selectedColorId = colorId;
            Button option = new() { text = PlayerColorPalette.GetName(colorId) };
            option.style.width = 118;
            option.style.height = 38;
            option.style.marginRight = 6;
            option.style.marginBottom = 6;
            option.style.backgroundColor = PlayerColorPalette.GetColor(colorId);
            option.style.color = Color.white;
            option.style.unityFontStyleAndWeight = FontStyle.Bold;
            option.style.borderTopLeftRadius = 8;
            option.style.borderTopRightRadius = 8;
            option.style.borderBottomLeftRadius = 8;
            option.style.borderBottomRightRadius = 8;
            bool occupiedByAnother = session.Players != null && session.Players.Any(p => p.ColorId == selectedColorId && p.ClientId != player.ClientId);
            option.SetEnabled(session.IsHost || !occupiedByAnother);
            option.clicked += () =>
            {
                session.RequestColorChange(player.ClientId, selectedColorId);
                popup.RemoveFromHierarchy();
            };
            grid.Add(option);
        }

        Button close = new() { text = "Cerrar" };
        close.style.marginTop = 8;
        close.clicked += popup.RemoveFromHierarchy;
        popup.Add(close);
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

        if (startButton != null)
        {
            startButton.SetEnabled(isHost && allReady);
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
                ? "Valor precargado por el contenido. Al modificarlo, el host cancela ese override."
                : string.Empty;
        }
        if (fixedTeamsToggle != null)
        {
            bool overridden = session.ActiveOverrides.Any(item => item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
            fixedTeamsToggle.SetValueWithoutNotify(session.FixedTeams);
            fixedTeamsToggle.SetEnabled(isHost && !session.FixedTeamsForcedByScenario);
            fixedTeamsToggle.tooltip = session.FixedTeamsForcedByScenario
                ? "Los equipos están bloqueados por el atributo fixedTeams del escenario."
                : overridden
                    ? "Valor precargado por el contenido. Al modificarlo, el host cancela ese override."
                    : string.Empty;
        }
        if (contentLabel != null)
            contentLabel.text = $"{(session.SelectedContentType == GameContentType.Campaign ? "Campaña" : "Escenario")}: {session.SelectedContentId}";
        if (allReadyLight != null)
            allReadyLight.style.backgroundColor = allReady ? new Color(0.12f, 0.75f, 0.25f) : new Color(0.85f, 0.12f, 0.12f);
        if (allReadyLabel != null)
            allReadyLabel.text = allReady ? "Todos listos" : "Faltan jugadores por confirmar";

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
