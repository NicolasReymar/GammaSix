using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Prototipo autoritativo de unidades RTS con selección local avanzada.
/// El servidor conserva el estado real y valida todas las órdenes.
/// </summary>
public class NetworkUnitSystem : MonoBehaviour
{
    private const string SnapshotMessage = "GammaSix.UnitSnapshot";
    private const string MoveCommandMessage = "GammaSix.UnitMoveCommand";
    private const float SnapshotInterval = 0.10f;
    private const float DefaultMoveSpeed = 4f;
    private const int MaxSelectedUnits = 50;
    private const float DragThreshold = 8f;
    private const float DoubleClickWindow = 0.30f;
    private const float DirectInputInterval = 0.08f;

    public static NetworkUnitSystem Instance { get; private set; }

    private readonly Dictionary<int, UnitRuntimeState> serverUnits = new();
    private readonly Dictionary<int, NetworkUnitView> unitViews = new();
    private readonly List<NetworkUnitView> selectedUnits = new();
    private int inspectedSelectionGroupIndex;
    public IReadOnlyList<NetworkUnitView> SelectedEntities => selectedUnits;
    public NetworkUnitView PrimarySelectedEntity => GetInspectedSelectionGroup()?.Representative;
    public int InspectedSelectionGroupIndex => inspectedSelectionGroupIndex;
    private readonly Dictionary<int, List<int>> controlGroups = new();

    private NetworkManager Manager => NetworkRuntimeBootstrap.Instance != null
        ? NetworkRuntimeBootstrap.Instance.NetworkManager
        : null;

