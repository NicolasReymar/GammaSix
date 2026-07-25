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
