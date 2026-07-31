using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Ventana modular de diplomacia. Presenta la postura del equipo local hacia
/// cada equipo y la postura inversa, dejando visible la asimetría. Los cambios
/// se envían como órdenes autoritativas; esta fase no transfiere recursos.
/// </summary>
public sealed class DiplomacyHudController : MonoBehaviour
{
    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private VisualElement overlay;
    private VisualElement panel;
    private ScrollView teamList;
    private Label localTeamLabel;
    private Label statusLabel;
    private Button closeButton;
    private bool isOpen;
    private float refreshTimer;

    private void Awake()
    {
        VisualTreeAsset tree = Resources.Load<VisualTreeAsset>("UI/GameHud/DiplomacyHud");
        if (tree == null)
        {
            Debug.LogError("[DiplomacyHudController] No se encontró DiplomacyHud.uxml.");
            enabled = false;
            return;
        }

        uiDocument = HudDocumentFactory.Create(gameObject, tree, 920, out runtimePanelSettings);
        if (uiDocument == null)
        {
            enabled = false;
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        overlay = root.Q<VisualElement>("diplomacy-overlay");
        panel = root.Q<VisualElement>("diplomacy-panel");
        teamList = root.Q<ScrollView>("diplomacy-team-list");
        localTeamLabel = root.Q<Label>("diplomacy-local-team");
        statusLabel = root.Q<Label>("diplomacy-status");
        closeButton = root.Q<Button>("diplomacy-close");

        if (closeButton != null)
            closeButton.clicked += Close;
        DiplomacyClientState.Changed += OnDiplomacyChanged;
        SetOpen(false);
    }

    private void Update()
    {
        if (isOpen)
        {
            if (GameInputReader.OPressedThisFrame || GameInputReader.EscapePressedThisFrame)
            {
                Close();
                return;
            }

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.4f;
                Refresh();
            }
            return;
        }

        if (!GameUiModalService.IsModalOpen && GameInputReader.OPressedThisFrame)
            Open();
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.clicked -= Close;
        DiplomacyClientState.Changed -= OnDiplomacyChanged;
        GameUiModalService.Release(this);
        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void Open()
    {
        SetOpen(true);
        SetStatus("Selecciona la postura que tu equipo mantiene hacia cada facción.", false);
        Refresh();
    }

    private void Close()
    {
        SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        isOpen = open;
        if (overlay != null)
        {
            overlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            overlay.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
        }
        GameUiModalService.SetOpen(this, open);
    }

    private void OnDiplomacyChanged()
    {
        if (isOpen)
            Refresh();
    }

    private void Refresh()
    {
        if (teamList == null)
            return;

        int localTeamId = ResolveLocalTeamId();
        if (localTeamLabel != null)
        {
            localTeamLabel.text = localTeamId > 0
                ? $"Tu equipo: Equipo {localTeamId}"
                : "No se pudo resolver tu equipo";
        }

        teamList.Clear();
        if (localTeamId <= 0)
        {
            SetStatus("La diplomacia no está disponible sin un equipo local válido.", true);
            return;
        }

        int[] teamIds = ResolveKnownTeamIds(localTeamId)
            .Where(teamId => teamId > 0 && teamId != localTeamId)
            .Distinct()
            .OrderBy(teamId => teamId)
            .ToArray();
        if (teamIds.Length == 0)
        {
            Label empty = new("No existen otros equipos en esta partida.");
            empty.AddToClassList("diplomacy-empty");
            teamList.Add(empty);
            return;
        }

        foreach (int targetTeamId in teamIds)
            teamList.Add(CreateTeamRow(localTeamId, targetTeamId));
    }

    private VisualElement CreateTeamRow(int localTeamId, int targetTeamId)
    {
        DiplomacyStance outgoing = DiplomacyClientState.GetStance(localTeamId, targetTeamId);
        DiplomacyStance incoming = DiplomacyClientState.GetStance(targetTeamId, localTeamId);

        VisualElement row = new();
        row.AddToClassList("diplomacy-team-row");

        VisualElement identity = new();
        identity.AddToClassList("diplomacy-team-identity");
        Label title = new($"Equipo {targetTeamId}");
        title.AddToClassList("diplomacy-team-title");
        Label participants = new(ResolveTeamParticipants(targetTeamId));
        participants.AddToClassList("diplomacy-team-participants");
        Label incomingLabel = new($"Ellos hacia ti: {Translate(incoming)}");
        incomingLabel.AddToClassList($"diplomacy-incoming diplomacy-stance-{incoming.ToString().ToLowerInvariant()}");
        identity.Add(title);
        identity.Add(participants);
        identity.Add(incomingLabel);

        VisualElement controls = new();
        controls.AddToClassList("diplomacy-team-controls");
        Label outgoingLabel = new("Tu postura hacia ellos");
        outgoingLabel.AddToClassList("diplomacy-outgoing-label");
        controls.Add(outgoingLabel);

        VisualElement buttons = new();
        buttons.AddToClassList("diplomacy-stance-buttons");
        buttons.Add(CreateStanceButton(targetTeamId, DiplomacyStance.Ally, outgoing));
        buttons.Add(CreateStanceButton(targetTeamId, DiplomacyStance.Neutral, outgoing));
        buttons.Add(CreateStanceButton(targetTeamId, DiplomacyStance.Enemy, outgoing));
        controls.Add(buttons);

        row.Add(identity);
        row.Add(controls);
        return row;
    }

    private Button CreateStanceButton(
        int targetTeamId,
        DiplomacyStance stance,
        DiplomacyStance current)
    {
        Button button = new(() => SetStance(targetTeamId, stance))
        {
            text = Translate(stance)
        };
        button.AddToClassList("diplomacy-stance-button");
        button.AddToClassList($"diplomacy-stance-button--{stance.ToString().ToLowerInvariant()}");
        if (stance == current)
            button.AddToClassList("diplomacy-stance-button--selected");
        return button;
    }

    private void SetStance(int targetTeamId, DiplomacyStance stance)
    {
        bool queued = NetworkEntityCoordinator.Instance?.RequestDiplomacyStance(targetTeamId, stance) == true;
        SetStatus(
            queued
                ? $"Cambio solicitado: tu equipo → Equipo {targetTeamId} = {Translate(stance)}."
                : "No se pudo enviar el cambio diplomático.",
            !queued);
    }

    private int ResolveLocalTeamId()
    {
        NetworkPlayerInfo local = NetworkSessionManager.Instance?.GetLocalPlayer();
        if (local != null && local.TeamId > 0)
            return local.TeamId;

        AuthoritativeMatchRuntime runtime = MatchRuntimeController.Instance?.Runtime;
        int participantId = MatchRuntimeController.Instance?.LocalParticipantId ?? -1;
        return runtime?.Participants != null &&
               runtime.Participants.TryGet(participantId, out MatchParticipantRuntimeState participant)
            ? participant.TeamId
            : -1;
    }

    private static IEnumerable<int> ResolveKnownTeamIds(int localTeamId)
    {
        HashSet<int> result = new(DiplomacyClientState.TeamIds);
        if (localTeamId > 0)
            result.Add(localTeamId);

        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session?.Players != null)
        {
            foreach (NetworkPlayerInfo participant in session.Players)
            {
                if (participant != null && participant.TeamId > 0)
                    result.Add(participant.TeamId);
            }
        }

        AuthoritativeMatchRuntime runtime = MatchRuntimeController.Instance?.Runtime;
        if (runtime?.Teams != null)
        {
            foreach (MatchTeamRuntimeState team in runtime.Teams.All)
                result.Add(team.TeamId);
        }
        return result;
    }