    private Camera gameplayCamera;
    private RTSCameraController cameraController;
    private float snapshotTimer;
    private float directInputTimer;
    private bool handlersRegistered;
    private bool serverInitialized;
    private bool draggingSelection;
    private Vector2 dragStart;
    private Vector2 dragCurrent;
    private float lastClickTime = -10f;
    private int lastClickedUnitId = -1;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        gameplayCamera = Camera.main;
        PreparePrototypeScene();
        EnsureCameraController();
        TryRegisterMessageHandlers();
    }

    private void OnDestroy()
    {
        UnregisterMessageHandlers();
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (Manager == null || !Manager.IsListening)
            return;

        TryRegisterMessageHandlers();

        if (Manager.IsServer)
        {
            if (!serverInitialized)
                InitializeServerUnits();

            UpdateServerMovement(Time.deltaTime);
            snapshotTimer += Time.deltaTime;
            if (snapshotTimer >= SnapshotInterval)
            {
                snapshotTimer = 0f;
                BroadcastSnapshot();
            }
        }

        HandleLocalInput();
    }

    private void OnGUI()
    {
        ConsumeGameplayAltEvent();

        if (!draggingSelection)
            return;

        Rect rect = GetScreenRect(dragStart, dragCurrent);
        DrawSelectionRectangle(rect);
    }

    private void ConsumeGameplayAltEvent()
    {
        Event current = Event.current;
        if (current == null)
            return;

        bool cameraInteraction = cameraController != null && cameraController.IsLocked;
        bool altShortcut = current.alt && current.keyCode == KeyCode.R;
        bool altCameraInput = cameraInteraction && current.alt;

        if ((altShortcut || altCameraInput) &&
            (current.type == EventType.KeyDown || current.type == EventType.KeyUp))
        {
            current.Use();
        }
    }

    private void PreparePrototypeScene()
    {
        if (GameObject.Find("RTS Prototype Ground") == null)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "RTS Prototype Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
        }
    }

    private void EnsureCameraController()
    {
        if (gameplayCamera == null)
            return;

        cameraController = gameplayCamera.GetComponent<RTSCameraController>();
        if (cameraController == null)
            cameraController = gameplayCamera.gameObject.AddComponent<RTSCameraController>();
        cameraController.Initialize(gameplayCamera);
    }

    private void InitializeServerUnits()
    {
        NetworkSessionManager session = NetworkSessionManager.Instance;
        if (session == null)
            return;

        IReadOnlyList<NetworkPlayerInfo> players = session.Players;
        if (players == null || players.Count == 0)
            return;

        serverUnits.Clear();

        ScenarioDefinition scenario = GameContentRepository.LoadScenario(session.SelectedScenarioId);
        bool loadedFromScenario = TryCreateScenarioUnits(scenario, players);

        if (!loadedFromScenario)
            CreateFallbackPlayerUnits(players);

        serverInitialized = true;
        BroadcastSnapshot();
        Debug.Log($"[NetworkUnitSystem] {serverUnits.Count} unidades iniciales creadas " +
                  $"{(loadedFromScenario ? "desde el escenario" : "mediante fallback")}.");
    }

    private bool TryCreateScenarioUnits(
        ScenarioDefinition scenario,
        IReadOnlyList<NetworkPlayerInfo> players)
    {
        if (scenario?.entities == null || scenario.entities.Length == 0)
            return false;

        Dictionary<int, List<NetworkPlayerInfo>> playersByTeam = players
            .GroupBy(player => player.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(player => player.ClientId).ToList());

        int runtimeId = 1;
        foreach (ScenarioEntityDefinition placement in scenario.entities)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.entityId))
                continue;

            EntityDefinition definition = EntityDefinitionRepository.Load(placement.entityId);
            if (definition == null)
                continue;

            ulong ownerClientId = ulong.MaxValue;
            int colorId = PlayerColorPalette.Neutral;

            if (placement.teamId != 0)
            {
                if (!playersByTeam.TryGetValue(placement.teamId, out List<NetworkPlayerInfo> teamPlayers) ||
                    teamPlayers.Count == 0)
                {
                    Debug.LogWarning($"[NetworkUnitSystem] La instancia '{placement.id}' pertenece al equipo " +
                                     $"{placement.teamId}, pero ese equipo no tiene jugadores conectados.");
                    continue;
                }

                int requestedSlot = Mathf.Max(1, placement.ownerTeamSlot);
                int ownerIndex = Mathf.Clamp(requestedSlot - 1, 0, teamPlayers.Count - 1);
                NetworkPlayerInfo owner = teamPlayers[ownerIndex];
                ownerClientId = owner.ClientId;
                colorId = owner.ColorId;
            }

            Vector3 spawnPosition = placement.position != null
                ? placement.position.ToVector3()
                : GetSpawnPosition(runtimeId - 1, scenario.entities.Length);
            spawnPosition.y = GetEntityGroundY(definition, spawnPosition.y);

            EntityAttributeSet attributes = EntityAttributeCatalog.Create(
                definition.attributes,
                placement.attributes);
            UnitRuntimeState entity = new()
            {
                UnitId = runtimeId,
                EntityDefinitionId = definition.id,
                UnitName = string.IsNullOrWhiteSpace(definition.name) ? definition.id : definition.name,
                UnitTypeId = definition.kind,
                Attributes = attributes,
                OwnerClientId = ownerClientId,
                TeamId = placement.teamId,
                ColorId = colorId,
                Position = spawnPosition,
                Destination = spawnPosition,
                Health = definition.maxHealth > 0 ? definition.maxHealth : 1,
                MaxHealth = definition.maxHealth > 0 ? definition.maxHealth : 1,
                MoveSpeed = definition.moveSpeed,
                Solid = definition.solid,
                BoundsSize = definition.GetScale(new Vector3(0.8f, 1f, 0.8f))
            };

            serverUnits.Add(entity.UnitId, entity);
            runtimeId++;
        }

        return serverUnits.Count > 0;
    }

    private static float GetEntityGroundY(EntityDefinition definition, float requestedY)
    {
        if (requestedY > 0f)
            return requestedY;
        Vector3 scale = definition.GetScale(new Vector3(0.8f, 1f, 0.8f));
        return string.Equals(definition.kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase)
            ? scale.y * 0.5f
            : 0.5f;
    }

    private void CreateFallbackPlayerUnits(IReadOnlyList<NetworkPlayerInfo> players)
    {
        EntityDefinition definition = EntityDefinitionRepository.Load("unit.humanoid.default");
        if (definition == null)
            return;

        int index = 0;
        foreach (NetworkPlayerInfo player in players.OrderBy(p => p.TeamId).ThenBy(p => p.ClientId))
        {
            Vector3 spawnPosition = GetSpawnPosition(index, players.Count);
            UnitRuntimeState unit = new()
            {
                UnitId = index + 1,
                EntityDefinitionId = definition.id,
                UnitName = definition.name,
                UnitTypeId = definition.kind,
                Attributes = EntityAttributeCatalog.Create(definition.attributes),
                OwnerClientId = player.ClientId,
                TeamId = player.TeamId,
                ColorId = player.ColorId,
                Position = spawnPosition,
                Destination = spawnPosition,
                Health = definition.maxHealth,
                MaxHealth = definition.maxHealth,
                MoveSpeed = definition.moveSpeed,
                Solid = definition.solid,
                BoundsSize = definition.GetScale(new Vector3(0.8f, 1f, 0.8f))
            };

            serverUnits.Add(unit.UnitId, unit);
            index++;
        }
    }

    private static Vector3 GetSpawnPosition(int index, int playerCount)
    {
        if (playerCount <= 1)
            return new Vector3(0f, 0.5f, 0f);

        float angle = index * Mathf.PI * 2f / playerCount;
        const float radius = 7f;
        return new Vector3(Mathf.Cos(angle) * radius, 0.5f, Mathf.Sin(angle) * radius);
    }

    private void UpdateServerMovement(float deltaTime)
    {
        foreach (UnitRuntimeState unit in serverUnits.Values)
        {
            if (unit.Attributes == null || !unit.Attributes.Has(EntityAttributeIds.Controllable) || unit.MoveSpeed <= 0f)
                continue;

            Vector3 difference = unit.Destination - unit.Position;
            difference.y = 0f;

            if (difference.sqrMagnitude <= 0.01f)
            {
                unit.Position = new Vector3(unit.Destination.x, 0.5f, unit.Destination.z);
                continue;
            }

            Vector3 next = Vector3.MoveTowards(unit.Position, unit.Destination, unit.MoveSpeed * deltaTime);
            next.y = unit.Position.y;
            if (!IsPositionBlocked(unit, next))
                unit.Position = next;
            else
                unit.Destination = unit.Position;
        }
    }

    private bool IsPositionBlocked(UnitRuntimeState moving, Vector3 candidate)
    {
        if (!moving.Solid)
            return false;

        float movingRadius = Mathf.Max(moving.BoundsSize.x, moving.BoundsSize.z) * 0.5f;
        foreach (UnitRuntimeState other in serverUnits.Values)
        {
            if (other.UnitId == moving.UnitId || !other.Solid)
                continue;

            if (other.Attributes != null && other.Attributes.Has(EntityAttributeIds.Building))
            {
                float halfX = other.BoundsSize.x * 0.5f + movingRadius;
                float halfZ = other.BoundsSize.z * 0.5f + movingRadius;
                if (Mathf.Abs(candidate.x - other.Position.x) < halfX &&
                    Mathf.Abs(candidate.z - other.Position.z) < halfZ)
                    return true;
            }
            else
            {
                float otherRadius = Mathf.Max(other.BoundsSize.x, other.BoundsSize.z) * 0.5f;
                Vector2 delta = new(candidate.x - other.Position.x, candidate.z - other.Position.z);
                if (delta.sqrMagnitude < (movingRadius + otherRadius) * (movingRadius + otherRadius))
                    return true;
            }
        }
        return false;
    }

    private void HandleLocalInput()
    {
        if (gameplayCamera == null || !Manager.IsConnectedClient)
            return;

        HandleControlGroups();
        HandleSelectionInspectionCycle();
        HandleCameraLockShortcut();

        // En tercera persona bloqueada, el cursor está capturado y no puede
        // seleccionar entidades. Al desbloquear el cursor con doble Alt, la
        // cámara sigue fijada a la unidad pero la selección RTS vuelve a estar activa.
        if (cameraController == null || cameraController.CanSelectWithPointer)
            HandleSelectionInput();
        else
            CancelPendingSelectionDrag();

        // Alt + R es la única transición entre control RTS y control directo.
        // Cámara libre: órdenes por clic. Cámara fijada: movimiento WASD de la unidad conductora.
        if (cameraController != null && cameraController.IsLocked)
            HandleDirectMovement();
        else
            HandleClickMovement();
    }

    private void CancelPendingSelectionDrag()
    {
        draggingSelection = false;
        dragStart = Vector2.zero;
        dragCurrent = Vector2.zero;
    }

    private void HandleSelectionInput()
    {
        Vector2 mousePosition = ReadMousePosition();

        if (LeftPressedThisFrame())
        {
            dragStart = mousePosition;
            dragCurrent = mousePosition;
            draggingSelection = false;
        }

        if (LeftIsPressed())
        {
            dragCurrent = mousePosition;
            if (!draggingSelection && Vector2.Distance(dragStart, dragCurrent) >= DragThreshold)
                draggingSelection = true;
        }

        if (!LeftReleasedThisFrame())
            return;

        bool shift = IsShiftHeld();
        if (draggingSelection)
            CompleteDragSelection(shift);
        else
            CompleteClickSelection(mousePosition, shift);

        draggingSelection = false;
    }

    private void CompleteClickSelection(Vector2 mousePosition, bool shift)
    {
        NetworkUnitView clicked = GetSelectableEntityUnderCursor(mousePosition);
        if (clicked == null)
        {
            if (!shift && !KeepLockedTargetSelected())
                ClearSelection();
            return;
        }

        bool isDoubleClick = clicked.UnitId == lastClickedUnitId && Time.unscaledTime - lastClickTime <= DoubleClickWindow;
        lastClickedUnitId = clicked.UnitId;
        lastClickTime = Time.unscaledTime;

        // El doble clic grupal solo puede seleccionar unidades propias.
        // Las entidades enemigas o neutrales siguen siendo inspeccionables,
        // pero nunca arrastran a otras entidades de su equipo a la selección.
        if (isDoubleClick && IsOwnedByLocalPlayer(clicked))
        {
            SelectSameTypeVisible(clicked, shift);
            return;
        }

        if (shift)
            ToggleSelection(clicked);
        else
            SetExclusiveSelection(clicked);
    }

    private void CompleteDragSelection(bool shift)
    {
        Rect selectionRect = GetScreenRect(dragStart, dragCurrent);
        List<NetworkUnitView> inside = unitViews.Values
            .Where(IsOwnedByLocalPlayer)
            .Where(view => IsViewInsideScreenRect(view, selectionRect))
            .OrderBy(view => view.UnitId)
            .Take(MaxSelectedUnits)
            .ToList();

        if (!shift)
        {
            NetworkUnitView locked = GetLockedTargetForSelection();
            ClearSelection();
            if (locked != null)
                AddSelection(locked);
        }

        foreach (NetworkUnitView view in inside)
        {
            if (shift)
                ToggleSelection(view);
            else
                AddSelection(view);
        }
    }

    private void SelectSameTypeVisible(NetworkUnitView source, bool shift)
    {
        List<NetworkUnitView> sameType = unitViews.Values
            .Where(IsOwnedByLocalPlayer)
            .Where(view => view != null && view.HasAttribute(EntityAttributeIds.Selectable))
            .Where(view => string.Equals(view.UnitName, source.UnitName, StringComparison.OrdinalIgnoreCase))
            .Where(IsVisibleOnScreen)
            .OrderBy(view => view.UnitId)
            .Take(MaxSelectedUnits)
            .ToList();

        if (!shift)
            ClearSelection();

        foreach (NetworkUnitView view in sameType)
        {
            if (shift)
                ToggleSelection(view);
            else
                AddSelection(view);
        }
    }

    private void HandleClickMovement()
    {
        if (!RightPressedThisFrame() || selectedUnits.Count == 0)
            return;

        if (!TryGetGroundPoint(ReadMousePosition(), out Vector3 destination))
            return;

        SendFormationMove(destination);
    }

    private void HandleDirectMovement()
    {
        if (selectedUnits.Count == 0)
            return;

        Vector2 input = ReadWASDInput();
        if (input.sqrMagnitude <= 0.01f)
            return;

        directInputTimer -= Time.deltaTime;
        if (directInputTimer > 0f)
            return;
        directInputTimer = DirectInputInterval;

        NetworkUnitView cameraTarget = GetThirdPersonCameraSelection();
        if (cameraTarget == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(gameplayCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(gameplayCamera.transform.right, Vector3.up).normalized;
        Vector3 direction = (forward * input.y + right * input.x).normalized;
        Vector3 destination = cameraTarget.transform.position + direction * 2.2f;
        RequestMove(cameraTarget.UnitId, destination);
    }

    private void HandleCameraLockShortcut()
    {
        if (!IsAltHeld() || !RPressedThisFrame())
            return;

        NetworkUnitView target = GetThirdPersonCameraSelection();
        if (target == null)
        {
            Debug.Log("[NetworkUnitSystem] La unidad seleccionada no posee el atributo heroic o camera.third-person.");
            return;
        }

        cameraController?.ToggleLock(target);
    }

    private NetworkUnitView GetThirdPersonCameraSelection()
    {
        return selectedUnits.FirstOrDefault(unit =>
            IsOwnedByLocalPlayer(unit) &&
            (unit.HasAttribute(EntityAttributeIds.Heroic) ||
             unit.HasAttribute(EntityAttributeIds.ThirdPersonCamera)));
    }

    private void HandleControlGroups()
    {
        for (int group = 1; group <= 3; group++)
        {
            if (!NumberPressedThisFrame(group))
                continue;

            if (IsControlHeld())
                StoreControlGroup(group);
            else
                RecallControlGroup(group);
        }
    }

    private void StoreControlGroup(int group)
    {
        controlGroups[group] = selectedUnits
            .Where(unit => unit != null)
            .Select(unit => unit.UnitId)
            .Take(MaxSelectedUnits)
            .ToList();
        Debug.Log($"[NetworkUnitSystem] Grupo {group} guardado con {controlGroups[group].Count} unidades.");
    }

    private void RecallControlGroup(int group)
    {
        if (!controlGroups.TryGetValue(group, out List<int> ids))
            return;

        ClearSelection();
        foreach (int id in ids)
        {
            if (unitViews.TryGetValue(id, out NetworkUnitView view) && IsOwnedByLocalPlayer(view))
                AddSelection(view);
        }
    }

    private void SendFormationMove(Vector3 center)
    {
        List<NetworkUnitView> controllable = selectedUnits
            .Where(IsOwnedByLocalPlayer)
            .Where(view => view.HasAttribute(EntityAttributeIds.Controllable))
            .ToList();
        if (controllable.Count == 0)
            return;

        const float spacing = 1.7f;
        int count = controllable.Count;
        int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt(count / (float)columns);

        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            float offsetX = (column - (columns - 1) * 0.5f) * spacing;
            float offsetZ = (row - (rows - 1) * 0.5f) * spacing;
            RequestMove(controllable[i].UnitId, center + new Vector3(offsetX, 0f, offsetZ));
        }
    }

    private NetworkUnitView GetSelectableEntityUnderCursor(Vector2 mousePosition)
    {
        Ray ray = gameplayCamera.ScreenPointToRay(mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f))
            return null;

        NetworkUnitView view = hit.collider.GetComponentInParent<NetworkUnitView>();
        return view != null && view.HasAttribute(EntityAttributeIds.Selectable) ? view : null;
    }


    private bool IsOwnedByLocalPlayer(NetworkUnitView view)
    {
        return view != null && Manager != null &&
               view.TeamId != 0 &&
               view.OwnerClientId == Manager.LocalClientId &&
               view.HasAttribute(EntityAttributeIds.Selectable);
    }

    private bool IsViewInsideScreenRect(NetworkUnitView view, Rect rect)
    {
        Vector3 screen = gameplayCamera.WorldToScreenPoint(view.transform.position);
        if (screen.z <= 0f)
            return false;
        screen.y = Screen.height - screen.y;
        return rect.Contains(screen);
    }

    private bool IsVisibleOnScreen(NetworkUnitView view)
    {
        Vector3 screen = gameplayCamera.WorldToViewportPoint(view.transform.position);
        return screen.z > 0f && screen.x >= 0f && screen.x <= 1f && screen.y >= 0f && screen.y <= 1f;
    }

    private void SetExclusiveSelection(NetworkUnitView view)
    {
        NetworkUnitView locked = GetLockedTargetForSelection();
        ClearSelection();
        if (locked != null && locked != view)
            AddSelection(locked);
        AddSelection(view);
    }

    private void ToggleSelection(NetworkUnitView view)
    {
        if (selectedUnits.Contains(view))
        {
            // La entidad controlada en tercera persona debe permanecer seleccionada.
            if (view == GetLockedTargetForSelection())
                return;
            RemoveSelection(view);
        }
        else
            AddSelection(view);
    }

    private void AddSelection(NetworkUnitView view)
    {
        if (view == null || selectedUnits.Contains(view) || selectedUnits.Count >= MaxSelectedUnits)
            return;
        selectedUnits.Add(view);
        view.SetSelected(true);
        NormalizeInspectedSelectionGroupIndex();
    }

    private void RemoveSelection(NetworkUnitView view)
    {
        if (view == null || !selectedUnits.Remove(view))
            return;
        view.SetSelected(false);
        NormalizeInspectedSelectionGroupIndex();
    }

    private void ClearSelection()
    {
        foreach (NetworkUnitView view in selectedUnits)
        {
            if (view != null)
                view.SetSelected(false);
        }
        selectedUnits.Clear();
        inspectedSelectionGroupIndex = 0;
    }

    public IReadOnlyList<SelectionInspectionGroup> GetSelectionInspectionGroups()
    {
        List<SelectionInspectionGroup> groups = new();

        foreach (NetworkUnitView heroic in selectedUnits
                     .Where(view => view != null && view.HasAttribute(EntityAttributeIds.Heroic))
                     .OrderBy(view => view.UnitId))
        {
            groups.Add(new SelectionInspectionGroup(
                $"heroic:{heroic.UnitId}", heroic.UnitName, true,
                new List<NetworkUnitView> { heroic }, heroic));
        }

        foreach (IGrouping<string, NetworkUnitView> group in selectedUnits
                     .Where(view => view != null && !view.HasAttribute(EntityAttributeIds.Heroic))
                     .GroupBy(view => string.IsNullOrWhiteSpace(view.EntityDefinitionId)
                         ? view.UnitTypeId
                         : view.EntityDefinitionId)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<NetworkUnitView> members = group.OrderBy(view => view.UnitId).ToList();
            NetworkUnitView representative = members
                .OrderByDescending(view => view.Health)
                .ThenByDescending(view => view.MaxHealth)
                .ThenBy(view => view.UnitId)
                .First();
            groups.Add(new SelectionInspectionGroup(
                $"group:{group.Key}", representative.UnitName, false, members, representative));
        }

        return groups;
    }

    public SelectionInspectionGroup GetInspectedSelectionGroup()
    {
        IReadOnlyList<SelectionInspectionGroup> groups = GetSelectionInspectionGroups();
        if (groups.Count == 0)
            return null;
        inspectedSelectionGroupIndex = Mathf.Clamp(inspectedSelectionGroupIndex, 0, groups.Count - 1);
        return groups[inspectedSelectionGroupIndex];
    }

    public void SetInspectedSelectionGroup(int index)
    {
        IReadOnlyList<SelectionInspectionGroup> groups = GetSelectionInspectionGroups();
        if (groups.Count == 0)
        {
            inspectedSelectionGroupIndex = 0;
            return;
        }
        inspectedSelectionGroupIndex = Mathf.Clamp(index, 0, groups.Count - 1);
    }

    private void HandleSelectionInspectionCycle()
    {
        if (!TabPressedThisFrame())
            return;

        IReadOnlyList<SelectionInspectionGroup> groups = GetSelectionInspectionGroups();
        if (groups.Count <= 1)
            return;

        inspectedSelectionGroupIndex = (inspectedSelectionGroupIndex + 1) % groups.Count;
    }

    private void NormalizeInspectedSelectionGroupIndex()
    {
        int count = GetSelectionInspectionGroups().Count;
        inspectedSelectionGroupIndex = count == 0 ? 0 : Mathf.Clamp(inspectedSelectionGroupIndex, 0, count - 1);
    }

    private bool KeepLockedTargetSelected()
    {
        NetworkUnitView locked = GetLockedTargetForSelection();
        if (locked == null)
            return false;
        if (!selectedUnits.Contains(locked))
            AddSelection(locked);
        return true;
    }

    private NetworkUnitView GetLockedTargetForSelection()
    {
        return cameraController != null && cameraController.IsLocked
            ? cameraController.LockedTarget
            : null;
    }

    private bool TryGetGroundPoint(Vector2 mousePosition, out Vector3 point)
    {
        Ray ray = gameplayCamera.ScreenPointToRay(mousePosition);
        Plane groundPlane = new(Vector3.up, Vector3.zero);

        if (!groundPlane.Raycast(ray, out float distance))
        {
            point = default;
            return false;
        }

        point = ray.GetPoint(distance);
        point.y = 0.5f;
        return true;
    }

    private void RequestMove(int unitId, Vector3 destination)
    {
        UnitMoveCommand command = new()
        {
            UnitId = unitId,
            X = destination.x,
            Y = 0.5f,
            Z = destination.z
        };

        if (Manager.IsServer)
        {
            ApplyMoveCommand(Manager.LocalClientId, command);
            return;
        }

        string json = JsonUtility.ToJson(command);
        FixedString512Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(MoveCommandMessage, NetworkManager.ServerClientId, writer);
    }

    private void HandleMoveCommand(ulong senderClientId, FastBufferReader reader)
    {
        if (!Manager.IsServer)
            return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        UnitMoveCommand command = JsonUtility.FromJson<UnitMoveCommand>(payload.ToString());
        ApplyMoveCommand(senderClientId, command);
    }

    private void ApplyMoveCommand(ulong senderClientId, UnitMoveCommand command)
    {
        if (!serverUnits.TryGetValue(command.UnitId, out UnitRuntimeState unit))
            return;

        if (unit.OwnerClientId != senderClientId)
        {
            Debug.LogWarning($"[NetworkUnitSystem] Cliente {senderClientId} intentó mover una unidad ajena ({command.UnitId}).");
            return;
        }

        if (unit.Attributes == null || !unit.Attributes.Has(EntityAttributeIds.Controllable))
        {
            Debug.LogWarning($"[NetworkUnitSystem] La entidad {command.UnitId} no posee el atributo de control.");
            return;
        }

        Vector3 requestedDestination = new(command.X, 0.5f, command.Z);
        const float mapLimit = 19f;
        requestedDestination.x = Mathf.Clamp(requestedDestination.x, -mapLimit, mapLimit);
        requestedDestination.z = Mathf.Clamp(requestedDestination.z, -mapLimit, mapLimit);
        unit.Destination = requestedDestination;
    }

    private void BroadcastSnapshot()
    {
        if (!Manager.IsServer || Manager.CustomMessagingManager == null)
            return;

        UnitSnapshotPayload snapshot = new();
        foreach (UnitRuntimeState unit in serverUnits.Values.OrderBy(u => u.UnitId))
        {
            snapshot.Units.Add(new UnitSnapshotData
            {
                UnitId = unit.UnitId,
                EntityDefinitionId = unit.EntityDefinitionId,
                UnitName = unit.UnitName,
                UnitTypeId = unit.UnitTypeId,
                OwnerClientId = unit.OwnerClientId,
                TeamId = unit.TeamId,
                ColorId = unit.ColorId,
                X = unit.Position.x,
                Y = unit.Position.y,
                Z = unit.Position.z,
                Health = unit.Health,
                MaxHealth = unit.MaxHealth,
                Solid = unit.Solid,
                Attributes = unit.Attributes?.ToArray()
            });
        }

        ApplySnapshot(snapshot);

        if (Manager.ConnectedClientsIds.Count <= 1)
            return;

        string json = JsonUtility.ToJson(snapshot);
        FixedString4096Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        // Los snapshots pueden superar el MTU cuando el escenario contiene varias
        // entidades y listas de atributos. Esta entrega fragmenta el mensaje y
        // garantiza que el cliente lo reconstruya completo y en orden.
        Manager.CustomMessagingManager.SendNamedMessageToAll(
            SnapshotMessage,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void HandleSnapshot(ulong senderClientId, FastBufferReader reader)
    {
        if (Manager.IsServer)
            return;

        reader.ReadValueSafe(out FixedString4096Bytes payload);
        UnitSnapshotPayload snapshot = JsonUtility.FromJson<UnitSnapshotPayload>(payload.ToString());
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(UnitSnapshotPayload snapshot)
    {
        if (snapshot?.Units == null)
            return;

        HashSet<int> receivedIds = new();

        foreach (UnitSnapshotData state in snapshot.Units)
        {
            receivedIds.Add(state.UnitId);

            if (!unitViews.TryGetValue(state.UnitId, out NetworkUnitView view))
            {
                view = CreateUnitView(state);
                unitViews.Add(state.UnitId, view);
            }

            view.ApplyState(state);
        }

        foreach (int removedId in unitViews.Keys.Where(id => !receivedIds.Contains(id)).ToList())
        {
            NetworkUnitView removed = unitViews[removedId];
            RemoveSelection(removed);
            if (cameraController != null && cameraController.LockedTarget == removed)
                cameraController.Unlock();
            Destroy(removed.gameObject);
            unitViews.Remove(removedId);
        }
    }

    private NetworkUnitView CreateUnitView(UnitSnapshotData state)
    {
        EntityDefinition definition = EntityDefinitionRepository.Load(state.EntityDefinitionId);
        if (definition == null)
            return CreateMissingEntityView(state);

        GameObject entityObject;
        if (string.Equals(definition.kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase))
            entityObject = BuildingEntityView.Create(definition, state.UnitId, state.TeamId);
        else
        {
            entityObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            entityObject.name = $"{definition.name} {state.UnitId} - Equipo {state.TeamId}";
            entityObject.transform.localScale = definition.GetScale(new Vector3(0.8f, 1f, 0.8f));
            Collider collider = entityObject.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = definition.solid;
        }

        NetworkUnitView view = entityObject.AddComponent<NetworkUnitView>();
        view.Initialize(state.UnitId, state.EntityDefinitionId, state.UnitName, state.UnitTypeId,
            state.OwnerClientId, state.TeamId, state.ColorId, state.Attributes);
        return view;
    }

    private NetworkUnitView CreateMissingEntityView(UnitSnapshotData state)
    {
        GameObject entityObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        entityObject.name = $"Missing Entity {state.EntityDefinitionId}";
        NetworkUnitView view = entityObject.AddComponent<NetworkUnitView>();
        view.Initialize(state.UnitId, state.EntityDefinitionId, state.UnitName, state.UnitTypeId,
            state.OwnerClientId, state.TeamId, state.ColorId, state.Attributes);
        return view;
    }

    private void TryRegisterMessageHandlers()
    {
        if (handlersRegistered || Manager?.CustomMessagingManager == null)
            return;

        Manager.CustomMessagingManager.RegisterNamedMessageHandler(SnapshotMessage, HandleSnapshot);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(MoveCommandMessage, HandleMoveCommand);
        handlersRegistered = true;
    }

    private void UnregisterMessageHandlers()
    {
        if (!handlersRegistered || Manager?.CustomMessagingManager == null)
            return;

        Manager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessage);
        Manager.CustomMessagingManager.UnregisterNamedMessageHandler(MoveCommandMessage);
        handlersRegistered = false;
    }

    private static Rect GetScreenRect(Vector2 start, Vector2 end)
    {
        start.y = Screen.height - start.y;
        end.y = Screen.height - end.y;
        Vector2 topLeft = Vector2.Min(start, end);
        Vector2 bottomRight = Vector2.Max(start, end);
        return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }

    private static void DrawSelectionRectangle(Rect rect)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0.15f, 0.85f, 0.35f, 0.12f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static Vector2 ReadMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    private static bool LeftPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private static bool LeftReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(0);
#endif
    }

    private static bool LeftIsPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private static bool RightPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(1);
#endif
    }

    private static bool IsShiftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }

    private static bool IsControlHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
    }

    private static bool IsAltHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
    }

    private static bool RPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    private static Vector2 ReadWASDInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null)
            return Vector2.zero;
        return new Vector2(
            (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f),
            (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f));
