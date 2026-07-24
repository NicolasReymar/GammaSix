using UnityEngine;

public static class BuildingEntityView
{
    public static GameObject Create(EntityDefinition definition, int runtimeId, int teamId)
    {
        PrimitiveType primitive = string.Equals(definition.visual, "aura", System.StringComparison.OrdinalIgnoreCase)
            ? PrimitiveType.Cylinder
            : PrimitiveType.Cube;

        GameObject building = GameObject.CreatePrimitive(primitive);
        building.name = $"{definition.name} {runtimeId} - Equipo {teamId}";

        Vector3 fallback = primitive == PrimitiveType.Cube
            ? new Vector3(4f, 4f, 4f)
            : new Vector3(2f, 0.04f, 2f);
        building.transform.localScale = definition.GetScale(fallback);

        Collider collider = building.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = !definition.solid;
            collider.enabled = true;
        }

        if (!definition.solid)
        {
            Rigidbody body = building.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        if (string.Equals(definition.visual, "aura", System.StringComparison.OrdinalIgnoreCase))
            building.AddComponent<AuraBuildingTrigger>();

        return building;
    }
}

public class AuraBuildingTrigger : MonoBehaviour
{
    private Renderer auraRenderer;
    private int humanoidsInside;
    private static readonly Color EmptyColor = new(0.12f, 0.8f, 0.22f, 0.72f);
    private static readonly Color ActiveColor = new(0.95f, 0.82f, 0.12f, 0.82f);

    private void Awake()
    {
        auraRenderer = GetComponent<Renderer>();
        ApplyColor();
    }

    private void OnTriggerEnter(Collider other)
    {
        NetworkUnitView view = other.GetComponentInParent<NetworkUnitView>();
        if (view == null || !view.HasAttribute(EntityAttributeIds.Humanoid))
            return;
        humanoidsInside++;
        ApplyColor();
        Debug.Log($"[AuraBuildingTrigger] Humanoide ingresó en {name}. Trigger de evento disponible.");
    }

    private void OnTriggerExit(Collider other)
    {
        NetworkUnitView view = other.GetComponentInParent<NetworkUnitView>();
        if (view == null || !view.HasAttribute(EntityAttributeIds.Humanoid))
            return;
        humanoidsInside = Mathf.Max(0, humanoidsInside - 1);
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (auraRenderer != null)
            auraRenderer.material.color = humanoidsInside > 0 ? ActiveColor : EmptyColor;
    }
}
