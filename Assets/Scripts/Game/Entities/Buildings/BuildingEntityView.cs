using System;
using UnityEngine;

public static class BuildingEntityView
{
    public static GameObject Create(
        EntityDefinition definition,
        int runtimeId,
        int teamId,
        bool effectiveSolid,
        bool isAreaEntity)
    {
        bool auraVisual = string.Equals(definition.visual, "aura", StringComparison.OrdinalIgnoreCase);
        bool rectangularArea = isAreaEntity &&
                               definition.area != null &&
                               string.Equals(definition.area.shape, EntityAreaShapes.Rectangle, StringComparison.OrdinalIgnoreCase);
        PrimitiveType primitive = auraVisual
            ? (rectangularArea ? PrimitiveType.Cube : PrimitiveType.Cylinder)
            : PrimitiveType.Cube;

        GameObject building = GameObject.CreatePrimitive(primitive);
        building.name = $"{definition.name} {runtimeId} - Equipo {teamId}";

        Vector3 fallback = primitive == PrimitiveType.Cube
            ? new Vector3(4f, 4f, 4f)
            : new Vector3(2f, 0.04f, 2f);
        Vector3 scale = definition.GetScale(fallback);
        if (isAreaEntity && auraVisual && definition.area != null)
        {
            if (string.Equals(definition.area.shape, EntityAreaShapes.Rectangle, StringComparison.OrdinalIgnoreCase) &&
                definition.area.size != null)
            {
                Vector3 areaSize = definition.area.size.ToVector3();
                scale = new Vector3(
                    Mathf.Max(0.05f, areaSize.x),
                    Mathf.Max(0.02f, scale.y),
                    Mathf.Max(0.05f, areaSize.z));
            }
            else if (definition.area.radius > 0f)
            {
                float diameter = definition.area.radius * 2f;
                scale = new Vector3(diameter, Mathf.Max(0.02f, scale.y), diameter);
            }
        }

        building.transform.localScale = scale;
        ConfigureCollider(building, effectiveSolid);

        if (isAreaEntity)
        {
            AreaEntityVisual areaVisual = building.AddComponent<AreaEntityVisual>();
            areaVisual.Configure(definition);
        }

        return building;
    }

    private static void ConfigureCollider(GameObject building, bool effectiveSolid)
    {
        Collider collider = building.GetComponent<Collider>();
        if (collider != null)
        {
            // El collider solo sirve para raycast/presentación. La detección de
            // área real se calcula en EntityAreaRuntimeSystem sobre el servidor.
            collider.enabled = true;
            collider.isTrigger = !effectiveSolid;
        }

        if (!effectiveSolid && building.GetComponent<Rigidbody>() == null)
        {
            Rigidbody body = building.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }
}

/// <summary>
/// Skin provisional para entidades de área. No ejecuta lógica de gameplay:
/// únicamente refleja visualmente si el área tiene ocupantes.
/// </summary>
public sealed class AreaEntityVisual : MonoBehaviour
{
    private Renderer areaRenderer;
    private Color emptyColor = new(0.12f, 0.8f, 0.22f, 0.55f);
    private Color activeColor = new(0.95f, 0.82f, 0.12f, 0.78f);
    private int occupantCount;

    public void Configure(EntityDefinition definition)
    {
        areaRenderer = GetComponentInChildren<Renderer>();
        EntityAttributeSet attributes = EntityAttributeResolver.Resolve(definition?.attributes);
        if (attributes.Has(EntityAttributeIds.AreaAura) && !attributes.Has(EntityAttributeIds.AreaTrigger))
        {
            emptyColor = new Color(0.15f, 0.45f, 0.95f, 0.48f);
            activeColor = new Color(0.2f, 0.9f, 1f, 0.76f);
        }

        if (definition?.area != null && !definition.area.visible && areaRenderer != null)
            areaRenderer.enabled = false;
        ApplyColor();
    }

    public void SetOccupantCount(int count)
    {
        occupantCount = Mathf.Max(0, count);
        ApplyColor();
    }

    private void Awake()
    {
        areaRenderer = GetComponentInChildren<Renderer>();
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (areaRenderer != null)
            areaRenderer.material.color = occupantCount > 0 ? activeColor : emptyColor;
    }
}
