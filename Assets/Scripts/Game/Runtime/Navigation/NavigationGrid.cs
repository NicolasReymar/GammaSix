using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rejilla de navegación autoritativa derivada de los límites del escenario.
/// No depende de NavMesh ni de componentes colocados manualmente en la escena.
/// </summary>
public sealed class NavigationGrid
{
    private readonly bool[] blocked;

    public MatchWorldBounds Bounds { get; }
    public float CellSize { get; }
    public int Width { get; }
    public int Height { get; }
    public bool AllowDiagonal { get; }

    public int CellCount => Width * Height;

    public NavigationGrid(MatchWorldBounds bounds, float cellSize, bool allowDiagonal)
    {
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        CellSize = Mathf.Clamp(cellSize, 0.35f, 4f);
        Width = Mathf.Max(2, Mathf.CeilToInt((bounds.MaxX - bounds.MinX) / CellSize));
        Height = Mathf.Max(2, Mathf.CeilToInt((bounds.MaxZ - bounds.MinZ) / CellSize));
        AllowDiagonal = allowDiagonal;
        blocked = new bool[Width * Height];
    }

    public void ClearBlocked()
    {
        Array.Clear(blocked, 0, blocked.Length);
    }

    public bool IsInside(int x, int z)
    {
        return x >= 0 && z >= 0 && x < Width && z < Height;
    }

    public int ToIndex(int x, int z) => z * Width + x;

    public void FromIndex(int index, out int x, out int z)
    {
        z = index / Width;
        x = index - z * Width;
    }

    public void WorldToCell(Vector3 position, out int x, out int z)
    {
        x = Mathf.Clamp(Mathf.FloorToInt((position.x - Bounds.MinX) / CellSize), 0, Width - 1);
        z = Mathf.Clamp(Mathf.FloorToInt((position.z - Bounds.MinZ) / CellSize), 0, Height - 1);
    }

    public Vector3 CellToWorld(int x, int z, float y)
    {
        return new Vector3(
            Bounds.MinX + (x + 0.5f) * CellSize,
            y,
            Bounds.MinZ + (z + 0.5f) * CellSize);
    }

    public bool IsBlocked(int x, int z)
    {
        return !IsInside(x, z) || blocked[ToIndex(x, z)];
    }

    public bool IsBlockedIndex(int index)
    {
        return index < 0 || index >= blocked.Length || blocked[index];
    }

    public void SetBlocked(int x, int z, bool value)
    {
        if (IsInside(x, z))
            blocked[ToIndex(x, z)] = value;
    }

    public bool TryFindNearestWalkable(int originX, int originZ, int maxRadius, out int resultX, out int resultZ)
    {
        if (!IsBlocked(originX, originZ))
        {
            resultX = originX;
            resultZ = originZ;
            return true;
        }

        for (int radius = 1; radius <= Mathf.Max(1, maxRadius); radius++)
        {
            int minX = originX - radius;
            int maxX = originX + radius;
            int minZ = originZ - radius;
            int maxZ = originZ + radius;

            for (int x = minX; x <= maxX; x++)
            {
                if (!IsBlocked(x, minZ))
                {
                    resultX = x;
                    resultZ = minZ;
                    return true;
                }
                if (!IsBlocked(x, maxZ))
                {
                    resultX = x;
                    resultZ = maxZ;
                    return true;
                }
            }

            for (int z = minZ + 1; z < maxZ; z++)
            {
                if (!IsBlocked(minX, z))
                {
                    resultX = minX;
                    resultZ = z;
                    return true;
                }
                if (!IsBlocked(maxX, z))
                {
                    resultX = maxX;
                    resultZ = z;
                    return true;
                }
            }
        }

        resultX = originX;
        resultZ = originZ;
        return false;
    }

    public IEnumerable<int> GetNeighbourIndices(int index)
    {
        FromIndex(index, out int x, out int z);
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;
                if (!AllowDiagonal && dx != 0 && dz != 0)
                    continue;

                int nx = x + dx;
                int nz = z + dz;
                if (IsBlocked(nx, nz))
                    continue;

                if (dx != 0 && dz != 0 &&
                    (IsBlocked(x + dx, z) || IsBlocked(x, z + dz)))
                {
                    continue;
                }

                yield return ToIndex(nx, nz);
            }
        }
    }

    public bool HasGridLineOfSight(int startIndex, int endIndex)
    {
        FromIndex(startIndex, out int x0, out int z0);
        FromIndex(endIndex, out int x1, out int z1);

        int dx = Mathf.Abs(x1 - x0);
        int dz = Mathf.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1;
        int sz = z0 < z1 ? 1 : -1;
        int error = dx - dz;

        while (true)
        {
            if (IsBlocked(x0, z0))
                return false;
            if (x0 == x1 && z0 == z1)
                return true;

            int previousX = x0;
            int previousZ = z0;
            int twice = error * 2;
            if (twice > -dz)
            {
                error -= dz;
                x0 += sx;
            }
            if (twice < dx)
            {
                error += dx;
                z0 += sz;
            }

            // La simplificación no puede reintroducir el corte diagonal que A*
            // ya prohíbe entre dos obstáculos ortogonales.
            if (x0 != previousX && z0 != previousZ &&
                (IsBlocked(x0, previousZ) || IsBlocked(previousX, z0)))
            {
                return false;
            }
        }
    }
}
