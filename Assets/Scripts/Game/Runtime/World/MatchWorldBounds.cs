using UnityEngine;

/// <summary>
/// Límites autoritativos del mundo. Sustituye el límite fijo de movimiento y
/// se deriva del tamaño declarado por el escenario.
/// </summary>
public sealed class MatchWorldBounds
{
    private const float FallbackHalfExtent = 19f;

    public float MinX { get; }
    public float MaxX { get; }
    public float MinZ { get; }
    public float MaxZ { get; }

    public MatchWorldBounds(float minX, float maxX, float minZ, float maxZ)
    {
        MinX = Mathf.Min(minX, maxX);
        MaxX = Mathf.Max(minX, maxX);
        MinZ = Mathf.Min(minZ, maxZ);
        MaxZ = Mathf.Max(minZ, maxZ);
    }

    public Vector3 Clamp(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, MinX, MaxX);
        position.z = Mathf.Clamp(position.z, MinZ, MaxZ);
        return position;
    }

    public static MatchWorldBounds FromScenario(ScenarioDefinition scenario)
    {
        float width = scenario?.worldSize != null && scenario.worldSize.width > 0f
            ? scenario.worldSize.width
            : FallbackHalfExtent * 2f;
        float height = scenario?.worldSize != null && scenario.worldSize.height > 0f
            ? scenario.worldSize.height
            : FallbackHalfExtent * 2f;

        float halfWidth = Mathf.Max(1f, width * 0.5f);
        float halfHeight = Mathf.Max(1f, height * 0.5f);
        return new MatchWorldBounds(-halfWidth, halfWidth, -halfHeight, halfHeight);
    }
}
