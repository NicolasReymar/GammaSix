using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Catálogo inmutable de definiciones cargadas para una partida. Combina las
/// entidades iniciales, el catálogo base del modo de juego y las referencias
/// adicionales declaradas por el escenario activo.
/// </summary>
public sealed class MatchEntityCatalog
{
    public const string DefaultFallbackEntityId = "unit.humanoid.default";

    private readonly Dictionary<string, MatchEntityCatalogEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> aliases =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MatchEntityCatalogEntry> Entries => entries.Values
        .OrderBy(item => item.ReferenceId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<MatchEntityCatalogEntry> SpawnableEntries => entries.Values
        .Where(item => item.CanSpawnDynamically)
        .OrderBy(item => item.ReferenceId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public int Count => entries.Count;

    public static MatchEntityCatalog Create(ScenarioDefinition scenario)
    {
        MatchEntityCatalog catalog = new();
        HashSet<string> initialReferences = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> dynamicReferences = new(StringComparer.OrdinalIgnoreCase);

        if (scenario?.entities != null)
        {
            foreach (ScenarioEntityPlacement placement in scenario.entities)
            {
                if (placement != null && !string.IsNullOrWhiteSpace(placement.entityId))
                    initialReferences.Add(placement.entityId.Trim());
            }
        }

        bool includeGameModeDefaults = scenario?.entityCatalog == null ||
                                       scenario.entityCatalog.includeGameModeDefaults;
        if (includeGameModeDefaults)
        {
            foreach (string entityId in GameModeEntityCatalogRegistry
                         .GetDefaultSpawnableEntityIds(scenario?.gameModeId))
            {
                if (!string.IsNullOrWhiteSpace(entityId))
                    dynamicReferences.Add(entityId.Trim());
            }
        }

        if (scenario?.waveControllers != null)
        {
            foreach (ScenarioWaveControllerDefinition controller in scenario.waveControllers)
            {
                if (controller == null || !controller.enabled || controller.waves == null)
                    continue;
                foreach (ScenarioWaveDefinition wave in controller.waves)
                {
                    if (wave?.groups == null)
                        continue;
                    foreach (ScenarioWaveGroupDefinition group in wave.groups)
                    {
                        if (group != null && !string.IsNullOrWhiteSpace(group.entityId))
                            dynamicReferences.Add(group.entityId.Trim());
                    }
                }
            }
        }

        string[] configuredSpawnable = scenario?.entityCatalog?.spawnableEntityIds;
        bool hasExplicitSpawnableCatalog = configuredSpawnable != null &&
                                           configuredSpawnable.Any(value => !string.IsNullOrWhiteSpace(value));
        if (hasExplicitSpawnableCatalog)
        {
            foreach (string entityId in configuredSpawnable)
            {
                if (!string.IsNullOrWhiteSpace(entityId))
                    dynamicReferences.Add(entityId.Trim());
            }
        }

        if (dynamicReferences.Count == 0)
        {
            // Compatibilidad con escenarios anteriores o modos sin catálogo base:
            // sus propios tipos colocados se consideran disponibles.
            dynamicReferences.UnionWith(initialReferences);
        }

        if (initialReferences.Count == 0 && dynamicReferences.Count == 0)
        {
            initialReferences.Add(DefaultFallbackEntityId);
            dynamicReferences.Add(DefaultFallbackEntityId);
        }

        foreach (string reference in initialReferences.Concat(dynamicReferences)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            catalog.Add(reference, dynamicReferences.Contains(reference));
        }

        catalog.ExpandDeclaredEntityDependencies();

        if (catalog.Count == 0)
        {
            catalog.Add(DefaultFallbackEntityId, true);
            Debug.LogWarning("[MatchEntityCatalog] El escenario no cargó definiciones válidas. Se habilitó el soldado base como fallback.");
        }

        return catalog;
    }

    public bool TryResolveLoaded(
        string requestedId,
        out string resolvedReferenceId,
        out EntityDefinition definition)
    {
        return TryResolve(requestedId, false, out resolvedReferenceId, out definition);
    }

    public bool TryResolveSpawnable(
        string requestedId,
        out string resolvedReferenceId,
        out EntityDefinition definition)
    {
        return TryResolve(requestedId, true, out resolvedReferenceId, out definition);
    }

    public bool IsSpawnable(string requestedId)
    {
        return TryResolveSpawnable(requestedId, out _, out _);
    }

    private void Add(string referenceId, bool canSpawnDynamically)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
            return;

        string normalizedReference = referenceId.Trim();
        EntityDefinition definition = EntityDefinitionRepository.Load(normalizedReference);
        if (definition == null)
        {
            Debug.LogError($"[MatchEntityCatalog] El escenario declaró la entidad inexistente '{normalizedReference}'.");
            return;
        }

        if (entries.TryGetValue(normalizedReference, out MatchEntityCatalogEntry existing))
        {
            existing.CanSpawnDynamically |= canSpawnDynamically;
            return;
        }

        MatchEntityCatalogEntry entry = new(
            normalizedReference,
            definition,
            canSpawnDynamically);
        entries[normalizedReference] = entry;

        RegisterAlias(normalizedReference, normalizedReference);
        RegisterAlias(ContentReference.Parse(normalizedReference).LocalId, normalizedReference);
        RegisterAlias(definition.id, normalizedReference);
        RegisterAlias(ContentReference.Parse(definition.id).LocalId, normalizedReference);
    }

    private void ExpandDeclaredEntityDependencies()
    {
        Queue<MatchEntityCatalogEntry> pending = new(entries.Values);
        HashSet<string> inspected = new(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            MatchEntityCatalogEntry source = pending.Dequeue();
            if (source == null || !inspected.Add(source.ReferenceId))
                continue;

            string replacementId = source.Definition?.life?.deathReplacementEntityId;
            if (string.IsNullOrWhiteSpace(replacementId))
                continue;

            if (TryResolveLoaded(
                    replacementId,
                    out string existingReference,
                    out _) &&
                entries.TryGetValue(existingReference, out MatchEntityCatalogEntry existingDependency))
            {
                existingDependency.CanSpawnDynamically = true;
                pending.Enqueue(existingDependency);
                continue;
            }

            int previousCount = entries.Count;
            Add(replacementId.Trim(), true);
            if (entries.Count <= previousCount)
                continue;

            if (TryResolveLoaded(
                    replacementId,
                    out string resolvedReference,
                    out _) &&
                entries.TryGetValue(resolvedReference, out MatchEntityCatalogEntry dependency))
            {
                pending.Enqueue(dependency);
            }
        }
    }

    private bool TryResolve(
        string requestedId,
        bool requireDynamicPermission,
        out string resolvedReferenceId,
        out EntityDefinition definition)
    {
        resolvedReferenceId = null;
        definition = null;
        if (string.IsNullOrWhiteSpace(requestedId))
            return false;

        string requested = requestedId.Trim();
        if (!entries.TryGetValue(requested, out MatchEntityCatalogEntry entry))
        {
            if (!aliases.TryGetValue(requested, out string reference) ||
                string.IsNullOrWhiteSpace(reference) ||
                !entries.TryGetValue(reference, out entry))
            {
                return false;
            }
        }

        if (requireDynamicPermission && !entry.CanSpawnDynamically)
            return false;

        resolvedReferenceId = entry.ReferenceId;
        definition = entry.Definition;
        return true;
    }

    private void RegisterAlias(string alias, string referenceId)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return;

        if (aliases.TryGetValue(alias, out string previous) &&
            !string.Equals(previous, referenceId, StringComparison.OrdinalIgnoreCase))
        {
            // Alias ambiguo: obliga a utilizar el ID completo con namespace.
            aliases[alias] = string.Empty;
            return;
        }

        aliases[alias] = referenceId;
    }
}

public sealed class MatchEntityCatalogEntry
{
    public string ReferenceId { get; }
    public EntityDefinition Definition { get; }
    public bool CanSpawnDynamically { get; internal set; }

    public MatchEntityCatalogEntry(
        string referenceId,
        EntityDefinition definition,
        bool canSpawnDynamically)
    {
        ReferenceId = referenceId;
        Definition = definition;
        CanSpawnDynamically = canSpawnDynamically;
    }
}
