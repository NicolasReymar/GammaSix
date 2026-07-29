using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Repositorio de contenido base y paquetes instalados. Conserva las carpetas
/// heredadas para escenarios existentes, pero el contenido importable queda
/// aislado bajo GameContent/Packages.
/// </summary>
public static class GameContentRepository
{
    private const string RootFolderName = "GameContent";
    private const string ScenariosFolderName = "Scenarios";
    private const string CampaignsFolderName = "Campaigns";
    private const string EntitiesFolderName = "Entities";
    private const string TerrainsFolderName = "Terrains";
    private const string PackagesFolderName = "Packages";
    private const string ImportFolderName = "Import";
    private const string TempFolderName = "Temp";
    private const string PackageRegistryFileName = "registry.json";

    private static bool ensuringFolders;

    public static string RootPath => Path.Combine(Application.persistentDataPath, RootFolderName);
    public static string ScenariosPath => Path.Combine(RootPath, ScenariosFolderName);
    public static string CampaignsPath => Path.Combine(RootPath, CampaignsFolderName);
    public static string EntitiesPath => Path.Combine(RootPath, EntitiesFolderName);
    public static string TerrainsPath => Path.Combine(RootPath, TerrainsFolderName);
    public static string PackagesPath => Path.Combine(RootPath, PackagesFolderName);
    public static string ImportPath => Path.Combine(RootPath, ImportFolderName);
    public static string TempPath => Path.Combine(RootPath, TempFolderName);
    public static string PackageRegistryPath => Path.Combine(PackagesPath, PackageRegistryFileName);

    public static IReadOnlyList<GameContentEntry> LoadAllContent()
    {
        EnsureFolders();
        List<GameContentEntry> result = new();
        LoadLegacyFolder(ScenariosPath, GameContentType.Scenario, result);
        LoadLegacyFolder(CampaignsPath, GameContentType.Campaign, result);
        LoadInstalledPackages(result);

        return result
            .GroupBy(item => $"{item.ContentType}:{item.ContentId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.ContentType)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ScenarioDefinition LoadScenario(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return null;

        ContentReference reference = ContentReference.Parse(scenarioId);
        if (reference.IsQualified && !reference.IsBase)
            return PackageContentResolver.LoadScenario(reference.ToString());

        string legacyId = reference.IsBase ? reference.LocalId : scenarioId;
        return ReadScenario(FindContentFile(legacyId, ScenariosPath));
    }

    public static CampaignDefinition LoadCampaign(string campaignId)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
            return null;

        ContentReference reference = ContentReference.Parse(campaignId);
        if (reference.IsQualified && !reference.IsBase)
            return PackageContentResolver.LoadCampaign(reference.ToString());

        string legacyId = reference.IsBase ? reference.LocalId : campaignId;
        return ReadCampaign(FindContentFile(legacyId, CampaignsPath));
    }

    public static ScenarioDefinition ResolveFirstScenario(GameContentEntry entry)
    {
        if (entry == null)
            return null;
        if (entry.ContentType == GameContentType.Scenario)
            return LoadScenario(entry.ContentId);

        CampaignDefinition campaign = LoadCampaign(entry.ContentId);
        string scenarioId = campaign?.steps?
            .FirstOrDefault(step => string.Equals(step.type, "scenario", StringComparison.OrdinalIgnoreCase))?
            .scenarioId;
        return LoadScenario(scenarioId);
    }

