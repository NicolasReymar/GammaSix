using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Construye documentos UI Toolkit runtime usando una plantilla PanelSettings
/// que incluye el Theme Style Sheet del proyecto.
/// </summary>
public static class HudDocumentFactory
{
    private const string PanelSettingsResourcePath = "UI/GameHud/GameHudPanelSettings";

    public static UIDocument Create(GameObject owner, VisualTreeAsset visualTree, int sortingOrder, out PanelSettings panelSettings)
    {
        PanelSettings template = Resources.Load<PanelSettings>(PanelSettingsResourcePath);
        if (template == null)
        {
            Debug.LogError($"[HudDocumentFactory] No se encontró {PanelSettingsResourcePath}.asset en Resources.");
            panelSettings = null;
            return null;
        }

        // Cada módulo recibe una instancia propia, pero conserva el Theme Style Sheet.
        panelSettings = Object.Instantiate(template);
        panelSettings.name = $"{owner.name} PanelSettings (Runtime)";
        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.referenceResolution = new Vector2Int(1920, 1024);
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.match = 0.5f;

        UIDocument document = owner.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.visualTreeAsset = visualTree;
        document.sortingOrder = sortingOrder;
        return document;
    }
}
