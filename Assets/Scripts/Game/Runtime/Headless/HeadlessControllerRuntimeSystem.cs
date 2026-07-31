using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Hospeda controladores Headless únicamente en la autoridad de la partida.
/// Cada controlador percibe una vista de solo lectura y modifica el gameplay
/// exclusivamente mediante MatchCommandBus.
/// </summary>
public sealed class HeadlessControllerRuntimeSystem
{
    private sealed class RuntimeInstance
    {
        public MatchParticipantRuntimeState Participant;
        public HeadlessProfileDefinition Profile;
        public ScenarioHeadlessControllerSettings Settings;
        public IHeadlessController Controller;
        public HeadlessControllerInstanceState State;
    }

    private readonly EntityWorld world;
    private readonly DiplomacyRuntimeService diplomacy;
    private readonly Func<int, string, MatchCommandType, object, long> enqueueCommand;
    private readonly List<RuntimeInstance> instances = new();

    public IReadOnlyList<HeadlessControllerInstanceState> Controllers => instances
        .Select(item => item.State)
        .OrderBy(item => item.ParticipantId)
        .ToList();

    public int ControllerCount => instances.Count;
    public int ActiveControllerCount => instances.Count(item =>
        item.State.Status == HeadlessControllerRuntimeStatus.Ready ||
        item.State.Status == HeadlessControllerRuntimeStatus.Running);

    public HeadlessControllerRuntimeSystem(
        ScenarioDefinition scenario,
        MatchParticipantRegistry participants,
        EntityWorld world,
        DiplomacyRuntimeService diplomacy,
        Func<int, string, MatchCommandType, object, long> enqueueCommand)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.diplomacy = diplomacy ?? throw new ArgumentNullException(nameof(diplomacy));
        this.enqueueCommand = enqueueCommand ?? throw new ArgumentNullException(nameof(enqueueCommand));

        Dictionary<string, HeadlessProfileDefinition> profiles =
            HeadlessProfileCatalog.GetAvailableProfiles(scenario)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (MatchParticipantRuntimeState participant in participants?.All ??
                     Array.Empty<MatchParticipantRuntimeState>())
        {
            if (participant == null || !participant.IsHeadless)
                continue;

            RuntimeInstance instance = CreateInstance(participant, profiles);
            instances.Add(instance);
        }
    }

    public void Update(float elapsedTime)
    {
        foreach (RuntimeInstance instance in instances)
        {
            if (instance.Controller == null)
                continue;
            if (elapsedTime + 0.0001f < instance.State.NextUpdateAt)
                continue;

            float interval = NormalizeUpdateInterval(instance.Settings?.updateInterval ?? 0.5f);
            instance.State.NextUpdateAt = elapsedTime + interval;

            if (!instance.Participant.CanIssueCommands)
            {
                instance.State.Status = HeadlessControllerRuntimeStatus.Suspended;
                instance.State.LastDecision = "Suspendido: el participante no puede emitir órdenes.";
                continue;
            }

            try
            {
                instance.State.Status = HeadlessControllerRuntimeStatus.Running;
                instance.State.LastUpdateAt = elapsedTime;
                HeadlessPerceptionContext perception = new(
                    world,
                    instance.Participant,
                    instance.Settings,
                    diplomacy);
                HeadlessControllerUpdateContext context = new(
                    elapsedTime,
                    instance.Participant,
                    instance.Profile,
                    instance.Settings,
                    perception,
                    instance.State,
                    (commandType, payload) => enqueueCommand(
                        instance.Participant.ParticipantId,
                        instance.Profile.Id,
                        commandType,
                        payload));

                instance.Controller.Tick(context);
            }
            catch (Exception exception)
            {
                instance.State.Status = HeadlessControllerRuntimeStatus.Failed;
                instance.State.LastError = exception.Message;
                instance.State.LastDecision = "Controlador deshabilitado por una excepción.";
                Debug.LogError(
                    $"[HeadlessControllerRuntimeSystem] El controlador '{instance.State.RuntimeControllerId}' " +
                    $"del participante {instance.Participant.ParticipantId} falló: {exception}");
                instance.Controller = null;
            }
        }
    }

    private static RuntimeInstance CreateInstance(
        MatchParticipantRuntimeState participant,
        IReadOnlyDictionary<string, HeadlessProfileDefinition> profiles)
    {
        HeadlessControllerInstanceState state = new()
        {
            ParticipantId = participant.ParticipantId,
            ParticipantName = participant.DisplayName,
            ProfileId = participant.ControllerProfileId,
            Status = HeadlessControllerRuntimeStatus.ProfileNotFound,
            LastDecision = "No se encontró el perfil Headless del participante."
        };

        RuntimeInstance instance = new()
        {
            Participant = participant,
            State = state,
            Settings = NormalizeSettings(null)
        };

        if (string.IsNullOrWhiteSpace(participant.ControllerProfileId) ||
            !profiles.TryGetValue(participant.ControllerProfileId, out HeadlessProfileDefinition profile))
        {
            return instance;
        }

        instance.Profile = profile;
        instance.Settings = NormalizeSettings(profile.ControllerSettings);
        state.ProfileId = profile.Id;
        state.RuntimeControllerId = profile.RuntimeControllerId;

        if (!profile.RuntimeImplemented)
        {
            state.Status = HeadlessControllerRuntimeStatus.ProfileNotImplemented;
            state.LastDecision = "El perfil está disponible en el lobby, pero su runtime todavía no está implementado.";
            return instance;
        }

        if (!HeadlessControllerRegistry.TryCreate(profile.RuntimeControllerId, out IHeadlessController controller))
        {
            state.Status = HeadlessControllerRuntimeStatus.ControllerNotRegistered;
            state.LastDecision = $"No existe una implementación registrada para '{profile.RuntimeControllerId}'.";
            return instance;
        }

        instance.Controller = controller;
        state.Status = HeadlessControllerRuntimeStatus.Ready;
        state.NextUpdateAt = 0f;
        controller.Initialize(new HeadlessControllerInitializationContext(participant, profile, state));
        return instance;
    }

    private static ScenarioHeadlessControllerSettings NormalizeSettings(
        ScenarioHeadlessControllerSettings source)
    {
        source ??= new ScenarioHeadlessControllerSettings();
        return new ScenarioHeadlessControllerSettings
        {
            updateInterval = NormalizeUpdateInterval(source.updateInterval),
            maxOrdersPerUpdate = Math.Max(1, source.maxOrdersPerUpdate),
            targetPolicy = string.IsNullOrWhiteSpace(source.targetPolicy)
                ? "nearest-hostile"
                : source.targetPolicy.Trim().ToLowerInvariant(),
            includeNeutralTargets = source.includeNeutralTargets,
            controlledRequiredAttributes = source.controlledRequiredAttributes ?? Array.Empty<string>(),
            controlledExcludedAttributes = source.controlledExcludedAttributes ?? Array.Empty<string>(),
            targetRequiredAttributes = source.targetRequiredAttributes ?? Array.Empty<string>(),
            targetExcludedAttributes = source.targetExcludedAttributes ?? Array.Empty<string>()
        };
    }

    private static float NormalizeUpdateInterval(float value)
    {
        return Mathf.Clamp(value <= 0f ? 0.5f : value, 0.1f, 10f);
    }
}
