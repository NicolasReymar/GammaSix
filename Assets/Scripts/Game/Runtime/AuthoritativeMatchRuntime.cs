using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fuente de verdad de la partida. No conoce UI ni mensajes de red: recibe
/// órdenes autenticadas y aplica sistemas generales en un orden estable.
/// </summary>
public sealed class AuthoritativeMatchRuntime
{
    public EntityWorld World { get; } = new();
    public RuntimeEntityIdAllocator EntityIds { get; } = new();
    public MatchCommandBus CommandBus { get; } = new();

    public EntityLifecycleService EntityLifecycle { get; private set; }
    public EntityQueryService EntityQueries { get; private set; }
    public MatchEntityCatalog EntityCatalog { get; private set; }
    public MatchParticipantRegistry Participants { get; private set; }
    public MatchTeamRegistry Teams { get; private set; }
    public MatchRuntimeState MatchState { get; private set; }
    public RuntimeEventBus EventBus { get; private set; }
    public EntityAreaRuntimeSystem Areas { get; private set; }
    public RuleRuntimeSystem Rules { get; private set; }
    public RuntimeChannelSystem Channels { get; private set; }
    public DamageRuntimeService Damage { get; private set; }
    public CombatRuntimeSystem Combat { get; private set; }
    public MatchWorldBounds WorldBounds { get; private set; }
    public ScenarioDefinition Scenario { get; private set; }
    public float ElapsedTime { get; private set; }
    public bool IsInitialized { get; private set; }

    public event Action<MatchCommandEnvelope, MatchCommandResult> CommandProcessed;
    public event Action<EntitySpawnedEvent> EntitySpawned;
    public event Action<EntityDespawnedEvent> EntityDespawned;
    public event Action<string, bool> RuntimeMessageRaised;

    public AuthoritativeMatchRuntime()
    {
        CommandBus.CommandProcessed += OnCommandProcessed;
    }

    public void Initialize(
        ScenarioDefinition scenario,
        IReadOnlyList<MatchParticipantRuntimeState> participants)
    {
        Scenario = scenario;
        Participants = new MatchParticipantRegistry(participants, scenario);
        Teams = new MatchTeamRegistry(scenario, Participants.All);
        MatchState = new MatchRuntimeState();
        EventBus = new RuntimeEventBus();
        WorldBounds = MatchWorldBounds.FromScenario(scenario);
        ElapsedTime = 0f;
        World.Clear();
        EntityIds.Reset();
        CommandBus.Clear();

        EntityCatalog = MatchEntityCatalog.Create(scenario);
        EntityQueries = new EntityQueryService(World);
        EntityLifecycle = new EntityLifecycleService(
            World,
            EntityIds,
            Participants,
            WorldBounds,
            EntityCatalog);

        SubscribeRuntimeEvents();

        Areas = new EntityAreaRuntimeSystem(World, EventBus);
        Channels = new RuntimeChannelSystem(
            World,
            Participants,
            Areas,
            EventBus);
        Rules = new RuleRuntimeSystem(
            scenario?.rules,
            EventBus,
            Participants,
            Teams,
            World,
            EntityLifecycle,
            MatchState,
            Areas,
            Channels);
        Rules.MessageRaised += OnRuleMessageRaised;
        Damage = new DamageRuntimeService(World, EventBus, Rules);
        Rules.BindDamageService(Damage);
        Combat = new CombatRuntimeSystem(World, Damage);

        bool loadedFromScenario = ScenarioEntitySpawner.TryPopulate(
            EntityLifecycle,
            scenario,
            Participants.All);

        if (!loadedFromScenario)
            ScenarioEntitySpawner.CreateFallback(EntityLifecycle, Participants.All);

        MatchState.Start();
        EventBus.Publish(RuntimeEventContext.MatchStarted(ElapsedTime));
        EventBus.Flush();
        EntityLifecycle.FlushPending();
        EventBus.Flush();
        EntityStatusRuntimeSystem.Update(World, ElapsedTime);

        IsInitialized = true;
        Debug.Log($"[AuthoritativeMatchRuntime] Inicializado con {World.Count} entidades, " +
                  $"{Participants.Count} participantes, {Teams.Count} equipos, " +
                  $"{EntityCatalog.Count} definiciones y {Rules.RuleCount} reglas.");
    }

    public void Update(float deltaTime)
    {
        if (!IsInitialized || MatchState == null || MatchState.IsCompleted)
            return;

        float safeDelta = Mathf.Max(0f, deltaTime);
        ElapsedTime += safeDelta;

        // Orden estable del tick. Las reglas y áreas nunca modifican directamente
        // EntityWorld: encolan cambios en EntityLifecycleService.
        CommandBus.ProcessPending(HandleCommand);
        EntityLifecycle.FlushPending();
        EventBus.Flush();

        ResourceExtractionService.Update(World, EntityLifecycle, safeDelta);
        EntityInteractionService.Update(World);
        Combat.Update(safeDelta, ElapsedTime);
        EventBus.Flush();

        EntityMovementService.Update(World, safeDelta);
        DeathRuntimeService.Update(World, EntityLifecycle, safeDelta);
        EntityStatusRuntimeSystem.Update(World, ElapsedTime);

        Areas.Update(ElapsedTime);
        EventBus.Flush();
        Channels.Update(safeDelta, ElapsedTime);
        EventBus.Flush();
        EntityLifecycle.FlushPending();
        EventBus.Flush();
    }

