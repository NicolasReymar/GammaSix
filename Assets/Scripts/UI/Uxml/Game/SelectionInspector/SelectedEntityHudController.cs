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

        if (entity.HasAttribute(EntityAttributeIds.Resource))
        {
            string resources = entity.Resources != null && entity.Resources.Count > 0
                ? string.Join(", ", entity.Resources.Select(resource =>
                    $"{resource.ResourceId}: {(entity.ResourceInfinite ? "∞" : resource.Amount.ToString())}"))
                : "Sin recursos disponibles";
            Label resourceInfo = new($"Recurso tier {entity.ResourceTier} · {resources}");
            resourceInfo.AddToClassList("selected-entity-empty");
            attributesList.Add(resourceInfo);
        }

        if (entity.HasAttribute(EntityAttributeIds.Worker))
        {
            string carried = string.IsNullOrWhiteSpace(entity.WorkerResourceName)
                ? "Sin recurso transportado"
                : $"Transporta {entity.WorkerResourceName}: {entity.WorkerCarriedAmount}";
            string status = entity.WorkerIsExtracting ? "Extrayendo" : "Disponible";
            Label workerInfo = new($"{status} · {carried}");
            workerInfo.AddToClassList("selected-entity-empty");
            attributesList.Add(workerInfo);
        }

        if (entity.HasAttack)
        {
            float effectiveSpeed = Mathf.Max(0.05f, entity.BaseAttackSpeed * entity.AttackSpeedMultiplier);
            float effectiveAttackTime = entity.AttackTime / effectiveSpeed;
            float effectiveRecoveryTime = entity.RecoveryTime / effectiveSpeed;
            Label attackInfo = new(
                $"Ataque {entity.AttackDelivery}/{entity.AttackDamageType} · Daño {entity.AttackBaseDamage} · " +
                $"Velocidad {effectiveSpeed:0.##}x · Alcance {entity.AttackRange:0.##} · " +
                $"Preparación {effectiveAttackTime:0.##}s · Recuperación {effectiveRecoveryTime:0.##}s · " +
                $"Postura {GetCombatStanceLabel(entity.CombatStance)}");
            attackInfo.AddToClassList("selected-entity-empty");
            attributesList.Add(attackInfo);
        }
    }

    private void RefreshEntitySummary(NetworkEntityView entity)
    {
        if (entitySummaryLabel == null || entity == null)
            return;

        string team = entity.TeamId == 0 ? "Neutral" : $"Equipo {entity.TeamId}";
        string activity = GetActivityLabel(entity.ActivityState);
        string combat = entity.InCombat ? " · En combate" : string.Empty;
        string underAttack = entity.IsUnderAttack ? " · Bajo ataque" : string.Empty;
        string life = entity.LifeState == EntityLifeState.Alive
            ? string.Empty
            : $" · {entity.LifeState}";
        string phase = entity.AttackPhase != EntityAttackPhase.None
            ? $" · {GetAttackPhaseLabel(entity.AttackPhase)}"
            : string.Empty;
        entitySummaryLabel.text =
            $"{team} · Vida {entity.Health}/{entity.MaxHealth} · {activity}{phase}{combat}{underAttack}{life} · ID {entity.EntityDefinitionId}";
    }

    private static string GetActivityLabel(EntityActivityState state)
    {
        return state switch
        {
            EntityActivityState.Moving => "Moviéndose",
            EntityActivityState.Performing => "Realizando actividad",
            EntityActivityState.Attacking => "Atacando",
            EntityActivityState.Recovering => "Recuperándose",
            EntityActivityState.Dead => "Muerta",
            _ => "Inactiva"
        };
    }


    private static string GetCombatStanceLabel(EntityCombatStance stance)
    {
        return stance == EntityCombatStance.Passive ? "Pasiva" : "Agresiva";
    }

    private static string GetAttackPhaseLabel(EntityAttackPhase phase)
    {
        return phase switch
        {
            EntityAttackPhase.Approaching => "Acercándose al objetivo",
            EntityAttackPhase.Windup => "Preparando ataque",
            EntityAttackPhase.Recovery => "Recuperación de ataque",
            _ => string.Empty
        };
    }
}