    public static void EnsureFolders()
    {
        if (ensuringFolders)
            return;

        ensuringFolders = true;
        try
        {
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(ScenariosPath);
            Directory.CreateDirectory(CampaignsPath);
            Directory.CreateDirectory(EntitiesPath);
            Directory.CreateDirectory(TerrainsPath);
            Directory.CreateDirectory(PackagesPath);
            Directory.CreateDirectory(ImportPath);
            Directory.CreateDirectory(TempPath);
            CopyBuiltInExamples();
            GameContentPackageImporter.ImportPendingPackages();
            EntityDefinitionRepository.EnsureDefinitions();
            TerrainDefinitionRepository.EnsureDefinitions();
            Debug.Log($"[GameContentRepository] Contenido base: {RootPath}");
            Debug.Log($"[GameContentRepository] Paquetes: {PackagesPath}");
            Debug.Log($"[GameContentRepository] Bandeja de importación: {ImportPath}");
        }
        finally
        {
            ensuringFolders = false;
        }
    }

    private static void CopyBuiltInExamples()
    {
        string sourceRoot = Path.Combine(Application.streamingAssetsPath, RootFolderName);
        CopyJsonFiles(Path.Combine(sourceRoot, ScenariosFolderName), ScenariosPath);
        CopyJsonFiles(Path.Combine(sourceRoot, CampaignsFolderName), CampaignsPath);
        CopyJsonFiles(Path.Combine(sourceRoot, EntitiesFolderName), EntitiesPath);
        CopyJsonFiles(Path.Combine(sourceRoot, TerrainsFolderName), TerrainsPath);
    }

    private static void CopyJsonFiles(string sourceFolder, string targetFolder)
    {
        if (!Directory.Exists(sourceFolder))
            return;

        foreach (string sourceFile in Directory.GetFiles(sourceFolder, "*.json"))
        {
            string targetFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
            if (!File.Exists(targetFile))
            {
                File.Copy(sourceFile, targetFile);
                continue;
            }

            TryMigrateObsoleteBuiltInScenario(sourceFile, targetFile);
        }
    }

    private static void TryMigrateObsoleteBuiltInScenario(string sourceFile, string targetFile)
    {
        if (!string.Equals(
                Path.GetFileName(sourceFile),
                "scenario_capture_rescue_test.json",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            string current = File.ReadAllText(targetFile);
            bool obsoletePhaseSeven =
                current.IndexOf("\"capture-participant\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.IndexOf("\"rescue-participant\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.IndexOf("\"entity-captured\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.IndexOf("\"entity-rescued\"", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!obsoletePhaseSeven)
                return;

            File.Copy(sourceFile, targetFile, true);
            Debug.Log("[GameContentRepository] Se migró el escenario técnico de Fase 7 a acciones declarativas genéricas.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GameContentRepository] No se pudo migrar '{targetFile}': {exception.Message}");
        }
    }

    private static void LoadLegacyFolder(
        string folder,
        GameContentType fallbackType,
        List<GameContentEntry> output)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                if (fallbackType == GameContentType.Campaign)
                {
                    CampaignDefinition campaign = JsonUtility.FromJson<CampaignDefinition>(File.ReadAllText(file));
                    if (campaign == null || string.IsNullOrWhiteSpace(campaign.id))
                        continue;

                    output.Add(new GameContentEntry
                    {
                        ContentId = campaign.id,
                        DisplayName = string.IsNullOrWhiteSpace(campaign.name) ? campaign.id : campaign.name,
                        Description = campaign.description,
                        ContentType = GameContentType.Campaign,
                        FilePath = file,
                        FirstScenarioId = campaign.steps?
                            .FirstOrDefault(step => string.Equals(step.type, "scenario", StringComparison.OrdinalIgnoreCase))?
                            .scenarioId,
                        IsPackaged = false,
                        PackageId = ContentReference.BasePackageId
                    });
                    continue;
                }

                ScenarioDefinition scenario = JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(file));
                if (scenario == null)
                    continue;

