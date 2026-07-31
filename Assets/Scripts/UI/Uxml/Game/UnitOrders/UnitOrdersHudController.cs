using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Tarjeta modular de órdenes para la selección local. Las órdenes se envían al
/// mismo Command Bus que el clic contextual y los futuros controladores Headless.
/// </summary>
public sealed class UnitOrdersHudController : MonoBehaviour
{
    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private VisualElement panel;
    private VisualElement dragHandle;
    private Button attackButton;
    private Button stopButton;
    private Button stanceButton;
    private Button patrolButton;
    private Label selectionLabel;
    private Label statusLabel;
    private DraggableHudPanel draggablePanel;
    private NetworkEntityCoordinator subscribedCoordinator;
    private float refreshTimer;

    private void Awake()
    {
        VisualTreeAsset tree = Resources.Load<VisualTreeAsset>("UI/GameHud/UnitOrdersHud");
        if (tree == null)
        {
            Debug.LogError("[UnitOrdersHudController] No se encontró UnitOrdersHud.uxml.");
            enabled = false;
            return;
        }

        uiDocument = HudDocumentFactory.Create(gameObject, tree, 104, out runtimePanelSettings);
        if (uiDocument == null)
        {
            enabled = false;
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        panel = root.Q<VisualElement>("unit-orders-panel");
        dragHandle = root.Q<VisualElement>("unit-orders-drag-handle");
        attackButton = root.Q<Button>("unit-orders-attack");
        stopButton = root.Q<Button>("unit-orders-stop");
        stanceButton = root.Q<Button>("unit-orders-stance");
        patrolButton = root.Q<Button>("unit-orders-patrol");
        selectionLabel = root.Q<Label>("unit-orders-selection");
        statusLabel = root.Q<Label>("unit-orders-status");

        if (panel != null)
        {
            draggablePanel = new DraggableHudPanel(
                root,
                panel,
                "GammaSix.Hud.UnitOrders",
                dragHandle);
        }

        if (attackButton != null)
            attackButton.clicked += OnAttackClicked;
        if (stopButton != null)
            stopButton.clicked += OnStopClicked;
        if (stanceButton != null)
            stanceButton.clicked += OnStanceClicked;
        if (patrolButton != null)
            patrolButton.clicked += OnPatrolClicked;

        Refresh();
    }

    private void Start()
    {
        TrySubscribe();
        Refresh();
    }

    private void Update()
    {
        TrySubscribe();
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f)
            return;

        refreshTimer = 0.15f;
        Refresh();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (attackButton != null)
            attackButton.clicked -= OnAttackClicked;
        if (stopButton != null)
            stopButton.clicked -= OnStopClicked;
        if (stanceButton != null)
            stanceButton.clicked -= OnStanceClicked;
        if (patrolButton != null)
            patrolButton.clicked -= OnPatrolClicked;

        draggablePanel?.Dispose();
        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void TrySubscribe()
    {
        NetworkEntityCoordinator coordinator = NetworkEntityCoordinator.Instance;
        if (coordinator == null || coordinator == subscribedCoordinator)
            return;

        Unsubscribe();
        subscribedCoordinator = coordinator;
        subscribedCoordinator.SelectionChanged += Refresh;
        subscribedCoordinator.UnitOrderStateChanged += Refresh;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (subscribedCoordinator == null)
            return;

        subscribedCoordinator.SelectionChanged -= Refresh;
        subscribedCoordinator.UnitOrderStateChanged -= Refresh;
        subscribedCoordinator = null;
    }

    private void OnAttackClicked()
    {
        NetworkEntityCoordinator.Instance?.ToggleAttackOrderMode();
    }

    private void OnStopClicked()
    {
        NetworkEntityCoordinator.Instance?.IssueStopOrderForSelection();
    }

    private void OnStanceClicked()
    {
        NetworkEntityCoordinator.Instance?.TogglePassiveStanceForSelection();
    }

    private void OnPatrolClicked()
    {
        NetworkEntityCoordinator.Instance?.TogglePatrolOrderMode();
    }

    private void Refresh()
    {
        NetworkEntityCoordinator coordinator = NetworkEntityCoordinator.Instance;
        var selected = coordinator?.GetOwnedControllableSelection();
        int selectedCount = selected?.Count ?? 0;
        int attackCount = selected?.Count(view => view != null && view.HasAttack) ?? 0;
        bool allPassive = attackCount > 0 && selected
            .Where(view => view != null && view.HasAttack)
            .All(view => view.CombatStance == EntityCombatStance.Passive);

        if (selectionLabel != null)
        {
            selectionLabel.text = selectedCount == 0
                ? "Sin unidades propias seleccionadas"
                : $"{selectedCount} seleccionada(s) · {attackCount} con ataque";
        }

        if (attackButton != null)
        {
            attackButton.SetEnabled(attackCount > 0);
            attackButton.EnableInClassList(
                "unit-order-button--active",
                coordinator?.ActiveOrderMode == UnitOrderTargetingMode.Attack);
        }

        stopButton?.SetEnabled(selectedCount > 0);
        if (patrolButton != null)
        {
            patrolButton.SetEnabled(selectedCount > 0);
            patrolButton.EnableInClassList(
                "unit-order-button--active",
                coordinator?.ActiveOrderMode == UnitOrderTargetingMode.Patrol);
        }
        if (stanceButton != null)
        {
            stanceButton.SetEnabled(attackCount > 0);
            stanceButton.text = allPassive ? "Agresivo [P]" : "Pasivo [P]";
            stanceButton.EnableInClassList("unit-order-button--passive", allPassive);
        }

        if (statusLabel != null)
        {
            string status = coordinator?.ActiveOrderStatus;
            if (string.IsNullOrWhiteSpace(status))
            {
                status = allPassive
                    ? "Postura actual: Pasiva. Las órdenes manuales de ataque siguen permitidas."
                    : "A ataca/avanza · R patrulla · S detiene · P postura";
            }
            statusLabel.text = status;
        }
    }
}