    public bool QueueEntitySpawn(EntitySpawnRequest request, out string rejectionReason)
    {
        if (EntityLifecycle == null)
        {
            rejectionReason = "El ciclo de vida de entidades no está inicializado.";
            return false;
        }

        return EntityLifecycle.QueueSpawn(request, out rejectionReason);
    }

    public bool QueueEntityDespawn(
        int entityId,
        EntityLifecycleReason reason,
        out string rejectionReason)
    {
        if (EntityLifecycle == null)
        {
            rejectionReason = "El ciclo de vida de entidades no está inicializado.";
            return false;
        }

        return EntityLifecycle.QueueDespawn(entityId, reason, out rejectionReason);
    }

    public bool ApplyDamage(
        int sourceEntityId,
        int targetEntityId,
        int amount,
        string damageType,
        string reason,
        out EntityDamageResult result)
    {
        if (Damage == null)
        {
            result = new EntityDamageResult
            {
                Message = "El sistema de daño no está inicializado."
            };
            return false;
        }

        return Damage.ApplyDamage(
            sourceEntityId,
            targetEntityId,
            amount,
            damageType,
            reason,
            ElapsedTime,
            out result);
    }

    public long EnqueueHumanCommand(
        int participantId,
        ulong clientId,
        MatchCommandType commandType,
        object payload)
    {
        return CommandBus.Enqueue(
            MatchCommandIssuer.Human(participantId, clientId),
            commandType,
            payload);
    }

    public long EnqueueHeadlessCommand(
        int participantId,
        string controllerProfileId,
        MatchCommandType commandType,
        object payload)
    {
        return CommandBus.Enqueue(
            MatchCommandIssuer.Headless(participantId, controllerProfileId),
            commandType,
            payload);
    }

    public long EnqueueRuleCommand(
        int participantId,
        MatchCommandType commandType,
        object payload)
    {
        return CommandBus.Enqueue(
            MatchCommandIssuer.RuntimeRule(participantId),
            commandType,
            payload);
    }

    private MatchCommandResult HandleCommand(MatchCommandEnvelope envelope)
    {
        if (envelope == null)
            return MatchCommandResult.Rejected("Comando inexistente.");

        if (!Participants.ValidateIssuer(envelope.Issuer, out string issuerRejection))
            return MatchCommandResult.Rejected(issuerRejection);

        int participantId = envelope.Issuer.ParticipantId;
        switch (envelope.CommandType)
        {
            case MatchCommandType.Move:
                if (envelope.Payload is not EntityMoveCommand move)
                    return MatchCommandResult.Rejected("Payload de movimiento inválido.");
                return EntityMovementService.TryApplyMove(
                    World,
                    participantId,
                    move,
                    WorldBounds,
                    out string moveRejection)
                    ? MatchCommandResult.Success()
                    : MatchCommandResult.Rejected(moveRejection);

            case MatchCommandType.ResourceInteraction:
                if (envelope.Payload is not ResourceInteractionCommand resource)
                    return MatchCommandResult.Rejected("Payload de extracción inválido.");
                return ResourceExtractionService.TryAssignExtraction(
                    World,
                    participantId,
                    resource,
                    out string resourceRejection)
                    ? MatchCommandResult.Success()
                    : MatchCommandResult.Rejected(resourceRejection);

            case MatchCommandType.EntityInteraction:
                if (envelope.Payload is not EntityInteractionCommand interaction)
                    return MatchCommandResult.Rejected("Payload de interacción inválido.");
                return EntityInteractionService.TryAssignFollow(
                    World,
                    participantId,
                    interaction,
                    out string interactionRejection)
                    ? MatchCommandResult.Success()
                    : MatchCommandResult.Rejected(interactionRejection);

            case MatchCommandType.Attack:
                if (envelope.Payload is not EntityAttackCommand attack)
                    return MatchCommandResult.Rejected("Payload de ataque inválido.");
                return Combat.TryAssignAttack(
                    participantId,
                    attack,
                    out string attackRejection)
                    ? MatchCommandResult.Success()
                    : MatchCommandResult.Rejected(attackRejection);

            default:
                return MatchCommandResult.Rejected($"Tipo de comando no soportado: {envelope.CommandType}.");
        }
    }

    private void SubscribeRuntimeEvents()
    {
        EntityLifecycle.EntitySpawned += OnEntitySpawned;
        EntityLifecycle.EntityDespawned += OnEntityDespawned;
        Participants.ParticipantStateChanged += OnParticipantStateChanged;
        Participants.ParticipantControlChanged += OnParticipantControlChanged;
        Participants.ParticipantAttributeChanged += OnParticipantAttributeChanged;
        Participants.ParticipantVariableChanged += OnParticipantVariableChanged;
        Participants.ParticipantResourceChanged += OnResourceChanged;
        Teams.TeamResourceChanged += OnResourceChanged;
        MatchState.ResultDeclared += OnMatchResultDeclared;
    }