                string id = string.IsNullOrWhiteSpace(scenario.id)
                    ? Path.GetFileNameWithoutExtension(file)
                    : scenario.id;
                output.Add(new GameContentEntry
                {
                    ContentId = id,
                    DisplayName = string.IsNullOrWhiteSpace(scenario.name) ? id : scenario.name,
                    Description = scenario.description,
                    ContentType = GameContentType.Scenario,
                    FilePath = file,
                    FirstScenarioId = id,
                    IsPackaged = false,
                    PackageId = ContentReference.BasePackageId
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GameContentRepository] No se pudo leer {file}: {exception.Message}");
            }
        }
    }

    private static void LoadInstalledPackages(List<GameContentEntry> output)
    {
        foreach (InstalledGameContentPackage package in PackageContentResolver.GetInstalledPackages())
        {
            LoadPackagedScenarios(package, output);
            LoadPackagedCampaigns(package, output);
        }
    }

    private static void LoadPackagedScenarios(
        InstalledGameContentPackage package,
        List<GameContentEntry> output)
    {
        foreach (string file in PackageContentResolver.EnumerateContentFiles(
                     package,
                     PackageContentResolver.ScenariosFolderName))
        {
            try
            {
                ScenarioDefinition scenario = JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(file));
                if (scenario == null || string.IsNullOrWhiteSpace(scenario.id))
                    continue;

                string qualifiedId = ContentReference.Qualify(package.PackageId, scenario.id);
                output.Add(new GameContentEntry
                {
                    ContentId = qualifiedId,
                    DisplayName = string.IsNullOrWhiteSpace(scenario.name) ? scenario.id : scenario.name,
                    Description = scenario.description,
                    ContentType = GameContentType.Scenario,
                    FilePath = file,
                    FirstScenarioId = qualifiedId,
                    IsPackaged = true,
                    PackageId = package.PackageId,
                    PackageVersion = package.PackageVersion,
                    ContentHash = package.ContentHash
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GameContentRepository] Escenario de paquete inválido '{file}': {exception.Message}");
            }
        }
    }

    private static void LoadPackagedCampaigns(
        InstalledGameContentPackage package,
        List<GameContentEntry> output)
    {
        foreach (string file in PackageContentResolver.EnumerateContentFiles(
                     package,
                     PackageContentResolver.CampaignsFolderName))
        {
            try
            {
                CampaignDefinition campaign = JsonUtility.FromJson<CampaignDefinition>(File.ReadAllText(file));
                if (campaign == null || string.IsNullOrWhiteSpace(campaign.id))
                    continue;

                string qualifiedId = ContentReference.Qualify(package.PackageId, campaign.id);
                string firstScenario = campaign.steps?
                    .FirstOrDefault(step => string.Equals(step.type, "scenario", StringComparison.OrdinalIgnoreCase))?
                    .scenarioId;
                output.Add(new GameContentEntry
                {
                    ContentId = qualifiedId,
                    DisplayName = string.IsNullOrWhiteSpace(campaign.name) ? campaign.id : campaign.name,
                    Description = campaign.description,
                    ContentType = GameContentType.Campaign,
                    FilePath = file,
                    FirstScenarioId = ContentReference.Qualify(package.PackageId, firstScenario),
                    IsPackaged = true,
                    PackageId = package.PackageId,
                    PackageVersion = package.PackageVersion,
                    ContentHash = package.ContentHash
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GameContentRepository] Campaña de paquete inválida '{file}': {exception.Message}");
            }
        }
    }

    private static string FindContentFile(string id, string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        string exactPath = Path.Combine(folder, $"{id}.json");
        if (File.Exists(exactPath))
            return exactPath;

        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                IdOnlyModel model = JsonUtility.FromJson<IdOnlyModel>(File.ReadAllText(file));
                if (model != null && string.Equals(model.id, id, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            catch
            {
                // La lectura detallada reportará el error cuando corresponda.
            }
        }

        return null;
    }

    private static ScenarioDefinition ReadScenario(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        return JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(path));
    }

    private static CampaignDefinition ReadCampaign(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        return JsonUtility.FromJson<CampaignDefinition>(File.ReadAllText(path));
    }

    [Serializable]
    private sealed class IdOnlyModel
    {
        public string id;
    }
}
