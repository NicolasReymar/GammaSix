using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Registro común de las superficies visibles del HUD. Permite que los sistemas
/// de juego ignoren clics iniciados sobre UI Toolkit, incluso cuando el panel
/// está desbloqueado para ser arrastrado.
/// </summary>
public static class HudPointerGuard
{
    private static readonly HashSet<VisualElement> Panels = new();

    public static void Register(VisualElement panel)
    {
        if (panel != null)
            Panels.Add(panel);
    }

    public static void Unregister(VisualElement panel)
    {
        if (panel != null)
            Panels.Remove(panel);
    }

    public static bool IsPointerOverHud(Vector2 screenPosition)
    {
        // Input.mousePosition usa origen inferior izquierdo. UI Toolkit utiliza
        // origen superior izquierdo para worldBound.
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
