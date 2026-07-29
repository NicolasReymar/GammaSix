using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class GameGoldHudController : MonoBehaviour
{
    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private Label goldValueLabel;
    private DraggableHudPanel draggablePanel;
    private int displayedGold = int.MinValue;

    private void Awake()
    {
        VisualTreeAsset visualTree = Resources.Load<VisualTreeAsset>("UI/GameHud/GoldHud");
        if (visualTree == null)
        {
            Debug.LogError("[GameGoldHudController] No se encontró GoldHud.uxml.");
            return;
        }

        uiDocument = HudDocumentFactory.Create(gameObject, visualTree, 101, out runtimePanelSettings);
        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        goldValueLabel = root.Q<Label>("game-gold-value");

        VisualElement panel = root.Q<VisualElement>("game-gold-panel");
        if (panel != null)
            draggablePanel = new DraggableHudPanel(root, panel, "GammaSix.Hud.Gold");

        RefreshGold();
    }

    private void Update()
    {
        RefreshGold();
    }

    private void OnDestroy()
    {
        draggablePanel?.Dispose();
        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void RefreshGold()
    {
        if (goldValueLabel == null)
            return;

        int gold = ResolveLocalTeamGold();
        if (gold == displayedGold)
            return;

        displayedGold = gold;
        goldValueLabel.text = gold.ToString();
    }

    private int ResolveLocalTeamGold()
    {
        MatchRuntimeController controller = MatchRuntimeController.Instance;
        AuthoritativeMatchRuntime runtime = controller?.Runtime;
        if (runtime?.Participants != null && runtime.Teams != null &&
            runtime.Participants.TryGet(controller.LocalParticipantId, out MatchParticipantRuntimeState participant) &&
            runtime.Teams.TryGet(participant.TeamId, out MatchTeamRuntimeState team))
        {
            return Mathf.Max(0, team.Resources.Get("gold"));
        }

        // Los clientes remotos todavía reciben el valor inicial desde el contenido.
        // La sincronización completa de estados de participante/equipo llegará con
        // el HUD de participantes y objetivos.
        string scenarioId = MatchManager.Instance?.CurrentMatchConfig?.ScenarioId;
        if (string.IsNullOrWhiteSpace(scenarioId) && NetworkSessionManager.Instance != null)
            scenarioId = NetworkSessionManager.Instance.SelectedScenarioId;

        ScenarioDefinition scenario = GameContentRepository.LoadScenario(scenarioId);
        if (scenario?.teamResources == null)
            return 0;

        int localTeamId = NetworkSessionManager.Instance?.GetLocalPlayer()?.TeamId ?? 1;
        ScenarioTeamResourceDefinition resource = scenario.teamResources
            .FirstOrDefault(item => item != null && item.teamId == localTeamId);

        return Mathf.Max(0, resource?.gold ?? 0);
    }
}