#else
        return new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
#endif
    }

    private static bool TabPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }

    private static bool NumberPressedThisFrame(int number)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null)
            return false;
        return number switch
        {
            1 => Keyboard.current.digit1Key.wasPressedThisFrame,
            2 => Keyboard.current.digit2Key.wasPressedThisFrame,
            3 => Keyboard.current.digit3Key.wasPressedThisFrame,
            _ => false
        };
#else
        return number switch
        {
            1 => Input.GetKeyDown(KeyCode.Alpha1),
            2 => Input.GetKeyDown(KeyCode.Alpha2),
            3 => Input.GetKeyDown(KeyCode.Alpha3),
            _ => false
        };
#endif
    }

    private class UnitRuntimeState
    {
        public int UnitId;
        public string EntityDefinitionId;
        public string UnitName;
        public string UnitTypeId;
        public EntityAttributeSet Attributes;
        public ulong OwnerClientId;
        public int TeamId;
        public int ColorId;
        public Vector3 Position;
        public Vector3 Destination;
        public int Health;
        public int MaxHealth;
        public float MoveSpeed;
        public bool Solid;
        public Vector3 BoundsSize;
    }

    [Serializable]
    private class UnitMoveCommand
    {
        public int UnitId;
        public float X;
        public float Y;
        public float Z;
    }

    [Serializable]
    private class UnitSnapshotPayload
    {
        public List<UnitSnapshotData> Units = new();
    }

    [Serializable]
    public class UnitSnapshotData
    {
        public int UnitId;
        public string EntityDefinitionId;
        public string UnitName;
        public string UnitTypeId;
        public ulong OwnerClientId;
        public int TeamId;
        public int ColorId;
        public float X;
        public float Y;
        public float Z;
        public int Health;
        public int MaxHealth;
        public bool Solid;
        public string[] Attributes;
    }
}

