using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class EntityDefinitionRepository
{
    private const string RootFolderName = "GameContent";
    private const string EntitiesFolderName = "Entities";
    private static readonly Dictionary<string, EntityDefinition> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string EntitiesPath => Path.Combine(Application.persistentDataPath, RootFolderName, EntitiesFolderName);

    public static void EnsureDefinitions()
    {
        Directory.CreateDirectory(EntitiesPath);
        string source = Path.Combine(Application.streamingAssetsPath, RootFolderName, EntitiesFolderName);
        if (Directory.Exists(source))
        {
            foreach (string sourceFile in Directory.GetFiles(source, "*.json"))
            {
                string target = Path.Combine(EntitiesPath, Path.GetFileName(sourceFile));
                if (!File.Exists(target))
                    File.Copy(sourceFile, target);
            }
        }
        Cache.Clear();
    }

    public static EntityDefinition Load(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return null;

        EnsureDefinitionsIfNeeded();
        if (Cache.TryGetValue(entityId, out EntityDefinition cached))
            return cached;

        string exact = Path.Combine(EntitiesPath, $"{entityId}.json");
        if (File.Exists(exact))
            return Read(exact, entityId);

        foreach (string file in Directory.GetFiles(EntitiesPath, "*.json"))
        {
            EntityDefinition candidate = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
            if (candidate != null && string.Equals(candidate.id, entityId, StringComparison.OrdinalIgnoreCase))
            {
                Validate(candidate, file);
                Cache[entityId] = candidate;
                return candidate;
            }
        }

        Debug.LogError($"[EntityDefinitionRepository] No existe la definición '{entityId}' en {EntitiesPath}.");
        return null;
    }

    private static EntityDefinition Read(string file, string requestedId)
    {
        EntityDefinition definition = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
        Validate(definition, file);
        Cache[requestedId] = definition;
        return definition;
    }

    private static void Validate(EntityDefinition definition, string file)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.id))
            throw new InvalidDataException($"Definición inválida: {file}");
        if (!string.Equals(definition.kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(definition.kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"La entidad '{definition.id}' tiene kind inválido: '{definition.kind}'.");
    }

    private static void EnsureDefinitionsIfNeeded()
    {
        if (!Directory.Exists(EntitiesPath))
            EnsureDefinitions();
    }
}
