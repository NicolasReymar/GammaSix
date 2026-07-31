using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Resuelve contenido instalado sin mezclarlo con las carpetas globales heredadas.
/// </summary>
public static class PackageContentResolver
{
    public const string ScenariosFolderName = "Scenarios";
    public const string CampaignsFolderName = "Campaigns";
    public const string EntitiesFolderName = "Entities";
    public const string TerrainsFolderName = "Terrains";

    private static readonly Dictionary<string, string> FileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> HashCache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCache()
    {
        FileCache.Clear();
        HashCache.Clear();
    }

    public static IReadOnlyList<InstalledGameContentPackage> GetInstalledPackages()
    {
        return GameContentPackageRegistry.LoadAll();
    }

    public static bool TryResolveInstalledPackage(
        string packageId,
        out InstalledGameContentPackage package,
        out string installPath)
    {
        installPath = null;
        if (!GameContentPackageRegistry.TryGet(packageId, out package))
            return false;
        if (!string.Equals(package.RequiredGameVersion, Application.version, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"[PackageContentResolver] El paquete '{packageId}' requiere GammaSix {package.RequiredGameVersion}, pero se ejecuta {Application.version}.");
            return false;
        }

        installPath = Path.Combine(
            GameContentRepository.RootPath,
            package.RelativeInstallPath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(installPath))
            return false;

        if (!HashCache.TryGetValue(package.PackageId, out string actualHash))
        {
            actualHash = GameContentPackageHashService.ComputeDirectorySha256(installPath);
            HashCache[package.PackageId] = actualHash;
        }
        package.ContentHash = actualHash;
        return true;
    }

    public static bool TryFindContentFile(
        string qualifiedOrLocalId,
        string folderName,
        out string filePath,
        out InstalledGameContentPackage package)
    {
        filePath = null;
        package = null;
        ContentReference reference = ContentReference.Parse(qualifiedOrLocalId);
        if (!reference.IsQualified || reference.IsBase)
            return false;

        string cacheKey = $"{reference.PackageId}|{folderName}|{reference.LocalId}";
        if (FileCache.TryGetValue(cacheKey, out string cached) && File.Exists(cached))
        {
            filePath = cached;
            return TryResolveInstalledPackage(reference.PackageId, out package, out _);
        }

        if (!TryResolveInstalledPackage(reference.PackageId, out package, out string installPath))
            return false;

        string folder = Path.Combine(installPath, folderName);
        if (!Directory.Exists(folder))
            return false;

        string exact = Path.Combine(folder, $"{reference.LocalId}.json");
        if (File.Exists(exact))
        {
            FileCache[cacheKey] = exact;
            filePath = exact;
            return true;
        }

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            string id = ReadId(file);
            if (!string.Equals(id, reference.LocalId, StringComparison.OrdinalIgnoreCase))
                continue;
            FileCache[cacheKey] = file;
            filePath = file;
            return true;
        }

