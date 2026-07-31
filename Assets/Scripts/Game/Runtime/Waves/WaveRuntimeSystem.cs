using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Orquestador autoritativo de oleadas definido por el escenario. Solo agenda
/// spawns y publica eventos genéricos; no conoce Kodos, enemigos ni objetivos.
/// </summary>
public sealed class WaveRuntimeSystem
{
    private const int MaxSpawnsPerUpdate = 128;

    private readonly MatchParticipantRegistry participants;
    private readonly EntityWorld world;
    private readonly EntityLifecycleService lifecycle;
    private readonly RuntimeEventBus eventBus;
    private readonly Dictionary<string, WaveControllerRuntimeState> controllers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingWaveSpawn> pendingByScenarioInstance =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, WaveControllerRuntimeState> controllerByEntityId = new();
    private readonly System.Random random;
    private int spawnSequence;

    public int ControllerCount => controllers.Count;
    public IReadOnlyList<WaveControllerRuntimeState> Controllers => controllers.Values
        .OrderBy(item => item.ControllerId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public WaveRuntimeSystem(
        ScenarioWaveControllerDefinition[] definitions,
        MatchParticipantRegistry participants,
        EntityWorld world,
        EntityLifecycleService lifecycle,
        RuntimeEventBus eventBus,
        string scenarioId)
    {
        this.participants = participants ?? throw new ArgumentNullException(nameof(participants));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        random = new System.Random(StableHash(scenarioId));

        if (definitions != null)
        {
            int generatedIndex = 0;
            foreach (ScenarioWaveControllerDefinition definition in definitions)
            {
                if (definition == null || !definition.enabled)
                    continue;

                string id = string.IsNullOrWhiteSpace(definition.id)
                    ? $"wave-controller.{++generatedIndex}"
                    : definition.id.Trim();
                if (controllers.ContainsKey(id))
                {
                    Debug.LogWarning($"[WaveRuntimeSystem] Controlador de oleadas duplicado: {id}.");
                    continue;
                }

                controllers.Add(id, new WaveControllerRuntimeState
                {
                    ControllerId = id,
                    Definition = definition
                });
            }
        }

        lifecycle.EntitySpawned += OnEntitySpawned;
        lifecycle.EntityDespawned += OnEntityDespawned;
        eventBus.EventPublished += OnRuntimeEvent;
    }

    public void StartAutomaticControllers(float elapsedTime)
    {
        foreach (WaveControllerRuntimeState controller in controllers.Values)
        {
            if (controller.Definition.autoStart)
                TryStart(controller.ControllerId, elapsedTime, out _);
        }
    }

    public void Update(float elapsedTime)
    {
        int remainingSpawnBudget = MaxSpawnsPerUpdate;
        foreach (WaveControllerRuntimeState controller in controllers.Values)
        {
            if (controller.Status == WaveControllerRuntimeStatus.Paused ||
                controller.Status == WaveControllerRuntimeStatus.Idle ||
                controller.Status == WaveControllerRuntimeStatus.Completed ||
                controller.Status == WaveControllerRuntimeStatus.Stopped)
            {
                continue;
            }

            if (controller.Status == WaveControllerRuntimeStatus.Preparing)
            {
                if (elapsedTime >= controller.PhaseEndsAt)
                    BeginCurrentWave(controller, elapsedTime);
                continue;
            }

            if (controller.Status == WaveControllerRuntimeStatus.Spawning)
            {
                ProcessSpawns(controller, elapsedTime, ref remainingSpawnBudget);
                if (AllGroupsCompleted(controller))
                {
                    if (IsSpawnCompletion(controller.CurrentWaveDefinition))
                        CompleteCurrentWave(controller, elapsedTime, false);
                    else
                        controller.Status = WaveControllerRuntimeStatus.WaitingForResolution;
                }
                continue;
            }

            if (controller.Status == WaveControllerRuntimeStatus.WaitingForResolution &&
                controller.PendingSpawnCount <= 0 &&
                controller.ActiveEntityCount <= 0)
            {
                CompleteCurrentWave(controller, elapsedTime, false);
            }
        }
    }

    public bool TryGet(string controllerId, out WaveControllerRuntimeState controller)
    {
        controller = null;
        return !string.IsNullOrWhiteSpace(controllerId) &&
               controllers.TryGetValue(controllerId.Trim(), out controller);
    }

    public bool TryStart(string controllerId, float elapsedTime, out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(controllerId, out WaveControllerRuntimeState controller))
        {
            rejectionReason = $"No existe el controlador de oleadas '{controllerId}'.";
            return false;
        }
        if (controller.Definition.waves == null || controller.Definition.waves.Length == 0)
        {
            rejectionReason = $"El controlador '{controller.ControllerId}' no contiene oleadas.";
            return false;
        }
        if (controller.Status != WaveControllerRuntimeStatus.Idle &&
            controller.Status != WaveControllerRuntimeStatus.Completed &&
            controller.Status != WaveControllerRuntimeStatus.Stopped)
        {
            rejectionReason = $"El controlador '{controller.ControllerId}' ya está ejecutándose.";
            return false;
        }

        RemoveTrackedEntities(controller);
        controller.ClearForRestart();
        controller.Cycle = 1;
        controller.CurrentWaveIndex = 0;
        controller.Status = WaveControllerRuntimeStatus.Preparing;
        PublishWaveEvent(RuntimeEventType.WaveControllerStarted, controller, null, elapsedTime, "start");
        PrepareWave(controller, 0, elapsedTime, Mathf.Max(0f, controller.Definition.initialDelay));
        return true;
    }

