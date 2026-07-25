using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class SelectedEntityHudController : MonoBehaviour
{
    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private Label entityNameLabel;
    private Label entitySummaryLabel;
    private VisualElement attributesList;
    private NetworkEntityView lastSelection;
    private NetworkEntityCoordinator subscribedSystem;
    private DraggableHudPanel draggablePanel;

    private void Awake()
    {
        VisualTreeAsset visualTree = Resources.Load<VisualTreeAsset>("UI/GameHud/SelectedEntityHud");
        if (visualTree == null)
        {
            Debug.LogError("[SelectedEntityHudController] No se encontró SelectedEntityHud.uxml.");
            return;
        }

        uiDocument = HudDocumentFactory.Create(gameObject, visualTree, 102, out runtimePanelSettings);
        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        entityNameLabel = root.Q<Label>("selected-entity-name");
        entitySummaryLabel = root.Q<Label>("selected-entity-summary");
        attributesList = root.Q<VisualElement>("selected-entity-attributes");

        VisualElement panel = root.Q<VisualElement>("selected-entity-panel");
        if (panel != null)
            draggablePanel = new DraggableHudPanel(root, panel, "GammaSix.Hud.SelectedEntity");

        RefreshSelectedEntity(null);
    }

    private void Start()
    {
        TrySubscribe();
        HandleSelectionChanged();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        draggablePanel?.Dispose();
        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void Update()
    {
        if (subscribedSystem == null)
            TrySubscribe();

        if (lastSelection != null)
            RefreshEntitySummary(lastSelection);
    }

    private void TrySubscribe()
    {
        NetworkEntityCoordinator system = NetworkEntityCoordinator.Instance;
        if (system == null || system == subscribedSystem)
            return;

        Unsubscribe();
        subscribedSystem = system;
        subscribedSystem.SelectionChanged += HandleSelectionChanged;
        subscribedSystem.InspectedSelectionChanged += HandleSelectionChanged;
    }

    private void Unsubscribe()
    {
        if (subscribedSystem == null)
            return;
        subscribedSystem.SelectionChanged -= HandleSelectionChanged;
        subscribedSystem.InspectedSelectionChanged -= HandleSelectionChanged;
        subscribedSystem = null;
    }

    private void HandleSelectionChanged()
    {
        NetworkEntityView current = subscribedSystem != null
            ? subscribedSystem.PrimarySelectedEntity
            : NetworkEntityCoordinator.Instance?.PrimarySelectedEntity;
        lastSelection = current;
        RefreshSelectedEntity(current);
    }

    private void RefreshSelectedEntity(NetworkEntityView entity)
    {
        if (entityNameLabel == null || entitySummaryLabel == null || attributesList == null)
            return;

        attributesList.Clear();

        if (entity == null)
        {
            entityNameLabel.text = "Sin entidad seleccionada";
            entitySummaryLabel.text = "Selecciona una unidad o edificio para ver sus atributos.";
            Label empty = new("No hay atributos para mostrar.");
            empty.AddToClassList("selected-entity-empty");
            attributesList.Add(empty);
            return;
        }

        entityNameLabel.text = entity.UnitName;
        RefreshEntitySummary(entity);

        string[] attributes = entity.Attributes?.ToArray() ?? Array.Empty<string>();
        if (attributes.Length == 0)
        {
            Label empty = new("Esta entidad no tiene atributos.");
            empty.AddToClassList("selected-entity-empty");
            attributesList.Add(empty);
            return;
        }

        foreach (string attribute in attributes)
        {
            Label chip = new(attribute);
            chip.AddToClassList("selected-entity-attribute-chip");
            attributesList.Add(chip);
        }
    }

    private void RefreshEntitySummary(NetworkEntityView entity)
    {
        if (entitySummaryLabel == null || entity == null)
            return;

        string team = entity.TeamId == 0 ? "Neutral" : $"Equipo {entity.TeamId}";
        entitySummaryLabel.text = $"{team} · Vida {entity.Health}/{entity.MaxHealth} · ID {entity.EntityDefinitionId}";
    }
}
