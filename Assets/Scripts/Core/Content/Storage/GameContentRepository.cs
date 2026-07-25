using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Repositorio único de escenarios y campañas. No lee la carpeta heredada Maps:
/// todo contenido válido debe vivir bajo GameContent.
/// </summary>
public static class GameContentRepository
{
    private const string RootFolderName = "GameContent";
    private const string ScenariosFolderName = "Scenarios";
    private const string CampaignsFolderName = "Campaigns";
    private const string EntitiesFolderName = "Entities";

    public static string RootPath => Path.Combine(Application.persistentDataPath, RootFolderName);
    public static string ScenariosPath => Path.Combine(RootPath, ScenariosFolderName);
    public static string CampaignsPath => Path.Combine(RootPath, CampaignsFolderName);
    public static string EntitiesPath => Path.Combine(RootPath, EntitiesFolderName);

    public static IReadOnlyList<GameContentEntry> LoadAllContent()
    {
        EnsureFolders();
        List<GameContentEntry> result = new();
        LoadFolder(ScenariosPath, GameContentType.Scenario, result);
        LoadFolder(CampaignsPath, GameContentType.Campaign, result);

        return result
            .GroupBy(item => $"{item.ContentType}:{item.ContentId}")
            .Select(group => group.First())
            .OrderBy(item => item.ContentType)
            .ThenBy(item => item.DisplayName)
            .ToList();
    }

    public static ScenarioDefinition LoadScenario(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return null;
        return ReadScenario(FindContentFile(scenarioId, ScenariosPath));
    }

    public static CampaignDefinition LoadCampaign(string campaignId)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
            return null;
        return ReadCampaign(FindContentFile(campaignId, CampaignsPath));
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
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ScenariosPath);
        Directory.CreateDirectory(CampaignsPath);
        Directory.CreateDirectory(EntitiesPath);
        CopyBuiltInExamples();
        EntityDefinitionRepository.EnsureDefinitions();
        Debug.Log($"[GameContentRepository] Escenarios: {ScenariosPath}");
        Debug.Log($"[GameContentRepository] Campañas: {CampaignsPath}");
    }

    private static void CopyBuiltInExamples()
    {
        string sourceRoot = Path.Combine(Application.streamingAssetsPath, RootFolderName);
        CopyJsonFiles(Path.Combine(sourceRoot, ScenariosFolderName), ScenariosPath);
        CopyJsonFiles(Path.Combine(sourceRoot, CampaignsFolderName), CampaignsPath);
        CopyJsonFiles(Path.Combine(sourceRoot, EntitiesFolderName), EntitiesPath);
    }

    private static void CopyJsonFiles(string sourceFolder, string targetFolder)
    {
        if (!Directory.Exists(sourceFolder))
            return;

        foreach (string sourceFile in Directory.GetFiles(sourceFolder, "*.json"))
        {
            string targetFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
            if (!File.Exists(targetFile))
                File.Copy(sourceFile, targetFile);
        }
    }

    private static void LoadFolder(string folder, GameContentType fallbackType, List<GameContentEntry> output)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                bool isCampaign = fallbackType == GameContentType.Campaign;

                if (isCampaign)
                {
                    CampaignDefinition campaign = JsonUtility.FromJson<CampaignDefinition>(json);
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
                            .scenarioId
                    });
                    continue;
                }

                ScenarioDefinition scenario = JsonUtility.FromJson<ScenarioDefinition>(json);
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
                    FirstScenarioId = id
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GameContentRepository] No se pudo leer {file}: {exception.Message}");
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
            string json = File.ReadAllText(file);
            if (json.Contains($"\"id\": \"{id}\"") || json.Contains($"\"id\":\"{id}\""))
                return file;
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
}
