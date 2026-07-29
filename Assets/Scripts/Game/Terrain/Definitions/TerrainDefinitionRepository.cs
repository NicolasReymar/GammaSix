using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class TerrainDefinitionRepository
{
    private const string RootFolderName = "GameContent";
    private const string TerrainsFolderName = "Terrains";
    private static readonly Dictionary<string, TerrainDefinition> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string TerrainsPath => Path.Combine(Application.persistentDataPath, RootFolderName, TerrainsFolderName);

    public static void ClearCache() => Cache.Clear();

    public static void EnsureDefinitions()
    {
        Directory.CreateDirectory(TerrainsPath);
        string source = Path.Combine(Application.streamingAssetsPath, RootFolderName, TerrainsFolderName);
        if (Directory.Exists(source))
        {
            foreach (string sourceFile in Directory.GetFiles(source, "*.json"))
            {
                string target = Path.Combine(TerrainsPath, Path.GetFileName(sourceFile));
                if (!File.Exists(target))
                    File.Copy(sourceFile, target);
            }
        }

        Cache.Clear();
    }

    public static TerrainDefinition Load(string terrainId)
    {
        if (string.IsNullOrWhiteSpace(terrainId))
            return null;

        EnsureDefinitionsIfNeeded();
        ContentReference reference = ContentReference.Parse(terrainId);
        string cacheKey = reference.ToString();
        if (Cache.TryGetValue(cacheKey, out TerrainDefinition cached))
            return cached;

        if (reference.IsQualified && !reference.IsBase)
        {
            if (!PackageContentResolver.TryFindContentFile(
                    reference.ToString(),
                    PackageContentResolver.TerrainsFolderName,
                    out string packagedFile,
                    out InstalledGameContentPackage package))
            {
                Debug.LogError($"[TerrainDefinitionRepository] No existe el terreno empaquetado '{terrainId}'.");
                return null;
            }

            TerrainDefinition packaged = JsonUtility.FromJson<TerrainDefinition>(File.ReadAllText(packagedFile));
            Validate(packaged, packagedFile);
            packaged.id = ContentReference.Qualify(package.PackageId, packaged.id);
            Cache[cacheKey] = packaged;
            Cache[packaged.id] = packaged;
            return packaged;
        }

        string legacyId = reference.LocalId;
        string exact = Path.Combine(TerrainsPath, $"{legacyId}.json");
        if (File.Exists(exact))
            return Read(exact, cacheKey);

        foreach (string file in Directory.GetFiles(TerrainsPath, "*.json"))
        {
            TerrainDefinition candidate = JsonUtility.FromJson<TerrainDefinition>(File.ReadAllText(file));
            if (candidate != null && string.Equals(candidate.id, legacyId, StringComparison.OrdinalIgnoreCase))
            {
                Validate(candidate, file);
                Cache[cacheKey] = candidate;
                Cache[candidate.id] = candidate;
                return candidate;
            }
        }

        Debug.LogError($"[TerrainDefinitionRepository] No existe el terreno '{terrainId}'.");
        return null;
    }

    private static TerrainDefinition Read(string file, string requestedId)
    {
        TerrainDefinition definition = JsonUtility.FromJson<TerrainDefinition>(File.ReadAllText(file));
        Validate(definition, file);
        Cache[requestedId] = definition;
        Cache[definition.id] = definition;
        return definition;
    }

    private static void Validate(TerrainDefinition definition, string file)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.id))
            throw new InvalidDataException($"Definición de terreno inválida: {file}");
        if (definition.tileSize <= 0f)
            throw new InvalidDataException($"El terreno '{definition.id}' debe tener tileSize mayor que 0.");
        if (string.IsNullOrWhiteSpace(definition.category))
            throw new InvalidDataException($"El terreno '{definition.id}' debe declarar una categoría.");
    }

    private static void EnsureDefinitionsIfNeeded()
    {
        if (!Directory.Exists(TerrainsPath))
            EnsureDefinitions();
    }
}
