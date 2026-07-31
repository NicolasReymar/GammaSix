using System.Collections.Generic;
using UnityEngine;

/// <summary>A* determinista sobre NavigationGrid.</summary>
public sealed class NavigationPathfinder
{
    private readonly NavigationGrid grid;
    private readonly float[] gScore;
    private readonly float[] fScore;
    private readonly int[] parent;
    private readonly byte[] state;
    private readonly List<int> open = new();

    public NavigationPathfinder(NavigationGrid grid)
    {
        this.grid = grid;
        gScore = new float[grid.CellCount];
        fScore = new float[grid.CellCount];
        parent = new int[grid.CellCount];
        state = new byte[grid.CellCount];
    }

    public bool TryFindPath(Vector3 start, Vector3 destination, float y, List<Vector3> output, out string status)
    {
        output.Clear();
        grid.WorldToCell(start, out int startX, out int startZ);
        grid.WorldToCell(destination, out int requestedTargetX, out int requestedTargetZ);
        int targetX = requestedTargetX;
        int targetZ = requestedTargetZ;

        if (!grid.TryFindNearestWalkable(startX, startZ, 4, out startX, out startZ))
        {
            status = "start-blocked";
            return false;
        }

        if (!grid.TryFindNearestWalkable(targetX, targetZ, 8, out targetX, out targetZ))
        {
            status = "destination-blocked";
            return false;
        }

        bool targetWasRelocated = targetX != requestedTargetX || targetZ != requestedTargetZ;
        Vector3 resolvedDestination = targetWasRelocated
            ? grid.CellToWorld(targetX, targetZ, y)
            : new Vector3(destination.x, y, destination.z);

        int startIndex = grid.ToIndex(startX, startZ);
        int targetIndex = grid.ToIndex(targetX, targetZ);
        if (startIndex == targetIndex)
        {
            output.Add(resolvedDestination);
            status = targetWasRelocated ? "same-nearest-cell" : "same-cell";
            return true;
        }

        ResetBuffers();
        gScore[startIndex] = 0f;
        fScore[startIndex] = Heuristic(startX, startZ, targetX, targetZ);
        parent[startIndex] = -1;
        state[startIndex] = 1;
        open.Add(startIndex);

        while (open.Count > 0)
        {
            int current = PopLowestScore();
            if (current == targetIndex)
            {
                BuildPath(startIndex, targetIndex, resolvedDestination, y, output);
                status = output.Count > 0 ? "path-found" : "path-empty";
                return output.Count > 0;
            }

            state[current] = 2;
            grid.FromIndex(current, out int currentX, out int currentZ);
            foreach (int neighbour in grid.GetNeighbourIndices(current))
            {
                if (state[neighbour] == 2)
                    continue;

                grid.FromIndex(neighbour, out int neighbourX, out int neighbourZ);
                bool diagonal = currentX != neighbourX && currentZ != neighbourZ;
                float tentative = gScore[current] + (diagonal ? 1.41421356f : 1f);
                if (state[neighbour] == 1 && tentative >= gScore[neighbour])
                    continue;

                parent[neighbour] = current;
                gScore[neighbour] = tentative;
                fScore[neighbour] = tentative + Heuristic(neighbourX, neighbourZ, targetX, targetZ);
                if (state[neighbour] != 1)
                {
                    state[neighbour] = 1;
                    open.Add(neighbour);
                }
            }
        }

        status = "unreachable";
        return false;
    }

    private void ResetBuffers()
    {
        open.Clear();
        for (int index = 0; index < state.Length; index++)
        {
            state[index] = 0;
            gScore[index] = float.PositiveInfinity;
            fScore[index] = float.PositiveInfinity;
            parent[index] = -1;
        }
    }

    private int PopLowestScore()
    {
        int bestListIndex = 0;
        int bestNode = open[0];
        float bestScore = fScore[bestNode];
        for (int index = 1; index < open.Count; index++)
        {
            int candidate = open[index];
            float score = fScore[candidate];
            if (score < bestScore || (Mathf.Approximately(score, bestScore) && candidate < bestNode))
            {
                bestScore = score;
                bestNode = candidate;
                bestListIndex = index;
            }
        }

        open.RemoveAt(bestListIndex);
        return bestNode;
    }

    private void BuildPath(int startIndex, int targetIndex, Vector3 exactDestination, float y, List<Vector3> output)
    {
        List<int> reversed = new();
        int current = targetIndex;
        while (current >= 0 && current != startIndex)
        {
            reversed.Add(current);
            current = parent[current];
        }
        reversed.Reverse();

        if (reversed.Count == 0)
        {
            output.Add(new Vector3(exactDestination.x, y, exactDestination.z));
            return;
        }

        int anchor = startIndex;
        int cursor = 0;
        while (cursor < reversed.Count)
        {
            int furthest = cursor;
            for (int probe = cursor + 1; probe < reversed.Count; probe++)
            {
                if (!grid.HasGridLineOfSight(anchor, reversed[probe]))
                    break;
                furthest = probe;
            }

            int waypointIndex = reversed[furthest];
            grid.FromIndex(waypointIndex, out int x, out int z);
            output.Add(grid.CellToWorld(x, z, y));
            anchor = waypointIndex;
            cursor = furthest + 1;
        }

        Vector3 final = new(exactDestination.x, y, exactDestination.z);
        if (output.Count == 0 || (output[output.Count - 1] - final).sqrMagnitude > 0.04f)
            output.Add(final);
    }

    private static float Heuristic(int x, int z, int targetX, int targetZ)
    {
        int dx = Mathf.Abs(targetX - x);
        int dz = Mathf.Abs(targetZ - z);
        int diagonal = Mathf.Min(dx, dz);
        int straight = Mathf.Max(dx, dz) - diagonal;
        return diagonal * 1.41421356f + straight;
    }
}
