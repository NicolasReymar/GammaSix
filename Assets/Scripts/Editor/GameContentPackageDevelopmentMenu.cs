#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

public static class GameContentPackageDevelopmentMenu
{
    private const string ExamplePackageId = "example.dynamic-content";

    [MenuItem("GammaSix/Content/Create and Import Package Example")]
    public static void CreateAndImportPackageExample()
    {
        GameContentRepository.EnsureFolders();
        string sourceRoot = Path.Combine(GameContentRepository.TempPath, "example-package-source");
        if (Directory.Exists(sourceRoot))
            Directory.Delete(sourceRoot, true);

        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(sourceRoot, "Scenarios"));

        GameContentPackageManifest manifest = new()
        {
            packageId = ExamplePackageId,
            packageVersion = "1.6.0",
            displayName = "Ejemplo de contenido dinámico",
            description = "Paquete de prueba que utiliza entidades reales del juego base.",
            author = "GammaSix",
            requiredGameVersion = Application.version,
            contentFormatVersion = GameContentPackageValidator.SupportedContentFormatVersion,
            entryScenarioId = "scenario.dynamic-package-test",
            requiredFeatures = new[]
            {
                "content.packages.v1",
                "runtime.participants.v1",
                "runtime.command-bus.v1",
                "runtime.dynamic-entities.v1",
                "runtime.match-state.v1",
                "runtime.entity-areas.v1",
                "runtime.rules.v1",
                "runtime.entity-life.v1",
                "runtime.combat.v1",
                "runtime.death-outcomes.v1",
                "runtime.declarative-actions.v2",
                "runtime.participant-variables.v1",
                "runtime.event-snapshots.v1",
                "runtime.channels.v1"
            }
        };

        ScenarioDefinition scenario = new()
        {
            id = "scenario.dynamic-package-test",
            name = "Paquete dinámico - Prueba",
            description = "Escenario empaquetado que carga soldados y trabajadores completos del juego base.",
            maxTeams = 1,
            maxPlayers = 1,
            fixedTeams = true,
            gameModeId = HeadlessProfileCatalog.NormalGameModeId,
            participantConfiguration = new ScenarioParticipantConfiguration
            {
                maximumHumanPlayers = 1,
                maximumParticipants = 1
            },
            worldSize = new ScenarioWorldSize { width = 40f, height = 40f },
            terrain = new ScenarioTerrainDefinition
            {
                defaultTerrainId = "base:praderas_primavera",
                tiles = Array.Empty<ScenarioTerrainTilePlacement>()
            },
            entityCatalog = new ScenarioEntityCatalogDefinition
            {
                spawnableEntityIds = new[]
                {
                    "base:unit.humanoid.default",
                    "base:unit.humanoid.worker",
                    "base:building.aura"
                }
            },
            entities = new[]
            {
                new ScenarioEntityPlacement
                {
                    id = "package_soldier_01",
                    entityId = "base:unit.humanoid.default",
                    teamId = 1,
                    ownerTeamSlot = 1,
                    position = new ScenarioVector3 { x = 0f, y = 0.5f, z = 0f }
                },
                new ScenarioEntityPlacement
                {
                    id = "package_area_01",
                    entityId = "base:building.aura",
                    teamId = 0,
                    ownerTeamSlot = 0,
                    position = new ScenarioVector3 { x = 5f, y = 0.04f, z = 0f }
                }
            },
            rules = new[]
            {
                new ScenarioRuleDefinition
                {
                    id = "rule.package-area-enter",
                    eventType = "entity-entered-area",
                    conditions = new[]
                    {
                        new ScenarioRuleConditionDefinition
                        {
                            type = "area-has-attribute",
                            attribute = EntityAttributeIds.AuraTrigger
                        },
                        new ScenarioRuleConditionDefinition
                        {
                            type = "entity-has-attribute",
                            attribute = EntityAttributeIds.Humanoid
                        }
                    },
                    actions = new[]
                    {
                        new ScenarioRuleActionDefinition
                        {
                            type = "show-message",
                            message = "La regla del paquete detectó una unidad dentro del área."
                        }
                    }
                }
            },
            missions = Array.Empty<ScenarioMissionDefinition>(),
            teamResources = Array.Empty<ScenarioTeamResourceDefinition>(),
            settingOverrides = Array.Empty<ScenarioSettingOverride>()
        };

        File.WriteAllText(Path.Combine(sourceRoot, "manifest.json"), JsonUtility.ToJson(manifest, true));
        File.WriteAllText(
            Path.Combine(sourceRoot, "Scenarios", "scenario.dynamic-package-test.json"),
            JsonUtility.ToJson(scenario, true));

        string archivePath = Path.Combine(GameContentRepository.ImportPath, $"{ExamplePackageId}.gsixpackage");
        if (File.Exists(archivePath))
            File.Delete(archivePath);
        ZipFile.CreateFromDirectory(
            sourceRoot,
            archivePath,
            System.IO.Compression.CompressionLevel.Optimal,
            false);

        GameContentPackageImportResult result = GameContentPackageImporter.ImportPackage(archivePath);
        if (result.Success)
        {
            File.Delete(archivePath);
            Debug.Log($"[GammaSix] {result.Message} Hash: {result.ContentHash}");
            EditorUtility.DisplayDialog("GammaSix", result.Message, "Aceptar");
        }
        else
        {
            string errors = result.Errors == null || result.Errors.Count == 0
                ? result.Message
                : string.Join("\n", result.Errors);
            Debug.LogError($"[GammaSix] No se importó el paquete de ejemplo:\n{errors}");
            EditorUtility.DisplayDialog("GammaSix - Error", errors, "Aceptar");
        }

        if (Directory.Exists(sourceRoot))
            Directory.Delete(sourceRoot, true);
    }
}
#endif
