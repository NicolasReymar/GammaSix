using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Prototipo autoritativo de unidades RTS con selección local avanzada.
/// El servidor conserva el estado real y valida todas las órdenes.
/// </summary>
public class NetworkEntityCoordinator : MonoBehaviour
{
    private const float SnapshotInterval = 0.10f;
    private const int MaxSelectedUnits = 50;
    private const float DragThreshold = 8f;
    private const float DoubleClickWindow = 0.30f;
    private const float DirectInputInterval = 0.08f;

    public static NetworkEntityCoordinator Instance { get; private set; }

    private readonly Dictionary<int, EntityRuntimeState> serverUnits = new();
    private readonly Dictionary<int, NetworkEntityView> unitViews = new();
    private EntitySelectionService selectionService;
    private ControlGroupService controlGroupService;

    public IReadOnlyList<NetworkEntityView> SelectedEntities => selectionService?.Selected ?? Array.Empty<NetworkEntityView>();
    public NetworkEntityView PrimarySelectedEntity => selectionService?.PrimarySelected;
    public int InspectedSelectionGroupIndex => selectionService?.InspectedGroupIndex ?? 0;

    public event Action SelectionChanged;
    public event Action InspectedSelectionChanged;

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
    private bool selectionPointerStartedOverHud;

    private void Awake()
    {
        Instance = this;
        selectionService = new EntitySelectionService(
            MaxSelectedUnits,
            IsOwnedByLocalPlayer,
            GetLockedTargetForSelection);
        controlGroupService = new ControlGroupService(MaxSelectedUnits);
        selectionService.SelectionChanged += () => SelectionChanged?.Invoke();
        selectionService.InspectionChanged += () => InspectedSelectionChanged?.Invoke();
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

            EntityMovementService.Update(serverUnits, Time.deltaTime);
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

        if (!draggingSelection || HudInteractionService.IsDragging)
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
        bool loadedFromScenario = ScenarioEntitySpawner.TryPopulate(serverUnits, scenario, players);

        if (!loadedFromScenario)
            ScenarioEntitySpawner.CreateFallback(serverUnits, players);

        serverInitialized = true;
        BroadcastSnapshot();
        Debug.Log($"[NetworkEntityCoordinator] {serverUnits.Count} unidades iniciales creadas " +
                  $"{(loadedFromScenario ? "desde el escenario" : "mediante fallback")}.");
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
        if (HudInteractionService.IsDragging)
        {
            CancelPendingSelectionDrag();
            selectionPointerStartedOverHud = true;
            if (GameInputReader.LeftReleasedThisFrame)
                selectionPointerStartedOverHud = false;
            return;
        }

        Vector2 mousePosition = GameInputReader.PointerPosition;

        if (GameInputReader.LeftPressedThisFrame)
        {
            selectionPointerStartedOverHud = HudInteractionService.IsPointerOverHud(mousePosition);
            if (selectionPointerStartedOverHud)
            {
                CancelPendingSelectionDrag();
                return;
            }

            dragStart = mousePosition;
            dragCurrent = mousePosition;
            draggingSelection = false;
        }

        if (selectionPointerStartedOverHud)
        {
            if (GameInputReader.LeftReleasedThisFrame)
                selectionPointerStartedOverHud = false;
            return;
        }

        if (GameInputReader.LeftIsPressed)
        {
            // Si el cursor entra sobre un panel durante el gesto, se cancela la
            // selección para no dibujar el rectángulo verde detrás del HUD.
            if (HudInteractionService.IsPointerOverHud(mousePosition))
            {
                CancelPendingSelectionDrag();
                selectionPointerStartedOverHud = true;
                return;
            }

            dragCurrent = mousePosition;
            if (!draggingSelection && Vector2.Distance(dragStart, dragCurrent) >= DragThreshold)
                draggingSelection = true;
        }

        if (!GameInputReader.LeftReleasedThisFrame)
            return;

        bool shift = GameInputReader.ShiftHeld;
        if (draggingSelection)
            CompleteDragSelection(shift);
        else
            CompleteClickSelection(mousePosition, shift);

        draggingSelection = false;
        selectionPointerStartedOverHud = false;
    }

