using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registro compartido de ventanas modales dentro de la partida.
/// Mientras exista al menos una ventana abierta, la cámara, selección y órdenes
/// locales deben dejar de procesar input de gameplay.
/// </summary>
public static class GameUiModalService
{
    private static readonly HashSet<object> Owners = new();

    public static event Action<bool> ModalStateChanged;

    private static int blockGameplayThroughFrame = -1;

    public static bool IsModalOpen => Owners.Count > 0;

    /// <summary>
    /// Incluye un frame de protección al cerrar una ventana, evitando que el
    /// clic usado en un botón atraviese el menú y seleccione u ordene en el mapa.
    /// </summary>
    public static bool BlocksGameplayInput => IsModalOpen || Time.frameCount <= blockGameplayThroughFrame;

    public static void SetOpen(object owner, bool open)
    {
        if (owner == null)
            return;

        bool wasOpen = IsModalOpen;
        if (open)
            Owners.Add(owner);
        else
            Owners.Remove(owner);

        if (wasOpen && !IsModalOpen)
            blockGameplayThroughFrame = Time.frameCount + 1;

        if (wasOpen != IsModalOpen)
            ModalStateChanged?.Invoke(IsModalOpen);
    }

    public static void Release(object owner)
    {
        SetOpen(owner, false);
    }

    public static void Reset()
    {
        if (!IsModalOpen)
            return;

        Owners.Clear();
        blockGameplayThroughFrame = Time.frameCount;
        ModalStateChanged?.Invoke(false);
    }
}