        return false;
    }

    public static IReadOnlyList<string> EnumerateContentFiles(
        InstalledGameContentPackage package,
        string folderName)
    {
        if (package == null ||
            !TryResolveInstalledPackage(package.PackageId, out _, out string installPath))
        {
            return Array.Empty<string>();
        }

        string folder = Path.Combine(installPath, folderName);
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
    }

    public static string QualifyLocalReference(string packageId, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        ContentReference reference = ContentReference.Parse(value);
        if (reference.IsQualified)
            return reference.ToString();
        return ContentReference.Qualify(packageId, reference.LocalId);
    }

    public static string ResolveEntityReference(string packageId, string value)
    {
        return ResolvePackageFirstReference(packageId, value, EntitiesFolderName);
    }

    public static string ResolveTerrainReference(string packageId, string value)
    {
        return ResolvePackageFirstReference(packageId, value, TerrainsFolderName);
    }

    public static ScenarioDefinition LoadScenario(string qualifiedScenarioId)
    {
        if (!TryFindContentFile(
                qualifiedScenarioId,
                ScenariosFolderName,
                out string file,
                out InstalledGameContentPackage package))
        {
            return null;
        }

        ScenarioDefinition scenario = JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(file));
        if (scenario == null)
            return null;

        QualifyScenario(scenario, package);
        return scenario;
    }

    public static CampaignDefinition LoadCampaign(string qualifiedCampaignId)
    {
        if (!TryFindContentFile(
                qualifiedCampaignId,
                CampaignsFolderName,
                out string file,
                out InstalledGameContentPackage package))
        {
            return null;
        }

        CampaignDefinition campaign = JsonUtility.FromJson<CampaignDefinition>(File.ReadAllText(file));
        if (campaign == null)
            return null;

        campaign.id = ContentReference.Qualify(package.PackageId, campaign.id);
        if (campaign.steps != null)
        {
            foreach (CampaignStepDefinition step in campaign.steps)
            {
                if (step != null && string.Equals(step.type, "scenario", StringComparison.OrdinalIgnoreCase))
                    step.scenarioId = QualifyLocalReference(package.PackageId, step.scenarioId);
            }
        }
        return campaign;
    }

    public static void QualifyScenario(
        ScenarioDefinition scenario,
        InstalledGameContentPackage package)
    {
        if (scenario == null || package == null)
            return;

        scenario.sourcePackageId = package.PackageId;
        scenario.sourcePackageVersion = package.PackageVersion;
        scenario.sourceContentHash = package.ContentHash;
        scenario.id = ContentReference.Qualify(package.PackageId, ContentReference.Parse(scenario.id).LocalId);

        if (!string.IsNullOrWhiteSpace(scenario.gameModeId) &&
            !ContentReference.Parse(scenario.gameModeId).IsQualified)
        {
            scenario.gameModeId = ContentReference.Qualify(package.PackageId, scenario.gameModeId);
        }

        if (scenario.terrain != null)
        {
            scenario.terrain.defaultTerrainId = ResolveTerrainReference(package.PackageId, scenario.terrain.defaultTerrainId);
            if (scenario.terrain.tiles != null)
            {
                foreach (ScenarioTerrainTilePlacement tile in scenario.terrain.tiles)
                {
                    if (tile != null)
                        tile.terrainId = ResolveTerrainReference(package.PackageId, tile.terrainId);
                }
            }
        }

        if (scenario.entities != null)
        {
            foreach (ScenarioEntityPlacement placement in scenario.entities)
            {
                if (placement != null)
                    placement.entityId = ResolveEntityReference(package.PackageId, placement.entityId);
            }
        }


        if (scenario.entityCatalog?.spawnableEntityIds != null)
        {
            for (int i = 0; i < scenario.entityCatalog.spawnableEntityIds.Length; i++)
            {
                scenario.entityCatalog.spawnableEntityIds[i] = ResolveEntityReference(
                    package.PackageId,
                    scenario.entityCatalog.spawnableEntityIds[i]);
            }
        }

        if (scenario.waveControllers != null)
        {
            foreach (ScenarioWaveControllerDefinition controller in scenario.waveControllers)
            {
                if (controller?.waves == null)
                    continue;
                foreach (ScenarioWaveDefinition wave in controller.waves)
                {
                    if (wave?.groups == null)
                        continue;
                    foreach (ScenarioWaveGroupDefinition group in wave.groups)
                    {
                        if (group != null)
                            group.entityId = ResolveEntityReference(package.PackageId, group.entityId);
                    }
                }
            }
        }

        if (scenario.rules != null)
        {
            foreach (ScenarioRuleDefinition rule in scenario.rules)
            {
                if (rule?.actions == null)
                    continue;
                foreach (ScenarioRuleActionDefinition action in rule.actions)
                {
                    if (action != null &&
                        string.Equals(action.type, "spawn-entity", StringComparison.OrdinalIgnoreCase))
                    {
                        action.entityId = ResolveEntityReference(package.PackageId, action.entityId);
                    }
                }
            }
        }

        if (scenario.headlessProfiles != null)
        {
            foreach (ScenarioHeadlessProfileDefinition profile in scenario.headlessProfiles)
            {
                if (profile == null)
                    continue;
                profile.id = QualifyLocalReference(package.PackageId, profile.id);
                profile.sourceId = package.PackageId;
                if (!string.IsNullOrWhiteSpace(profile.gameModeId))
                    profile.gameModeId = QualifyLocalReference(package.PackageId, profile.gameModeId);
            }
        }

        ScenarioParticipantConfiguration participants = scenario.participantConfiguration;
        if (participants?.availableHeadlessProfiles != null)
        {
            for (int i = 0; i < participants.availableHeadlessProfiles.Length; i++)
                participants.availableHeadlessProfiles[i] = QualifyLocalReference(package.PackageId, participants.availableHeadlessProfiles[i]);
        }

        if (participants?.requiredParticipants != null)
        {
            foreach (ScenarioRequiredParticipantDefinition required in participants.requiredParticipants)
            {
                if (required != null)
                    required.controllerProfileId = QualifyLocalReference(package.PackageId, required.controllerProfileId);
            }
        }
    }

    private static string ResolvePackageFirstReference(
        string packageId,
        string value,
        string folderName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        ContentReference reference = ContentReference.Parse(value);
        if (reference.IsQualified)
            return reference.ToString();

        string qualified = ContentReference.Qualify(packageId, reference.LocalId);
        return TryFindContentFile(qualified, folderName, out _, out _)
            ? qualified
            : $"{ContentReference.BasePackageId}:{reference.LocalId}";
    }

    private static string ReadId(string file)
    {
        try
        {
            IdOnlyModel model = JsonUtility.FromJson<IdOnlyModel>(File.ReadAllText(file));
            return model?.id;
        }
        catch
        {
            return null;
        }
    }

    [Serializable]
    private sealed class IdOnlyModel
    {
        public string id;
    }
}
