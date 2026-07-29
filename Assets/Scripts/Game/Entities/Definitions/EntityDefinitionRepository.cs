using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class EntityDefinitionRepository
{
    private const string RootFolderName = "GameContent";
    private const string EntitiesFolderName = "Entities";
    private static readonly Dictionary<string, EntityDefinition> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string EntitiesPath => Path.Combine(Application.persistentDataPath, RootFolderName, EntitiesFolderName);

    public static void ClearCache() => Cache.Clear();

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
                {
                    File.Copy(sourceFile, target);
                    continue;
                }

                TryMigrateBuiltInLifeDefinition(sourceFile, target);
            }
        }
        Cache.Clear();
    }

    public static EntityDefinition Load(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return null;

        EnsureDefinitionsIfNeeded();
        ContentReference reference = ContentReference.Parse(entityId);
        string cacheKey = reference.ToString();
        if (Cache.TryGetValue(cacheKey, out EntityDefinition cached))
            return cached;

        if (reference.IsQualified && !reference.IsBase)
        {
            if (!PackageContentResolver.TryFindContentFile(
                    reference.ToString(),
                    PackageContentResolver.EntitiesFolderName,
                    out string packagedFile,
                    out InstalledGameContentPackage package))
            {
                Debug.LogError($"[EntityDefinitionRepository] No existe la definición empaquetada '{entityId}'.");
                return null;
            }

            EntityDefinition packaged = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(packagedFile));
            Validate(packaged, packagedFile);
            packaged.id = ContentReference.Qualify(package.PackageId, packaged.id);
            ResolvePackagedEntityReferences(packaged, package.PackageId);
            Cache[cacheKey] = packaged;
            Cache[packaged.id] = packaged;
            return packaged;
        }

        string legacyId = reference.IsBase ? reference.LocalId : reference.LocalId;
        string exact = Path.Combine(EntitiesPath, $"{legacyId}.json");
        if (File.Exists(exact))
            return Read(exact, cacheKey);

        foreach (string file in Directory.GetFiles(EntitiesPath, "*.json"))
        {
            EntityDefinition candidate = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
            if (candidate != null && string.Equals(candidate.id, legacyId, StringComparison.OrdinalIgnoreCase))
            {
                Validate(candidate, file);
                Cache[cacheKey] = candidate;
                Cache[candidate.id] = candidate;
                return candidate;
            }
        }

        Debug.LogError($"[EntityDefinitionRepository] No existe la definición '{entityId}'.");
        return null;
    }

    public static IReadOnlyList<EntityDefinition> LoadAll()
    {
        EnsureDefinitionsIfNeeded();
        Cache.Clear();
        List<EntityDefinition> definitions = new();

        foreach (string file in Directory.GetFiles(EntitiesPath, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                EntityDefinition definition = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
                Validate(definition, file);
                definitions.Add(definition);
                Cache[definition.id] = definition;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EntityDefinitionRepository] No se pudo cargar '{file}': {exception.Message}");
            }
        }

        foreach (InstalledGameContentPackage package in PackageContentResolver.GetInstalledPackages())
        {
            foreach (string file in PackageContentResolver.EnumerateContentFiles(package, PackageContentResolver.EntitiesFolderName))
            {
                try
                {
                    EntityDefinition definition = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
                    Validate(definition, file);
                    definition.id = ContentReference.Qualify(package.PackageId, definition.id);
                    ResolvePackagedEntityReferences(definition, package.PackageId);
                    definitions.Add(definition);
                    Cache[definition.id] = definition;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[EntityDefinitionRepository] No se pudo cargar '{file}': {exception.Message}");
                }
            }
        }

        return definitions
            .OrderBy(definition => definition.kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool Exists(string entityId)
    {
        return Load(entityId) != null;
    }

    public static void Save(EntityDefinition definition, string previousId = null)
    {
        EnsureDefinitionsIfNeeded();
        if (ContentReference.Parse(definition?.id).IsQualified)
            throw new InvalidOperationException("Las definiciones instaladas desde paquetes son de solo lectura.");
        ValidateId(definition?.id);
        Normalize(definition);
        Validate(definition, definition.id);

        string json = JsonUtility.ToJson(definition, true);
        string targetFile = Path.Combine(EntitiesPath, $"{definition.id}.json");
        File.WriteAllText(targetFile, json);

        if (!string.IsNullOrWhiteSpace(previousId) &&
            !string.Equals(previousId, definition.id, StringComparison.OrdinalIgnoreCase))
        {
            string previousFile = FindFile(previousId);
            if (!string.IsNullOrEmpty(previousFile) && !string.Equals(previousFile, targetFile, StringComparison.OrdinalIgnoreCase))
                File.Delete(previousFile);
        }

#if UNITY_EDITOR
        MirrorEditorDefinition(definition.id, previousId, json);
#endif

        Cache.Clear();
        Cache[definition.id] = definition;
    }

    public static bool Delete(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return false;

        EnsureDefinitionsIfNeeded();
        if (ContentReference.Parse(entityId).IsQualified)
            return false;
        string file = FindFile(entityId);
        if (string.IsNullOrEmpty(file))
            return false;

        File.Delete(file);
#if UNITY_EDITOR
        DeleteEditorDefinition(entityId);
#endif
        Cache.Clear();
        return true;
    }

#if UNITY_EDITOR
    private static void MirrorEditorDefinition(string entityId, string previousId, string json)
    {
        string editorFolder = Path.Combine(Application.streamingAssetsPath, RootFolderName, EntitiesFolderName);
        Directory.CreateDirectory(editorFolder);
        string editorFile = Path.Combine(editorFolder, $"{entityId}.json");
        File.WriteAllText(editorFile, json);

        if (!string.IsNullOrWhiteSpace(previousId) &&
            !string.Equals(previousId, entityId, StringComparison.OrdinalIgnoreCase))
            DeleteFileAndMeta(Path.Combine(editorFolder, $"{previousId}.json"));
    }

    private static void DeleteEditorDefinition(string entityId)
    {
        string editorFile = Path.Combine(
            Application.streamingAssetsPath,
            RootFolderName,
            EntitiesFolderName,
            $"{entityId}.json");
        DeleteFileAndMeta(editorFile);
    }

    private static void DeleteFileAndMeta(string file)
    {
        if (File.Exists(file))
            File.Delete(file);
        string meta = $"{file}.meta";
        if (File.Exists(meta))
            File.Delete(meta);
    }
#endif

    private static EntityDefinition Read(string file, string requestedId)
    {
        EntityDefinition definition = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
        Validate(definition, file);
        Cache[requestedId] = definition;
        return definition;
    }

    private static string FindFile(string entityId)
    {
        string exact = Path.Combine(EntitiesPath, $"{entityId}.json");
        if (File.Exists(exact))
            return exact;

        foreach (string file in Directory.GetFiles(EntitiesPath, "*.json"))
        {
            try
            {
                EntityDefinition candidate = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
                if (candidate != null && string.Equals(candidate.id, entityId, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            catch (Exception)
            {
                // Un archivo inválido no debe impedir encontrar y administrar el resto.
            }
        }

        return null;
    }

    private static void TryMigrateBuiltInLifeDefinition(
        string sourceFile,
        string targetFile)
    {
        try
        {
            EntityDefinition source =
                JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(sourceFile));
            EntityDefinition target =
                JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(targetFile));

            if (source?.life == null ||
                string.IsNullOrWhiteSpace(source.life.deathOutcome) ||
                target == null ||
                !string.Equals(source.id, target.id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool usesUnmodifiedLegacyDeathDefaults =
                target.life == null ||
                (string.IsNullOrWhiteSpace(target.life.deathOutcome) &&
                 target.life.removeOnDeath &&
                 Mathf.Approximately(target.life.deathRemovalDelay, 0.75f));

            if (!usesUnmodifiedLegacyDeathDefaults)
                return;

            target.life = source.life;
            File.WriteAllText(targetFile, JsonUtility.ToJson(target, true));
            Debug.Log(
                $"[EntityDefinitionRepository] Se migró la configuración de muerte " +
                $"de la entidad base '{target.id}'.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[EntityDefinitionRepository] No se pudo migrar '{targetFile}': " +
                exception.Message);
        }
    }

    private static void ResolvePackagedEntityReferences(
        EntityDefinition definition,
        string packageId)
    {
        if (definition?.resource != null &&
            !string.IsNullOrWhiteSpace(definition.resource.onResourcesSpentEntityId))
        {
            definition.resource.onResourcesSpentEntityId =
                PackageContentResolver.ResolveEntityReference(
                    packageId,
                    definition.resource.onResourcesSpentEntityId);
        }

        if (definition?.life != null &&
            !string.IsNullOrWhiteSpace(definition.life.deathReplacementEntityId))
        {
            definition.life.deathReplacementEntityId =
                PackageContentResolver.ResolveEntityReference(
                    packageId,
                    definition.life.deathReplacementEntityId);
        }
    }

    private static void Normalize(EntityDefinition definition)
    {
        definition.id = definition.id?.Trim();
        definition.name = definition.name?.Trim();
        definition.description = definition.description?.Trim();
        definition.kind = string.IsNullOrWhiteSpace(definition.kind) ? EntityKinds.Unit : definition.kind.Trim().ToLowerInvariant();
        definition.entityType = string.IsNullOrWhiteSpace(definition.entityType) ? "none" : definition.entityType.Trim().ToLowerInvariant();
        definition.visual = string.IsNullOrWhiteSpace(definition.visual) ? "capsule" : definition.visual.Trim().ToLowerInvariant();
        definition.prefabResource = string.IsNullOrWhiteSpace(definition.prefabResource) ? null : definition.prefabResource.Trim();
        if (definition.life != null)
        {
            definition.life.deathOutcome = string.IsNullOrWhiteSpace(definition.life.deathOutcome)
                ? null
                : definition.life.deathOutcome.Trim().ToLowerInvariant();
            definition.life.deathReplacementEntityId =
                string.IsNullOrWhiteSpace(definition.life.deathReplacementEntityId)
                    ? null
                    : definition.life.deathReplacementEntityId.Trim();
        }
        definition.attributes ??= Array.Empty<string>();
        definition.attributes = definition.attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
            .Select(attribute => attribute.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(attribute => attribute, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateId(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            throw new InvalidDataException("La entidad debe tener un ID.");

        foreach (char character in entityId)
        {
            if (!char.IsLetterOrDigit(character) && character != '.' && character != '-' && character != '_')
                throw new InvalidDataException($"El ID '{entityId}' contiene el carácter inválido '{character}'.");
        }
    }

    private static void Validate(EntityDefinition definition, string file)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.id))
            throw new InvalidDataException($"Definición inválida: {file}");
        if (string.IsNullOrWhiteSpace(definition.name))
            throw new InvalidDataException($"La entidad '{definition.id}' debe tener nombre.");
        if (!string.Equals(definition.kind, EntityKinds.Unit, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(definition.kind, EntityKinds.Building, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(definition.kind, EntityKinds.Environment, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"La entidad '{definition.id}' tiene kind inválido: '{definition.kind}'.");
        if (definition.maxHealth < 1)
            throw new InvalidDataException($"La entidad '{definition.id}' debe tener al menos 1 punto de vida.");
        if (definition.moveSpeed < 0f)
            throw new InvalidDataException($"La entidad '{definition.id}' no puede tener velocidad negativa.");
        if (string.Equals(definition.visual, "prefab", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(definition.prefabResource))
            throw new InvalidDataException($"La entidad '{definition.id}' usa visual prefab, pero no declara prefabResource.");

        if (definition.life != null)
        {
            EntityDeathOutcome outcome;
            if (string.IsNullOrWhiteSpace(definition.life.deathOutcome))
            {
                outcome = definition.life.removeOnDeath
                    ? EntityDeathOutcome.Despawn
                    : EntityDeathOutcome.Remain;
            }
            else if (!Enum.TryParse(definition.life.deathOutcome, true, out outcome))
            {
                throw new InvalidDataException(
                    $"La entidad '{definition.id}' declara deathOutcome inválido: " +
                    $"'{definition.life.deathOutcome}'.");
            }

            float deathDelay = definition.life.deathOutcomeDelay >= 0f
                ? definition.life.deathOutcomeDelay
                : definition.life.deathRemovalDelay;
            if (deathDelay < 0f)
            {
                throw new InvalidDataException(
                    $"La entidad '{definition.id}' no puede declarar un retraso de muerte negativo.");
            }

            if (outcome == EntityDeathOutcome.Replace &&
                string.IsNullOrWhiteSpace(definition.life.deathReplacementEntityId))
            {
                throw new InvalidDataException(
                    $"La entidad '{definition.id}' usa deathOutcome='replace' " +
                    "sin deathReplacementEntityId.");
            }
        }

        bool hasResourceAttribute = definition.attributes != null &&
                                    Array.Exists(definition.attributes, value =>
                                        string.Equals(value, EntityAttributeIds.Resource, StringComparison.OrdinalIgnoreCase));
        if (hasResourceAttribute && definition.resource == null)
            throw new InvalidDataException($"La entidad recurso '{definition.id}' debe declarar el bloque resource.");
        if (hasResourceAttribute &&
            (definition.resource.resources == null || definition.resource.resources.Length == 0))
            throw new InvalidDataException($"La entidad recurso '{definition.id}' debe declarar al menos un recurso y su cantidad.");

        bool hasWorkerAttribute = definition.attributes != null &&
                                  Array.Exists(definition.attributes, value =>
                                      string.Equals(value, EntityAttributeIds.Worker, StringComparison.OrdinalIgnoreCase));
        if (hasWorkerAttribute && definition.worker == null)
            throw new InvalidDataException($"La entidad trabajadora '{definition.id}' debe declarar el bloque worker.");
    }

    private static void EnsureDefinitionsIfNeeded()
    {
        if (!Directory.Exists(EntitiesPath))
            EnsureDefinitions();
    }
}