    public bool TryPause(string controllerId, float elapsedTime, out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(controllerId, out WaveControllerRuntimeState controller))
        {
            rejectionReason = $"No existe el controlador de oleadas '{controllerId}'.";
            return false;
        }
        if (controller.Status == WaveControllerRuntimeStatus.Paused)
            return true;
        if (controller.Status == WaveControllerRuntimeStatus.Idle ||
            controller.Status == WaveControllerRuntimeStatus.Completed ||
            controller.Status == WaveControllerRuntimeStatus.Stopped)
        {
            rejectionReason = $"El controlador '{controller.ControllerId}' no se puede pausar en estado {controller.Status}.";
            return false;
        }

        controller.StatusBeforePause = controller.Status;
        controller.Status = WaveControllerRuntimeStatus.Paused;
        controller.PausedAt = elapsedTime;
        PublishWaveEvent(RuntimeEventType.WaveControllerPaused, controller, controller.CurrentWaveDefinition, elapsedTime, "pause");
        return true;
    }

    public bool TryResume(string controllerId, float elapsedTime, out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(controllerId, out WaveControllerRuntimeState controller))
        {
            rejectionReason = $"No existe el controlador de oleadas '{controllerId}'.";
            return false;
        }
        if (controller.Status != WaveControllerRuntimeStatus.Paused)
        {
            rejectionReason = $"El controlador '{controller.ControllerId}' no está pausado.";
            return false;
        }

        float pausedDuration = Mathf.Max(0f, elapsedTime - controller.PausedAt);
        controller.PhaseEndsAt += pausedDuration;
        foreach (WaveGroupRuntimeState group in controller.MutableGroups)
            group.NextSpawnAt += pausedDuration;
        controller.Status = controller.StatusBeforePause;
        controller.PausedAt = 0f;
        PublishWaveEvent(RuntimeEventType.WaveControllerResumed, controller, controller.CurrentWaveDefinition, elapsedTime, "resume");
        return true;
    }

    public bool TryStop(string controllerId, float elapsedTime, out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(controllerId, out WaveControllerRuntimeState controller))
        {
            rejectionReason = $"No existe el controlador de oleadas '{controllerId}'.";
            return false;
        }
        if (controller.Status == WaveControllerRuntimeStatus.Stopped)
            return true;

        controller.Status = WaveControllerRuntimeStatus.Stopped;
        PublishWaveEvent(RuntimeEventType.WaveControllerStopped, controller, controller.CurrentWaveDefinition, elapsedTime, "stop");
        return true;
    }

    public bool TryAdvance(string controllerId, float elapsedTime, out string rejectionReason)
    {
        rejectionReason = null;
        if (!TryGet(controllerId, out WaveControllerRuntimeState controller))
        {
            rejectionReason = $"No existe el controlador de oleadas '{controllerId}'.";
            return false;
        }
        if (controller.Status == WaveControllerRuntimeStatus.Idle ||
            controller.Status == WaveControllerRuntimeStatus.Completed ||
            controller.Status == WaveControllerRuntimeStatus.Stopped)
        {
            rejectionReason = $"El controlador '{controller.ControllerId}' no tiene una oleada activa.";
            return false;
        }

        CompleteCurrentWave(controller, elapsedTime, true);
        return true;
    }

    private void PrepareWave(
        WaveControllerRuntimeState controller,
        int waveIndex,
        float elapsedTime,
        float externalDelay)
    {
        ScenarioWaveDefinition[] waves = controller.Definition.waves;
        if (waves == null || waveIndex < 0 || waveIndex >= waves.Length)
        {
            CompleteController(controller, elapsedTime);
            return;
        }

        ScenarioWaveDefinition wave = waves[waveIndex];
        controller.BeginWaveState(wave, waveIndex);
        float preparation = Mathf.Max(0f, wave?.preparationTime ?? 0f);
        controller.PhaseEndsAt = elapsedTime + Mathf.Max(0f, externalDelay) + preparation;
        controller.Status = WaveControllerRuntimeStatus.Preparing;
        PublishWaveEvent(RuntimeEventType.WavePreparationStarted, controller, wave, elapsedTime, "preparation");
    }

    private void BeginCurrentWave(WaveControllerRuntimeState controller, float elapsedTime)
    {
        ScenarioWaveDefinition wave = controller.CurrentWaveDefinition;
        controller.MutableGroups.Clear();
        ScenarioWaveGroupDefinition[] groups = wave?.groups ?? Array.Empty<ScenarioWaveGroupDefinition>();
        for (int index = 0; index < groups.Length; index++)
        {
            ScenarioWaveGroupDefinition definition = groups[index];
            if (definition == null)
                continue;
            controller.MutableGroups.Add(new WaveGroupRuntimeState
            {
                GroupId = string.IsNullOrWhiteSpace(definition.id) ? $"group.{index + 1}" : definition.id.Trim(),
                RequestedCount = Mathf.Max(0, definition.count),
                NextSpawnAt = elapsedTime + Mathf.Max(0f, definition.startDelay)
            });
        }

        controller.Status = WaveControllerRuntimeStatus.Spawning;
        PublishWaveEvent(RuntimeEventType.WaveStarted, controller, wave, elapsedTime, "wave-start");
        if (controller.MutableGroups.Count == 0)
            CompleteCurrentWave(controller, elapsedTime, false);
    }

    private void ProcessSpawns(
        WaveControllerRuntimeState controller,
        float elapsedTime,
        ref int remainingSpawnBudget)
    {
        ScenarioWaveGroupDefinition[] definitions = controller.CurrentWaveDefinition?.groups ??
                                                    Array.Empty<ScenarioWaveGroupDefinition>();
        int runtimeIndex = 0;
        for (int definitionIndex = 0;
             definitionIndex < definitions.Length && runtimeIndex < controller.MutableGroups.Count;
             definitionIndex++)
        {
            ScenarioWaveGroupDefinition definition = definitions[definitionIndex];
            if (definition == null)
                continue;

            WaveGroupRuntimeState group = controller.MutableGroups[runtimeIndex++];
            if (group.Completed)
                continue;
            if (!group.Started && elapsedTime >= group.NextSpawnAt)
            {
                group.Started = true;
                PublishWaveEvent(RuntimeEventType.WaveGroupStarted, controller, controller.CurrentWaveDefinition, elapsedTime, group.GroupId);
            }
            if (!group.Started)
                continue;

            float interval = Mathf.Max(0f, definition.spawnInterval);
            while (group.QueuedCount + group.FailedCount < group.RequestedCount &&
                   elapsedTime >= group.NextSpawnAt &&
                   remainingSpawnBudget > 0)
            {
                remainingSpawnBudget--;
                if (QueueGroupSpawn(controller, definition, group, elapsedTime))
                    group.QueuedCount++;
                else
                    group.FailedCount++;

                group.NextSpawnAt = interval <= 0f
                    ? elapsedTime
                    : group.NextSpawnAt + interval;
            }

            if (group.QueuedCount + group.FailedCount >= group.RequestedCount)
            {
                group.Completed = true;
                PublishWaveEvent(RuntimeEventType.WaveGroupCompleted, controller, controller.CurrentWaveDefinition, elapsedTime, group.GroupId);
            }
        }
    }

    private bool QueueGroupSpawn(
        WaveControllerRuntimeState controller,
        ScenarioWaveGroupDefinition definition,
        WaveGroupRuntimeState group,
        float elapsedTime)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.entityId))
            return false;

        ResolveOwnership(definition, out int ownerParticipantId, out int teamId, out int colorId);
        if (teamId > 0 && ownerParticipantId <= 0)
        {
            Debug.LogWarning(
                $"[WaveRuntimeSystem] El grupo '{group.GroupId}' del controlador '{controller.ControllerId}' " +
                $"no encontró un participante propietario para el equipo {teamId}.");
            return false;
        }

        Vector3 position = ResolveSpawnPosition(definition, teamId);
        string scenarioInstanceId =
            $"wave.{Sanitize(controller.ControllerId)}.{controller.Cycle}.{controller.CurrentWaveIndex + 1}." +
            $"{Sanitize(group.GroupId)}.{++spawnSequence}";
        EntitySpawnRequest request = new()
        {
            EntityDefinitionId = definition.entityId,
            ScenarioInstanceId = scenarioInstanceId,
            InstanceAttributes = definition.attributes,
            OwnerParticipantId = ownerParticipantId,
            TeamId = teamId,
            ColorId = colorId,
            Position = position,
            AlignToDefinitionGround = true,
            Reason = EntityLifecycleReason.Wave
        };

        if (!lifecycle.QueueSpawn(request, out string rejectionReason))
        {
            Debug.LogWarning(
                $"[WaveRuntimeSystem] Spawn rechazado en {controller.ControllerId}/{group.GroupId}: {rejectionReason}");
            controller.TotalFailedThisWave++;
            return false;
        }

        pendingByScenarioInstance[scenarioInstanceId] = new PendingWaveSpawn
        {
            ScenarioInstanceId = scenarioInstanceId,
            Controller = controller,
            GroupId = group.GroupId
        };
        controller.PendingSpawnCount++;
        controller.TotalQueuedThisWave++;
        return true;
    }

    private void CompleteCurrentWave(
        WaveControllerRuntimeState controller,
        float elapsedTime,
        bool forced)
    {
        ScenarioWaveDefinition completedWave = controller.CurrentWaveDefinition;
        PublishWaveEvent(RuntimeEventType.WaveCompleted, controller, completedWave, elapsedTime, forced ? "forced-advance" : "completed");
        if (forced || IsSpawnCompletion(completedWave))
            RemoveTrackedEntities(controller);

        int nextWaveIndex = controller.CurrentWaveIndex + 1;
        ScenarioWaveDefinition[] waves = controller.Definition.waves ?? Array.Empty<ScenarioWaveDefinition>();
        if (nextWaveIndex < waves.Length)
        {
            float delay = completedWave != null && completedWave.delayAfterCompletion >= 0f
                ? completedWave.delayAfterCompletion
                : Mathf.Max(0f, controller.Definition.defaultInterWaveDelay);
            PrepareWave(controller, nextWaveIndex, elapsedTime, delay);
            return;
        }

        bool loop = string.Equals(
            controller.Definition.repeatMode,
            ScenarioWaveRepeatModes.Loop,
            StringComparison.OrdinalIgnoreCase);
        int repeatCount = controller.Definition.repeatCount;
        if (loop && (repeatCount <= 0 || controller.Cycle < repeatCount))
        {
            controller.Cycle++;
            float delay = completedWave != null && completedWave.delayAfterCompletion >= 0f
                ? completedWave.delayAfterCompletion
                : Mathf.Max(0f, controller.Definition.defaultInterWaveDelay);
            PrepareWave(controller, 0, elapsedTime, delay);
            return;
        }

        CompleteController(controller, elapsedTime);
    }

    private void CompleteController(WaveControllerRuntimeState controller, float elapsedTime)
    {
        controller.Status = WaveControllerRuntimeStatus.Completed;
        PublishWaveEvent(RuntimeEventType.WaveControllerCompleted, controller, controller.CurrentWaveDefinition, elapsedTime, "completed");
    }

    private void OnEntitySpawned(EntitySpawnedEvent lifecycleEvent)
    {
        EntityRuntimeState entity = lifecycleEvent?.Entity;
        if (entity == null || lifecycleEvent.Reason != EntityLifecycleReason.Wave ||
            string.IsNullOrWhiteSpace(entity.ScenarioInstanceId) ||
            !pendingByScenarioInstance.TryGetValue(entity.ScenarioInstanceId, out PendingWaveSpawn pending))
        {
            return;
        }

        pendingByScenarioInstance.Remove(entity.ScenarioInstanceId);
        pending.Controller.PendingSpawnCount = Mathf.Max(0, pending.Controller.PendingSpawnCount - 1);
        pending.Controller.AddActiveEntity(entity.UnitId);
        controllerByEntityId[entity.UnitId] = pending.Controller;
    }

    private void OnEntityDespawned(EntityDespawnedEvent lifecycleEvent)
    {
        ResolveTrackedEntity(lifecycleEvent?.EntityId ?? -1);
    }

    private void OnRuntimeEvent(RuntimeEventContext runtimeEvent)
    {
        if (runtimeEvent?.Type == RuntimeEventType.EntityDied)
            ResolveTrackedEntity(runtimeEvent.EntityId);
    }

    private void ResolveTrackedEntity(int entityId)
    {
        if (entityId <= 0 || !controllerByEntityId.TryGetValue(entityId, out WaveControllerRuntimeState controller))
            return;
        controllerByEntityId.Remove(entityId);
        controller.RemoveActiveEntity(entityId);
    }

    private void RemoveTrackedEntities(WaveControllerRuntimeState controller)
    {
        foreach (int entityId in controller.ActiveEntityIds.ToArray())
        {
            controllerByEntityId.Remove(entityId);
            controller.RemoveActiveEntity(entityId);
        }
        foreach (string token in pendingByScenarioInstance
                     .Where(item => ReferenceEquals(item.Value.Controller, controller))
                     .Select(item => item.Key)
                     .ToArray())
        {
            pendingByScenarioInstance.Remove(token);
        }
        controller.PendingSpawnCount = 0;
    }

    private void ResolveOwnership(
        ScenarioWaveGroupDefinition definition,
        out int ownerParticipantId,
        out int teamId,
        out int colorId)
    {
        MatchParticipantRuntimeState owner = null;
        if (definition.ownerParticipantId > 0)
            participants.TryGet(definition.ownerParticipantId, out owner);
        if (owner == null && !string.IsNullOrWhiteSpace(definition.ownerSlotId))
            participants.TryGetBySlotId(definition.ownerSlotId, out owner);
        if (owner == null && definition.teamId > 0)
        {
            owner = participants.All
                .Where(item => item.TeamId == definition.teamId)
                .OrderBy(item => item.SlotIndex)
                .ThenBy(item => item.ParticipantId)
                .FirstOrDefault();
        }

        ownerParticipantId = owner?.ParticipantId ?? -1;
        teamId = owner?.TeamId ?? definition.teamId;
        colorId = definition.colorId >= 0 ? definition.colorId : owner?.ColorId ?? -1;
    }

    private Vector3 ResolveSpawnPosition(ScenarioWaveGroupDefinition definition, int teamId)
    {
        Vector3 center = definition.position?.ToVector3() ?? Vector3.zero;
        EntityRuntimeState area = null;
        if (!string.IsNullOrWhiteSpace(definition.spawnAreaAttribute))
        {
            area = world.Values
                .Where(item => item.Area != null &&
                               item.Life?.State != EntityLifeState.Dead &&
                               item.Attributes?.Has(definition.spawnAreaAttribute) == true)
                .OrderByDescending(item => teamId > 0 && item.TeamId == teamId)
                .ThenByDescending(item => item.TeamId == 0)
                .ThenBy(item => item.UnitId)
                .FirstOrDefault();
            if (area != null)
                center = area.Position;
        }

        Vector3 offset = Vector3.zero;
        if (area?.Area != null && definition.randomizePositionInArea)
        {
            if (string.Equals(area.Area.Shape, EntityAreaShapes.Rectangle, StringComparison.OrdinalIgnoreCase))
            {
                offset.x = Range(-area.Area.Size.x * 0.5f, area.Area.Size.x * 0.5f);
                offset.z = Range(-area.Area.Size.z * 0.5f, area.Area.Size.z * 0.5f);
            }
            else
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(Range(0f, 1f)) * Mathf.Max(0f, area.Area.Radius);
                offset.x = Mathf.Cos(angle) * radius;
                offset.z = Mathf.Sin(angle) * radius;
            }
        }

        float jitter = Mathf.Max(0f, definition.positionJitterRadius);
        if (jitter > 0f)
        {
            float angle = Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Range(0f, 1f)) * jitter;
            offset.x += Mathf.Cos(angle) * radius;
            offset.z += Mathf.Sin(angle) * radius;
        }

        return center + offset;
    }

    private void PublishWaveEvent(
        RuntimeEventType type,
        WaveControllerRuntimeState controller,
        ScenarioWaveDefinition wave,
        float elapsedTime,
        string reasonOrGroup)
    {
        eventBus.Publish(new RuntimeEventContext
        {
            Type = type,
            ElapsedTime = elapsedTime,
            WaveControllerId = controller.ControllerId,
            WaveId = string.IsNullOrWhiteSpace(wave?.id) ? controller.CurrentWaveId : wave.id,
            WaveIndex = controller.CurrentWaveIndex,
            WaveCycle = controller.Cycle,
            WaveControllerStatus = controller.Status.ToString(),
            WaveGroupId = type == RuntimeEventType.WaveGroupStarted ||
                          type == RuntimeEventType.WaveGroupCompleted
                ? reasonOrGroup
                : null,
            Reason = reasonOrGroup
        });
    }

    private static bool IsSpawnCompletion(ScenarioWaveDefinition wave)
    {
        return string.Equals(
            wave?.completionCondition,
            ScenarioWaveCompletionConditions.SpawnComplete,
            StringComparison.OrdinalIgnoreCase) ||
               string.Equals(wave?.completionCondition, "all-groups-spawned", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AllGroupsCompleted(WaveControllerRuntimeState controller)
    {
        return controller.Groups.Count == 0 || controller.Groups.All(item => item.Completed);
    }

    private float Range(float minimum, float maximum)
    {
        return minimum + (float)random.NextDouble() * (maximum - minimum);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value ?? "GammaSix")
                hash = hash * 31 + character;
            return hash;
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unnamed";
        return new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
    }
}
