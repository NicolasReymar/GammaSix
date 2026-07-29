using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class MainMenuController
{
    private sealed class EntityAttributeOption
    {
        public readonly string Id;
        public readonly string Label;

        public EntityAttributeOption(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }

    private static readonly EntityAttributeOption[] EntityAttributeOptions =
    {
        new(EntityAttributeIds.Heroic, "Heroica"),
        new(EntityAttributeIds.GroundMovement, "Movimiento terrestre"),
        new(EntityAttributeIds.Selectable, "Seleccionable"),
        new(EntityAttributeIds.NotSelectable, "No seleccionable"),
        new(EntityAttributeIds.Controllable, "Controlable"),
        new(EntityAttributeIds.Own, "Propiedad del equipo"),
        new(EntityAttributeIds.AuraTrigger, "Activa aura"),
        new(EntityAttributeIds.EntityArea, "Entidad de área"),
        new(EntityAttributeIds.AreaAura, "Comportamiento aura"),
        new(EntityAttributeIds.AreaTrigger, "Emite triggers"),
        new(EntityAttributeIds.ThirdPersonCamera, "Cámara en tercera persona")
    };

    private static readonly HashSet<string> ManagedEntityAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        EntityAttributeIds.Entity,
        EntityAttributeIds.Unit,
        EntityAttributeIds.Building,
        EntityAttributeIds.Environment,
        EntityAttributeIds.Humanoid,
        EntityAttributeIds.Beast,
        "unit.machine",
        "unit.undead",
        "unit.elemental",
        EntityAttributeIds.Solid,
        EntityAttributeIds.NotSolid,
        EntityAttributeIds.Resource,
        EntityAttributeIds.Worker
    };

    private readonly List<EntityDefinition> entityCreatorDefinitions = new();
    private readonly Dictionary<string, Toggle> entityCreatorAttributeToggles = new(StringComparer.OrdinalIgnoreCase);

    private EntityDefinition entityCreatorSelection;
    private string entityCreatorOriginalId;
    private bool entityCreatorPopulating;
    private bool entityCreatorDeleteConfirmation;
    private bool entityCreatorListPanelCollapsed;

    private VisualElement entityCreatorListPanel;
    private VisualElement entityCreatorListContent;
    private Label entityCreatorListTitle;
    private Button entityCreatorListToggleButton;
    private ScrollView entityCreatorList;
    private Label entityCreatorCountLabel;
    private Label entityCreatorSelectionLabel;
    private Label entityCreatorStatusLabel;
    private Label entityCreatorPreviewName;
    private Label entityCreatorPreviewClassification;
    private Label entityCreatorPreviewDescription;
    private TextField entityCreatorSearchField;
    private DropdownField entityCreatorFilterKind;
    private TextField entityCreatorIdField;
    private TextField entityCreatorNameField;
    private TextField entityCreatorDescriptionField;
    private DropdownField entityCreatorKindField;
    private DropdownField entityCreatorTypeField;
    private TextField entityCreatorCustomTypeField;
    private IntegerField entityCreatorMaxHealthField;
    private FloatField entityCreatorMoveSpeedField;
    private Toggle entityCreatorSolidToggle;
    private DropdownField entityCreatorVisualField;
    private TextField entityCreatorPrefabField;
    private FloatField entityCreatorScaleX;
    private FloatField entityCreatorScaleY;
    private FloatField entityCreatorScaleZ;
    private FloatField entityCreatorVisualSizeX;
    private FloatField entityCreatorVisualSizeY;
    private FloatField entityCreatorVisualSizeZ;
    private FloatField entityCreatorCollisionX;
    private FloatField entityCreatorCollisionY;
    private FloatField entityCreatorCollisionZ;
    private FloatField entityCreatorGroundOffsetField;
    private TextField entityCreatorCustomAttributesField;
    private Toggle entityCreatorResourceToggle;
    private VisualElement entityCreatorResourcePanel;
    private Toggle entityCreatorResourceInfinite;
    private TextField entityCreatorResourceLines;
    private IntegerField entityCreatorResourceTier;
    private IntegerField entityCreatorResourceAmount;
    private FloatField entityCreatorResourceRange;
    private TextField entityCreatorResourceSpentEntity;
    private TextField entityCreatorResourceTools;
    private Toggle entityCreatorWorkerToggle;
    private VisualElement entityCreatorWorkerPanel;
    private TextField entityCreatorWorkerResourceName;
    private FloatField entityCreatorWorkerTime;
    private IntegerField entityCreatorWorkerTier;
    private FloatField entityCreatorWorkerRange;
    private Toggle entityCreatorWorkerRepeat;
    private TextField entityCreatorWorkerTools;
    private TextField entityCreatorJsonPreview;
    private Button entityCreatorDuplicateButton;
    private Button entityCreatorDeleteButton;
    private Button entityCreatorSaveButton;

    private void ShowEntityCreator()
    {
        LoadScreen(entityCreatorUxml);
        VisualElement root = uiDocument.rootVisualElement;
        if (!BindEntityCreatorElements(root))
        {
            Debug.LogError("[EntityCreator] El UXML no contiene todos los elementos obligatorios.");
            return;
        }

        ConfigureEntityCreatorControls(root);
        RegisterEntityCreatorCallbacks(root);
        ReloadEntityDefinitions(selectId: entityCreatorOriginalId);
    }

    private bool BindEntityCreatorElements(VisualElement root)
    {
        entityCreatorListPanel = root.Q<VisualElement>("entity-editor-list-panel");
        entityCreatorListContent = root.Q<VisualElement>("entity-editor-list-content");
        entityCreatorListTitle = root.Q<Label>("entity-editor-list-title");
        entityCreatorListToggleButton = root.Q<Button>("entity-editor-list-toggle-button");
        entityCreatorList = root.Q<ScrollView>("entity-editor-list");
        entityCreatorCountLabel = root.Q<Label>("entity-editor-count-label");
        entityCreatorSelectionLabel = root.Q<Label>("entity-editor-selection-label");
        entityCreatorStatusLabel = root.Q<Label>("entity-editor-status-label");
        entityCreatorPreviewName = root.Q<Label>("entity-editor-preview-name");
        entityCreatorPreviewClassification = root.Q<Label>("entity-editor-preview-classification");
        entityCreatorPreviewDescription = root.Q<Label>("entity-editor-preview-description");
        entityCreatorSearchField = root.Q<TextField>("entity-editor-search-field");
        entityCreatorFilterKind = root.Q<DropdownField>("entity-editor-filter-kind");
        entityCreatorIdField = root.Q<TextField>("entity-editor-id-field");
        entityCreatorNameField = root.Q<TextField>("entity-editor-name-field");
        entityCreatorDescriptionField = root.Q<TextField>("entity-editor-description-field");
        entityCreatorKindField = root.Q<DropdownField>("entity-editor-kind-field");
        entityCreatorTypeField = root.Q<DropdownField>("entity-editor-type-field");
        entityCreatorCustomTypeField = root.Q<TextField>("entity-editor-custom-type-field");
        entityCreatorMaxHealthField = root.Q<IntegerField>("entity-editor-max-health-field");
        entityCreatorMoveSpeedField = root.Q<FloatField>("entity-editor-move-speed-field");
        entityCreatorSolidToggle = root.Q<Toggle>("entity-editor-solid-toggle");
        entityCreatorVisualField = root.Q<DropdownField>("entity-editor-visual-field");
        entityCreatorPrefabField = root.Q<TextField>("entity-editor-prefab-field");
        entityCreatorScaleX = root.Q<FloatField>("entity-editor-scale-x");
        entityCreatorScaleY = root.Q<FloatField>("entity-editor-scale-y");
        entityCreatorScaleZ = root.Q<FloatField>("entity-editor-scale-z");
        entityCreatorVisualSizeX = root.Q<FloatField>("entity-editor-visual-size-x");
        entityCreatorVisualSizeY = root.Q<FloatField>("entity-editor-visual-size-y");
        entityCreatorVisualSizeZ = root.Q<FloatField>("entity-editor-visual-size-z");
        entityCreatorCollisionX = root.Q<FloatField>("entity-editor-collision-x");
        entityCreatorCollisionY = root.Q<FloatField>("entity-editor-collision-y");
        entityCreatorCollisionZ = root.Q<FloatField>("entity-editor-collision-z");
        entityCreatorGroundOffsetField = root.Q<FloatField>("entity-editor-ground-offset-field");
        entityCreatorCustomAttributesField = root.Q<TextField>("entity-editor-custom-attributes-field");
        entityCreatorResourceToggle = root.Q<Toggle>("entity-editor-resource-toggle");
        entityCreatorResourcePanel = root.Q<VisualElement>("entity-editor-resource-panel");
        entityCreatorResourceInfinite = root.Q<Toggle>("entity-editor-resource-infinite");
        entityCreatorResourceLines = root.Q<TextField>("entity-editor-resource-lines");
        entityCreatorResourceTier = root.Q<IntegerField>("entity-editor-resource-tier");
        entityCreatorResourceAmount = root.Q<IntegerField>("entity-editor-resource-amount");
        entityCreatorResourceRange = root.Q<FloatField>("entity-editor-resource-range");
        entityCreatorResourceSpentEntity = root.Q<TextField>("entity-editor-resource-spent-entity");
        entityCreatorResourceTools = root.Q<TextField>("entity-editor-resource-tools");
        entityCreatorWorkerToggle = root.Q<Toggle>("entity-editor-worker-toggle");
        entityCreatorWorkerPanel = root.Q<VisualElement>("entity-editor-worker-panel");
        entityCreatorWorkerResourceName = root.Q<TextField>("entity-editor-worker-resource-name");
        entityCreatorWorkerTime = root.Q<FloatField>("entity-editor-worker-time");
        entityCreatorWorkerTier = root.Q<IntegerField>("entity-editor-worker-tier");
        entityCreatorWorkerRange = root.Q<FloatField>("entity-editor-worker-range");
        entityCreatorWorkerRepeat = root.Q<Toggle>("entity-editor-worker-repeat");
        entityCreatorWorkerTools = root.Q<TextField>("entity-editor-worker-tools");
        entityCreatorJsonPreview = root.Q<TextField>("entity-editor-json-preview");
        entityCreatorDuplicateButton = root.Q<Button>("entity-editor-duplicate-button");
        entityCreatorDeleteButton = root.Q<Button>("entity-editor-delete-button");
        entityCreatorSaveButton = root.Q<Button>("entity-editor-save-button");

        return entityCreatorListPanel != null &&
               entityCreatorListContent != null &&
               entityCreatorListToggleButton != null &&
               entityCreatorList != null &&
               entityCreatorStatusLabel != null &&
               entityCreatorIdField != null &&
               entityCreatorNameField != null &&
               entityCreatorKindField != null &&
               entityCreatorTypeField != null &&
               entityCreatorJsonPreview != null;
    }

    private void ConfigureEntityCreatorControls(VisualElement root)
    {
        entityCreatorFilterKind.choices = new List<string> { "Todas", "Unidades", "Edificios", "Entorno" };
        entityCreatorFilterKind.SetValueWithoutNotify("Todas");
        entityCreatorKindField.choices = new List<string> { "Unidad", "Edificio", "Entorno" };
        entityCreatorTypeField.choices = new List<string>
        {
            "Humanoide",
            "Bestia",
            "Máquina",
            "No muerto",
            "Elemental",
            "Personalizado",
            "No aplica"
        };
        entityCreatorVisualField.choices = new List<string> { "capsule", "cube", "aura", "prefab" };
        entityCreatorJsonPreview.isReadOnly = true;

        Button testButton = root.Q<Button>("entity-editor-test-button");
        if (testButton != null)
        {
            testButton.SetEnabled(false);
            testButton.tooltip = "La escena sandbox para probar entidades se agregará en una etapa posterior.";
        }

        BuildEntityAttributeToggles(root.Q<VisualElement>("entity-editor-known-attributes"));
        ApplyEntityCreatorListPanelState();
        ResetEntityDeleteConfirmation();
    }

    private void RegisterEntityCreatorCallbacks(VisualElement root)
    {
        RegisterButton(root, "entity-editor-back-button", ShowMainMenu);
        RegisterButton(root, "entity-editor-reload-button", () => ReloadEntityDefinitions(entityCreatorOriginalId));
        RegisterButton(root, "entity-editor-new-button", CreateNewEntityDefinition);
        RegisterButton(root, "entity-editor-duplicate-button", DuplicateEntityDefinition);
        RegisterButton(root, "entity-editor-delete-button", DeleteEntityDefinition);
        RegisterButton(root, "entity-editor-save-button", SaveEntityDefinition);
        RegisterButton(root, "entity-editor-list-toggle-button", ToggleEntityCreatorListPanel);

        entityCreatorSearchField.RegisterValueChangedCallback(_ => RefreshEntityCreatorList());
        entityCreatorFilterKind.RegisterValueChangedCallback(_ => RefreshEntityCreatorList());
        entityCreatorKindField.RegisterValueChangedCallback(_ =>
        {
            if (entityCreatorPopulating) return;
            HandleEntityKindChanged();
            UpdateEntityCreatorPreview();
        });
        entityCreatorTypeField.RegisterValueChangedCallback(_ =>
        {
            if (entityCreatorPopulating) return;
            UpdateEntityCreatorSpecialVisibility();
            UpdateEntityCreatorPreview();
        });
        entityCreatorVisualField.RegisterValueChangedCallback(_ =>
        {
            if (entityCreatorPopulating) return;
            UpdateEntityCreatorSpecialVisibility();
            UpdateEntityCreatorPreview();
        });
        entityCreatorResourceToggle.RegisterValueChangedCallback(_ =>
        {
            if (entityCreatorPopulating) return;
            UpdateEntityCreatorSpecialVisibility();
            UpdateEntityCreatorPreview();
        });
        entityCreatorWorkerToggle.RegisterValueChangedCallback(_ =>
        {
            if (entityCreatorPopulating) return;
            UpdateEntityCreatorSpecialVisibility();
            UpdateEntityCreatorPreview();
        });

        RegisterPreviewCallback(entityCreatorIdField);
        RegisterPreviewCallback(entityCreatorNameField);
        RegisterPreviewCallback(entityCreatorDescriptionField);
        RegisterPreviewCallback(entityCreatorCustomTypeField);
        RegisterPreviewCallback(entityCreatorMaxHealthField);
        RegisterPreviewCallback(entityCreatorMoveSpeedField);
        RegisterPreviewCallback(entityCreatorSolidToggle);
        RegisterPreviewCallback(entityCreatorPrefabField);
        RegisterPreviewCallback(entityCreatorScaleX);
        RegisterPreviewCallback(entityCreatorScaleY);
        RegisterPreviewCallback(entityCreatorScaleZ);
        RegisterPreviewCallback(entityCreatorVisualSizeX);
        RegisterPreviewCallback(entityCreatorVisualSizeY);
        RegisterPreviewCallback(entityCreatorVisualSizeZ);
        RegisterPreviewCallback(entityCreatorCollisionX);
        RegisterPreviewCallback(entityCreatorCollisionY);
        RegisterPreviewCallback(entityCreatorCollisionZ);
        RegisterPreviewCallback(entityCreatorGroundOffsetField);
        RegisterPreviewCallback(entityCreatorCustomAttributesField);
        RegisterPreviewCallback(entityCreatorResourceInfinite);
        RegisterPreviewCallback(entityCreatorResourceLines);
        RegisterPreviewCallback(entityCreatorResourceTier);
        RegisterPreviewCallback(entityCreatorResourceAmount);
        RegisterPreviewCallback(entityCreatorResourceRange);
        RegisterPreviewCallback(entityCreatorResourceSpentEntity);
        RegisterPreviewCallback(entityCreatorResourceTools);
        RegisterPreviewCallback(entityCreatorWorkerResourceName);
        RegisterPreviewCallback(entityCreatorWorkerTime);
        RegisterPreviewCallback(entityCreatorWorkerTier);
        RegisterPreviewCallback(entityCreatorWorkerRange);
        RegisterPreviewCallback(entityCreatorWorkerRepeat);
        RegisterPreviewCallback(entityCreatorWorkerTools);
    }

    private void ToggleEntityCreatorListPanel()
    {
        entityCreatorListPanelCollapsed = !entityCreatorListPanelCollapsed;
        ApplyEntityCreatorListPanelState();
    }

    private void ApplyEntityCreatorListPanelState()
    {
        if (entityCreatorListPanel == null ||
            entityCreatorListContent == null ||
            entityCreatorListToggleButton == null)
        {
            return;
        }

        entityCreatorListPanel.EnableInClassList(
            "entity-list-panel-collapsed",
            entityCreatorListPanelCollapsed);
        entityCreatorListToggleButton.EnableInClassList(
            "entity-list-toggle-button-collapsed",
            entityCreatorListPanelCollapsed);

        entityCreatorListContent.style.display = entityCreatorListPanelCollapsed
            ? DisplayStyle.None
            : DisplayStyle.Flex;

        if (entityCreatorListTitle != null)
        {
            entityCreatorListTitle.style.display = entityCreatorListPanelCollapsed
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        entityCreatorListToggleButton.text = entityCreatorListPanelCollapsed ? "›" : "‹";
        entityCreatorListToggleButton.tooltip = entityCreatorListPanelCollapsed
            ? "Mostrar panel de entidades"
            : "Contraer panel de entidades";
    }

    private void RegisterPreviewCallback<T>(BaseField<T> field)
    {
        field?.RegisterValueChangedCallback(_ =>
        {
            if (!entityCreatorPopulating)
                UpdateEntityCreatorPreview();
        });
    }

    private void BuildEntityAttributeToggles(VisualElement container)
    {
        entityCreatorAttributeToggles.Clear();
        if (container == null)
            return;

        container.Clear();
        foreach (EntityAttributeOption option in EntityAttributeOptions)
        {
            Toggle toggle = new(option.Label) { name = $"entity-attribute-{option.Id}" };
            toggle.AddToClassList("entity-attribute-toggle");
            toggle.RegisterValueChangedCallback(_ =>
            {
                if (!entityCreatorPopulating)
                    UpdateEntityCreatorPreview();
            });
            container.Add(toggle);
            entityCreatorAttributeToggles[option.Id] = toggle;
        }
    }

    private void ReloadEntityDefinitions(string selectId = null)
    {
        try
        {
            entityCreatorDefinitions.Clear();
            entityCreatorDefinitions.AddRange(EntityDefinitionRepository.LoadAll());
            RefreshEntityCreatorList();

            EntityDefinition definition = !string.IsNullOrWhiteSpace(selectId)
                ? entityCreatorDefinitions.FirstOrDefault(item => string.Equals(item.id, selectId, StringComparison.OrdinalIgnoreCase))
                : entityCreatorDefinitions.FirstOrDefault();

            if (definition != null)
                SelectEntityDefinition(definition);
            else
                CreateNewEntityDefinition();

            SetEntityCreatorStatus($"Definiciones cargadas desde: {EntityDefinitionRepository.EntitiesPath}");
        }
        catch (Exception exception)
        {
            SetEntityCreatorStatus($"No se pudieron cargar las entidades: {exception.Message}", true);
        }
    }

    private void RefreshEntityCreatorList()
    {
        if (entityCreatorList == null)
            return;

        entityCreatorList.Clear();
        string search = entityCreatorSearchField?.value?.Trim() ?? string.Empty;
        string kindFilter = EntityKindFromFilter(entityCreatorFilterKind?.value);

        IEnumerable<EntityDefinition> filtered = entityCreatorDefinitions.Where(definition =>
        {
            bool matchesSearch = string.IsNullOrEmpty(search) ||
                                 (definition.name?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                                 (definition.id?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                                 (definition.entityType?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
            bool matchesKind = string.IsNullOrEmpty(kindFilter) || string.Equals(definition.kind, kindFilter, StringComparison.OrdinalIgnoreCase);
            return matchesSearch && matchesKind;
        });

        int visibleCount = 0;
        foreach (EntityDefinition definition in filtered)
        {
            visibleCount++;
            Button item = new();
            item.AddToClassList("entity-list-item");
            if (!string.IsNullOrWhiteSpace(entityCreatorOriginalId) &&
                string.Equals(entityCreatorOriginalId, definition.id, StringComparison.OrdinalIgnoreCase))
                item.AddToClassList("entity-list-item-selected");

            Label nameLabel = new(string.IsNullOrWhiteSpace(definition.name) ? definition.id : definition.name);
            nameLabel.AddToClassList("entity-list-item-name");
            Label idLabel = new($"{GetKindDisplayName(definition.kind)} · {InferEntityType(definition)}\n{definition.id}");
            idLabel.AddToClassList("entity-list-item-id");
            item.Add(nameLabel);
            item.Add(idLabel);
            item.clicked += () => SelectEntityDefinition(definition);
            entityCreatorList.Add(item);
        }

        if (visibleCount == 0)
        {
            Label empty = new("No hay entidades que coincidan con el filtro.");
            empty.AddToClassList("entity-muted-label");
            entityCreatorList.Add(empty);
        }

        if (entityCreatorCountLabel != null)
            entityCreatorCountLabel.text = $"{visibleCount} visibles · {entityCreatorDefinitions.Count} totales";
    }

    private void SelectEntityDefinition(EntityDefinition definition)
    {
        if (definition == null)
            return;

        entityCreatorSelection = CloneEntityDefinition(definition);
        entityCreatorOriginalId = definition.id;
        ResetEntityDeleteConfirmation();
        PopulateEntityCreatorForm(entityCreatorSelection);
        RefreshEntityCreatorList();
        SetEntityCreatorStatus($"Editando '{definition.id}'. Los cambios se guardan en persistentDataPath.");
    }

    private void CreateNewEntityDefinition()
    {
        EntityDefinition definition = new()
        {
            id = GenerateUniqueEntityId("unit.new_entity"),
            name = "Nueva entidad",
            description = "",
            kind = EntityKinds.Unit,
            entityType = "humanoid",
            maxHealth = 100,
            moveSpeed = 4f,
            solid = true,
            visual = "capsule",
            scale = CreateVector(1f, 1f, 1f),
            groundOffset = -1f,
            attributes = new[]
            {
                EntityAttributeIds.Entity,
                EntityAttributeIds.Unit,
                EntityAttributeIds.Humanoid,
                EntityAttributeIds.GroundMovement,
                EntityAttributeIds.Selectable,
                EntityAttributeIds.Controllable,
                EntityAttributeIds.Solid
            }
        };

        entityCreatorSelection = definition;
        entityCreatorOriginalId = null;
        ResetEntityDeleteConfirmation();
        PopulateEntityCreatorForm(definition);
        RefreshEntityCreatorList();
        SetEntityCreatorStatus("Nueva entidad preparada. Cambia el ID y completa sus datos antes de guardar.");
    }

    private void DuplicateEntityDefinition()
    {
        EntityDefinition source = BuildEntityDefinitionFromForm(out string validationError);
        if (source == null)
        {
            SetEntityCreatorStatus(validationError, true);
            return;
        }

        EntityDefinition copy = CloneEntityDefinition(source);
        copy.id = GenerateUniqueEntityId($"{source.id}.copy");
        copy.name = string.IsNullOrWhiteSpace(source.name) ? "Copia" : $"{source.name} (copia)";
        entityCreatorSelection = copy;
        entityCreatorOriginalId = null;
        ResetEntityDeleteConfirmation();
        PopulateEntityCreatorForm(copy);
        RefreshEntityCreatorList();
        SetEntityCreatorStatus("Copia creada en memoria. Guarda para crear su archivo JSON.");
    }

    private void DeleteEntityDefinition()
    {
        if (string.IsNullOrWhiteSpace(entityCreatorOriginalId))
        {
            CreateNewEntityDefinition();
            SetEntityCreatorStatus("La entidad nueva no estaba guardada; se descartaron sus cambios.");
            return;
        }

        if (!entityCreatorDeleteConfirmation)
        {
            entityCreatorDeleteConfirmation = true;
            if (entityCreatorDeleteButton != null)
                entityCreatorDeleteButton.text = "Confirmar eliminar";
            SetEntityCreatorStatus($"Presiona nuevamente para eliminar '{entityCreatorOriginalId}'. Esta acción borra su JSON guardado.", true);
            return;
        }

        string deletedId = entityCreatorOriginalId;
        try
        {
            if (!EntityDefinitionRepository.Delete(deletedId))
            {
                SetEntityCreatorStatus($"No se encontró el archivo de '{deletedId}'.", true);
                return;
            }

            entityCreatorOriginalId = null;
            ReloadEntityDefinitions();
            SetEntityCreatorStatus($"Entidad '{deletedId}' eliminada.");
        }
        catch (Exception exception)
        {
            SetEntityCreatorStatus($"No se pudo eliminar la entidad: {exception.Message}", true);
        }
    }

    private void SaveEntityDefinition()
    {
        EntityDefinition definition = BuildEntityDefinitionFromForm(out string validationError);
        if (definition == null)
        {
            SetEntityCreatorStatus(validationError, true);
            return;
        }

        bool changesId = !string.IsNullOrWhiteSpace(entityCreatorOriginalId) &&
                         !string.Equals(entityCreatorOriginalId, definition.id, StringComparison.OrdinalIgnoreCase);
        bool isNew = string.IsNullOrWhiteSpace(entityCreatorOriginalId);
        if ((isNew || changesId) && EntityDefinitionRepository.Exists(definition.id))
        {
            SetEntityCreatorStatus($"Ya existe una entidad con el ID '{definition.id}'. Elige otro ID.", true);
            return;
        }

        try
        {
            EntityDefinitionRepository.Save(definition, entityCreatorOriginalId);
            entityCreatorOriginalId = definition.id;
            ResetEntityDeleteConfirmation();
            ReloadEntityDefinitions(definition.id);
            SetEntityCreatorStatus($"Entidad '{definition.id}' guardada correctamente.");
        }
        catch (Exception exception)
        {
            SetEntityCreatorStatus($"No se pudo guardar: {exception.Message}", true);
        }
    }

    private void PopulateEntityCreatorForm(EntityDefinition definition)
    {
        entityCreatorPopulating = true;
        try
        {
            string entityType = InferEntityType(definition);
            entityCreatorIdField.SetValueWithoutNotify(definition.id ?? string.Empty);
            entityCreatorNameField.SetValueWithoutNotify(definition.name ?? string.Empty);
            entityCreatorDescriptionField.SetValueWithoutNotify(definition.description ?? string.Empty);
            entityCreatorKindField.SetValueWithoutNotify(GetKindChoice(definition.kind));
            entityCreatorTypeField.SetValueWithoutNotify(GetTypeChoice(entityType, definition.kind));
            entityCreatorCustomTypeField.SetValueWithoutNotify(IsKnownEntityType(entityType) ? string.Empty : entityType);
            entityCreatorMaxHealthField.SetValueWithoutNotify(Mathf.Max(1, definition.maxHealth));
            entityCreatorMoveSpeedField.SetValueWithoutNotify(Mathf.Max(0f, definition.moveSpeed));
            entityCreatorSolidToggle.SetValueWithoutNotify(definition.solid);
            entityCreatorVisualField.SetValueWithoutNotify(string.IsNullOrWhiteSpace(definition.visual) ? "capsule" : definition.visual);
            entityCreatorPrefabField.SetValueWithoutNotify(definition.prefabResource ?? string.Empty);
            SetVectorFields(definition.scale, entityCreatorScaleX, entityCreatorScaleY, entityCreatorScaleZ, 1f, 1f, 1f);
            SetVectorFields(definition.visualSize, entityCreatorVisualSizeX, entityCreatorVisualSizeY, entityCreatorVisualSizeZ, 0f, 0f, 0f);
            SetVectorFields(definition.collisionSize, entityCreatorCollisionX, entityCreatorCollisionY, entityCreatorCollisionZ, 0f, 0f, 0f);
            entityCreatorGroundOffsetField.SetValueWithoutNotify(definition.groundOffset);

            HashSet<string> attributes = new(definition.attributes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (EntityAttributeOption option in EntityAttributeOptions)
            {
                if (entityCreatorAttributeToggles.TryGetValue(option.Id, out Toggle toggle))
                    toggle.SetValueWithoutNotify(attributes.Contains(option.Id));
            }

            HashSet<string> editableKnown = new(EntityAttributeOptions.Select(option => option.Id), StringComparer.OrdinalIgnoreCase);
            string[] customAttributes = attributes
                .Where(attribute => !ManagedEntityAttributes.Contains(attribute) && !editableKnown.Contains(attribute))
                .OrderBy(attribute => attribute, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            entityCreatorCustomAttributesField.SetValueWithoutNotify(string.Join("\n", customAttributes));

            ResourceEntityDefinition resource = definition.resource;
            bool hasResource = resource != null || attributes.Contains(EntityAttributeIds.Resource);
            entityCreatorResourceToggle.SetValueWithoutNotify(hasResource);
            entityCreatorResourceInfinite.SetValueWithoutNotify(resource?.infinite ?? false);
            entityCreatorResourceLines.SetValueWithoutNotify(FormatResourceAmounts(resource?.resources));
            entityCreatorResourceTier.SetValueWithoutNotify(Mathf.Max(1, resource?.resourceTier ?? 1));
            entityCreatorResourceAmount.SetValueWithoutNotify(Mathf.Max(1, resource?.amountPerExtraction ?? 1));
            entityCreatorResourceRange.SetValueWithoutNotify(Mathf.Max(0f, resource?.interactionRange ?? 1.25f));
            entityCreatorResourceSpentEntity.SetValueWithoutNotify(resource?.onResourcesSpentEntityId ?? string.Empty);
            entityCreatorResourceTools.SetValueWithoutNotify(string.Join("\n", resource?.extractionTools ?? Array.Empty<string>()));

            WorkerEntityDefinition worker = definition.worker;
            bool hasWorker = worker != null || attributes.Contains(EntityAttributeIds.Worker);
            entityCreatorWorkerToggle.SetValueWithoutNotify(hasWorker);
            entityCreatorWorkerResourceName.SetValueWithoutNotify(worker?.resourceName ?? "wood");
            entityCreatorWorkerTime.SetValueWithoutNotify(Mathf.Max(0.01f, worker?.extractionTime ?? 1f));
            entityCreatorWorkerTier.SetValueWithoutNotify(Mathf.Max(1, worker?.workerTier ?? 1));
            entityCreatorWorkerRange.SetValueWithoutNotify(Mathf.Max(0f, worker?.interactionRange ?? 1.25f));
            entityCreatorWorkerRepeat.SetValueWithoutNotify(worker?.repeatExtraction ?? true);
            entityCreatorWorkerTools.SetValueWithoutNotify(string.Join("\n", worker?.tools ?? Array.Empty<string>()));
        }
        finally
        {
            entityCreatorPopulating = false;
        }

        UpdateEntityCreatorSpecialVisibility();
        UpdateEntityCreatorPreview();
        entityCreatorDuplicateButton?.SetEnabled(true);
        entityCreatorDeleteButton?.SetEnabled(true);
        entityCreatorSaveButton?.SetEnabled(true);
    }

    private EntityDefinition BuildEntityDefinitionFromForm(out string validationError)
    {
        validationError = null;
        string id = entityCreatorIdField?.value?.Trim();
        string name = entityCreatorNameField?.value?.Trim();
        string kind = GetKindCode(entityCreatorKindField?.value);
        string entityType = GetEntityTypeCode(entityCreatorTypeField?.value, entityCreatorCustomTypeField?.value);

        if (string.IsNullOrWhiteSpace(id))
        {
            validationError = "Debes indicar un ID para la entidad.";
            return null;
        }
        if (id.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '-' && character != '_'))
        {
            validationError = "El ID solo puede contener letras, números, puntos, guiones y guiones bajos.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            validationError = "Debes indicar un nombre visible.";
            return null;
        }
        if (entityCreatorMaxHealthField.value < 1)
        {
            validationError = "La vida máxima debe ser igual o superior a 1.";
            return null;
        }
        if (entityCreatorMoveSpeedField.value < 0f)
        {
            validationError = "La velocidad no puede ser negativa.";
            return null;
        }
        if (!ValidateRequiredVector(entityCreatorScaleX.value, entityCreatorScaleY.value, entityCreatorScaleZ.value))
        {
            validationError = "Los tres valores de escala deben ser mayores que 0.";
            return null;
        }
        if (!ValidateOptionalVector(entityCreatorVisualSizeX.value, entityCreatorVisualSizeY.value, entityCreatorVisualSizeZ.value))
        {
            validationError = "El tamaño visual debe tener sus tres valores en 0 o sus tres valores mayores que 0.";
            return null;
        }
        if (!ValidateOptionalVector(entityCreatorCollisionX.value, entityCreatorCollisionY.value, entityCreatorCollisionZ.value))
        {
            validationError = "El tamaño de colisión debe tener sus tres valores en 0 o sus tres valores mayores que 0.";
            return null;
        }
        if (string.Equals(entityCreatorVisualField.value, "prefab", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(entityCreatorPrefabField.value))
        {
            validationError = "Seleccionaste visual prefab, pero falta el recurso del prefab.";
            return null;
        }

        HashSet<string> attributes = BuildEntityAttributes(kind, entityType);
        if (attributes.Contains(EntityAttributeIds.Selectable) && attributes.Contains(EntityAttributeIds.NotSelectable))
        {
            validationError = "Una entidad no puede ser seleccionable y no seleccionable al mismo tiempo.";
            return null;
        }

        ResourceEntityDefinition resource = null;
        if (entityCreatorResourceToggle.value)
        {
            if (!TryParseResourceAmounts(entityCreatorResourceLines.value, out ResourceAmountDefinition[] amounts, out validationError))
                return null;

            resource = new ResourceEntityDefinition
            {
                infinite = entityCreatorResourceInfinite.value,
                onResourcesSpentEntityId = EmptyToNull(entityCreatorResourceSpentEntity.value),
                resources = amounts,
                resourceTier = Mathf.Max(1, entityCreatorResourceTier.value),
                extractionTools = ParseTokenList(entityCreatorResourceTools.value),
                interactionRange = Mathf.Max(0f, entityCreatorResourceRange.value),
                amountPerExtraction = Mathf.Max(1, entityCreatorResourceAmount.value)
            };
        }

        WorkerEntityDefinition worker = null;
        if (entityCreatorWorkerToggle.value)
        {
            if (!string.Equals(kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase))
            {
                validationError = "El componente trabajador solo puede utilizarse en entidades de categoría Unidad.";
                return null;
            }
            if (string.IsNullOrWhiteSpace(entityCreatorWorkerResourceName.value))
            {
                validationError = "El trabajador debe indicar el recurso principal que extrae.";
                return null;
            }

            worker = new WorkerEntityDefinition
            {
                extractionTime = Mathf.Max(0.01f, entityCreatorWorkerTime.value),
                repeatExtraction = entityCreatorWorkerRepeat.value,
                resourceName = entityCreatorWorkerResourceName.value.Trim(),
                workerTier = Mathf.Max(1, entityCreatorWorkerTier.value),
                tools = ParseTokenList(entityCreatorWorkerTools.value),
                interactionRange = Mathf.Max(0f, entityCreatorWorkerRange.value)
            };
        }

        EntityDefinition definition = new()
        {
            id = id,
            name = name,
            description = EmptyToNull(entityCreatorDescriptionField.value),
            kind = kind,
            entityType = string.Equals(kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase) ? entityType : kind,
            maxHealth = entityCreatorMaxHealthField.value,
            moveSpeed = entityCreatorMoveSpeedField.value,
            solid = entityCreatorSolidToggle.value,
            visual = entityCreatorVisualField.value,
            prefabResource = EmptyToNull(entityCreatorPrefabField.value),
            scale = CreateVector(entityCreatorScaleX.value, entityCreatorScaleY.value, entityCreatorScaleZ.value),
            visualSize = CreateOptionalVector(entityCreatorVisualSizeX.value, entityCreatorVisualSizeY.value, entityCreatorVisualSizeZ.value),
            collisionSize = CreateOptionalVector(entityCreatorCollisionX.value, entityCreatorCollisionY.value, entityCreatorCollisionZ.value),
            groundOffset = entityCreatorGroundOffsetField.value,
            attributes = attributes.OrderBy(attribute => attribute, StringComparer.OrdinalIgnoreCase).ToArray(),
            resource = resource,
            worker = worker,
            area = entityCreatorSelection?.area
        };

        return definition;
    }

    private HashSet<string> BuildEntityAttributes(string kind, string entityType)
    {
        HashSet<string> attributes = new(StringComparer.OrdinalIgnoreCase)
        {
            EntityAttributeIds.Entity
        };

        if (string.Equals(kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase))
        {
            attributes.Add(EntityAttributeIds.Unit);
            string typeAttribute = GetEntityTypeAttribute(entityType);
            if (!string.IsNullOrWhiteSpace(typeAttribute))
                attributes.Add(typeAttribute);
        }
        else if (string.Equals(kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase))
        {
            attributes.Add(EntityAttributeIds.Building);
        }
        else
        {
            attributes.Add(EntityAttributeIds.Environment);
        }

        foreach (KeyValuePair<string, Toggle> pair in entityCreatorAttributeToggles)
        {
            if (pair.Value.value)
                attributes.Add(pair.Key);
        }

        foreach (string customAttribute in ParseTokenList(entityCreatorCustomAttributesField.value))
        {
            if (!ManagedEntityAttributes.Contains(customAttribute))
                attributes.Add(customAttribute);
        }

        attributes.Remove(EntityAttributeIds.Solid);
        attributes.Remove(EntityAttributeIds.NotSolid);
        attributes.Add(entityCreatorSolidToggle.value ? EntityAttributeIds.Solid : EntityAttributeIds.NotSolid);

        attributes.Remove(EntityAttributeIds.Resource);
        if (entityCreatorResourceToggle.value)
            attributes.Add(EntityAttributeIds.Resource);

        attributes.Remove(EntityAttributeIds.Worker);
        if (entityCreatorWorkerToggle.value)
            attributes.Add(EntityAttributeIds.Worker);

        return attributes;
    }

    private void HandleEntityKindChanged()
    {
        bool isUnit = string.Equals(GetKindCode(entityCreatorKindField.value), EntityKinds.Unit, StringComparison.OrdinalIgnoreCase);
        entityCreatorPopulating = true;
        try
        {
            if (isUnit && string.Equals(entityCreatorTypeField.value, "No aplica", StringComparison.OrdinalIgnoreCase))
                entityCreatorTypeField.SetValueWithoutNotify("Humanoide");
            if (!isUnit)
            {
                entityCreatorTypeField.SetValueWithoutNotify("No aplica");
                entityCreatorWorkerToggle.SetValueWithoutNotify(false);
            }
        }
        finally
        {
            entityCreatorPopulating = false;
        }
        UpdateEntityCreatorSpecialVisibility();
    }

    private void UpdateEntityCreatorSpecialVisibility()
    {
        bool isUnit = string.Equals(GetKindCode(entityCreatorKindField?.value), EntityKinds.Unit, StringComparison.OrdinalIgnoreCase);
        bool customType = isUnit && string.Equals(entityCreatorTypeField?.value, "Personalizado", StringComparison.OrdinalIgnoreCase);
        bool prefabVisual = string.Equals(entityCreatorVisualField?.value, "prefab", StringComparison.OrdinalIgnoreCase);

        entityCreatorTypeField?.SetEnabled(isUnit);
        entityCreatorWorkerToggle?.SetEnabled(isUnit);
        SetDisplay(entityCreatorCustomTypeField, customType);
        SetDisplay(entityCreatorPrefabField, prefabVisual);
        SetDisplay(entityCreatorResourcePanel, entityCreatorResourceToggle?.value ?? false);
        SetDisplay(entityCreatorWorkerPanel, isUnit && (entityCreatorWorkerToggle?.value ?? false));
    }

    private void UpdateEntityCreatorPreview()
    {
        if (entityCreatorJsonPreview == null)
            return;

        EntityDefinition preview = BuildEntityDefinitionForPreview();
        if (entityCreatorPreviewName != null)
            entityCreatorPreviewName.text = string.IsNullOrWhiteSpace(preview.name) ? "Entidad sin nombre" : preview.name;
        if (entityCreatorPreviewClassification != null)
            entityCreatorPreviewClassification.text = $"{preview.kind} · {preview.entityType}";
        if (entityCreatorPreviewDescription != null)
            entityCreatorPreviewDescription.text = string.IsNullOrWhiteSpace(preview.description)
                ? "Sin descripción."
                : preview.description;
        entityCreatorJsonPreview.SetValueWithoutNotify(JsonUtility.ToJson(preview, true));

        if (entityCreatorSelectionLabel != null)
            entityCreatorSelectionLabel.text = string.IsNullOrWhiteSpace(entityCreatorOriginalId)
                ? "Nueva entidad sin guardar"
                : $"Archivo: {entityCreatorOriginalId}.json";
    }

    private EntityDefinition BuildEntityDefinitionForPreview()
    {
        string kind = GetKindCode(entityCreatorKindField?.value);
        string type = GetEntityTypeCode(entityCreatorTypeField?.value, entityCreatorCustomTypeField?.value);
        HashSet<string> attributes = BuildEntityAttributes(kind, type);

        EntityDefinition preview = new()
        {
            id = entityCreatorIdField?.value?.Trim(),
            name = entityCreatorNameField?.value?.Trim(),
            description = EmptyToNull(entityCreatorDescriptionField?.value),
            kind = kind,
            entityType = string.Equals(kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase) ? type : kind,
            maxHealth = Mathf.Max(1, entityCreatorMaxHealthField?.value ?? 1),
            moveSpeed = Mathf.Max(0f, entityCreatorMoveSpeedField?.value ?? 0f),
            solid = entityCreatorSolidToggle?.value ?? false,
            visual = entityCreatorVisualField?.value ?? "capsule",
            prefabResource = EmptyToNull(entityCreatorPrefabField?.value),
            scale = CreateVector(entityCreatorScaleX?.value ?? 1f, entityCreatorScaleY?.value ?? 1f, entityCreatorScaleZ?.value ?? 1f),
            visualSize = CreateOptionalVector(entityCreatorVisualSizeX?.value ?? 0f, entityCreatorVisualSizeY?.value ?? 0f, entityCreatorVisualSizeZ?.value ?? 0f),
            collisionSize = CreateOptionalVector(entityCreatorCollisionX?.value ?? 0f, entityCreatorCollisionY?.value ?? 0f, entityCreatorCollisionZ?.value ?? 0f),
            groundOffset = entityCreatorGroundOffsetField?.value ?? -1f,
            attributes = attributes.OrderBy(attribute => attribute, StringComparer.OrdinalIgnoreCase).ToArray()
        };

        if (entityCreatorResourceToggle?.value ?? false)
        {
            TryParseResourceAmounts(entityCreatorResourceLines?.value, out ResourceAmountDefinition[] resources, out _);
            preview.resource = new ResourceEntityDefinition
            {
                infinite = entityCreatorResourceInfinite?.value ?? false,
                onResourcesSpentEntityId = EmptyToNull(entityCreatorResourceSpentEntity?.value),
                resources = resources ?? Array.Empty<ResourceAmountDefinition>(),
                resourceTier = Mathf.Max(1, entityCreatorResourceTier?.value ?? 1),
                extractionTools = ParseTokenList(entityCreatorResourceTools?.value),
                interactionRange = Mathf.Max(0f, entityCreatorResourceRange?.value ?? 0f),
                amountPerExtraction = Mathf.Max(1, entityCreatorResourceAmount?.value ?? 1)
            };
        }

        if (entityCreatorWorkerToggle?.value ?? false)
        {
            preview.worker = new WorkerEntityDefinition
            {
                extractionTime = Mathf.Max(0.01f, entityCreatorWorkerTime?.value ?? 1f),
                repeatExtraction = entityCreatorWorkerRepeat?.value ?? false,
                resourceName = entityCreatorWorkerResourceName?.value,
                workerTier = Mathf.Max(1, entityCreatorWorkerTier?.value ?? 1),
                tools = ParseTokenList(entityCreatorWorkerTools?.value),
                interactionRange = Mathf.Max(0f, entityCreatorWorkerRange?.value ?? 0f)
            };
        }

        return preview;
    }

    private void ResetEntityDeleteConfirmation()
    {
        entityCreatorDeleteConfirmation = false;
        if (entityCreatorDeleteButton != null)
            entityCreatorDeleteButton.text = "Eliminar";
    }

    private void SetEntityCreatorStatus(string message, bool error = false)
    {
        if (entityCreatorStatusLabel != null)
        {
            entityCreatorStatusLabel.text = message ?? string.Empty;
            entityCreatorStatusLabel.style.color = error
                ? new StyleColor(new Color(1f, 0.64f, 0.64f))
                : new StyleColor(new Color(0.82f, 0.93f, 0.88f));
        }
        Debug.Log($"[EntityCreator] {message}");
    }

    private string GenerateUniqueEntityId(string baseId)
    {
        string normalized = string.IsNullOrWhiteSpace(baseId) ? "unit.new_entity" : baseId.Trim();
        HashSet<string> ids = new(entityCreatorDefinitions.Select(definition => definition.id), StringComparer.OrdinalIgnoreCase);
        if (!ids.Contains(normalized))
            return normalized;

        int index = 2;
        while (ids.Contains($"{normalized}_{index}"))
            index++;
        return $"{normalized}_{index}";
    }

    private static EntityDefinition CloneEntityDefinition(EntityDefinition source)
    {
        return source == null ? null : JsonUtility.FromJson<EntityDefinition>(JsonUtility.ToJson(source));
    }

    private static string EntityKindFromFilter(string filter)
    {
        return filter switch
        {
            "Unidades" => EntityKinds.Unit,
            "Edificios" => EntityKinds.Building,
            "Entorno" => EntityKinds.Environment,
            _ => null
        };
    }

    private static string GetKindChoice(string kind)
    {
        if (string.Equals(kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase)) return "Edificio";
        if (string.Equals(kind, EntityKinds.Environment, StringComparison.OrdinalIgnoreCase)) return "Entorno";
        return "Unidad";
    }

    private static string GetKindCode(string choice)
    {
        return choice switch
        {
            "Edificio" => EntityKinds.Building,
            "Entorno" => EntityKinds.Environment,
            _ => EntityKinds.Unit
        };
    }

    private static string GetKindDisplayName(string kind)
    {
        if (string.Equals(kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase)) return "Edificio";
        if (string.Equals(kind, EntityKinds.Environment, StringComparison.OrdinalIgnoreCase)) return "Entorno";
        return "Unidad";
    }

    private static string InferEntityType(EntityDefinition definition)
    {
        if (definition == null)
            return "none";
        if (!string.Equals(definition.kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(definition.kind) ? "none" : definition.kind.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(definition.entityType) && !string.Equals(definition.entityType, "none", StringComparison.OrdinalIgnoreCase))
            return definition.entityType.Trim().ToLowerInvariant();

        HashSet<string> attributes = new(definition.attributes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (attributes.Contains(EntityAttributeIds.Humanoid)) return "humanoid";
        if (attributes.Contains(EntityAttributeIds.Beast)) return "beast";
        if (attributes.Contains("unit.machine")) return "machine";
        if (attributes.Contains("unit.undead")) return "undead";
        if (attributes.Contains("unit.elemental")) return "elemental";
        return string.Equals(definition.kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase) ? "custom" : definition.kind;
    }

    private static string GetTypeChoice(string type, string kind)
    {
        if (!string.Equals(kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase)) return "No aplica";
        return type switch
        {
            "humanoid" => "Humanoide",
            "beast" => "Bestia",
            "machine" => "Máquina",
            "undead" => "No muerto",
            "elemental" => "Elemental",
            _ => "Personalizado"
        };
    }

    private static string GetEntityTypeCode(string choice, string customType)
    {
        return choice switch
        {
            "Humanoide" => "humanoid",
            "Bestia" => "beast",
            "Máquina" => "machine",
            "No muerto" => "undead",
            "Elemental" => "elemental",
            "Personalizado" => string.IsNullOrWhiteSpace(customType) ? "custom" : customType.Trim().ToLowerInvariant(),
            _ => "none"
        };
    }

    private static string GetEntityTypeAttribute(string entityType)
    {
        return entityType switch
        {
            "humanoid" => EntityAttributeIds.Humanoid,
            "beast" => EntityAttributeIds.Beast,
            "machine" => "unit.machine",
            "undead" => "unit.undead",
            "elemental" => "unit.elemental",
            "none" => null,
            _ => string.IsNullOrWhiteSpace(entityType) ? null : $"unit.{entityType.Trim().ToLowerInvariant().Replace(' ', '_')}"
        };
    }

    private static bool IsKnownEntityType(string type)
    {
        return type is "humanoid" or "beast" or "machine" or "undead" or "elemental" or "none" or "building" or "environment";
    }

    private static void SetVectorFields(
        ScenarioVector3 vector,
        FloatField xField,
        FloatField yField,
        FloatField zField,
        float fallbackX,
        float fallbackY,
        float fallbackZ)
    {
        xField.SetValueWithoutNotify(vector?.x ?? fallbackX);
        yField.SetValueWithoutNotify(vector?.y ?? fallbackY);
        zField.SetValueWithoutNotify(vector?.z ?? fallbackZ);
    }

    private static ScenarioVector3 CreateVector(float x, float y, float z)
    {
        return new ScenarioVector3 { x = x, y = y, z = z };
    }

    private static ScenarioVector3 CreateOptionalVector(float x, float y, float z)
    {
        return Mathf.Approximately(x, 0f) && Mathf.Approximately(y, 0f) && Mathf.Approximately(z, 0f)
            ? null
            : CreateVector(x, y, z);
    }

    private static bool ValidateRequiredVector(float x, float y, float z)
    {
        return x > 0f && y > 0f && z > 0f;
    }

    private static bool ValidateOptionalVector(float x, float y, float z)
    {
        bool allZero = Mathf.Approximately(x, 0f) && Mathf.Approximately(y, 0f) && Mathf.Approximately(z, 0f);
        bool allPositive = x > 0f && y > 0f && z > 0f;
        return allZero || allPositive;
    }

    private static string[] ParseTokenList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryParseResourceAmounts(
        string value,
        out ResourceAmountDefinition[] resources,
        out string validationError)
    {
        resources = Array.Empty<ResourceAmountDefinition>();
        validationError = null;
        string[] lines = (value ?? string.Empty)
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

        List<ResourceAmountDefinition> parsed = new();
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf('=');
            if (separator < 0)
                separator = line.IndexOf(':');
            if (separator <= 0 || separator >= line.Length - 1)
            {
                validationError = $"El recurso '{line}' no usa el formato nombre=cantidad.";
                return false;
            }

            string resourceId = line[..separator].Trim();
            string amountText = line[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(resourceId) || !int.TryParse(amountText, out int amount) || amount < 1)
            {
                validationError = $"El recurso '{line}' debe tener un nombre y una cantidad entera mayor que 0.";
                return false;
            }

            parsed.Add(new ResourceAmountDefinition { resourceId = resourceId, amount = amount });
        }

        if (parsed.Count == 0)
        {
            validationError = "Una entidad recurso debe declarar al menos un recurso, por ejemplo wood=100.";
            return false;
        }

        resources = parsed.ToArray();
        return true;
    }

    private static string FormatResourceAmounts(ResourceAmountDefinition[] resources)
    {
        if (resources == null || resources.Length == 0)
            return "wood=100";
        return string.Join("\n", resources.Select(resource => $"{resource.resourceId}={resource.amount}"));
    }

    private static string EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element != null)
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