public sealed class SelectionInspectionGroup
{
    public string Key { get; }
    public string DisplayName { get; }
    public bool IsHeroic { get; }
    public IReadOnlyList<NetworkUnitView> Members { get; }
    public NetworkUnitView Representative { get; }
    public int Count => Members?.Count ?? 0;

    public SelectionInspectionGroup(string key, string displayName, bool isHeroic,
        IReadOnlyList<NetworkUnitView> members, NetworkUnitView representative)
    {
        Key = key;
        DisplayName = displayName;
        IsHeroic = isHeroic;
        Members = members;
        Representative = representative;
    }
}

public class NetworkUnitView : MonoBehaviour
{
    public int UnitId { get; private set; }
    public string EntityDefinitionId { get; private set; }
    public string UnitName { get; private set; }
    public string UnitTypeId { get; private set; }
    public EntityAttributeSet Attributes { get; private set; } = new();
    public ulong OwnerClientId { get; private set; }
    public int TeamId { get; private set; }
    public int ColorId { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth { get; private set; }
    public float SelectionRadius => Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.75f;

    private Renderer unitRenderer;
    private GameObject selectionMarker;
    private Vector3 targetPosition;
    private bool hasState;

    public void Initialize(int unitId, string entityDefinitionId, string unitName, string unitTypeId, ulong ownerClientId, int teamId, int colorId, string[] attributes)
    {
        UnitId = unitId;
        EntityDefinitionId = entityDefinitionId;
        UnitName = string.IsNullOrWhiteSpace(unitName) ? entityDefinitionId : unitName;
        UnitTypeId = unitTypeId;
        Attributes = EntityAttributeCatalog.Create(attributes);
        OwnerClientId = ownerClientId;
        TeamId = teamId;
        ColorId = colorId;

        unitRenderer = GetComponent<Renderer>();
        if (unitRenderer != null && !HasAttribute(EntityAttributeIds.AuraTrigger))
            unitRenderer.material.color = PlayerColorPalette.GetColor(colorId);

        CreateSelectionHalo();
    }

    private void CreateSelectionHalo()
    {
        selectionMarker = new GameObject("Selection Halo");
        selectionMarker.transform.SetParent(transform, false);

        // La cápsula de prueba está centrada en Y=0.5 y mide 2 unidades de alto.
        // El valor anterior (-1) dejaba el aro bajo el suelo. Se coloca apenas encima del plano.
        selectionMarker.transform.localPosition = new Vector3(0f, -0.49f, 0f);
        selectionMarker.transform.localRotation = Quaternion.identity;

        LineRenderer line = selectionMarker.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.alignment = LineAlignment.TransformZ;
        line.widthMultiplier = 0.085f;
        line.positionCount = 64;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 50;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            Material haloMaterial = new Material(shader);
            haloMaterial.color = new Color(0.1f, 1f, 0.2f, 1f);
            line.material = haloMaterial;
        }

        line.startColor = new Color(0.1f, 1f, 0.2f, 1f);
        line.endColor = new Color(0.1f, 1f, 0.2f, 1f);

        // El aro usa coordenadas locales para heredar automáticamente la escala de la entidad.
        float radius = 0.72f;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        selectionMarker.SetActive(false);
    }

