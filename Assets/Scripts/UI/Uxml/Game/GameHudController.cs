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

    public bool HudEditingUnlocked => HudLayoutState.IsEditingUnlocked;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HudLayoutState.SetEditingUnlocked(hudEditingUnlocked);

        CreateModule<GameGoldHudController>("HUD - Oro");
        CreateModule<SelectedEntityHudController>("HUD - Entidad seleccionada");
        CreateModule<SelectedEntitiesExtendedHudController>("HUD - Selección extendida");
    }

    private void OnDestroy()
    {
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
        HudLayoutState.SetEditingUnlocked(unlocked);
    }

    public void ToggleHudEditing()
    {
        SetHudEditingUnlocked(!HudLayoutState.IsEditingUnlocked);
    }

    private void CreateModule<T>(string objectName) where T : Component
    {
        GameObject moduleObject = new(objectName);
        moduleObject.transform.SetParent(transform, false);
        moduleObject.AddComponent<T>();
    }
}
