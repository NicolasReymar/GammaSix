using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkEntityView : MonoBehaviour
{
    public int UnitId { get; private set; }
    public string EntityDefinitionId { get; private set; }
    public string UnitName { get; private set; }
    public string UnitTypeId { get; private set; }
    public EntityAttributeSet Attributes { get; private set; } = new();
    public int OwnerParticipantId { get; private set; }
    public ulong OwnerClientId { get; private set; }
    public int TeamId { get; private set; }
    public int ColorId { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth { get; private set; }
    public bool ResourceInfinite { get; private set; }
    public int ResourceTier { get; private set; }
    public IReadOnlyList<ResourceSnapshotData> Resources { get; private set; } = Array.Empty<ResourceSnapshotData>();
    public string WorkerResourceName { get; private set; }
    public int WorkerCarriedAmount { get; private set; }
    public bool WorkerIsExtracting { get; private set; }
    public int AreaOccupantCount { get; private set; }
    public EntityLifeState LifeState { get; private set; } = EntityLifeState.Alive;
    public EntityActivityState ActivityState { get; private set; } = EntityActivityState.Idle;
    public bool InCombat { get; private set; }
    public bool IsUnderAttack { get; private set; }
    public string ActivityDetail { get; private set; }
    public bool HasAttack { get; private set; }
    public int AttackBaseDamage { get; private set; }
    public float BaseAttackSpeed { get; private set; }
    public float AttackSpeedMultiplier { get; private set; } = 1f;
    public float AttackTime { get; private set; }
    public float RecoveryTime { get; private set; }
    public float AttackRange { get; private set; }
    public string AttackDelivery { get; private set; }
    public string AttackDamageType { get; private set; }
    public int AttackTargetEntityId { get; private set; } = -1;
    public EntityAttackPhase AttackPhase { get; private set; }
    public bool AttackForceTarget { get; private set; }
    public EntityCombatStance CombatStance { get; private set; } = EntityCombatStance.Aggressive;
    public EntityNavigationOrderType NavigationOrder { get; private set; }
    public EntityPathPurpose NavigationPathPurpose { get; private set; }
    public int NavigationWaypointIndex { get; private set; }
    public int NavigationWaypointCount { get; private set; }
    public string NavigationStatus { get; private set; }
    public float SelectionRadius => Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.75f;

    private Renderer unitRenderer;
    private bool tintByTeam = true;
    private GameObject selectionMarker;
    private GameObject commandFeedbackMarker;
    private Vector3 targetPosition;
    private bool hasState;
    private bool isSelected;
    private Coroutine interactionPulseRoutine;

    public void Initialize(int unitId, string entityDefinitionId, string unitName, string unitTypeId, int ownerParticipantId, ulong ownerClientId, int teamId, int colorId, string[] attributes)
    {
        UnitId = unitId;
        EntityDefinitionId = entityDefinitionId;
        UnitName = string.IsNullOrWhiteSpace(unitName) ? entityDefinitionId : unitName;
        UnitTypeId = unitTypeId;
        Attributes = EntityAttributeResolver.Resolve(attributes);
        OwnerParticipantId = ownerParticipantId;
        OwnerClientId = ownerClientId;
        TeamId = teamId;
        ColorId = colorId;

        EntityDefinition definition = EntityDefinitionRepository.Load(entityDefinitionId);
        tintByTeam = definition == null ||
                     !string.Equals(definition.kind, EntityKinds.Environment, StringComparison.OrdinalIgnoreCase);
        unitRenderer = GetComponentInChildren<Renderer>();
        if (unitRenderer != null && tintByTeam && !HasAttribute(EntityAttributeIds.AuraTrigger) && !HasAttribute(EntityAttributeIds.EntityArea))
            unitRenderer.material.color = PlayerColorPalette.GetColor(colorId);

        CreateSelectionHalo();
        CreateCommandFeedbackHalo();
    }

    private void CreateSelectionHalo()
    {
        selectionMarker = new GameObject("Selection Halo");
        selectionMarker.transform.SetParent(transform, false);

        Bounds bounds = CalculateVisualBounds();
        Vector3 worldGround = new(transform.position.x, bounds.min.y + 0.03f, transform.position.z);
        selectionMarker.transform.localPosition = transform.InverseTransformPoint(worldGround);
        selectionMarker.transform.localRotation = Quaternion.identity;

        LineRenderer line = selectionMarker.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.alignment = LineAlignment.TransformZ;
        line.widthMultiplier = 0.085f;
        line.positionCount = 64;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 50;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            Material haloMaterial = new Material(shader);
            haloMaterial.color = new Color(0.1f, 1f, 0.2f, 1f);
            line.material = haloMaterial;
        }

        line.startColor = new Color(0.1f, 1f, 0.2f, 1f);
        line.endColor = new Color(0.1f, 1f, 0.2f, 1f);

        float scaleX = Mathf.Max(0.0001f, transform.lossyScale.x);
        float scaleZ = Mathf.Max(0.0001f, transform.lossyScale.z);
        float radius = Mathf.Max(bounds.extents.x / scaleX, bounds.extents.z / scaleZ) * 1.08f;
        radius = Mathf.Max(0.55f, radius);
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        selectionMarker.SetActive(false);
    }


    private void CreateCommandFeedbackHalo()
    {
        commandFeedbackMarker = new GameObject("Command Feedback Halo");
        commandFeedbackMarker.transform.SetParent(transform, false);

        Bounds bounds = CalculateVisualBounds();
        Vector3 worldGround = new(transform.position.x, bounds.min.y + 0.055f, transform.position.z);
        commandFeedbackMarker.transform.localPosition = transform.InverseTransformPoint(worldGround);
        commandFeedbackMarker.transform.localRotation = Quaternion.identity;

        LineRenderer line = commandFeedbackMarker.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.alignment = LineAlignment.TransformZ;
        line.widthMultiplier = 0.12f;
        line.positionCount = 64;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 60;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Color feedbackColor = EntityRelationshipVisuals.Neutral;
        if (shader != null)
        {
            Material material = new Material(shader);
            material.color = feedbackColor;
            line.material = material;
        }

        line.startColor = feedbackColor;
        line.endColor = feedbackColor;

        float scaleX = Mathf.Max(0.0001f, transform.lossyScale.x);
        float scaleZ = Mathf.Max(0.0001f, transform.lossyScale.z);
        float radius = Mathf.Max(bounds.extents.x / scaleX, bounds.extents.z / scaleZ) * 1.22f;
        radius = Mathf.Max(0.68f, radius);
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        commandFeedbackMarker.SetActive(false);
    }

    private Bounds CalculateVisualBounds()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int index = 1; index < colliders.Length; index++)
                bounds.Encapsulate(colliders[index].bounds);
            return bounds;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        return new Bounds(transform.position, Vector3.one);
    }

    public void ApplyState(EntitySnapshotData state)
    {
        EntityDefinitionId = state.EntityDefinitionId;
        UnitName = string.IsNullOrWhiteSpace(state.UnitName) ? state.EntityDefinitionId : state.UnitName;
        UnitTypeId = state.UnitTypeId;
        Attributes = EntityAttributeResolver.Resolve(state.Attributes);
        if (isSelected && !IsSelectableForCurrentMatch())
        {
            isSelected = false;
            ApplySelectionVisual(false, GetDefaultHaloColor());
        }
        OwnerParticipantId = state.OwnerParticipantId;
        OwnerClientId = state.OwnerClientId;
        TeamId = state.TeamId;
        ColorId = state.ColorId;
        Health = state.Health;
        MaxHealth = state.MaxHealth;
        ResourceInfinite = state.ResourceInfinite;
        ResourceTier = state.ResourceTier;
        Resources = state.Resources ?? Array.Empty<ResourceSnapshotData>();
        WorkerResourceName = state.WorkerResourceName;
        WorkerCarriedAmount = state.WorkerCarriedAmount;
        WorkerIsExtracting = state.WorkerIsExtracting;
        AreaOccupantCount = state.AreaOccupantCount;
        Enum.TryParse(state.LifeState, true, out EntityLifeState parsedLifeState);
        LifeState = parsedLifeState;
        if (isSelected && !IsSelectableForCurrentMatch())
        {
            isSelected = false;
            ApplySelectionVisual(false, GetDefaultHaloColor());
        }
        Enum.TryParse(state.ActivityState, true, out EntityActivityState parsedActivityState);
        ActivityState = parsedActivityState;
        InCombat = state.InCombat;
        IsUnderAttack = state.IsUnderAttack;
        ActivityDetail = state.ActivityDetail;
        HasAttack = state.HasAttack;
        AttackBaseDamage = state.AttackBaseDamage;
        BaseAttackSpeed = state.BaseAttackSpeed;
        AttackSpeedMultiplier = state.AttackSpeedMultiplier;
        AttackTime = state.AttackTime;
        RecoveryTime = state.RecoveryTime;
        AttackRange = state.AttackRange;
        AttackDelivery = state.AttackDelivery;
        AttackDamageType = state.AttackDamageType;
        AttackTargetEntityId = state.AttackTargetEntityId;
        Enum.TryParse(state.AttackPhase, true, out EntityAttackPhase parsedAttackPhase);
        AttackPhase = parsedAttackPhase;
        AttackForceTarget = state.AttackForceTarget;
        Enum.TryParse(state.CombatStance, true, out EntityCombatStance parsedStance);
        CombatStance = parsedStance;
        Enum.TryParse(state.NavigationOrder, true, out EntityNavigationOrderType parsedNavigationOrder);
        NavigationOrder = parsedNavigationOrder;
        Enum.TryParse(state.NavigationPathPurpose, true, out EntityPathPurpose parsedPathPurpose);
        NavigationPathPurpose = parsedPathPurpose;
        NavigationWaypointIndex = state.NavigationWaypointIndex;
        NavigationWaypointCount = state.NavigationWaypointCount;
        NavigationStatus = state.NavigationStatus;
        GetComponent<AreaEntityVisual>()?.SetOccupantCount(AreaOccupantCount);
        if (unitRenderer != null && tintByTeam && !HasAttribute(EntityAttributeIds.AuraTrigger) && !HasAttribute(EntityAttributeIds.EntityArea))
            unitRenderer.material.color = PlayerColorPalette.GetColor(ColorId);
        targetPosition = new Vector3(state.X, state.Y, state.Z);

        if (!hasState)
        {
            transform.position = targetPosition;
            hasState = true;
        }
    }

    public bool HasAttribute(string attributeId) => Attributes != null && Attributes.Has(attributeId);

    public bool IsNotSelectableForCurrentMatch()
    {
        return EntityAttributeOverrideService.IsEffectivelyBlocked(
            Attributes,
            EntityAttributeIds.NotSelectable);
    }

    public bool IsSelectableForCurrentMatch()
    {
        return LifeState != EntityLifeState.Dead &&
               HasAttribute(EntityAttributeIds.Selectable) &&
               !IsNotSelectableForCurrentMatch();
    }

    public void SetSelected(bool selected)
    {
        bool effectiveSelection = selected && IsSelectableForCurrentMatch();
        isSelected = effectiveSelection;
        if (interactionPulseRoutine == null)
            ApplySelectionVisual(effectiveSelection, GetDefaultHaloColor());
    }

    /// <summary>
    /// Confirma localmente cualquier orden contextual haciendo parpadear una vez
    /// el halo con el color de relación del objetivo. No depende de si la acción es seguir,
    /// extraer, atacar u otra acción futura. Al terminar restaura el halo normal.
    /// </summary>
    public void PlayInteractionPulse()
    {
        PlayInteractionPulse(EntityRelationshipVisuals.GetColor(this));
    }

    public void PlayInteractionPulse(Color feedbackColor)
    {
        if (commandFeedbackMarker == null)
            return;

        ApplyCommandFeedbackColor(feedbackColor);

        if (interactionPulseRoutine != null)
        {
            StopCoroutine(interactionPulseRoutine);
            commandFeedbackMarker.SetActive(false);
        }

        interactionPulseRoutine = StartCoroutine(InteractionPulseCoroutine());
    }

    private IEnumerator InteractionPulseCoroutine()
    {
        // Este halo es independiente del halo de selección. De esta forma,
        // SetSelected y los refrescos del HUD no pueden ocultar el feedback.
        commandFeedbackMarker.SetActive(true);
        yield return new WaitForSecondsRealtime(0.14f);
        commandFeedbackMarker.SetActive(false);
        yield return new WaitForSecondsRealtime(0.07f);
        commandFeedbackMarker.SetActive(true);
        yield return new WaitForSecondsRealtime(0.14f);
        commandFeedbackMarker.SetActive(false);

        interactionPulseRoutine = null;
    }

    private Color GetDefaultHaloColor()
    {
        return EntityRelationshipVisuals.GetColor(this);
    }

    private void ApplyCommandFeedbackColor(Color color)
    {
        if (commandFeedbackMarker == null)
            return;

        LineRenderer line = commandFeedbackMarker.GetComponent<LineRenderer>();
        if (line == null)
            return;

        line.startColor = color;
        line.endColor = color;
        if (line.material != null)
            line.material.color = color;
    }

    private void ApplySelectionVisual(bool visible, Color color)
    {
        if (selectionMarker == null)
            return;

        LineRenderer line = selectionMarker.GetComponent<LineRenderer>();
        if (line != null)
        {
            line.startColor = color;
            line.endColor = color;
            if (line.material != null)
                line.material.color = color;
        }
        selectionMarker.SetActive(visible);
    }

    private void Update()
    {
        if (hasState)
            transform.position = Vector3.Lerp(transform.position, targetPosition, 18f * Time.deltaTime);
    }
}
