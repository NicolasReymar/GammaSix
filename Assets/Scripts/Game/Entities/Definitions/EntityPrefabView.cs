using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// Crea vistas de entidades basadas en prefabs/modelos de Resources.
///
/// Los modelos importados pueden usar unidades muy distintas (metros, centímetros,
/// milímetros, etc.). Por eso el modelo se instancia como hijo de una raíz estable y
/// se normaliza usando los bounds reales de sus renderers.
/// </summary>
public static class EntityPrefabView
{
    private const float BoundsEpsilon = 0.0001f;

    public static GameObject Create(
        EntityDefinition definition,
        int runtimeId,
        int teamId,
        bool effectiveSolid)
    {
        GameObject prefab = string.IsNullOrWhiteSpace(definition.prefabResource)
            ? null
            : Resources.Load<GameObject>(definition.prefabResource);

        GameObject root = new($"{definition.name} {runtimeId} - Equipo {teamId}");

        if (prefab == null)
        {
            Debug.LogWarning($"[EntityPrefabView] No se encontró Resources/{definition.prefabResource}. Se usará un cubo.");
            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = "Missing Prefab Visual";
            fallback.transform.SetParent(root.transform, false);
            fallback.transform.localScale = definition.GetPrefabTargetSize(Vector3.one);
            DisableColliders(fallback);
            ConfigureRootCollider(root, definition, fallback.GetComponent<Renderer>()?.bounds.size ?? Vector3.one, effectiveSolid);
            return root;
        }

        GameObject visual = UnityEngine.Object.Instantiate(prefab, root.transform, false);
        visual.name = "Visual";

        // La colisión de gameplay vive en la raíz y utiliza collisionSize. No se
        // utilizan los colliders internos del FBX porque heredan su escala de importación.
        DisableColliders(visual);

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            renderer.enabled = true;

        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[EntityPrefabView] El prefab '{definition.prefabResource}' no contiene renderers.");
            ConfigureRootCollider(root, definition, definition.GetCollisionSize(Vector3.one), effectiveSolid);
            return root;
        }

        Bounds originalBounds = CalculateRendererBounds(renderers);
        Vector3 targetSize = definition.GetPrefabTargetSize(originalBounds.size);
        FitVisualToSize(visual, renderers, targetSize);
        AlignVisualToRootGround(root, visual, renderers);

        Bounds finalBounds = CalculateRendererBounds(renderers);
        ConfigureRootCollider(root, definition, finalBounds.size, effectiveSolid);

        Debug.Log(
            $"[EntityPrefabView] '{definition.id}' normalizado. " +
            $"Bounds originales: {FormatVector(originalBounds.size)} · " +
            $"objetivo: {FormatVector(targetSize)} · " +
            $"finales: {FormatVector(finalBounds.size)}.");

        return root;
    }

    private static void FitVisualToSize(GameObject visual, Renderer[] renderers, Vector3 targetSize)
    {
        Bounds bounds = CalculateRendererBounds(renderers);
        float factor = CalculateUniformScaleFactor(bounds.size, targetSize);
        if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= BoundsEpsilon)
            return;

        visual.transform.localScale *= factor;
    }

    private static float CalculateUniformScaleFactor(Vector3 currentSize, Vector3 targetSize)
    {
        float factor = float.PositiveInfinity;
        factor = IncludeDimension(factor, currentSize.x, targetSize.x);
        factor = IncludeDimension(factor, currentSize.y, targetSize.y);
        factor = IncludeDimension(factor, currentSize.z, targetSize.z);

        return float.IsPositiveInfinity(factor) ? 1f : factor;
    }

    private static float IncludeDimension(float currentFactor, float currentSize, float targetSize)
    {
        if (currentSize <= BoundsEpsilon || targetSize <= BoundsEpsilon)
            return currentFactor;

        return Mathf.Min(currentFactor, targetSize / currentSize);
    }

    private static void AlignVisualToRootGround(GameObject root, GameObject visual, Renderer[] renderers)
    {
        Bounds bounds = CalculateRendererBounds(renderers);

        // Centra el modelo sobre X/Z y deja su base exactamente en el origen Y de
        // la entidad. Al mover la raíz mediante snapshots, el modelo permanece
        // correctamente apoyado en el terreno.
        Vector3 desiredGroundCenter = root.transform.position;
        Vector3 currentGroundCenter = new(bounds.center.x, bounds.min.y, bounds.center.z);
        visual.transform.position += desiredGroundCenter - currentGroundCenter;
    }

    private static void ConfigureRootCollider(
        GameObject root,
        EntityDefinition definition,
        Vector3 visualBoundsSize,
        bool effectiveSolid)
    {
        foreach (Collider collider in root.GetComponents<Collider>())
            UnityEngine.Object.Destroy(collider);

        Vector3 colliderSize = definition.GetCollisionSize(visualBoundsSize);
        colliderSize.x = Mathf.Max(BoundsEpsilon, colliderSize.x);
        colliderSize.y = Mathf.Max(BoundsEpsilon, colliderSize.y);
        colliderSize.z = Mathf.Max(BoundsEpsilon, colliderSize.z);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = colliderSize;
        box.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
        box.isTrigger = !effectiveSolid;

        if (!effectiveSolid && root.GetComponent<Rigidbody>() == null)
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    private static Bounds CalculateRendererBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void DisableColliders(GameObject instance)
    {
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }
}
