using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Coordina rejilla, obstáculos, pathfinding y órdenes persistentes. El sistema
/// no decide estrategia: solo convierte intenciones en caminos transitables.
/// </summary>
public sealed class NavigationRuntimeSystem
{
    private readonly EntityWorld world;
    private readonly MatchWorldBounds bounds;
    private readonly NavigationGrid grid;
    private readonly NavigationPathfinder pathfinder;
    private readonly DynamicObstacleService obstacles;
    private readonly float obstacleRefreshInterval;
    private readonly float repathInterval;
    private readonly float arrivalTolerance;
    private float nextObstacleRefresh;

    public int ObstacleRevision => obstacles.Revision;
    public int ObstacleCount => obstacles.ObstacleCount;
    public float CellSize => grid.CellSize;
    public float ArrivalTolerance => arrivalTolerance;
    public bool PathVisualizationEnabled { get; private set; }

    public NavigationRuntimeSystem(
        EntityWorld world,
        MatchWorldBounds bounds,
        ScenarioNavigationDefinition settings)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));

        float cellSize = settings != null && settings.cellSize > 0f
            ? settings.cellSize
            : 0.8f;
        bool allowDiagonal = settings == null || settings.allowDiagonal;
        obstacleRefreshInterval = Mathf.Max(0.05f, settings?.obstacleRefreshInterval ?? 0.35f);
        repathInterval = Mathf.Max(0.05f, settings?.repathInterval ?? 0.25f);
        arrivalTolerance = Mathf.Max(0.05f, settings?.arrivalTolerance ?? 0.18f);

        grid = new NavigationGrid(bounds, cellSize, allowDiagonal);
        pathfinder = new NavigationPathfinder(grid);
        obstacles = new DynamicObstacleService(world, grid);
        obstacles.RebuildIfChanged();
    }

    public void Update(float elapsedTime)
    {
        if (elapsedTime >= nextObstacleRefresh)
        {
            nextObstacleRefresh = elapsedTime + obstacleRefreshInterval;
            obstacles.RebuildIfChanged();
        }

        foreach (EntityRuntimeState entity in world.Values)
        {
            EnsureState(entity);
            if (entity.Life == null || !entity.Life.CanAct || entity.MoveSpeed <= 0f)
                continue;

            EntityNavigationRuntimeState navigation = entity.Navigation;
            if (navigation.PathPurpose == EntityPathPurpose.BaseOrder &&
                navigation.PathObstacleRevision != obstacles.Revision &&
                elapsedTime >= navigation.NextRepathTime)
            {
                PreparePath(entity, navigation.RequestedDestination, EntityPathPurpose.BaseOrder, elapsedTime, true);
            }

        }
    }

    public bool TrySetMove(
        EntityRuntimeState entity,
        Vector3 destination,
        EntityNavigationOrderType orderType,
        float elapsedTime,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (!CanNavigate(entity, out rejectionReason))
            return false;

        destination = bounds.Clamp(new Vector3(destination.x, entity.Position.y, destination.z));
        EnsureState(entity);
        EntityNavigationRuntimeState navigation = entity.Navigation;
        navigation.OrderType = orderType;
        navigation.OrderOrigin = entity.Position;
        navigation.OrderDestination = destination;
        navigation.PatrolTowardsDestination = true;

        if (!PreparePath(entity, destination, EntityPathPurpose.BaseOrder, elapsedTime, true))
        {
            rejectionReason = $"No existe una ruta transitable hacia {destination.x:0.##},{destination.z:0.##}.";
            navigation.ClearAll(entity.Position, "unreachable");
            entity.Destination = entity.Position;
            return false;
        }

        return true;
    }

    public bool TrySetPatrol(
        EntityRuntimeState entity,
        Vector3 destination,
        float elapsedTime,
        out string rejectionReason)
    {
        if (!TrySetMove(entity, destination, EntityNavigationOrderType.Patrol, elapsedTime, out rejectionReason))
            return false;

        entity.Navigation.OrderOrigin = entity.Position;
        entity.Navigation.PatrolTowardsDestination = true;
        return true;
    }

    public void SetChaseDestination(EntityRuntimeState entity, Vector3 destination, float elapsedTime)
    {
        PrepareDynamicPath(entity, destination, EntityPathPurpose.Chase, elapsedTime);
    }

    public void SetFollowDestination(EntityRuntimeState entity, Vector3 destination, float elapsedTime)
    {
        PrepareDynamicPath(entity, destination, EntityPathPurpose.Follow, elapsedTime);
    }

    public void SetResourceDestination(EntityRuntimeState entity, Vector3 destination, float elapsedTime)
    {
        PrepareDynamicPath(entity, destination, EntityPathPurpose.ResourceInteraction, elapsedTime);
    }

    public void HoldPosition(EntityRuntimeState entity, bool preserveBaseOrder = true, string status = "holding")
    {
        if (entity == null)
            return;
        EnsureState(entity);

        EntityNavigationOrderType order = entity.Navigation.OrderType;
        Vector3 origin = entity.Navigation.OrderOrigin;
        Vector3 destination = entity.Navigation.OrderDestination;
        bool patrolDirection = entity.Navigation.PatrolTowardsDestination;
        entity.Navigation.ClearPath(entity.Position, status);
        if (preserveBaseOrder)
        {
            entity.Navigation.OrderType = order;
            entity.Navigation.OrderOrigin = origin;
            entity.Navigation.OrderDestination = destination;
            entity.Navigation.PatrolTowardsDestination = patrolDirection;
        }
        else
        {
            entity.Navigation.OrderType = EntityNavigationOrderType.None;
        }
        entity.Destination = entity.Position;
    }

    public void ClearOrders(EntityRuntimeState entity, string status = "stopped")
    {
        if (entity == null)
            return;
        EnsureState(entity);
        entity.Navigation.ClearAll(entity.Position, status);
        entity.Destination = entity.Position;
    }

    public void ResumeBaseOrder(EntityRuntimeState entity, float elapsedTime)
    {
        if (entity?.Navigation == null || !entity.Navigation.HasBaseOrder)
        {
            HoldPosition(entity, false, "idle");
            return;
        }

        Vector3 destination = ResolveCurrentBaseDestination(entity.Navigation);
        PreparePath(entity, destination, EntityPathPurpose.BaseOrder, elapsedTime, true);
    }

    public bool TryGetNextWaypoint(EntityRuntimeState entity, out Vector3 waypoint)
    {
        waypoint = entity?.Position ?? Vector3.zero;
        if (entity?.Navigation == null || !entity.Navigation.HasPath)
            return false;

        waypoint = entity.Navigation.Waypoints[entity.Navigation.WaypointIndex];
        waypoint.y = entity.Position.y;
        return true;
    }

    public void AdvanceWaypointIfReached(EntityRuntimeState entity, float elapsedTime)
    {
        if (entity?.Navigation == null || !entity.Navigation.HasPath)
            return;

        Vector3 waypoint = entity.Navigation.Waypoints[entity.Navigation.WaypointIndex];
        Vector2 delta = new(entity.Position.x - waypoint.x, entity.Position.z - waypoint.z);
        if (delta.sqrMagnitude > arrivalTolerance * arrivalTolerance)
            return;

        entity.Navigation.WaypointIndex++;
        if (entity.Navigation.HasPath)
        {
            Vector3 next = entity.Navigation.Waypoints[entity.Navigation.WaypointIndex];
            entity.Destination = new Vector3(next.x, entity.Position.y, next.z);
            return;
        }

        EntityPathPurpose completedPurpose = entity.Navigation.PathPurpose;
        entity.Navigation.ClearPath(entity.Position, "arrived");
        entity.Destination = entity.Position;
        if (completedPurpose == EntityPathPurpose.BaseOrder)
            HandleBaseOrderArrival(entity, elapsedTime);
    }

    public void NotifyBlocked(EntityRuntimeState entity, float elapsedTime)
    {
        if (entity?.Navigation == null)
            return;

        EntityNavigationRuntimeState navigation = entity.Navigation;
        if (navigation.BlockedSince < 0f)
            navigation.BlockedSince = elapsedTime;

        if (elapsedTime < navigation.NextRepathTime)
            return;

        navigation.NextRepathTime = elapsedTime + repathInterval;
        if (navigation.PathPurpose != EntityPathPurpose.None)
            PreparePath(entity, navigation.RequestedDestination, navigation.PathPurpose, elapsedTime, true);
    }

    public void SetPathVisualizationEnabled(bool enabled)
    {
        PathVisualizationEnabled = enabled;
    }

    public IReadOnlyList<string> BuildDiagnostics(string filter = null)
    {
        string normalized = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        return world.Values
            .Where(item => item?.Navigation != null)
            .Where(item => normalized == null ||
                           item.UnitId.ToString() == normalized ||
                           (item.EntityDefinitionId?.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                           item.Navigation.OrderType.ToString().IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(item => item.UnitId)
            .Select(item =>
                $"{item.UnitId}:{item.EntityDefinitionId} order={item.Navigation.OrderType} " +
                $"purpose={item.Navigation.PathPurpose} waypoint={item.Navigation.WaypointIndex}/{item.Navigation.Waypoints.Count} " +
                $"status={item.Navigation.LastPathStatus}")
            .ToList();
    }

    private void PrepareDynamicPath(
        EntityRuntimeState entity,
        Vector3 destination,
        EntityPathPurpose purpose,
        float elapsedTime)
    {
        if (entity == null || entity.MoveSpeed <= 0f)
            return;
        EnsureState(entity);

        destination = bounds.Clamp(new Vector3(destination.x, entity.Position.y, destination.z));
        EntityNavigationRuntimeState navigation = entity.Navigation;
        Vector2 delta = new(
            navigation.RequestedDestination.x - destination.x,
            navigation.RequestedDestination.z - destination.z);
        bool destinationMoved = delta.sqrMagnitude > grid.CellSize * grid.CellSize * 0.36f;
        bool invalidRevision = navigation.PathObstacleRevision != obstacles.Revision;
        bool wrongPurpose = navigation.PathPurpose != purpose;
        if (!wrongPurpose && !destinationMoved && !invalidRevision && navigation.HasPath)
            return;
        if (!wrongPurpose && elapsedTime < navigation.NextRepathTime)
            return;

        PreparePath(entity, destination, purpose, elapsedTime, true);
    }

    private bool PreparePath(
        EntityRuntimeState entity,
        Vector3 destination,
        EntityPathPurpose purpose,
        float elapsedTime,
        bool force)
    {
        EnsureState(entity);
        EntityNavigationRuntimeState navigation = entity.Navigation;
        if (!force && elapsedTime < navigation.NextRepathTime)
            return navigation.HasPath;

        destination = bounds.Clamp(new Vector3(destination.x, entity.Position.y, destination.z));
        List<Vector3> path = new();
        bool found = pathfinder.TryFindPath(entity.Position, destination, entity.Position.y, path, out string status);

        navigation.Waypoints.Clear();
        navigation.WaypointIndex = 0;
        navigation.PathPurpose = purpose;
        navigation.RequestedDestination = destination;
        navigation.LastPathStart = entity.Position;
        navigation.PathObstacleRevision = obstacles.Revision;
        navigation.NextRepathTime = elapsedTime + repathInterval;
        navigation.BlockedSince = -1f;
        navigation.LastPathStatus = status;

        if (!found)
        {
            entity.Destination = entity.Position;
            return false;
        }

        navigation.Waypoints.AddRange(path);
        if (navigation.HasPath)
        {
            Vector3 first = navigation.Waypoints[0];
            entity.Destination = new Vector3(first.x, entity.Position.y, first.z);
        }
        else
        {
            entity.Destination = entity.Position;
        }

        return true;
    }

    private void HandleBaseOrderArrival(EntityRuntimeState entity, float elapsedTime)
    {
        EntityNavigationRuntimeState navigation = entity.Navigation;
        switch (navigation.OrderType)
        {
            case EntityNavigationOrderType.Patrol:
                navigation.PatrolTowardsDestination = !navigation.PatrolTowardsDestination;
                Vector3 next = ResolveCurrentBaseDestination(navigation);
                PreparePath(entity, next, EntityPathPurpose.BaseOrder, elapsedTime, true);
                break;

            case EntityNavigationOrderType.Move:
            case EntityNavigationOrderType.AttackMove:
                navigation.OrderType = EntityNavigationOrderType.None;
                navigation.LastPathStatus = "order-completed";
                entity.Destination = entity.Position;
                break;
        }
    }

    private static Vector3 ResolveCurrentBaseDestination(EntityNavigationRuntimeState navigation)
    {
        if (navigation.OrderType == EntityNavigationOrderType.Patrol)
        {
            return navigation.PatrolTowardsDestination
                ? navigation.OrderDestination
                : navigation.OrderOrigin;
        }

        return navigation.OrderDestination;
    }

    private static bool CanNavigate(EntityRuntimeState entity, out string rejectionReason)
    {
        rejectionReason = null;
        if (entity == null)
        {
            rejectionReason = "Entidad inexistente.";
            return false;
        }
        if (entity.Life == null || !entity.Life.CanAct)
        {
            rejectionReason = "La entidad no puede navegar en su estado actual.";
            return false;
        }
        if (entity.MoveSpeed <= 0f)
        {
            rejectionReason = "La entidad no posee velocidad de movimiento.";
            return false;
        }
        return true;
    }

    private static void EnsureState(EntityRuntimeState entity)
    {
        if (entity != null && entity.Navigation == null)
            entity.Navigation = new EntityNavigationRuntimeState();
    }
}
