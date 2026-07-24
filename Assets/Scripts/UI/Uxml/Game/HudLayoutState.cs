using System;

/// <summary>
/// Estado compartido para activar o bloquear la edición de posición del HUD.
/// Una futura pantalla de opciones puede llamar a SetEditingUnlocked.
/// </summary>
public static class HudLayoutState
{
    public static event Action<bool> EditingUnlockedChanged;

    public static bool IsEditingUnlocked { get; private set; }

    public static void SetEditingUnlocked(bool unlocked)
    {
        if (IsEditingUnlocked == unlocked)
            return;

        IsEditingUnlocked = unlocked;
        EditingUnlockedChanged?.Invoke(unlocked);
    }
}
