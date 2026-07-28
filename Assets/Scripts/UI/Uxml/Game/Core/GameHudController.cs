using UnityEngine;

/// <summary>
/// Punto de entrada del HUD de partida. Cada módulo posee su propio UIDocument,
/// UXML, USS y controlador para que pueda modificarse de forma independiente.
/// </summary>
public class GameHudController : MonoBehaviour
{
    public static GameHudController Instance { get; private set; }

    [Header("Edición del HUD")]
    [SerializeField] private bool hudEditingUnlocked = true;

    public bool HudEditingUnlocked => HudInteractionService.IsEditingUnlocked;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HudInteractionService.SetEditingUnlocked(hudEditingUnlocked);

        CreateModule<GameGoldHudController>("HUD - Oro");
        CreateModule<SelectedEntityHudController>("HUD - Entidad seleccionada");
        CreateModule<SelectedEntitiesExtendedHudController>("HUD - Selección extendida");
        CreateModule<GamePauseMenuController>("HUD - Menú de partida");
    }

    private void OnDestroy()
    {
        HudInteractionService.ResetDragState();
        GameUiModalService.Reset();
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Puede conectarse directamente a un Toggle del menú de opciones.
    /// true: los paneles se mueven con clic izquierdo y arrastre.
    /// false: los paneles quedan bloqueados para uso normal del HUD.
    /// </summary>
    public void SetHudEditingUnlocked(bool unlocked)
    {
        hudEditingUnlocked = unlocked;
        HudInteractionService.SetEditingUnlocked(unlocked);
    }

    public void ToggleHudEditing()
    {
        SetHudEditingUnlocked(!HudInteractionService.IsEditingUnlocked);
    }

    private void CreateModule<T>(string objectName) where T : Component
    {
        GameObject moduleObject = new(objectName);
        moduleObject.transform.SetParent(transform, false);
        moduleObject.AddComponent<T>();
    }
}
