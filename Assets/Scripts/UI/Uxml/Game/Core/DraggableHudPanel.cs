using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Panel arrastrable del HUD. La restauración se repite durante varios frames
/// hasta que UI Toolkit termina de aplicar UXML/USS y la geometría queda estable.
/// </summary>
public sealed class DraggableHudPanel
{
    private const int MaxRestoreAttempts = 30;

    private readonly VisualElement root;
    private readonly VisualElement panel;
    private readonly string preferenceKey;

    private bool dragging;
    private int pointerId = -1;
    private Vector2 pointerOffset;
    private Vector2 defaultPosition;
    private bool hasDefaultPosition;
    private bool initialized;
    private bool editingUnlocked;
    private int restoreAttempts;

    public DraggableHudPanel(VisualElement root, VisualElement panel, string preferenceKey)
    {
        this.root = root;
        this.panel = panel;
        this.preferenceKey = preferenceKey;
        editingUnlocked = HudInteractionService.IsEditingUnlocked;

        panel.RegisterCallback<PointerDownEvent>(OnPointerDown);
        panel.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        panel.RegisterCallback<PointerUpEvent>(OnPointerUp);
        panel.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        panel.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        HudInteractionService.EditingUnlockedChanged += SetEditingUnlocked;
        GameUiModalService.ModalStateChanged += OnModalStateChanged;
        HudInteractionService.RegisterPanel(panel);
        HudLayoutPersistenceService.Register(preferenceKey, this);

        ScheduleRestoreAttempt();
        UpdateEditingVisualState();
    }

    public void Dispose()
    {
        EndDrag();
        panel.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        panel.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        panel.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        panel.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        panel.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        HudInteractionService.EditingUnlockedChanged -= SetEditingUnlocked;
        GameUiModalService.ModalStateChanged -= OnModalStateChanged;
        HudInteractionService.UnregisterPanel(panel);
        HudLayoutPersistenceService.Unregister(preferenceKey, this);
    }

    public void SetEditingUnlocked(bool unlocked)
    {
        editingUnlocked = unlocked;
        if (!editingUnlocked)
            EndDrag();

        UpdateEditingVisualState();
    }

    /// <summary>
    /// Devuelve el panel a la posición definida por su UXML/USS, sin guardar una
    /// nueva coordenada personalizada.
    /// </summary>
    public bool RestoreDefaultPosition()
    {
        EndDrag();
        if (!EnsureInitialized() || !hasDefaultPosition)
            return false;

        return SetClampedPosition(defaultPosition);
    }

    private void OnModalStateChanged(bool isOpen)
    {
        if (isOpen)
            EndDrag();
    }

    private void UpdateEditingVisualState()
    {
        panel.EnableInClassList("hud-panel-editing-unlocked", editingUnlocked);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || !editingUnlocked || GameUiModalService.BlocksGameplayInput)
            return;

        if (!EnsureInitialized())
            return;

        dragging = true;
        HudInteractionService.BeginDrag();
        pointerId = evt.pointerId;
        panel.CapturePointer(pointerId);

