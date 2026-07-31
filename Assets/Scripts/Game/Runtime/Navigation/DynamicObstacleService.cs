using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Convierte entidades sólidas inmóviles en celdas bloqueadas. Las unidades
/// móviles se resuelven mediante evitación local para no invalidar la rejilla en
/// cada frame.
/// </summary>
public sealed class DynamicObstacleService
{
    private readonly EntityWorld world;
    private readonly NavigationGrid grid;
    private int lastSignature = int.MinValue;

    public int Revision { get; private set; }
    public int ObstacleCount { get; private set; }

    public DynamicObstacleService(EntityWorld world, NavigationGrid grid)
    {
        this.world = world;
        this.grid = grid;
    }

    public bool RebuildIfChanged()
    {
        List<EntityRuntimeState> obstacles = world.Values
            .Where(IsNavigationObstacle)
            .OrderBy(item => item.UnitId)
            .ToList();

        int signature = ComputeSignature(obstacles);
        if (signature == lastSignature)
            return false;

        lastSignature = signature;
        grid.ClearBlocked();
        foreach (EntityRuntimeState obstacle in obstacles)
            Rasterize(obstacle);

        ObstacleCount = obstacles.Count;
        Revision++;
        return true;
    }

    private static bool IsNavigationObstacle(EntityRuntimeState entity)
    {
        if (entity == null || !entity.Solid || entity.Life == null || entity.Life.State == EntityLifeState.Dead)
            return false;
        if (entity.Attributes != null && entity.Attributes.Has(EntityAttributeIds.EntityArea))
            return false;

        return entity.MoveSpeed <= 0f ||
               (entity.Attributes != null && entity.Attributes.Has(EntityAttributeIds.Building));
    }

    private static int ComputeSignature(IEnumerable<EntityRuntimeState> obstacles)
    {
        unchecked
        {
            int hash = 17;
            foreach (EntityRuntimeState item in obstacles)
            {
                hash = hash * 31 + item.UnitId;
                hash = hash * 31 + Mathf.RoundToInt(item.Position.x * 20f);
                hash = hash * 31 + Mathf.RoundToInt(item.Position.z * 20f);
                hash = hash * 31 + Mathf.RoundToInt(item.BoundsSize.x * 20f);
                hash = hash * 31 + Mathf.RoundToInt(item.BoundsSize.z * 20f);
                hash = hash * 31 + (item.Solid ? 1 : 0);
            }
            return hash;
        }
    }

    private void Rasterize(EntityRuntimeState obstacle)
    {
        float padding = grid.CellSize * 0.35f;
        bool rectangular = obstacle.Attributes != null && obstacle.Attributes.Has(EntityAttributeIds.Building);
        float halfX = Mathf.Max(0.05f, obstacle.BoundsSize.x * 0.5f) + padding;
        float halfZ = Mathf.Max(0.05f, obstacle.BoundsSize.z * 0.5f) + padding;

        Vector3 min = new(obstacle.Position.x - halfX, 0f, obstacle.Position.z - halfZ);
        Vector3 max = new(obstacle.Position.x + halfX, 0f, obstacle.Position.z + halfZ);
        grid.WorldToCell(min, out int minX, out int minZ);
        grid.WorldToCell(max, out int maxX, out int maxZ);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector3 center = grid.CellToWorld(x, z, obstacle.Position.y);
                bool blocked;
                if (rectangular)
                {
                    blocked = Mathf.Abs(center.x - obstacle.Position.x) <= halfX &&
                              Mathf.Abs(center.z - obstacle.Position.z) <= halfZ;
                }
                else
                {
                    float radius = Mathf.Max(halfX, halfZ);
                    Vector2 delta = new(center.x - obstacle.Position.x, center.z - obstacle.Position.z);
                    blocked = delta.sqrMagnitude <= radius * radius;
                }

                if (blocked)
                    grid.SetBlocked(x, z, true);
            }
        }
    }
}
