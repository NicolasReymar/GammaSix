using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Visualización local de diagnóstico para las rutas autoritativas. Solo existe
/// en la instancia con autoridad; el comando host cambia el estado dentro de
/// NavigationRuntimeSystem y este componente dibuja los waypoints restantes.
/// </summary>
public sealed class NavigationPathDebugVisualizer : MonoBehaviour
{
    private const float HeightOffset = 0.16f;
    private const float LineWidth = 0.055f;

    private readonly Dictionary<int, LineRenderer> renderers = new();
    private readonly HashSet<int> activeEntityIds = new();
    private readonly List<int> staleEntityIds = new();
    private Material lineMaterial;
    private Transform visualRoot;

    private void Awake()
    {
        GameObject rootObject = new("Navigation Path Debug Lines");
        rootObject.transform.SetParent(transform, false);
        visualRoot = rootObject.transform;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            lineMaterial = new Material(shader)
            {
                name = "Navigation Path Debug Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }

    private void LateUpdate()
    {
        MatchRuntimeController controller = MatchRuntimeController.Instance;
        AuthoritativeMatchRuntime runtime = controller?.Runtime;
        NavigationRuntimeSystem navigation = runtime?.Navigation;

        if (controller == null || !controller.IsAuthoritative ||
            runtime == null || !runtime.IsInitialized ||
            navigation == null || !navigation.PathVisualizationEnabled)
        {
            HideAll();
            return;
        }

        activeEntityIds.Clear();
        foreach (EntityRuntimeState entity in runtime.World.Values)
        {
            if (!ShouldDraw(entity))
                continue;

            LineRenderer line = GetOrCreateRenderer(entity.UnitId);
            UpdateRenderer(line, entity);
            activeEntityIds.Add(entity.UnitId);
        }

        staleEntityIds.Clear();
        foreach (KeyValuePair<int, LineRenderer> pair in renderers)
        {
            if (!runtime.World.Contains(pair.Key))
            {
                staleEntityIds.Add(pair.Key);
                continue;
            }

            if (pair.Value != null)
                pair.Value.enabled = activeEntityIds.Contains(pair.Key);
        }

        foreach (int entityId in staleEntityIds)
        {
            if (renderers.TryGetValue(entityId, out LineRenderer line) && line != null)
                Destroy(line.gameObject);
            renderers.Remove(entityId);
        }
    }

    private void OnDestroy()
    {
        foreach (LineRenderer line in renderers.Values.Where(item => item != null))
            Destroy(line.gameObject);
        renderers.Clear();

        if (lineMaterial != null)
            Destroy(lineMaterial);
    }

    private static bool ShouldDraw(EntityRuntimeState entity)
    {
        return entity != null &&
               entity.Life != null &&
               entity.Life.CanAct &&
               entity.MoveSpeed > 0f &&
               entity.Navigation != null &&
               entity.Navigation.HasPath;
    }

    private LineRenderer GetOrCreateRenderer(int entityId)
    {
        if (renderers.TryGetValue(entityId, out LineRenderer existing) && existing != null)
            return existing;

        GameObject lineObject = new($"Path {entityId}");
        lineObject.transform.SetParent(visualRoot, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.widthMultiplier = LineWidth;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        if (lineMaterial != null)
            line.sharedMaterial = lineMaterial;

        renderers[entityId] = line;
        return line;
    }

    private static void UpdateRenderer(LineRenderer line, EntityRuntimeState entity)
    {
        EntityNavigationRuntimeState navigation = entity.Navigation;
        int remaining = navigation.Waypoints.Count - navigation.WaypointIndex;
        int pointCount = remaining + 1;
        line.positionCount = pointCount;

        Vector3 start = entity.Position;
        start.y += HeightOffset;
        line.SetPosition(0, start);

        for (int i = 0; i < remaining; i++)
        {
            Vector3 point = navigation.Waypoints[navigation.WaypointIndex + i];
            point.y = entity.Position.y + HeightOffset;
            line.SetPosition(i + 1, point);
        }

        Color color = PlayerColorPalette.GetColor(entity.ColorId);
        color.a = 0.92f;
        line.startColor = color;
        line.endColor = new Color(color.r, color.g, color.b, 0.35f);
        line.enabled = true;
    }

    private void HideAll()
    {
        foreach (LineRenderer line in renderers.Values)
        {
            if (line != null)
                line.enabled = false;
        }
    }
}
