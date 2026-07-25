using System;
using UnityEngine;

public class NetworkEntityView : MonoBehaviour
{
    public int UnitId { get; private set; }
    public string EntityDefinitionId { get; private set; }
    public string UnitName { get; private set; }
    public string UnitTypeId { get; private set; }
    public EntityAttributeSet Attributes { get; private set; } = new();
    public ulong OwnerClientId { get; private set; }
    public int TeamId { get; private set; }
    public int ColorId { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth { get; private set; }
    public float SelectionRadius => Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.75f;

    private Renderer unitRenderer;
    private GameObject selectionMarker;
    private Vector3 targetPosition;
    private bool hasState;

    public void Initialize(int unitId, string entityDefinitionId, string unitName, string unitTypeId, ulong ownerClientId, int teamId, int colorId, string[] attributes)
    {
        UnitId = unitId;
        EntityDefinitionId = entityDefinitionId;
        UnitName = string.IsNullOrWhiteSpace(unitName) ? entityDefinitionId : unitName;
        UnitTypeId = unitTypeId;
        Attributes = EntityAttributeResolver.Resolve(attributes);
        OwnerClientId = ownerClientId;
        TeamId = teamId;
        ColorId = colorId;

        unitRenderer = GetComponent<Renderer>();
        if (unitRenderer != null && !HasAttribute(EntityAttributeIds.AuraTrigger))
            unitRenderer.material.color = PlayerColorPalette.GetColor(colorId);

        CreateSelectionHalo();
    }

    private void CreateSelectionHalo()
    {
        selectionMarker = new GameObject("Selection Halo");
        selectionMarker.transform.SetParent(transform, false);

        // La cápsula de prueba está centrada en Y=0.5 y mide 2 unidades de alto.
        // El valor anterior (-1) dejaba el aro bajo el suelo. Se coloca apenas encima del plano.
        selectionMarker.transform.localPosition = new Vector3(0f, -0.49f, 0f);
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

        // El aro usa coordenadas locales para heredar automáticamente la escala de la entidad.
        float radius = 0.72f;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        selectionMarker.SetActive(false);
    }

    public void ApplyState(EntitySnapshotData state)
    {
        EntityDefinitionId = state.EntityDefinitionId;
        UnitName = string.IsNullOrWhiteSpace(state.UnitName) ? state.EntityDefinitionId : state.UnitName;
        UnitTypeId = state.UnitTypeId;
        Attributes = EntityAttributeResolver.Resolve(state.Attributes);
        OwnerClientId = state.OwnerClientId;
        TeamId = state.TeamId;
        ColorId = state.ColorId;
        Health = state.Health;
        MaxHealth = state.MaxHealth;
        if (unitRenderer != null && !HasAttribute(EntityAttributeIds.AuraTrigger))
            unitRenderer.material.color = PlayerColorPalette.GetColor(ColorId);
        targetPosition = new Vector3(state.X, state.Y, state.Z);

        if (!hasState)
        {
            transform.position = targetPosition;
            hasState = true;
        }
    }

    public bool HasAttribute(string attributeId) => Attributes != null && Attributes.Has(attributeId);

    public void SetSelected(bool selected)
    {
        if (selectionMarker == null)
            return;

        LineRenderer line = selectionMarker.GetComponent<LineRenderer>();
        if (line != null)
        {
            bool owned = NetworkRuntimeBootstrap.Instance != null &&
                         NetworkRuntimeBootstrap.Instance.NetworkManager != null &&
                         TeamId != 0 &&
                         OwnerClientId == NetworkRuntimeBootstrap.Instance.NetworkManager.LocalClientId;
            Color haloColor = owned
                ? new Color(0.1f, 1f, 0.2f, 1f)
                : new Color(1f, 0.85f, 0.1f, 1f);
            line.startColor = haloColor;
            line.endColor = haloColor;
            if (line.material != null)
                line.material.color = haloColor;
        }

        selectionMarker.SetActive(selected);
    }

    private void Update()
    {
        if (hasState)
            transform.position = Vector3.Lerp(transform.position, targetPosition, 18f * Time.deltaTime);
    }
}