    public void ApplyState(NetworkUnitSystem.UnitSnapshotData state)
    {
        EntityDefinitionId = state.EntityDefinitionId;
        UnitName = string.IsNullOrWhiteSpace(state.UnitName) ? state.EntityDefinitionId : state.UnitName;
        UnitTypeId = state.UnitTypeId;
        Attributes = EntityAttributeCatalog.Create(state.Attributes);
        OwnerClientId = state.OwnerClientId;
        TeamId = state.TeamId;
        ColorId = state.ColorId;
        Health = state.Health;
        MaxHealth = state.MaxHealth;
        if (unitRenderer != null && !HasAttribute(EntityAttributeIds.AuraTrigger))
            unitRenderer.material.color = PlayerColorPalette.GetColor(ColorId);
        targetPosition = new Vector3(state.X, state.Y, state.Z);

        if (!hasState)
        {
            transform.position = targetPosition;
            hasState = true;
        }
    }

    public bool HasAttribute(string attributeId) => Attributes != null && Attributes.Has(attributeId);

    public void SetSelected(bool selected)
    {
        if (selectionMarker == null)
            return;

        LineRenderer line = selectionMarker.GetComponent<LineRenderer>();
        if (line != null)
        {
            bool owned = NetworkRuntimeBootstrap.Instance != null &&
                         NetworkRuntimeBootstrap.Instance.NetworkManager != null &&
                         TeamId != 0 &&
                         OwnerClientId == NetworkRuntimeBootstrap.Instance.NetworkManager.LocalClientId;
            Color haloColor = owned
                ? new Color(0.1f, 1f, 0.2f, 1f)
                : new Color(1f, 0.85f, 0.1f, 1f);
            line.startColor = haloColor;
            line.endColor = haloColor;
            if (line.material != null)
                line.material.color = haloColor;
        }

        selectionMarker.SetActive(selected);
    }

    private void Update()
    {
        if (hasState)
            transform.position = Vector3.Lerp(transform.position, targetPosition, 18f * Time.deltaTime);
    }
}