    private void CompleteClickSelection(Vector2 mousePosition, bool shift)
    {
        NetworkEntityView clicked = GetSelectableEntityUnderCursor(mousePosition);
        if (clicked == null)
        {
            if (!shift)
            {
                // Mientras la cámara sigue bloqueada sobre una unidad, un clic
                // sobre terreno o una entidad no seleccionable limpia cualquier
                // selección adicional y conserva únicamente la unidad controlada.
                if (GetLockedTargetForSelection() != null)
                    ClearSelectionExceptLockedTarget();
                else
                    ClearSelection();
            }
            return;
        }

        bool clickedOwnedByLocalPlayer = IsOwnedByLocalPlayer(clicked);

        // Una entidad ajena (enemiga, neutral o aliada de otro jugador) es siempre
        // una selección individual. Si ya está seleccionada, Shift no puede añadir
        // ninguna otra entidad al conjunto.
        if (shift && HasNonOwnedSelection())
            return;

        // Una selección múltiple propia no puede mezclarse ni reemplazarse con
        // entidades neutrales, enemigas o pertenecientes a otro jugador aliado.
        if (HasLocalOwnedSelectionGroup() && !clickedOwnedByLocalPlayer)
            return;

        // Shift solo extiende una selección propia con otras entidades propias.
        if (shift && !clickedOwnedByLocalPlayer)
            return;

        bool isDoubleClick = clicked.UnitId == lastClickedUnitId && Time.unscaledTime - lastClickTime <= DoubleClickWindow;
        lastClickedUnitId = clicked.UnitId;
        lastClickTime = Time.unscaledTime;

        if (isDoubleClick && clickedOwnedByLocalPlayer)
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
        List<NetworkEntityView> inside = unitViews.Values
            .Where(IsOwnedByLocalPlayer)
            .Where(view => IsViewInsideScreenRect(view, selectionRect))
            .OrderBy(view => view.UnitId)
            .Take(MaxSelectedUnits)
            .ToList();

        if (!shift)
        {
            NetworkEntityView locked = GetLockedTargetForSelection();
            ClearSelection();
            if (locked != null)
                AddSelection(locked);
        }

        foreach (NetworkEntityView view in inside)
        {
            if (shift)
                ToggleSelection(view);
            else
                AddSelection(view);
        }
    }

    private void SelectSameTypeVisible(NetworkEntityView source, bool shift)
    {
        List<NetworkEntityView> sameType = unitViews.Values
            .Where(IsOwnedByLocalPlayer)
            .Where(view => view != null && view.HasAttribute(EntityAttributeIds.Selectable))
            .Where(view => string.Equals(view.UnitName, source.UnitName, StringComparison.OrdinalIgnoreCase))
            .Where(IsVisibleOnScreen)
            .OrderBy(view => view.UnitId)
            .Take(MaxSelectedUnits)
            .ToList();

        if (!shift)
            ClearSelection();

        foreach (NetworkEntityView view in sameType)
        {
            if (shift)
                ToggleSelection(view);
            else
                AddSelection(view);
        }
    }

    private void HandleClickMovement()
    {
        if (!GameInputReader.RightPressedThisFrame || SelectedEntities.Count == 0)
            return;

        if (!TryGetGroundPoint(GameInputReader.PointerPosition, out Vector3 destination))
            return;

        SendFormationMove(destination);
    }

    private void HandleDirectMovement()
    {
        if (SelectedEntities.Count == 0)
            return;

        Vector2 input = GameInputReader.Wasd;
        if (input.sqrMagnitude <= 0.01f)
            return;

        directInputTimer -= Time.deltaTime;
        if (directInputTimer > 0f)
            return;
        directInputTimer = DirectInputInterval;

        NetworkEntityView cameraTarget = GetThirdPersonCameraSelection();
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
        if (!GameInputReader.AltHeld || !GameInputReader.RPressedThisFrame)
            return;

        NetworkEntityView target = GetThirdPersonCameraSelection();
        if (target == null)
        {
            Debug.Log("[NetworkEntityCoordinator] La unidad seleccionada no posee el atributo heroic o camera.third-person.");
            return;
        }

        cameraController?.ToggleLock(target);
    }