    private void OnCommandProcessed(MatchCommandEnvelope envelope, MatchCommandResult result)
    {
        if (result != null && !result.Accepted && !string.IsNullOrWhiteSpace(result.Message))
        {
            Debug.LogWarning(
                $"[AuthoritativeMatchRuntime] Comando {envelope?.Sequence} " +
                $"({envelope?.CommandType}) rechazado: {result.Message}");
        }

        CommandProcessed?.Invoke(envelope, result);
    }

    private void OnEntitySpawned(EntitySpawnedEvent lifecycleEvent)
    {
        EntitySpawned?.Invoke(lifecycleEvent);
        EntityRuntimeState entity = lifecycleEvent?.Entity;
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.EntitySpawned,
            ElapsedTime = ElapsedTime,
            EntityId = entity?.UnitId ?? -1,
            Entity = entity,
            ParticipantId = entity?.OwnerParticipantId ?? -1,
            Reason = lifecycleEvent?.Reason.ToString()
        });
    }

    private void OnEntityDespawned(EntityDespawnedEvent lifecycleEvent)
    {
        Combat?.ClearReferencesToEntity(lifecycleEvent?.EntityId ?? -1);
        EntityDespawned?.Invoke(lifecycleEvent);
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.EntityDespawned,
            ElapsedTime = ElapsedTime,
            EntityId = lifecycleEvent?.EntityId ?? -1,
            Entity = lifecycleEvent?.Entity,
            ParticipantId = lifecycleEvent?.OwnerParticipantId ?? -1,
            Reason = lifecycleEvent?.Reason.ToString()
        });
    }

    private void OnParticipantStateChanged(ParticipantStateChangedEvent stateEvent)
    {
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.ParticipantStateChanged,
            ElapsedTime = ElapsedTime,
            ParticipantId = stateEvent?.Participant?.ParticipantId ?? -1,
            Participant = stateEvent?.Participant,
            PreviousParticipantState = stateEvent?.PreviousState ?? ParticipantLifeState.Active,
            CurrentParticipantState = stateEvent?.CurrentState ?? ParticipantLifeState.Active,
            Reason = stateEvent?.Reason
        });
    }


    private void OnParticipantControlChanged(ParticipantControlChangedEvent controlEvent)
    {
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.ParticipantControlChanged,
            ElapsedTime = ElapsedTime,
            ParticipantId = controlEvent?.Participant?.ParticipantId ?? -1,
            Participant = controlEvent?.Participant,
            PreviousControlEnabled = controlEvent?.PreviousValue ?? false,
            CurrentControlEnabled = controlEvent?.CurrentValue ?? false,
            Reason = controlEvent?.Reason
        });
    }

    private void OnParticipantAttributeChanged(ParticipantAttributeChangedEvent attributeEvent)
    {
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.ParticipantAttributeChanged,
            ElapsedTime = ElapsedTime,
            ParticipantId = attributeEvent?.Participant?.ParticipantId ?? -1,
            Participant = attributeEvent?.Participant,
            ParticipantAttribute = attributeEvent?.Attribute,
            ParticipantAttributeAdded = attributeEvent?.Added ?? false,
            Reason = attributeEvent?.Reason
        });
    }

    private void OnParticipantVariableChanged(ParticipantVariableChangedEvent variableEvent)
    {
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.ParticipantVariableChanged,
            ElapsedTime = ElapsedTime,
            ParticipantId = variableEvent?.Participant?.ParticipantId ?? -1,
            Participant = variableEvent?.Participant,
            VariableName = variableEvent?.VariableName,
            PreviousVariableValue = variableEvent?.PreviousValue,
            CurrentVariableValue = variableEvent?.CurrentValue,
            Reason = variableEvent?.Reason
        });
    }

    private void OnResourceChanged(RuntimeResourceChangedEvent resourceEvent)
    {
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.ResourceChanged,
            ElapsedTime = ElapsedTime,
            ResourceScope = resourceEvent?.Scope,
            ResourceOwnerId = resourceEvent?.OwnerId ?? -1,
            ResourceId = resourceEvent?.ResourceId,
            PreviousResourceAmount = resourceEvent?.PreviousAmount ?? 0,
            CurrentResourceAmount = resourceEvent?.CurrentAmount ?? 0
        });
    }

    private void OnMatchResultDeclared(MatchResultDeclaredEvent resultEvent)
    {
        EventBus?.Publish(new RuntimeEventContext
        {
            Type = RuntimeEventType.MatchResultDeclared,
            ElapsedTime = ElapsedTime,
            MatchResult = resultEvent?.Result ?? MatchResultState.None,
            ResultTeamId = resultEvent?.TeamId ?? 0,
            Reason = resultEvent?.Reason
        });
    }

    private void OnRuleMessageRaised(string message, bool isError)
    {
        RuntimeMessageRaised?.Invoke(message, isError);
    }
}
