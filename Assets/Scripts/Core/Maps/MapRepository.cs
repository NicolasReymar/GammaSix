using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class MapRepository
{
    private const string MapsFolderName = "Maps";

    public static string MapsFolderPath => Path.Combine(Application.persistentDataPath, MapsFolderName);

    public static IReadOnlyList<MapEntry> LoadSavedMaps()
    {
        string mapsFolderPath = MapsFolderPath;
        Debug.Log($"[MapRepository] Buscando mapas en: {mapsFolderPath}");

        if (!Directory.Exists(mapsFolderPath))
        {
            Directory.CreateDirectory(mapsFolderPath);
            Debug.Log($"[MapRepository] Carpeta de mapas creada en: {mapsFolderPath}");
        }

        List<MapEntry> maps = new();

        string[] files = Directory.GetFiles(mapsFolderPath, "*.json");

        foreach (string file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);

            maps.Add(new MapEntry(
                scenarioId: fileName,
                displayName: fileName,
                filePath: file
            ));
        }

        return maps;
    }
}