    private NetworkEntityView GetThirdPersonCameraSelection()
    {
        return SelectedEntities.FirstOrDefault(unit =>
            IsOwnedByLocalPlayer(unit) &&
            (unit.HasAttribute(EntityAttributeIds.Heroic) ||
             unit.HasAttribute(EntityAttributeIds.ThirdPersonCamera)));
    }

    private void HandleControlGroups()
    {
        for (int group = 1; group <= 3; group++)
        {
            if (!GameInputReader.NumberPressedThisFrame(group))
                continue;

            if (GameInputReader.ControlHeld)
                StoreControlGroup(group);
            else
                RecallControlGroup(group);
        }
    }

    private void StoreControlGroup(int group)
    {
        int count = controlGroupService.Store(group, SelectedEntities);
        Debug.Log($"[NetworkEntityCoordinator] Grupo {group} guardado con {count} unidades.");
    }

    private void RecallControlGroup(int group)
    {
        IReadOnlyList<int> ids = controlGroupService.Recall(group);
        if (ids.Count == 0)
            return;

        ClearSelection();
        foreach (int id in ids)
        {
            if (unitViews.TryGetValue(id, out NetworkEntityView view) && IsOwnedByLocalPlayer(view))
                AddSelection(view);
        }
    }

    private void SendFormationMove(Vector3 center)
    {
        List<NetworkEntityView> controllable = SelectedEntities
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

    private NetworkEntityView GetSelectableEntityUnderCursor(Vector2 mousePosition)
    {
        Ray ray = gameplayCamera.ScreenPointToRay(mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f))
            return null;

        NetworkEntityView view = hit.collider.GetComponentInParent<NetworkEntityView>();
        return view != null && view.HasAttribute(EntityAttributeIds.Selectable) ? view : null;
    }

    private bool IsOwnedByLocalPlayer(NetworkEntityView view)
    {
        return view != null && Manager != null &&
               view.TeamId != 0 &&
               view.OwnerClientId == Manager.LocalClientId &&
               view.HasAttribute(EntityAttributeIds.Selectable);
    }

    private bool IsViewInsideScreenRect(NetworkEntityView view, Rect rect)
    {
        Vector3 screen = gameplayCamera.WorldToScreenPoint(view.transform.position);
        if (screen.z <= 0f)
            return false;
        screen.y = Screen.height - screen.y;
        return rect.Contains(screen);
    }

    private bool IsVisibleOnScreen(NetworkEntityView view)
    {
        Vector3 screen = gameplayCamera.WorldToViewportPoint(view.transform.position);
        return screen.z > 0f && screen.x >= 0f && screen.x <= 1f && screen.y >= 0f && screen.y <= 1f;
    }

    private bool HasLocalOwnedSelectionGroup()
    {
        return selectionService.HasOwnedGroup;
    }

    private bool HasNonOwnedSelection()
    {
        return selectionService.HasNonOwnedSelection;
    }

    private void ClearSelectionExceptLockedTarget()
    {
        selectionService.ClearExceptLockedTarget();
    }

    private void SetExclusiveSelection(NetworkEntityView view)
    {
        selectionService.SetExclusive(view);
    }

    private void ToggleSelection(NetworkEntityView view)
    {
        selectionService.Toggle(view);
    }

    private void AddSelection(NetworkEntityView view)
    {
        selectionService.Add(view);
    }

    private void RemoveSelection(NetworkEntityView view)
    {
        selectionService.Remove(view);
    }

    private void ClearSelection()
    {
        selectionService.Clear();
    }

    public IReadOnlyList<SelectionInspectionGroup> GetSelectionInspectionGroups()
    {
        return selectionService.GetInspectionGroups();
    }

