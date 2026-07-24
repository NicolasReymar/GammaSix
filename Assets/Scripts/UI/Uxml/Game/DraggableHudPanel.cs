using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Permite arrastrar un panel con clic izquierdo cuando la edición del HUD está desbloqueada.
/// Al menos la mitad del panel permanece visible dentro del HUD.
/// La posición se guarda en PlayerPrefs por panel.
/// </summary>
public sealed class DraggableHudPanel
{
    private readonly VisualElement root;
    private readonly VisualElement panel;
    private readonly string preferenceKey;

    private bool dragging;
    private int pointerId = -1;
    private Vector2 pointerOffset;
    private bool initialized;
    private bool editingUnlocked;

    public DraggableHudPanel(VisualElement root, VisualElement panel, string preferenceKey)
    {
        this.root = root;
        this.panel = panel;
        this.preferenceKey = preferenceKey;
        editingUnlocked = HudLayoutState.IsEditingUnlocked;

        panel.RegisterCallback<PointerDownEvent>(OnPointerDown);
        panel.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        panel.RegisterCallback<PointerUpEvent>(OnPointerUp);
        panel.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        panel.RegisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        HudLayoutState.EditingUnlockedChanged += SetEditingUnlocked;

        UpdateEditingVisualState();
    }

    public void Dispose()
    {
        EndDrag();
        panel.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        panel.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        panel.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        panel.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        panel.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        HudLayoutState.EditingUnlockedChanged -= SetEditingUnlocked;
    }

    public void SetEditingUnlocked(bool unlocked)
    {
        editingUnlocked = unlocked;
        if (!editingUnlocked)
            EndDrag();

        UpdateEditingVisualState();
    }

    private void UpdateEditingVisualState()
    {
        if (editingUnlocked)
            panel.AddToClassList("hud-panel-editing-unlocked");
        else
            panel.RemoveFromClassList("hud-panel-editing-unlocked");
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || !editingUnlocked)
            return;

        ConvertAnchorsToLeftTop();

        dragging = true;
        pointerId = evt.pointerId;
        panel.CapturePointer(pointerId);

        Vector2 pointerInRoot = root.WorldToLocal(new Vector2(evt.position.x, evt.position.y));
        pointerOffset = pointerInRoot - new Vector2(panel.resolvedStyle.left, panel.resolvedStyle.top);

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
        SavePosition();
        evt.StopImmediatePropagation();
    }

    private void OnPointerCancel(PointerCancelEvent evt)
    {
        if (dragging && evt.pointerId == pointerId)
            EndDrag();
    }

    private void EndDrag()
    {
        if (pointerId >= 0 && panel.HasPointerCapture(pointerId))
            panel.ReleasePointer(pointerId);

        dragging = false;
        pointerId = -1;
    }

    private void OnRootGeometryChanged(GeometryChangedEvent evt)
    {
        TryInitialize();
        ClampCurrentPosition();
    }

    private void OnPanelGeometryChanged(GeometryChangedEvent evt)
    {
        TryInitialize();
        ClampCurrentPosition();
    }

    private void TryInitialize()
    {
        if (initialized || root.resolvedStyle.width <= 0f || panel.resolvedStyle.width <= 0f)
            return;

        initialized = true;
        ConvertAnchorsToLeftTop();

        if (PlayerPrefs.HasKey($"{preferenceKey}.x") && PlayerPrefs.HasKey($"{preferenceKey}.y"))
        {
            Vector2 savedPosition = new(
                PlayerPrefs.GetFloat($"{preferenceKey}.x"),
                PlayerPrefs.GetFloat($"{preferenceKey}.y"));
            SetClampedPosition(savedPosition);
        }
        else
        {
            ClampCurrentPosition();
        }
    }

    private void ConvertAnchorsToLeftTop()
    {
        Rect worldBounds = panel.worldBound;
        Vector2 localPosition = root.WorldToLocal(worldBounds.position);

        panel.style.left = localPosition.x;
        panel.style.top = localPosition.y;
        panel.style.right = StyleKeyword.Auto;
        panel.style.bottom = StyleKeyword.Auto;
    }

    private void ClampCurrentPosition()
    {
        if (!initialized || dragging)
            return;

        SetClampedPosition(new Vector2(panel.resolvedStyle.left, panel.resolvedStyle.top));
    }

    private void SetClampedPosition(Vector2 desiredPosition)
    {
        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;
        float panelWidth = panel.resolvedStyle.width;
        float panelHeight = panel.resolvedStyle.height;

        if (rootWidth <= 0f || rootHeight <= 0f || panelWidth <= 0f || panelHeight <= 0f)
            return;

        float minX = -panelWidth * 0.5f;
        float maxX = rootWidth - panelWidth * 0.5f;
        float minY = -panelHeight * 0.5f;
        float maxY = rootHeight - panelHeight * 0.5f;

        panel.style.left = Mathf.Clamp(desiredPosition.x, minX, maxX);
        panel.style.top = Mathf.Clamp(desiredPosition.y, minY, maxY);
        panel.style.right = StyleKeyword.Auto;
        panel.style.bottom = StyleKeyword.Auto;
    }

    private void SavePosition()
    {
        PlayerPrefs.SetFloat($"{preferenceKey}.x", panel.resolvedStyle.left);
        PlayerPrefs.SetFloat($"{preferenceKey}.y", panel.resolvedStyle.top);
        PlayerPrefs.Save();
    }
}
