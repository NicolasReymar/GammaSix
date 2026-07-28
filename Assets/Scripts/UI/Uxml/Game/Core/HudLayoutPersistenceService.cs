using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Persistencia del layout del HUD en un JSON dentro de persistentDataPath.
/// Guarda coordenadas normalizadas para que el layout sobreviva reinicios,
/// cambios de resolución y recreaciones de UIDocument.
/// </summary>
public static class HudLayoutPersistenceService
{
    [Serializable]
    private sealed class HudLayoutFile
    {
        public int version = CurrentVersion;
        public List<HudPanelLayout> panels = new List<HudPanelLayout>();
    }

    [Serializable]
    private sealed class HudPanelLayout
    {
        public string key;
        public float normalizedX;
        public float normalizedY;
    }

    private const int CurrentVersion = 2;
    private const string SettingsDirectoryName = "Settings";
    private const string LayoutFileName = "hud-layout.json";

    private static readonly Dictionary<string, DraggableHudPanel> Panels =
        new Dictionary<string, DraggableHudPanel>(StringComparer.Ordinal);

    private static readonly Dictionary<string, HudPanelLayout> SavedLayouts =
        new Dictionary<string, HudPanelLayout>(StringComparer.Ordinal);

    private static bool storageLoaded;

    public static int RegisteredPanelCount { get { return Panels.Count; } }

    public static string LayoutFilePath
    {
        get
        {
            return Path.Combine(
                Application.persistentDataPath,
                SettingsDirectoryName,
                LayoutFileName);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        Panels.Clear();
        SavedLayouts.Clear();
        storageLoaded = false;
    }

    public static void Register(string key, DraggableHudPanel panel)
    {
        if (string.IsNullOrWhiteSpace(key) || panel == null)
            return;

        EnsureStorageLoaded();
        Panels[key] = panel;
    }

    public static void Unregister(string key, DraggableHudPanel panel)
    {
        if (string.IsNullOrWhiteSpace(key) || panel == null)
            return;

        DraggableHudPanel registered;
        if (Panels.TryGetValue(key, out registered) && ReferenceEquals(registered, panel))
            Panels.Remove(key);
    }

    public static bool TryGetSavedNormalizedPosition(string key, out Vector2 normalizedPosition)
    {
        normalizedPosition = default(Vector2);
        if (string.IsNullOrWhiteSpace(key))
            return false;

        EnsureStorageLoaded();

        HudPanelLayout saved;
        if (!SavedLayouts.TryGetValue(key, out saved) || saved == null)
            return false;

        if (!IsFinite(saved.normalizedX) || !IsFinite(saved.normalizedY))
            return false;

        normalizedPosition = new Vector2(
            Mathf.Clamp01(saved.normalizedX),
            Mathf.Clamp01(saved.normalizedY));
        return true;
    }

    public static int SaveAllRegisteredPositions()
    {
        int savedCount = WriteAllRegisteredPositions();
        Debug.Log(
            "[HudLayoutPersistence] Se guardaron " + savedCount +
            " posiciones en '" + LayoutFilePath + "'.");
        return savedCount;
    }

    /// <summary>
    /// Elimina la distribución persistida y devuelve los paneles activos a sus
    /// posiciones definidas por UXML/USS. Fuera de una partida solo elimina el
    /// archivo, por lo que los valores por defecto se aplican al iniciar después.
    /// </summary>
    public static int ResetToDefaults()
    {
        EnsureStorageLoaded();
        SavedLayouts.Clear();

        if (File.Exists(LayoutFilePath))
            File.Delete(LayoutFilePath);

        int resetCount = 0;
        foreach (KeyValuePair<string, DraggableHudPanel> entry in Panels.ToArray())
        {
            DraggableHudPanel panel = entry.Value;
            if (panel != null && panel.RestoreDefaultPosition())
                resetCount++;
        }

        Debug.Log(
            "[HudLayoutPersistence] Layout restablecido. Paneles activos: " +
            resetCount + ". Archivo eliminado: '" + LayoutFilePath + "'.");
        return resetCount;
    }

    public static int WriteAllRegisteredPositions()
    {
        EnsureStorageLoaded();

        int savedCount = 0;
        foreach (KeyValuePair<string, DraggableHudPanel> entry in Panels.ToArray())
        {
            DraggableHudPanel panel = entry.Value;
            Vector2 normalizedPosition;
            if (panel == null || !panel.TryCaptureNormalizedPosition(out normalizedPosition))
                continue;

            SavedLayouts[entry.Key] = new HudPanelLayout
            {
                key = entry.Key,
                normalizedX = normalizedPosition.x,
                normalizedY = normalizedPosition.y
            };
            savedCount++;
        }

        WriteStorageFile();
        return savedCount;
    }

    private static void EnsureStorageLoaded()
    {
        if (storageLoaded)
            return;

        storageLoaded = true;
        SavedLayouts.Clear();
        TryLoadStorageFile();
    }

    private static bool TryLoadStorageFile()
    {
        try
        {
            if (!File.Exists(LayoutFilePath))
            {
                Debug.Log("[HudLayoutPersistence] No existe layout previo en '" + LayoutFilePath + "'.");
                return false;
            }

            string json = File.ReadAllText(LayoutFilePath, Encoding.UTF8);
            HudLayoutFile file = JsonUtility.FromJson<HudLayoutFile>(json);
            if (file == null || file.panels == null)
                return false;

            foreach (HudPanelLayout panel in file.panels)
            {
                if (panel == null || string.IsNullOrWhiteSpace(panel.key))
                    continue;

                if (!IsFinite(panel.normalizedX) || !IsFinite(panel.normalizedY))
                    continue;

                SavedLayouts[panel.key] = panel;
            }

            Debug.Log(
                "[HudLayoutPersistence] Layout cargado: " + SavedLayouts.Count +
                " paneles desde '" + LayoutFilePath + "'.");
            return SavedLayouts.Count > 0;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[HudLayoutPersistence] No se pudo leer '" + LayoutFilePath + "': " + exception);
            return false;
        }
    }

    private static void WriteStorageFile()
    {
        string directory = Path.GetDirectoryName(LayoutFilePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException("No se pudo resolver el directorio del layout del HUD.");

        Directory.CreateDirectory(directory);

        HudLayoutFile file = new HudLayoutFile
        {
            version = CurrentVersion,
            panels = SavedLayouts.Values
                .Where(panel => panel != null && !string.IsNullOrWhiteSpace(panel.key))
                .OrderBy(panel => panel.key, StringComparer.Ordinal)
                .ToList()
        };

        string json = JsonUtility.ToJson(file, true);
        File.WriteAllText(LayoutFilePath, json, new UTF8Encoding(false));

        if (!File.Exists(LayoutFilePath))
            throw new IOException("El archivo de layout no existe después de escribirlo.");

        Debug.Log(
            "[HudLayoutPersistence] Archivo escrito correctamente: '" +
            LayoutFilePath + "' (" + new FileInfo(LayoutFilePath).Length + " bytes).");
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
