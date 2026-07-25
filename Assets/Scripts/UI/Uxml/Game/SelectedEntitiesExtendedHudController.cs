using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Visor extendido de la selección. Presenta hasta treinta grupos en una
/// cuadrícula de tres filas por diez columnas. Los heroicos nunca se agrupan.
/// </summary>
public sealed class SelectedEntitiesExtendedHudController : MonoBehaviour
{
    private const int MaxVisibleGroups = 30;

    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private VisualElement groupsGrid;
    private Label emptyLabel;
    private readonly List<Button> slots = new();
    private DraggableHudPanel draggablePanel;
    private string lastSignature;

    private void Awake()
    {
        VisualTreeAsset visualTree = Resources.Load<VisualTreeAsset>("UI/GameHud/SelectedEntitiesExtendedHud");
        if (visualTree == null)
        {
            Debug.LogError("[SelectedEntitiesExtendedHudController] No se encontró SelectedEntitiesExtendedHud.uxml.");
            return;
        }

        uiDocument = HudDocumentFactory.Create(gameObject, visualTree, 103, out runtimePanelSettings);
        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        groupsGrid = root.Q<VisualElement>("selected-entities-extended-grid");
        emptyLabel = root.Q<Label>("selected-entities-extended-empty");

        VisualElement panel = root.Q<VisualElement>("selected-entities-extended-panel");
        if (panel != null)
            draggablePanel = new DraggableHudPanel(root, panel, "GammaSix.Hud.SelectedEntitiesExtended");

        CreateSlots();
        Refresh(force: true);
    }

    private void OnDestroy()
    {
        draggablePanel?.Dispose();
        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void Update()
    {
        Refresh(force: false);
    }

    private void CreateSlots()
    {
        if (groupsGrid == null)
            return;

        groupsGrid.Clear();
        slots.Clear();

        for (int index = 0; index < MaxVisibleGroups; index++)
        {
            int capturedIndex = index;
            Button slot = new() { name = $"selected-entity-group-{index}" };
            slot.AddToClassList("selected-entities-extended-slot");
            slot.style.display = DisplayStyle.None;
            slot.clicked += () => NetworkUnitSystem.Instance?.SetInspectedSelectionGroup(capturedIndex);

            Label name = new() { name = "group-name" };
            name.AddToClassList("selected-entities-extended-slot-name");
            slot.Add(name);

            Label count = new() { name = "group-count" };
            count.AddToClassList("selected-entities-extended-slot-count");
            slot.Add(count);

            slots.Add(slot);
            groupsGrid.Add(slot);
        }
    }

    private void Refresh(bool force)
    {
        NetworkUnitSystem system = NetworkUnitSystem.Instance;
        IReadOnlyList<SelectionInspectionGroup> groups = system?.GetExtendedSelectionInspectionGroups()
            ?? Array.Empty<SelectionInspectionGroup>();
        int inspectedIndex = system?.InspectedSelectionGroupIndex ?? 0;

        string signature = string.Join("|", groups.Select(group =>
            $"{group.Key}:{group.Count}:{group.Representative?.Health}")) + $"#{inspectedIndex}";

        if (!force && signature == lastSignature)
            return;
        lastSignature = signature;

        bool showYellowSelection = groups.Count > 1;
        if (emptyLabel != null)
            emptyLabel.style.display = groups.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

        for (int index = 0; index < slots.Count; index++)
        {
            Button slot = slots[index];
            if (index >= groups.Count)
            {
                slot.style.display = DisplayStyle.None;
                continue;
            }

            SelectionInspectionGroup group = groups[index];
            slot.style.display = DisplayStyle.Flex;
            slot.EnableInClassList("selected-entities-extended-slot--multi", showYellowSelection);
            slot.EnableInClassList("selected-entities-extended-slot--active", showYellowSelection && index == inspectedIndex);
            slot.EnableInClassList("selected-entities-extended-slot--heroic", group.IsHeroic);

            Label name = slot.Q<Label>("group-name");
            Label count = slot.Q<Label>("group-count");
            if (name != null)
                name.text = group.IsHeroic ? "H" : string.Empty;
            if (count != null)
                count.text = group.IsHeroic || group.Count <= 1 ? string.Empty : $"x{group.Count}";
            slot.tooltip = group.IsHeroic
                ? $"{group.DisplayName} heroico"
                : $"{group.DisplayName} · {group.Count} entidad(es)";
        }
    }
}
