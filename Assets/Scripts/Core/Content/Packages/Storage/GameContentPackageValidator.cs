using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class GameContentPackageValidator
{
    public const int SupportedContentFormatVersion = 1;
    private const int MaxFiles = 2048;
    private const long MaxExpandedBytes = 128L * 1024L * 1024L;

    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".com", ".bat", ".cmd", ".ps1", ".sh", ".cs", ".js", ".jar", ".so", ".dylib"
    };

    public static bool ValidateExtractedPackage(
        string packageRoot,
        out GameContentPackageManifest manifest,
        out List<string> errors)
    {
        errors = new List<string>();
        manifest = null;

        if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
        {
            errors.Add("La carpeta temporal del paquete no existe.");
            return false;
        }

        string manifestPath = Path.Combine(packageRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            errors.Add("Falta manifest.json en la raíz del paquete.");
            return false;
        }

        try
        {
            manifest = JsonUtility.FromJson<GameContentPackageManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception exception)
        {
            errors.Add($"manifest.json no es válido: {exception.Message}");
            return false;
        }

        ValidateManifest(manifest, errors);
        ValidateFiles(packageRoot, errors);
        ValidateContent(packageRoot, manifest, errors);
        return errors.Count == 0;
    }

    private static void ValidateManifest(GameContentPackageManifest manifest, List<string> errors)
    {
        if (manifest == null)
        {
            errors.Add("No se pudo leer el manifiesto.");
            return;
        }

        if (!IsSafeIdentifier(manifest.packageId))
            errors.Add("packageId debe contener solo letras, números, punto, guion o guion bajo.");
        else if (string.Equals(manifest.packageId, ContentReference.BasePackageId, StringComparison.OrdinalIgnoreCase))
            errors.Add("El packageId 'base' está reservado para el contenido incorporado en GammaSix.");
        if (!IsSafeVersion(manifest.packageVersion))
            errors.Add("packageVersion es obligatorio y contiene caracteres no permitidos.");
        if (manifest.contentFormatVersion != SupportedContentFormatVersion)
            errors.Add($"contentFormatVersion {manifest.contentFormatVersion} no es compatible. Se requiere {SupportedContentFormatVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.requiredGameVersion))
            errors.Add("requiredGameVersion es obligatorio.");
        else if (!string.Equals(manifest.requiredGameVersion.Trim(), Application.version, StringComparison.OrdinalIgnoreCase))
            errors.Add($"El paquete requiere GammaSix {manifest.requiredGameVersion}, pero el juego ejecuta {Application.version}.");
        if (string.IsNullOrWhiteSpace(manifest.entryScenarioId))
        {
            errors.Add("entryScenarioId es obligatorio.");
        }
        else
        {
            ContentReference entry = ContentReference.Parse(manifest.entryScenarioId);
            if (!IsSafeIdentifier(entry.LocalId))
                errors.Add("entryScenarioId contiene un identificador local inválido.");
        }

        if (manifest.requiredFeatures != null)
        {
            foreach (string feature in manifest.requiredFeatures.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!GameContentPackageFeatureCatalog.IsSupported(feature))
                    errors.Add($"La funcionalidad requerida '{feature}' no está disponible en esta versión.");
            }
        }
    }

    private static void ValidateFiles(string packageRoot, List<string> errors)
    {
        string[] files = Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories);
        if (files.Length > MaxFiles)
            errors.Add($"El paquete contiene {files.Length} archivos; el máximo es {MaxFiles}.");

        long totalBytes = 0;
        foreach (string file in files)
        {
            FileInfo info = new(file);
            totalBytes += info.Length;
            string extension = Path.GetExtension(file);
            if (ForbiddenExtensions.Contains(extension))
                errors.Add($"Tipo de archivo no permitido: {Path.GetFileName(file)}");
        }

        if (totalBytes > MaxExpandedBytes)
            errors.Add($"El paquete expandido supera {MaxExpandedBytes / (1024 * 1024)} MB.");
    }

    private static void ValidateContent(
        string packageRoot,
        GameContentPackageManifest manifest,
        List<string> errors)
    {
        if (manifest == null)
            return;

        string scenariosPath = Path.Combine(packageRoot, "Scenarios");
        if (!Directory.Exists(scenariosPath))
        {
            errors.Add("El paquete debe incluir la carpeta Scenarios.");
            return;
        }

        HashSet<string> entityIds = ReadDefinitionIds<EntityDefinition>(
            Path.Combine(packageRoot, "Entities"),
            definition => definition?.id,
            "entidad",
            errors);
        HashSet<string> terrainIds = ReadDefinitionIds<TerrainDefinition>(
            Path.Combine(packageRoot, "Terrains"),
            definition => definition?.id,
            "terreno",
            errors);
        ValidateEntityAreaDefinitions(Path.Combine(packageRoot, "Entities"), errors);
        ValidateEntityCombatDefinitions(Path.Combine(packageRoot, "Entities"), errors);
        ValidateEntityLifeDefinitions(
            Path.Combine(packageRoot, "Entities"),
            manifest.packageId,
            entityIds,
            errors);

        HashSet<string> scenarioIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(scenariosPath, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                ScenarioDefinition scenario = JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(file));
                if (scenario == null || string.IsNullOrWhiteSpace(scenario.id))
                {
                    errors.Add($"Escenario sin id: {Relative(packageRoot, file)}");
                    continue;
                }
                if (ContentReference.Parse(scenario.id).IsQualified)
                {
                    errors.Add($"El escenario '{scenario.id}' debe declarar un id local sin namespace.");
                    continue;
                }
                if (!IsSafeIdentifier(scenario.id))
                {
                    errors.Add($"El escenario '{scenario.id}' contiene un id inválido.");
                    continue;
                }

                if (!scenarioIds.Add(scenario.id.Trim()))
                    errors.Add($"ID de escenario duplicado: {scenario.id}");

                ValidateScenarioReferences(scenario, manifest.packageId, entityIds, terrainIds, errors);
            }
            catch (Exception exception)
            {
                errors.Add($"Escenario inválido {Relative(packageRoot, file)}: {exception.Message}");
            }
        }

        ContentReference entry = ContentReference.Parse(manifest.entryScenarioId);
        if (entry.IsQualified && !string.Equals(entry.PackageId, manifest.packageId, StringComparison.OrdinalIgnoreCase))
            errors.Add("entryScenarioId no puede apuntar a otro paquete.");
        string localEntryId = entry.IsQualified ? entry.LocalId : manifest.entryScenarioId;
        if (!scenarioIds.Contains(localEntryId))
            errors.Add($"entryScenarioId '{manifest.entryScenarioId}' no existe dentro del paquete.");
    }

    private static HashSet<string> ReadDefinitionIds<T>(
        string folder,
        Func<T, string> idSelector,
        string label,
        List<string> errors)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder))
            return ids;

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                T definition = JsonUtility.FromJson<T>(File.ReadAllText(file));
                string id = idSelector(definition);
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add($"Definición de {label} sin id: {Path.GetFileName(file)}");
                    continue;
                }
                if (ContentReference.Parse(id).IsQualified)
                {
                    errors.Add($"La definición de {label} '{id}' debe declarar un id local sin namespace.");
                    continue;
                }
                if (!IsSafeIdentifier(id))
                {
                    errors.Add($"La definición de {label} '{id}' contiene un id inválido.");
                    continue;
                }

                if (!ids.Add(id.Trim()))
                    errors.Add($"ID de {label} duplicado: {id}");
            }
            catch (Exception exception)
            {
                errors.Add($"Definición de {label} inválida {Path.GetFileName(file)}: {exception.Message}");
            }
        }

        return ids;
    }

    private static void ValidateScenarioReferences(
        ScenarioDefinition scenario,
        string packageId,
        HashSet<string> localEntities,
        HashSet<string> localTerrains,
        List<string> errors)
    {
        if (scenario.entities != null)
        {
            foreach (ScenarioEntityPlacement placement in scenario.entities)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.entityId))
                    continue;
                ValidateEntityReference(
                    scenario.id,
                    placement.entityId,
                    packageId,
                    localEntities,
                    errors,
                    "colocación inicial");
            }
        }

        if (scenario.entityCatalog?.spawnableEntityIds != null)
        {
            foreach (string entityId in scenario.entityCatalog.spawnableEntityIds)
            {
                if (string.IsNullOrWhiteSpace(entityId))
                    continue;
                ValidateEntityReference(
                    scenario.id,
                    entityId,
                    packageId,
                    localEntities,
                    errors,
                    "catálogo de spawn");
            }
        }

        ValidateNavigation(scenario, errors);
        ValidateDiplomacy(scenario, errors);
        ValidateWaveControllers(scenario, packageId, localEntities, errors);
        ValidateHeadlessProfiles(scenario, errors);

        if (scenario.rules != null)
        {
            foreach (ScenarioRuleDefinition rule in scenario.rules)
            {
                if (rule == null || !rule.enabled)
                    continue;
                if (string.IsNullOrWhiteSpace(rule.id))
                    errors.Add($"El escenario '{scenario.id}' contiene una regla sin id.");
                if (!RuleRuntimeSystem.TryParseEventType(rule.eventType, out _))
                    errors.Add($"La regla '{rule.id}' usa el evento desconocido '{rule.eventType}'.");

                if (rule.conditions != null)
                {
                    foreach (ScenarioRuleConditionDefinition condition in rule.conditions)
                    {
                        if (condition != null && !RuleRuntimeSystem.IsSupportedConditionType(condition.type))
                            errors.Add($"La regla '{rule.id}' usa la condición desconocida '{condition.type}'.");
                    }
                }

                if (rule.actions == null)
                    continue;
                foreach (ScenarioRuleActionDefinition action in rule.actions)
                {
                    if (action == null)
                        continue;
                    if (!RuleRuntimeSystem.IsSupportedActionType(action.type))
                    {
                        errors.Add($"La regla '{rule.id}' usa la acción desconocida '{action.type}'.");
                        continue;
                    }
                    if (string.Equals(action.type, "start-channel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(action.channelId))
                            errors.Add($"La regla '{rule.id}' declara start-channel sin channelId.");
                        if (action.duration <= 0f)
                            errors.Add($"La regla '{rule.id}' declara start-channel con duración no válida.");
                    }

                    string normalizedAction = action.type?.Trim().Replace('_', '-').ToLowerInvariant();
                    if (normalizedAction == "set-diplomacy-stance")
                    {
                        string stanceValue = !string.IsNullOrWhiteSpace(action.diplomacyStance)
                            ? action.diplomacyStance
                            : action.value;
                        if (action.sourceTeamId > 0 &&
                            action.targetTeamId > 0 &&
                            action.sourceTeamId == action.targetTeamId)
                        {
                            errors.Add($"La regla '{rule.id}' intenta configurar diplomacia de un equipo consigo mismo.");
                        }
                        if (!DiplomacyRuntimeService.TryParseStance(stanceValue, out _))
                            errors.Add($"La regla '{rule.id}' usa postura diplomática desconocida '{stanceValue}'.");
                    }

                    if (normalizedAction == "start-wave-controller" ||
                        normalizedAction == "pause-wave-controller" ||
                        normalizedAction == "resume-wave-controller" ||
                        normalizedAction == "stop-wave-controller" ||
                        normalizedAction == "advance-wave-controller")
                    {
                        if (string.IsNullOrWhiteSpace(action.waveControllerId) &&
                            string.IsNullOrWhiteSpace(action.value))
                        {
                            errors.Add($"La regla '{rule.id}' declara '{action.type}' sin waveControllerId.");
                        }
                    }

                    if (!string.Equals(action.type, "spawn-entity", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.IsNullOrWhiteSpace(action.entityId))
                    {
                        if (string.IsNullOrWhiteSpace(action.entityIdVariable))
                            errors.Add($"La regla '{rule.id}' declara spawn-entity sin entityId ni entityIdVariable.");
                        continue;
                    }
                    ValidateEntityReference(
                        scenario.id,
                        action.entityId,
                        packageId,
                        localEntities,
                        errors,
                        $"regla '{rule.id}'");
                }
            }
        }

        string defaultTerrain = scenario.terrain?.defaultTerrainId;
        if (!string.IsNullOrWhiteSpace(defaultTerrain))
        {
            ContentReference reference = ContentReference.Parse(defaultTerrain);
            if (reference.IsQualified)
            {
                if (!reference.IsBase && !string.Equals(reference.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"El escenario '{scenario.id}' referencia el paquete externo no declarado '{reference.PackageId}'.");
                else if (!reference.IsBase && !localTerrains.Contains(reference.LocalId))
                    errors.Add($"El escenario '{scenario.id}' referencia el terreno inexistente '{defaultTerrain}'.");
            }
            else if (!localTerrains.Contains(reference.LocalId))
            {
                errors.Add($"El escenario '{scenario.id}' referencia el terreno local inexistente '{defaultTerrain}'. Usa base:{defaultTerrain} para contenido del juego base.");
            }
        }

        if (scenario.terrain?.tiles == null)
            return;
        foreach (ScenarioTerrainTilePlacement tile in scenario.terrain.tiles)
        {
            if (tile == null || string.IsNullOrWhiteSpace(tile.terrainId))
                continue;
            ContentReference reference = ContentReference.Parse(tile.terrainId);
            if (reference.IsQualified)
            {
                if (!reference.IsBase && !string.Equals(reference.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"El escenario '{scenario.id}' referencia el paquete externo no declarado '{reference.PackageId}'.");
                else if (!reference.IsBase && !localTerrains.Contains(reference.LocalId))
                    errors.Add($"El escenario '{scenario.id}' referencia el terreno inexistente '{tile.terrainId}'.");
            }
            else if (!localTerrains.Contains(reference.LocalId))
            {
                errors.Add($"El escenario '{scenario.id}' referencia el terreno local inexistente '{tile.terrainId}'.");
            }
        }
    }


    private static void ValidateNavigation(
        ScenarioDefinition scenario,
        List<string> errors)
    {
        ScenarioNavigationDefinition navigation = scenario?.navigation;
        if (navigation == null)
            return;

        if (navigation.cellSize <= 0f)
            errors.Add($"El escenario '{scenario.id}' debe declarar navigation.cellSize mayor que cero.");
        if (navigation.obstacleRefreshInterval <= 0f)
            errors.Add($"El escenario '{scenario.id}' debe declarar navigation.obstacleRefreshInterval mayor que cero.");
        if (navigation.repathInterval <= 0f)
            errors.Add($"El escenario '{scenario.id}' debe declarar navigation.repathInterval mayor que cero.");
        if (navigation.arrivalTolerance <= 0f)
            errors.Add($"El escenario '{scenario.id}' debe declarar navigation.arrivalTolerance mayor que cero.");
        if (navigation.attackMoveAcquisitionRange <= 0f)
            errors.Add($"El escenario '{scenario.id}' debe declarar navigation.attackMoveAcquisitionRange mayor que cero.");
        if (navigation.individualAiInterval <= 0f)
            errors.Add($"El escenario '{scenario.id}' debe declarar navigation.individualAiInterval mayor que cero.");
    }


    private static void ValidateHeadlessProfiles(
        ScenarioDefinition scenario,
        List<string> errors)
    {
        if (scenario?.headlessProfiles == null)
            return;

        HashSet<string> profileIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ScenarioHeadlessProfileDefinition profile in scenario.headlessProfiles)
        {
            if (profile == null)
                continue;
            if (string.IsNullOrWhiteSpace(profile.id))
            {
                errors.Add($"El escenario '{scenario.id}' contiene un perfil Headless sin id.");
                continue;
            }
            if (!profileIds.Add(profile.id.Trim()))
                errors.Add($"Perfil Headless duplicado en '{scenario.id}': {profile.id}.");
            if (profile.maximumInstances <= 0)
                errors.Add($"El perfil Headless '{profile.id}' declara maximumInstances no válido.");

            if (!profile.runtimeImplemented)
                continue;
            if (string.IsNullOrWhiteSpace(profile.runtimeControllerId))
            {
                errors.Add($"El perfil Headless '{profile.id}' está implementado pero no declara runtimeControllerId.");
                continue;
            }
            if (!HeadlessControllerRegistry.IsRegistered(profile.runtimeControllerId))
                errors.Add($"El perfil Headless '{profile.id}' referencia el controlador no registrado '{profile.runtimeControllerId}'.");

            ScenarioHeadlessControllerSettings settings = profile.controllerSettings;
            if (settings == null)
                continue;
            if (settings.updateInterval < 0f)
                errors.Add($"El perfil Headless '{profile.id}' declara updateInterval negativo.");
            if (settings.maxOrdersPerUpdate < 0)
                errors.Add($"El perfil Headless '{profile.id}' declara maxOrdersPerUpdate negativo.");
            if (!string.IsNullOrWhiteSpace(settings.targetPolicy) &&
                !string.Equals(settings.targetPolicy, "nearest-hostile", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"El perfil Headless '{profile.id}' usa targetPolicy desconocida '{settings.targetPolicy}'.");
            }
        }
    }

    private static void ValidateDiplomacy(
        ScenarioDefinition scenario,
        List<string> errors)
    {
        if (scenario?.diplomacy == null)
            return;

        HashSet<long> directedPairs = new();
        foreach (ScenarioDiplomacyDefinition definition in scenario.diplomacy)
        {
            if (definition == null)
                continue;
            if (definition.sourceTeamId <= 0 || definition.targetTeamId <= 0)
            {
                errors.Add($"El escenario '{scenario.id}' declara diplomacia con equipos no válidos.");
                continue;
            }
            if (definition.sourceTeamId == definition.targetTeamId)
                errors.Add($"El escenario '{scenario.id}' intenta configurar diplomacia del equipo {definition.sourceTeamId} consigo mismo.");
            if (scenario.maxTeams > 0 &&
                (definition.sourceTeamId > scenario.maxTeams || definition.targetTeamId > scenario.maxTeams))
            {
                errors.Add($"El escenario '{scenario.id}' declara diplomacia fuera de maxTeams ({scenario.maxTeams}).");
            }
            if (!DiplomacyRuntimeService.TryParseStance(definition.stance, out _))
                errors.Add($"El escenario '{scenario.id}' usa postura diplomática desconocida '{definition.stance}'.");

            long key = ((long)definition.sourceTeamId << 32) ^ (uint)definition.targetTeamId;
            if (!directedPairs.Add(key))
                errors.Add($"El escenario '{scenario.id}' repite la relación {definition.sourceTeamId} → {definition.targetTeamId}.");
        }
    }

    private static void ValidateWaveControllers(
        ScenarioDefinition scenario,
        string packageId,
        HashSet<string> localEntities,
        List<string> errors)
    {
        if (scenario?.waveControllers == null)
            return;

        HashSet<string> controllerIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ScenarioWaveControllerDefinition controller in scenario.waveControllers)
        {
            if (controller == null || !controller.enabled)
                continue;
            if (string.IsNullOrWhiteSpace(controller.id))
            {
                errors.Add($"El escenario '{scenario.id}' contiene un controlador de oleadas sin id.");
                continue;
            }
            if (!controllerIds.Add(controller.id.Trim()))
                errors.Add($"Controlador de oleadas duplicado en '{scenario.id}': {controller.id}.");
            if (controller.initialDelay < 0f || controller.defaultInterWaveDelay < 0f)
                errors.Add($"El controlador '{controller.id}' declara retrasos negativos.");

            string repeatMode = string.IsNullOrWhiteSpace(controller.repeatMode)
                ? ScenarioWaveRepeatModes.None
                : controller.repeatMode.Trim();
            if (!string.Equals(repeatMode, ScenarioWaveRepeatModes.None, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(repeatMode, ScenarioWaveRepeatModes.Loop, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"El controlador '{controller.id}' usa repeatMode desconocido '{controller.repeatMode}'.");
            }

            if (controller.waves == null || controller.waves.Length == 0)
            {
                errors.Add($"El controlador '{controller.id}' no contiene oleadas.");
                continue;
            }

            HashSet<string> waveIds = new(StringComparer.OrdinalIgnoreCase);
            for (int waveIndex = 0; waveIndex < controller.waves.Length; waveIndex++)
            {
                ScenarioWaveDefinition wave = controller.waves[waveIndex];
                if (wave == null)
                {
                    errors.Add($"El controlador '{controller.id}' contiene una oleada nula en índice {waveIndex}.");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(wave.id) && !waveIds.Add(wave.id.Trim()))
                    errors.Add($"El controlador '{controller.id}' contiene wave id duplicado '{wave.id}'.");
                if (wave.preparationTime < 0f || wave.delayAfterCompletion < -1f)
                    errors.Add($"La oleada '{wave.id ?? waveIndex.ToString()}' declara tiempos negativos.");

                string completion = string.IsNullOrWhiteSpace(wave.completionCondition)
                    ? ScenarioWaveCompletionConditions.AllSpawnedResolved
                    : wave.completionCondition.Trim();
                bool supportedCompletion =
                    string.Equals(completion, ScenarioWaveCompletionConditions.SpawnComplete, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(completion, "all-groups-spawned", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(completion, ScenarioWaveCompletionConditions.AllSpawnedResolved, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(completion, "all-spawned-defeated", StringComparison.OrdinalIgnoreCase);
                if (!supportedCompletion)
                    errors.Add($"La oleada '{wave.id}' usa completionCondition desconocida '{wave.completionCondition}'.");

                if (wave.groups == null || wave.groups.Length == 0)
                {
                    errors.Add($"La oleada '{wave.id ?? waveIndex.ToString()}' no contiene grupos.");
                    continue;
                }

                HashSet<string> groupIds = new(StringComparer.OrdinalIgnoreCase);
                for (int groupIndex = 0; groupIndex < wave.groups.Length; groupIndex++)
                {
                    ScenarioWaveGroupDefinition group = wave.groups[groupIndex];
                    if (group == null)
                    {
                        errors.Add($"La oleada '{wave.id}' contiene un grupo nulo en índice {groupIndex}.");
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(group.id) && !groupIds.Add(group.id.Trim()))
                        errors.Add($"La oleada '{wave.id}' contiene group id duplicado '{group.id}'.");
                    if (string.IsNullOrWhiteSpace(group.entityId))
                    {
                        errors.Add($"El grupo '{group.id}' del controlador '{controller.id}' no declara entityId.");
                    }
                    else
                    {
                        ValidateEntityReference(
                            scenario.id,
                            group.entityId,
                            packageId,
                            localEntities,
                            errors,
                            $"oleada '{wave.id}', grupo '{group.id}'");
                    }
                    if (group.count <= 0)
                        errors.Add($"El grupo '{group.id}' debe declarar count mayor que cero.");
                    if (group.startDelay < 0f || group.spawnInterval < 0f || group.positionJitterRadius < 0f)
                        errors.Add($"El grupo '{group.id}' declara tiempos o radio negativos.");
                }
            }
        }
    }

    private static void ValidateEntityCombatDefinitions(string folder, List<string> errors)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                EntityDefinition definition = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
                if (definition?.attack == null)
                    continue;

                EntityAttackDefinition attack = definition.attack;
                if (string.IsNullOrWhiteSpace(attack.damageType))
                    errors.Add($"La entidad '{definition.id}' debe declarar attack.damageType.");
                if (attack.baseDamage <= 0)
                    errors.Add($"La entidad '{definition.id}' debe declarar attack.baseDamage mayor que cero.");
                if (attack.baseAttackSpeed <= 0f)
                    errors.Add($"La entidad '{definition.id}' debe declarar attack.baseAttackSpeed mayor que cero.");
                if (attack.attackTime < 0f || attack.recoveryTime < 0f ||
                    attack.attackTime + attack.recoveryTime <= 0f)
                {
                    errors.Add($"La entidad '{definition.id}' debe declarar un ciclo attackTime/recoveryTime válido.");
                }
                if (attack.attackRange <= 0f)
                    errors.Add($"La entidad '{definition.id}' debe declarar attack.attackRange mayor que cero.");

                string delivery = EntityCombatRules.NormalizeDelivery(attack.delivery);
                if (!string.Equals(delivery, EntityAttackDeliveryTypes.Melee, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"La entidad '{definition.id}' usa el delivery '{attack.delivery}', que aún no está implementado.");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"No se pudo validar el combate de {Path.GetFileName(file)}: {exception.Message}");
            }
        }
    }

    private static void ValidateEntityLifeDefinitions(
        string folder,
        string packageId,
        HashSet<string> localEntities,
        List<string> errors)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                EntityDefinition definition =
                    JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
                if (definition?.life == null)
                    continue;

                EntityLifeDefinition life = definition.life;
                EntityDeathOutcome outcome;
                if (string.IsNullOrWhiteSpace(life.deathOutcome))
                {
                    outcome = life.removeOnDeath
                        ? EntityDeathOutcome.Despawn
                        : EntityDeathOutcome.Remain;
                }
                else if (!Enum.TryParse(life.deathOutcome, true, out outcome))
                {
                    errors.Add(
                        $"La entidad '{definition.id}' declara life.deathOutcome " +
                        $"desconocido: '{life.deathOutcome}'.");
                    continue;
                }

                float delay = life.deathOutcomeDelay >= 0f
                    ? life.deathOutcomeDelay
                    : life.deathRemovalDelay;
                if (delay < 0f)
                    errors.Add($"La entidad '{definition.id}' no puede declarar un retraso de muerte negativo.");

                if (outcome != EntityDeathOutcome.Replace)
                    continue;

                if (string.IsNullOrWhiteSpace(life.deathReplacementEntityId))
                {
                    errors.Add(
                        $"La entidad '{definition.id}' usa life.deathOutcome='replace' " +
                        "sin declarar deathReplacementEntityId.");
                    continue;
                }

                ValidateEntityReference(
                    $"definición {definition.id}",
                    life.deathReplacementEntityId,
                    packageId,
                    localEntities,
                    errors,
                    "resultado de muerte");
            }
            catch (Exception exception)
            {
                errors.Add(
                    $"No se pudo validar el resultado de muerte de " +
                    $"{Path.GetFileName(file)}: {exception.Message}");
            }
        }
    }

    private static void ValidateEntityAreaDefinitions(string folder, List<string> errors)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                EntityDefinition definition = JsonUtility.FromJson<EntityDefinition>(File.ReadAllText(file));
                if (definition?.area == null)
                    continue;

                string shape = string.IsNullOrWhiteSpace(definition.area.shape)
                    ? EntityAreaShapes.Circle
                    : definition.area.shape.Trim();
                if (!string.Equals(shape, EntityAreaShapes.Circle, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(shape, EntityAreaShapes.Rectangle, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"La entidad '{definition.id}' usa la forma de área desconocida '{definition.area.shape}'.");
                }

                if (string.Equals(shape, EntityAreaShapes.Circle, StringComparison.OrdinalIgnoreCase) &&
                    definition.area.radius <= 0f)
                {
                    errors.Add($"La entidad '{definition.id}' debe declarar area.radius mayor que cero.");
                }

                if (string.Equals(shape, EntityAreaShapes.Rectangle, StringComparison.OrdinalIgnoreCase) &&
                    (definition.area.size == null || definition.area.size.x <= 0f || definition.area.size.z <= 0f))
                {
                    errors.Add($"La entidad '{definition.id}' debe declarar area.size x/z mayores que cero.");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"No se pudo validar el área de {Path.GetFileName(file)}: {exception.Message}");
            }
        }
    }

    private static void ValidateEntityReference(
        string scenarioId,
        string entityId,
        string packageId,
        HashSet<string> localEntities,
        List<string> errors,
        string usage)
    {
        ContentReference reference = ContentReference.Parse(entityId);
        if (reference.IsQualified)
        {
            if (reference.IsBase)
            {
                if (!EntityDefinitionRepository.Exists(reference.ToString()))
                    errors.Add($"El escenario '{scenarioId}' usa en {usage} la entidad base inexistente '{entityId}'.");
                return;
            }

            if (!string.Equals(reference.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"El escenario '{scenarioId}' referencia el paquete externo no declarado '{reference.PackageId}'.");
                return;
            }

            if (!localEntities.Contains(reference.LocalId))
                errors.Add($"El escenario '{scenarioId}' usa en {usage} la entidad inexistente '{entityId}'.");
            return;
        }

        if (!localEntities.Contains(reference.LocalId))
        {
            errors.Add($"El escenario '{scenarioId}' usa en {usage} la entidad local inexistente '{entityId}'. " +
                       $"Usa base:{entityId} para contenido del juego base.");
        }
    }

    private static bool IsSafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96)
            return false;
        return value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_');
    }

    private static bool IsSafeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
            return false;
        return value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_');
    }

    private static string Relative(string root, string file)
    {
        return Path.GetRelativePath(root, file).Replace('\\', '/');
    }
}
