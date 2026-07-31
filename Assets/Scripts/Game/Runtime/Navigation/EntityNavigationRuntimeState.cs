using System.Collections.Generic;
using UnityEngine;

public enum EntityNavigationOrderType
{
    None,
    Move,
    AttackMove,
    Patrol
}

public enum EntityPathPurpose
{
    None,
    BaseOrder,
    Chase,
    Follow,
    ResourceInteraction
}

/// <summary>
/// Estado autoritativo de navegación. Separa la orden persistente (mover,
/// attack-move o patrulla) del camino transitorio usado para perseguir, seguir o
/// interactuar con una entidad móvil.
/// </summary>
public sealed class EntityNavigationRuntimeState
{
    public EntityNavigationOrderType OrderType;
    public Vector3 OrderOrigin;
    public Vector3 OrderDestination;
    public bool PatrolTowardsDestination = true;

    public readonly List<Vector3> Waypoints = new();
    public int WaypointIndex;
    public EntityPathPurpose PathPurpose;
    public Vector3 RequestedDestination;
    public Vector3 LastPathStart;
    public int PathObstacleRevision = -1;
    public float NextRepathTime;
    public float BlockedSince = -1f;
    public string LastPathStatus = "idle";

    public bool HasBaseOrder => OrderType != EntityNavigationOrderType.None;
    public bool HasPath => WaypointIndex >= 0 && WaypointIndex < Waypoints.Count;

    public void ClearPath(Vector3 position, string status = "idle")
    {
        Waypoints.Clear();
        WaypointIndex = 0;
        PathPurpose = EntityPathPurpose.None;
        RequestedDestination = position;
        LastPathStart = position;
        PathObstacleRevision = -1;
        BlockedSince = -1f;
        LastPathStatus = status;
    }

    public void ClearAll(Vector3 position, string status = "stopped")
    {
        OrderType = EntityNavigationOrderType.None;
        OrderOrigin = position;
        OrderDestination = position;
        PatrolTowardsDestination = true;
        ClearPath(position, status);
    }
}
