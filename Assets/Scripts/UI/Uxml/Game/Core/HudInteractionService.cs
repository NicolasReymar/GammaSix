using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Estado compartido de interacción del HUD. Centraliza modo de edición,
/// paneles registrados y arrastre activo para que el gameplay pueda bloquear
/// selección sin conocer los detalles de UI Toolkit.
/// </summary>
public static class HudInteractionService
{
    private static readonly HashSet<VisualElement> Panels = new();
    private static int activeDragCount;

    public static event Action<bool> EditingUnlockedChanged;
    public static event Action<bool> DraggingChanged;

    public static bool IsEditingUnlocked { get; private set; }
    public static bool IsDragging => activeDragCount > 0;

    public static void SetEditingUnlocked(bool unlocked)
    {
        if (IsEditingUnlocked == unlocked)
            return;

        IsEditingUnlocked = unlocked;
        EditingUnlockedChanged?.Invoke(unlocked);
    }

    public static void RegisterPanel(VisualElement panel)
    {
        if (panel != null)
            Panels.Add(panel);
    }

    public static void UnregisterPanel(VisualElement panel)
    {
        if (panel != null)
            Panels.Remove(panel);
    }

    public static void BeginDrag()
    {
        bool wasDragging = IsDragging;
        activeDragCount++;
        if (!wasDragging)
            DraggingChanged?.Invoke(true);
    }

    public static void EndDrag()
    {
        if (activeDragCount <= 0)
            return;

        activeDragCount--;
        if (!IsDragging)
            DraggingChanged?.Invoke(false);
    }

    public static void ResetDragState()
    {
        if (!IsDragging)
            return;
        activeDragCount = 0;
        DraggingChanged?.Invoke(false);
    }

    public static bool IsPointerOverHud(Vector2 screenPosition)
    {
        Vector2 uiPosition = new(screenPosition.x, Screen.height - screenPosition.y);

        Panels.RemoveWhere(panel => panel == null || panel.panel == null);
        foreach (VisualElement panel in Panels)
        {
            if (panel.resolvedStyle.display == DisplayStyle.None ||
                panel.resolvedStyle.visibility == Visibility.Hidden)
                continue;

            if (panel.worldBound.Contains(uiPosition))
                return true;
        }

        return false;
    }
}