    public IReadOnlyList<SelectionInspectionGroup> GetExtendedSelectionInspectionGroups()
    {
        return selectionService.GetExtendedInspectionGroups();
    }

    public SelectionInspectionGroup GetInspectedSelectionGroup()
    {
        return selectionService.GetInspectedGroup();
    }

    public void SetInspectedSelectionGroup(int index)
    {
        selectionService.SetInspectedGroup(index);
    }

    private void HandleSelectionInspectionCycle()
    {
        if (GameInputReader.TabPressedThisFrame)
            selectionService.CycleInspectedGroup();
    }

    private NetworkEntityView GetLockedTargetForSelection()
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
        EntityMoveCommand command = new()
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
        Manager.CustomMessagingManager.SendNamedMessage(EntityNetworkMessageNames.MoveCommand, NetworkManager.ServerClientId, writer);
    }

    private void HandleMoveCommand(ulong senderClientId, FastBufferReader reader)
    {
        if (!Manager.IsServer)
            return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        EntityMoveCommand command = JsonUtility.FromJson<EntityMoveCommand>(payload.ToString());
        ApplyMoveCommand(senderClientId, command);
    }

    private void ApplyMoveCommand(ulong senderClientId, EntityMoveCommand command)
    {
        if (EntityMovementService.TryApplyMove(serverUnits, senderClientId, command, out string rejectionReason))
            return;

        if (!string.IsNullOrWhiteSpace(rejectionReason))
            Debug.LogWarning($"[NetworkEntityCoordinator] {rejectionReason}");
    }

    private void BroadcastSnapshot()
    {
        if (!Manager.IsServer || Manager.CustomMessagingManager == null)
            return;

        EntitySnapshotPayload snapshot = new();
        foreach (EntityRuntimeState unit in serverUnits.Values.OrderBy(u => u.UnitId))
        {
            snapshot.Units.Add(new EntitySnapshotData
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
            EntityNetworkMessageNames.Snapshot,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void HandleSnapshot(ulong senderClientId, FastBufferReader reader)
    {
        if (Manager.IsServer)
            return;

        reader.ReadValueSafe(out FixedString4096Bytes payload);
        EntitySnapshotPayload snapshot = JsonUtility.FromJson<EntitySnapshotPayload>(payload.ToString());
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(EntitySnapshotPayload snapshot)
    {
        if (snapshot?.Units == null)
            return;

        HashSet<int> receivedIds = new();

        foreach (EntitySnapshotData state in snapshot.Units)
        {
            receivedIds.Add(state.UnitId);

            if (!unitViews.TryGetValue(state.UnitId, out NetworkEntityView view))
            {
                view = EntityViewFactory.Create(state);
                unitViews.Add(state.UnitId, view);
            }

            view.ApplyState(state);
        }

        foreach (int removedId in unitViews.Keys.Where(id => !receivedIds.Contains(id)).ToList())
        {
            NetworkEntityView removed = unitViews[removedId];
            RemoveSelection(removed);
            if (cameraController != null && cameraController.LockedTarget == removed)
                cameraController.Unlock();
            Destroy(removed.gameObject);
            unitViews.Remove(removedId);
        }
    }

    private void TryRegisterMessageHandlers()
    {
        if (handlersRegistered || Manager?.CustomMessagingManager == null)
            return;

        Manager.CustomMessagingManager.RegisterNamedMessageHandler(EntityNetworkMessageNames.Snapshot, HandleSnapshot);
        Manager.CustomMessagingManager.RegisterNamedMessageHandler(EntityNetworkMessageNames.MoveCommand, HandleMoveCommand);
        handlersRegistered = true;
    }

    private void UnregisterMessageHandlers()
    {
        if (!handlersRegistered || Manager?.CustomMessagingManager == null)
            return;

        Manager.CustomMessagingManager.UnregisterNamedMessageHandler(EntityNetworkMessageNames.Snapshot);
        Manager.CustomMessagingManager.UnregisterNamedMessageHandler(EntityNetworkMessageNames.MoveCommand);
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

}