        Vector2 pointerInRoot = root.WorldToLocal(new Vector2(evt.position.x, evt.position.y));
        pointerOffset = pointerInRoot - CurrentPosition;
        evt.StopImmediatePropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!dragging || evt.pointerId != pointerId || !panel.HasPointerCapture(pointerId))
            return;

        Vector2 pointerInRoot = root.WorldToLocal(new Vector2(evt.position.x, evt.position.y));
        SetClampedPosition(pointerInRoot - pointerOffset);
        evt.StopImmediatePropagation();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (!dragging || evt.pointerId != pointerId)
            return;

        EndDrag();
        evt.StopImmediatePropagation();
    }

    private void OnPointerCancel(PointerCancelEvent evt)
    {
        if (dragging && evt.pointerId == pointerId)
            EndDrag();
    }

    private void EndDrag()
    {
        bool wasDragging = dragging;
        if (pointerId >= 0 && panel.HasPointerCapture(pointerId))
            panel.ReleasePointer(pointerId);

        dragging = false;
        pointerId = -1;

        if (wasDragging)
            HudInteractionService.EndDrag();
    }

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        if (!initialized)
        {
            ScheduleRestoreAttempt();
            return;
        }

        if (!dragging)
            ClampCurrentPosition();
    }

    private void ScheduleRestoreAttempt()
    {
        if (initialized || restoreAttempts >= MaxRestoreAttempts || panel.panel == null)
            return;

        panel.schedule.Execute(_ =>
        {
            if (initialized)
                return;

            restoreAttempts++;
            if (!TryInitialize())
                ScheduleRestoreAttempt();
        }).ExecuteLater(1);
    }

    private bool TryInitialize()
    {
        if (initialized)
            return true;

        if (!HasValidGeometry())
            return false;

        // La posición por defecto debe capturarse antes de cambiar right/bottom a auto.
        // De lo contrario, los paneles definidos desde el borde derecho o inferior en USS
        // saltan a (0, 0) antes de que podamos conservar su layout inicial.
        Rect defaultWorldBounds = panel.worldBound;
        Vector2 defaultLocalPosition = root.WorldToLocal(defaultWorldBounds.position);
        defaultPosition = defaultLocalPosition;
        hasDefaultPosition = true;

        Vector2 normalized;
        bool hasSavedPosition = HudLayoutPersistenceService.TryGetSavedNormalizedPosition(
            preferenceKey,
            out normalized);

        if (hasSavedPosition)
        {
            Vector2 restored = NormalizedToPosition(normalized);
            if (!SetClampedPosition(restored))
                return false;
        }
        else
        {
            if (!SetClampedPosition(defaultLocalPosition))
                return false;
        }

        initialized = true;
        Debug.Log(
            hasSavedPosition
                ? "[DraggableHudPanel] Posición restaurada para '" + preferenceKey +
                  "' desde coordenadas normalizadas " + normalized + "."
                : "[DraggableHudPanel] '" + preferenceKey + "' usa su posición por defecto.");
        return true;
    }

    private bool EnsureInitialized()
    {
        return initialized || TryInitialize();
    }

    private bool HasValidGeometry()
    {
        return IsPositiveFinite(root.resolvedStyle.width)
               && IsPositiveFinite(root.resolvedStyle.height)
               && IsPositiveFinite(panel.resolvedStyle.width)
               && IsPositiveFinite(panel.resolvedStyle.height);
    }

    private void ForceAbsoluteLeftTop()
    {
        panel.style.position = Position.Absolute;
        panel.style.right = StyleKeyword.Auto;
        panel.style.bottom = StyleKeyword.Auto;
    }

    private Vector2 CurrentPosition
    {
        get { return new Vector2(panel.resolvedStyle.left, panel.resolvedStyle.top); }
    }

    private void ClampCurrentPosition()
    {
        if (!initialized || dragging)
            return;

        SetClampedPosition(CurrentPosition);
    }

    private bool SetClampedPosition(Vector2 desiredPosition)
    {
        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;
        float panelWidth = panel.resolvedStyle.width;
        float panelHeight = panel.resolvedStyle.height;

        if (!IsPositiveFinite(rootWidth)
            || !IsPositiveFinite(rootHeight)
            || !IsPositiveFinite(panelWidth)
            || !IsPositiveFinite(panelHeight)
            || !IsFinite(desiredPosition.x)
            || !IsFinite(desiredPosition.y))
        {
            return false;
        }

        float minX = -panelWidth * 0.5f;
        float maxX = rootWidth - panelWidth * 0.5f;
        float minY = -panelHeight * 0.5f;
        float maxY = rootHeight - panelHeight * 0.5f;

        ForceAbsoluteLeftTop();
        panel.style.left = Mathf.Clamp(desiredPosition.x, minX, maxX);
        panel.style.top = Mathf.Clamp(desiredPosition.y, minY, maxY);
        return true;
    }

    public bool TryCaptureNormalizedPosition(out Vector2 normalizedPosition)
    {
        normalizedPosition = default(Vector2);

        if (panel == null || panel.panel == null || !EnsureInitialized() || !HasValidGeometry())
            return false;

        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;
        float panelWidth = panel.resolvedStyle.width;
        float panelHeight = panel.resolvedStyle.height;

        float minX = -panelWidth * 0.5f;
        float maxX = rootWidth - panelWidth * 0.5f;
        float minY = -panelHeight * 0.5f;
        float maxY = rootHeight - panelHeight * 0.5f;

        float xRange = maxX - minX;
        float yRange = maxY - minY;
        if (!IsPositiveFinite(xRange) || !IsPositiveFinite(yRange))
            return false;

        Vector2 position = CurrentPosition;
        normalizedPosition = new Vector2(
            Mathf.Clamp01((position.x - minX) / xRange),
            Mathf.Clamp01((position.y - minY) / yRange));

        Debug.Log(
            "[DraggableHudPanel] Captura '" + preferenceKey + "': px=" + position +
            ", normalizada=" + normalizedPosition + ".");
        return true;
    }

    private Vector2 NormalizedToPosition(Vector2 normalized)
    {
        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;
        float panelWidth = panel.resolvedStyle.width;
        float panelHeight = panel.resolvedStyle.height;

        float minX = -panelWidth * 0.5f;
        float maxX = rootWidth - panelWidth * 0.5f;
        float minY = -panelHeight * 0.5f;
        float maxY = rootHeight - panelHeight * 0.5f;

        return new Vector2(
            Mathf.Lerp(minX, maxX, Mathf.Clamp01(normalized.x)),
            Mathf.Lerp(minY, maxY, Mathf.Clamp01(normalized.y)));
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && IsFinite(value);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