    private static string ResolveTeamParticipants(int teamId)
    {
        List<string> values = new();
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session?.Players != null)
        {
            values.AddRange(session.Players
                .Where(item => item != null && item.TeamId == teamId)
                .OrderBy(item => item.SlotIndex)
                .Select(item => $"{item.PlayerName} ({(item.IsHeadless ? "Headless" : "Player")})"));
        }

        if (values.Count == 0)
        {
            AuthoritativeMatchRuntime runtime = MatchRuntimeController.Instance?.Runtime;
            if (runtime?.Participants != null)
            {
                values.AddRange(runtime.Participants.All
                    .Where(item => item.TeamId == teamId)
                    .Select(item => $"{item.DisplayName} ({(item.IsHeadless ? "Headless" : "Player")})"));
            }
        }

        return values.Count == 0 ? "Sin participantes visibles" : string.Join(" · ", values);
    }

    private void SetStatus(string text, bool error)
    {
        if (statusLabel == null)
            return;
        statusLabel.text = text ?? string.Empty;
        statusLabel.EnableInClassList("diplomacy-status--error", error);
    }

    private static string Translate(DiplomacyStance stance)
    {
        return stance switch
        {
            DiplomacyStance.Ally => "Aliado",
            DiplomacyStance.Enemy => "Enemigo",
            _ => "Neutral"
        };
    }
}
