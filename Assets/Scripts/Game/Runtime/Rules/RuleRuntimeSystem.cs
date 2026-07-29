using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>
/// Ejecuta reglas declaradas por el escenario. Solo admite condiciones y
/// acciones registradas; nunca ejecuta código incluido en el paquete.
/// El motor ofrece piezas generales y no conoce mecánicas concretas como
/// captura, rescate, apertura de puertas o control de objetivos.
/// </summary>
public sealed class RuleRuntimeSystem
{
    private readonly List<RuntimeRuleState> rules = new();
    private readonly Dictionary<string, string> matchVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeEventBus eventBus;
    private readonly MatchParticipantRegistry participants;
    private readonly MatchTeamRegistry teams;
    private readonly EntityWorld world;
    private readonly EntityLifecycleService lifecycle;
    private readonly MatchRuntimeState matchState;
    private readonly EntityAreaRuntimeSystem areas;
    private readonly RuntimeChannelSystem channels;
    private DamageRuntimeService damage;
    private int ruleSpawnSequence;

    public int RuleCount => rules.Count;
    public event Action<string, bool> MessageRaised;

    public RuleRuntimeSystem(
        ScenarioRuleDefinition[] definitions,
        RuntimeEventBus eventBus,
        MatchParticipantRegistry participants,
        MatchTeamRegistry teams,
        EntityWorld world,
        EntityLifecycleService lifecycle,
        MatchRuntimeState matchState,
        EntityAreaRuntimeSystem areas,
        RuntimeChannelSystem channels)
    {
        this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        this.participants = participants ?? throw new ArgumentNullException(nameof(participants));
        this.teams = teams ?? throw new ArgumentNullException(nameof(teams));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
        this.areas = areas ?? throw new ArgumentNullException(nameof(areas));
        this.channels = channels ?? throw new ArgumentNullException(nameof(channels));

        if (definitions != null)
        {
            foreach (ScenarioRuleDefinition definition in definitions
                         .Where(item => item != null && item.enabled && !string.IsNullOrWhiteSpace(item.eventType))
                         .OrderByDescending(item => item.priority))
            {
                if (!TryParseEventType(definition.eventType, out RuntimeEventType eventType))
                {
                    Debug.LogWarning($"[RuleRuntimeSystem] Regla '{definition.id}' usa el evento desconocido '{definition.eventType}'.");
                    continue;
                }

                rules.Add(new RuntimeRuleState(definition, eventType));
            }
        }

        eventBus.EventPublished += HandleEvent;
    }

    public void BindDamageService(DamageRuntimeService damageService)
    {
        damage = damageService ?? throw new ArgumentNullException(nameof(damageService));
    }

    /// <summary>
    /// Evalúa reglas entity-fatal-damage antes de confirmar la muerte. Esta
    /// capacidad sigue siendo genérica para prevención o estado Downed; las
    /// mecánicas basadas en una muerte consumada deben escuchar entity-died.
    /// </summary>
    public void ResolveFatalDamage(FatalDamageContext fatal, float elapsedTime)
    {
        if (fatal?.Target == null || matchState.IsCompleted)
            return;

        RuntimeEventContext runtimeEvent = new()
        {
            Type = RuntimeEventType.EntityFatalDamage,
            ElapsedTime = elapsedTime,
            EntityId = fatal.Target.UnitId,
            Entity = fatal.Target,
            SourceEntityId = fatal.Source?.UnitId ?? -1,
            SourceEntity = fatal.Source,
            DamageAmount = fatal.DamageAmount,
            PreviousHealth = fatal.PreviousHealth,
            CurrentHealth = fatal.Target.Health,
            DamageType = fatal.DamageType,
            FatalResolution = fatal.Resolution,
            ParticipantId = fatal.Target.OwnerParticipantId,
            Reason = fatal.Reason
        };
        runtimeEvent.CaptureEntitySnapshots();

        foreach (RuntimeRuleState state in rules)
        {
            if (state.EventType != RuntimeEventType.EntityFatalDamage || state.Completed)
                continue;
            if (elapsedTime < state.NextAllowedTime)
                continue;
            if (!EvaluateConditions(state.Definition.conditions, runtimeEvent))
                continue;

            ExecuteActions(state.Definition.actions, runtimeEvent, fatal);
            runtimeEvent.FatalResolution = fatal.Resolution;
            runtimeEvent.CurrentHealth = fatal.RestoredHealth;
            state.NextAllowedTime = elapsedTime + Mathf.Max(0f, state.Definition.cooldown);
            if (state.Definition.once)
                state.Completed = true;
        }
    }

    private void HandleEvent(RuntimeEventContext runtimeEvent)
    {
        if (runtimeEvent == null ||
            runtimeEvent.Type == RuntimeEventType.EntityFatalDamage ||
            (matchState.IsCompleted && runtimeEvent.Type != RuntimeEventType.MatchResultDeclared))
        {
            return;
        }

        foreach (RuntimeRuleState state in rules)
        {
            if (state.EventType != runtimeEvent.Type || state.Completed)
                continue;
            if (runtimeEvent.ElapsedTime < state.NextAllowedTime)
                continue;
            if (!EvaluateConditions(state.Definition.conditions, runtimeEvent))
                continue;

            ExecuteActions(state.Definition.actions, runtimeEvent, null);
            state.NextAllowedTime = runtimeEvent.ElapsedTime + Mathf.Max(0f, state.Definition.cooldown);
            if (state.Definition.once)
                state.Completed = true;
        }
    }

    private bool EvaluateConditions(
        ScenarioRuleConditionDefinition[] conditions,
        RuntimeEventContext runtimeEvent)
    {
        if (conditions == null || conditions.Length == 0)
            return true;

        foreach (ScenarioRuleConditionDefinition condition in conditions)
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.type))
                continue;

            string type = Normalize(condition.type);
            if (type == "area-has-attribute")
            {
                if (!EventEntityHasAttribute(runtimeEvent.AreaEntity, runtimeEvent.AreaEntitySnapshot, condition.attribute))
                    return false;
                continue;
            }

            if (type == "entity-has-attribute")
            {
                ResolveEntityContext(condition.entitySelector, runtimeEvent, out EntityRuntimeState entity, out RuntimeEntityEventSnapshot snapshot);
                if (!EventEntityHasAttribute(entity, snapshot, condition.attribute))
                    return false;
                continue;
            }

            if (type == "source-entity-has-attribute")
            {
                if (!EventEntityHasAttribute(runtimeEvent.SourceEntity, runtimeEvent.SourceEntitySnapshot, condition.attribute))
                    return false;
                continue;
            }

            if (type == "entity-definition-is")
            {
                ResolveEntityContext(condition.entitySelector, runtimeEvent, out EntityRuntimeState entity, out RuntimeEntityEventSnapshot snapshot);
                string definitionId = entity?.EntityDefinitionId ?? snapshot?.EntityDefinitionId;
                if (!string.Equals(definitionId, condition.value, StringComparison.OrdinalIgnoreCase))
                    return false;
                continue;
            }

            if (type == "entity-team-is")
            {
                int teamId = runtimeEvent.Entity?.TeamId ?? runtimeEvent.EntitySnapshot?.TeamId ?? 0;
                if (teamId != condition.teamId)
                    return false;
                continue;
            }

            if (type == "area-team-is")
            {
                int teamId = runtimeEvent.AreaEntity?.TeamId ?? runtimeEvent.AreaEntitySnapshot?.TeamId ?? 0;
                if (teamId != condition.teamId)
                    return false;
                continue;
            }

            if (type == "entity-life-state-is")
            {
                ResolveEntityContext(condition.entitySelector, runtimeEvent, out EntityRuntimeState entity, out RuntimeEntityEventSnapshot snapshot);
                EntityLifeState actual = entity?.Life?.State ?? snapshot?.LifeState ?? EntityLifeState.Alive;
                if (!Enum.TryParse(condition.state, true, out EntityLifeState expectedLife) || actual != expectedLife)
                    return false;
                continue;
            }

            if (type == "entity-health-at-or-below")
            {
                ResolveEntityContext(condition.entitySelector, runtimeEvent, out EntityRuntimeState entity, out RuntimeEntityEventSnapshot snapshot);
                int health = entity?.Health ?? snapshot?.Health ?? int.MaxValue;
                if (!int.TryParse(condition.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold) ||
                    health > threshold)
                    return false;
                continue;
            }

            if (type == "damage-type-is")
            {
                if (!string.Equals(Normalize(runtimeEvent.DamageType), Normalize(condition.value), StringComparison.Ordinal))
                    return false;
                continue;
            }

            if (type == "participant-state-is" || type == "entity-owner-state")
            {
                int participantId = ResolveParticipantId(condition.participantSelector, condition.participantId, runtimeEvent);
                if (!participants.TryGet(participantId, out MatchParticipantRuntimeState participant) ||
                    !TryParseParticipantState(condition.state, out ParticipantLifeState expected) ||
                    participant.LifeState != expected)
                    return false;
                continue;
            }

            if (type == "participant-control-is")
            {
                int participantId = ResolveParticipantId(condition.participantSelector, condition.participantId, runtimeEvent);
                if (!participants.TryGet(participantId, out MatchParticipantRuntimeState participant) ||
                    !bool.TryParse(condition.value, out bool expected) ||
                    participant.ControlEnabled != expected)
                    return false;
                continue;
            }

            if (type == "participant-has-attribute" || type == "participant-lacks-attribute")
            {
                int participantId = ResolveParticipantId(condition.participantSelector, condition.participantId, runtimeEvent);
                bool has = participants.TryGet(participantId, out MatchParticipantRuntimeState participant) &&
                           participant.Attributes.Has(condition.attribute);
                if ((type == "participant-has-attribute" && !has) ||
                    (type == "participant-lacks-attribute" && has))
                    return false;
                continue;
            }

            if (type == "participant-variable-is")
            {
                int participantId = ResolveParticipantId(condition.participantSelector, condition.participantId, runtimeEvent);
                if (!participants.TryGet(participantId, out MatchParticipantRuntimeState participant) ||
                    !participant.TryGetVariable(condition.variableName, out string actual) ||
                    !string.Equals(actual, condition.value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    return false;
                continue;
            }

            if (type == "rule-variable-is")
            {
                if (string.IsNullOrWhiteSpace(condition.variableName) ||
                    !matchVariables.TryGetValue(condition.variableName.Trim(), out string actual) ||
                    !string.Equals(actual, condition.value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    return false;
                continue;
            }

            if (type == "channel-id-is")
            {
                if (!string.Equals(Normalize(runtimeEvent.ChannelId), Normalize(condition.value), StringComparison.Ordinal))
                    return false;
                continue;
            }

            if (type == "match-phase-is")
            {
                if (!Enum.TryParse(condition.state, true, out MatchPhaseState expectedPhase) ||
                    matchState.Phase != expectedPhase)
                    return false;
                continue;
            }

            Debug.LogWarning($"[RuleRuntimeSystem] Condición desconocida '{condition.type}'.");
            return false;
        }

        return true;
    }

    private void ExecuteActions(
        ScenarioRuleActionDefinition[] actions,
        RuntimeEventContext runtimeEvent,
        FatalDamageContext fatal)
    {
        if (actions == null)
            return;

        foreach (ScenarioRuleActionDefinition action in actions)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.type))
                continue;

            string type = Normalize(action.type);
            if (type == "show-message")
            {
                if (!string.IsNullOrWhiteSpace(action.message))
                    MessageRaised?.Invoke(action.message, false);
                continue;
            }

            if (type == "prevent-death")
            {
                if (fatal != null)
                {
                    fatal.Resolution = FatalDamageResolution.Prevented;
                    fatal.RestoredHealth = Mathf.Max(1, action.amount > 0 ? action.amount : fatal.RestoredHealth);
                }
                continue;
            }

            if (type == "restore-health")
            {
                if (fatal != null)
                {
                    fatal.RestoredHealth = Mathf.Max(1, action.amount);
                    if (fatal.Resolution == FatalDamageResolution.Death)
                        fatal.Resolution = FatalDamageResolution.Prevented;
                }
                else
                {
                    EntityRuntimeState entity = ResolveEntity(action.entitySelector, runtimeEvent);
                    if (entity?.Life?.State == EntityLifeState.Alive && action.amount > 0)
                        entity.Health = Mathf.Clamp(entity.Health + action.amount, 0, entity.MaxHealth);
                }
                continue;
            }

            if (type == "set-fatal-resolution")
            {
                if (fatal != null && TryParseFatalResolution(
                        string.IsNullOrWhiteSpace(action.value) ? action.result : action.value,
                        out FatalDamageResolution resolution))
                {
                    fatal.Resolution = resolution;
                    if (action.amount > 0)
                        fatal.RestoredHealth = action.amount;
                }
                continue;
            }

            if (type == "set-participant-state")
            {
                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                if (!TryParseParticipantState(action.participantState, out ParticipantLifeState nextState))
                {
                    Debug.LogWarning($"[RuleRuntimeSystem] Estado de participante inválido: {action.participantState}.");
                    continue;
                }

                if (!participants.SetLifeState(participantId, nextState, action.reason, out string rejection))
                    Debug.LogWarning($"[RuleRuntimeSystem] No se pudo cambiar el estado: {rejection}.");
                continue;
            }

            if (type == "set-participant-control-enabled")
            {
                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                if (!participants.SetControlEnabled(participantId, action.controlEnabled, action.reason, out string rejection))
                    Debug.LogWarning($"[RuleRuntimeSystem] No se pudo cambiar el control: {rejection}.");
                continue;
            }

            if (type == "add-participant-attribute" || type == "remove-participant-attribute")
            {
                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                string rejection;
                bool success;
                if (type == "add-participant-attribute")
                    success = participants.AddAttribute(participantId, action.attribute, action.reason, out rejection);
                else
                    success = participants.RemoveAttribute(participantId, action.attribute, action.reason, out rejection);
                if (!success)
                    Debug.LogWarning($"[RuleRuntimeSystem] No se pudo modificar el atributo: {rejection}.");
                continue;
            }

            if (type == "set-participant-variable")
            {
                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                string value = ResolveValue(action.variableValue, action.valueSource, runtimeEvent, participantId);
                if (!participants.SetVariable(participantId, action.variableName, value, action.reason, out string rejection))
                    Debug.LogWarning($"[RuleRuntimeSystem] No se pudo guardar la variable: {rejection}.");
                continue;
            }

            if (type == "set-rule-variable")
            {
                if (string.IsNullOrWhiteSpace(action.variableName))
                {
                    Debug.LogWarning("[RuleRuntimeSystem] set-rule-variable requiere variableName.");
                    continue;
                }

                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                matchVariables[action.variableName.Trim()] = ResolveValue(
                    action.variableValue,
                    action.valueSource,
                    runtimeEvent,
                    participantId);
                continue;
            }

            if (type == "add-entity-attribute" || type == "remove-entity-attribute")
            {
                EntityRuntimeState entity = ResolveEntity(action.entitySelector, runtimeEvent);
                if (entity?.Attributes == null || string.IsNullOrWhiteSpace(action.attribute))
                {
                    Debug.LogWarning("[RuleRuntimeSystem] No se pudo modificar el atributo de la entidad.");
                    continue;
                }

                if (type == "add-entity-attribute")
                    entity.Attributes.Add(action.attribute);
                else
                    entity.Attributes.Remove(action.attribute);
                continue;
            }

            if (type == "set-entity-health")
            {
                EntityRuntimeState entity = ResolveEntity(action.entitySelector, runtimeEvent);
                if (entity != null)
                    entity.Health = Mathf.Clamp(action.amount, 0, Mathf.Max(1, entity.MaxHealth));
                continue;
            }

            if (type == "set-entity-life-state")
            {
                EntityRuntimeState entity = ResolveEntity(action.entitySelector, runtimeEvent);
                if (entity?.Life == null || !Enum.TryParse(action.entityState, true, out EntityLifeState nextState))
                {
                    Debug.LogWarning($"[RuleRuntimeSystem] Estado de entidad inválido: {action.entityState}.");
                    continue;
                }

                if (nextState == EntityLifeState.Dead && entity.Life.State != EntityLifeState.Dead)
                {
                    DestroyEntity(entity, action.reason ?? "rule-set-dead", runtimeEvent.ElapsedTime);
                }
                else
                {
                    entity.Life.State = nextState;
                    if (nextState == EntityLifeState.Alive && entity.Health <= 0)
                        entity.Health = Mathf.Max(1, action.amount > 0 ? action.amount : 1);
                    StopEntity(entity);
                }
                continue;
            }

            if (type == "move-entity-to-area")
            {
                EntityRuntimeState entity = ResolveEntity(action.entitySelector, runtimeEvent);
                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                if (entity == null)
                {
                    Debug.LogWarning("[RuleRuntimeSystem] move-entity-to-area no resolvió la entidad.");
                    continue;
                }
                if (!TryResolveAreaPosition(action.areaAttribute, participantId, runtimeEvent, out Vector3 areaPosition))
                {
                    Debug.LogWarning($"[RuleRuntimeSystem] No existe un área con atributo '{action.areaAttribute}'.");
                    continue;
                }

                MoveEntity(entity, areaPosition);
                continue;
            }

            if (type == "destroy-participant-entities")
            {
                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                DestroyParticipantEntities(participantId, action, runtimeEvent);
                continue;
            }

            if (type == "despawn-participant-entities")
            {
                int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                QueueParticipantEntityDespawns(participantId, action, runtimeEvent);
                continue;
            }

            if (type == "start-channel")
            {
                int targetParticipantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                ParticipantLifeState? requiredState = TryParseParticipantState(
                    action.requiredParticipantState,
                    out ParticipantLifeState parsedRequired)
                    ? parsedRequired
                    : null;
                bool requiresTarget = requiredState.HasValue || !string.IsNullOrWhiteSpace(action.requiredParticipantAttribute);
                if (targetParticipantId <= 0 && requiresTarget)
                    continue;

                EntityRuntimeState source = ResolveEntity(action.entitySelector, runtimeEvent) ?? runtimeEvent.Entity;
                int sourceId = source?.UnitId ?? runtimeEvent.EntityId;
                if (!channels.StartOrRefresh(
                        action.channelId,
                        sourceId,
                        runtimeEvent.AreaEntityId,
                        targetParticipantId,
                        action.duration,
                        requiredState,
                        action.requiredParticipantAttribute,
                        action.reason,
                        runtimeEvent.ElapsedTime,
                        out string rejection))
                {
                    Debug.LogWarning($"[RuleRuntimeSystem] Canalización rechazada: {rejection}.");
                }
                continue;
            }

            if (type == "cancel-channel")
            {
                int targetParticipantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
                EntityRuntimeState source = ResolveEntity(action.entitySelector, runtimeEvent);
                channels.CancelMatching(
                    action.channelId,
                    source?.UnitId ?? -1,
                    targetParticipantId,
                    action.reason,
                    runtimeEvent.ElapsedTime);
                continue;
            }

            if (type == "give-resource" || type == "remove-resource")
            {
                int signedAmount = type == "remove-resource" ? -Mathf.Abs(action.amount) : Mathf.Abs(action.amount);
                ChangeResource(action, runtimeEvent, signedAmount);
                continue;
            }

            if (type == "spawn-entity")
            {
                QueueSpawn(action, runtimeEvent);
                continue;
            }

            if (type == "despawn-event-entity")
            {
                int entityId = ResolveEntity(action.entitySelector, runtimeEvent)?.UnitId ?? runtimeEvent.EntityId;
                if (entityId > 0 && world.TryGet(entityId, out _))
                    lifecycle.QueueDespawn(entityId, EntityLifecycleReason.RuntimeRule, out _);
                continue;
            }

            if (type == "declare-victory" || type == "declare-defeat" || type == "declare-draw")
            {
                MatchResultState result = type == "declare-victory"
                    ? MatchResultState.Victory
                    : type == "declare-defeat"
                        ? MatchResultState.Defeat
                        : MatchResultState.Draw;
                int teamId = action.teamId > 0 ? action.teamId : ResolveTeamId(runtimeEvent);
                matchState.DeclareResult(result, teamId, action.reason);
                continue;
            }

            Debug.LogWarning($"[RuleRuntimeSystem] Acción desconocida '{action.type}'.");
        }
    }

    private void ChangeResource(
        ScenarioRuleActionDefinition action,
        RuntimeEventContext runtimeEvent,
        int delta)
    {
        if (string.IsNullOrWhiteSpace(action.resourceId) || delta == 0)
            return;

        string scope = string.IsNullOrWhiteSpace(action.resourceScope)
            ? "team"
            : action.resourceScope.Trim().ToLowerInvariant();
        if (scope == "participant")
        {
            int participantId = ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent);
            if (participants.TryGet(participantId, out MatchParticipantRuntimeState participant))
                participant.Resources.Add(action.resourceId, delta);
            return;
        }

        int teamId = action.teamId > 0 ? action.teamId : ResolveTeamId(runtimeEvent);
        if (teamId > 0 && teams.TryGet(teamId, out MatchTeamRuntimeState team))
            team.Resources.Add(action.resourceId, delta);
    }

    private void QueueSpawn(
        ScenarioRuleActionDefinition action,
        RuntimeEventContext runtimeEvent)
    {
        int ownerParticipantId = action.inheritEventOwner
            ? ResolveParticipantId(action.participantSelector, action.participantId, runtimeEvent)
            : action.participantId;
        participants.TryGet(ownerParticipantId, out MatchParticipantRuntimeState owner);

        string entityId = action.entityId;
        if (!string.IsNullOrWhiteSpace(action.entityIdVariable) && owner != null)
            owner.TryGetVariable(action.entityIdVariable, out entityId);
        if (string.IsNullOrWhiteSpace(entityId))
        {
            Debug.LogWarning("[RuleRuntimeSystem] spawn-entity no resolvió entityId.");
            return;
        }

        Vector3 position;
        if (!string.IsNullOrWhiteSpace(action.areaAttribute) &&
            TryResolveAreaPosition(action.areaAttribute, ownerParticipantId, runtimeEvent, out Vector3 areaPosition))
        {
            position = areaPosition;
        }
        else
        {
            position = ResolvePosition(action, runtimeEvent);
        }

        EntitySpawnRequest request = new()
        {
            EntityDefinitionId = entityId,
            ScenarioInstanceId = $"rule.{++ruleSpawnSequence}",
            OwnerParticipantId = ownerParticipantId,
            TeamId = action.teamId > 0 ? action.teamId : (owner?.TeamId ?? 0),
            ColorId = owner?.ColorId ?? -1,
            Position = position,
            Reason = EntityLifecycleReason.RuntimeRule
        };

        if (!lifecycle.QueueSpawn(request, out string rejection))
            Debug.LogWarning($"[RuleRuntimeSystem] Spawn de regla rechazado: {rejection}");
    }

    private void DestroyParticipantEntities(
        int participantId,
        ScenarioRuleActionDefinition action,
        RuntimeEventContext runtimeEvent)
    {
        if (participantId <= 0)
            return;

        foreach (EntityRuntimeState entity in world.SnapshotValues())
        {
            if (!MatchesParticipantEntity(entity, participantId, action, runtimeEvent))
                continue;
            if (entity.Life == null || entity.Life.State == EntityLifeState.Dead)
                continue;

            DestroyEntity(entity, action.reason ?? "rule-destroy-participant-entities", runtimeEvent.ElapsedTime);
        }
    }

    private void DestroyEntity(EntityRuntimeState entity, string reason, float elapsedTime)
    {
        if (entity == null || entity.Life == null || entity.Life.State == EntityLifeState.Dead)
            return;
        if (damage == null)
        {
            Debug.LogWarning("[RuleRuntimeSystem] El sistema de daño aún no está enlazado; no se puede destruir la entidad.");
            return;
        }

        if (!damage.ApplyForcedDeath(
                -1,
                entity.UnitId,
                reason,
                elapsedTime,
                out EntityDamageResult result))
        {
            Debug.LogWarning($"[RuleRuntimeSystem] Destrucción de {entity.UnitId} rechazada: {result?.Message}");
        }
    }

    private void QueueParticipantEntityDespawns(
        int participantId,
        ScenarioRuleActionDefinition action,
        RuntimeEventContext runtimeEvent)
    {
        if (participantId <= 0)
            return;

        foreach (EntityRuntimeState entity in world.SnapshotValues())
        {
            if (!MatchesParticipantEntity(entity, participantId, action, runtimeEvent))
                continue;

            lifecycle.QueueDespawn(entity.UnitId, EntityLifecycleReason.RuntimeRule, out _);
        }
    }

    private static bool MatchesParticipantEntity(
        EntityRuntimeState entity,
        int participantId,
        ScenarioRuleActionDefinition action,
        RuntimeEventContext runtimeEvent)
    {
        if (entity == null || entity.OwnerParticipantId != participantId)
            return false;
        if (action.excludeEventEntity && entity.UnitId == runtimeEvent.EntityId)
            return false;
        if (!string.IsNullOrWhiteSpace(action.entityAttribute) &&
            (entity.Attributes == null || !entity.Attributes.Has(action.entityAttribute)))
            return false;

        string preserve = !string.IsNullOrWhiteSpace(action.preserveAttribute)
            ? action.preserveAttribute
            : action.excludeEntityAttribute;
        if (!string.IsNullOrWhiteSpace(preserve) &&
            entity.Attributes != null && entity.Attributes.Has(preserve))
            return false;
        return true;
    }

    private EntityRuntimeState ResolveEntity(string selector, RuntimeEventContext runtimeEvent)
    {
        string normalized = Normalize(selector);
        if (normalized == "event-source")
            return runtimeEvent.SourceEntity;
        if (normalized == "event-area")
            return runtimeEvent.AreaEntity;
        return runtimeEvent.Entity;
    }

    private static void ResolveEntityContext(
        string selector,
        RuntimeEventContext runtimeEvent,
        out EntityRuntimeState entity,
        out RuntimeEntityEventSnapshot snapshot)
    {
        string normalized = Normalize(selector);
        if (normalized == "event-source")
        {
            entity = runtimeEvent.SourceEntity;
            snapshot = runtimeEvent.SourceEntitySnapshot;
            return;
        }
        if (normalized == "event-area")
        {
            entity = runtimeEvent.AreaEntity;
            snapshot = runtimeEvent.AreaEntitySnapshot;
            return;
        }

        entity = runtimeEvent.Entity;
        snapshot = runtimeEvent.EntitySnapshot;
    }

    private bool TryResolveAreaPosition(
        string areaAttribute,
        int participantId,
        RuntimeEventContext runtimeEvent,
        out Vector3 position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(areaAttribute))
        {
            if (runtimeEvent.AreaEntity != null || runtimeEvent.AreaEntitySnapshot != null)
            {
                position = runtimeEvent.AreaEntity?.Position ?? runtimeEvent.AreaEntitySnapshot.Position;
                return true;
            }
            return false;
        }

        participants.TryGet(participantId, out MatchParticipantRuntimeState participant);
        int teamId = participant?.TeamId ??
                     runtimeEvent.Entity?.TeamId ??
                     runtimeEvent.EntitySnapshot?.TeamId ?? 0;
        EntityRuntimeState selected = world.Values
            .Where(item => item.Area != null && item.Attributes?.Has(areaAttribute) == true)
            .OrderByDescending(item => teamId > 0 && item.TeamId == teamId)
            .ThenByDescending(item => item.TeamId == 0)
            .ThenBy(item => item.UnitId)
            .FirstOrDefault();
        if (selected == null)
            return false;

        position = selected.Position;
        return true;
    }

    private static Vector3 ResolvePosition(
        ScenarioRuleActionDefinition action,
        RuntimeEventContext runtimeEvent)
    {
        string selector = Normalize(action.positionSelector);
        if (selector == "event-area")
            return runtimeEvent.AreaEntity?.Position ?? runtimeEvent.AreaEntitySnapshot?.Position ?? Vector3.zero;
        if (selector == "event-source")
            return runtimeEvent.SourceEntity?.Position ?? runtimeEvent.SourceEntitySnapshot?.Position ?? Vector3.zero;
        if (selector == "event-entity")
            return runtimeEvent.Entity?.Position ?? runtimeEvent.EntitySnapshot?.Position ?? Vector3.zero;
        return action.position?.ToVector3() ??
               runtimeEvent.AreaEntity?.Position ?? runtimeEvent.AreaEntitySnapshot?.Position ??
               runtimeEvent.Entity?.Position ?? runtimeEvent.EntitySnapshot?.Position ?? Vector3.zero;
    }

    private int ResolveParticipantId(
        string selector,
        int explicitParticipantId,
        RuntimeEventContext runtimeEvent)
    {
        string normalized = Normalize(selector);
        if (normalized == "specific")
            return explicitParticipantId;
        if (normalized == "event-participant")
            return runtimeEvent.ParticipantId;
        if (normalized == "event-area-owner")
            return runtimeEvent.AreaEntity?.OwnerParticipantId ??
                   runtimeEvent.AreaEntitySnapshot?.OwnerParticipantId ?? explicitParticipantId;
        if (normalized == "event-source-owner")
            return runtimeEvent.SourceEntity?.OwnerParticipantId ??
                   runtimeEvent.SourceEntitySnapshot?.OwnerParticipantId ?? explicitParticipantId;
        if (normalized == "event-entity-owner" || string.IsNullOrWhiteSpace(normalized))
            return runtimeEvent.Entity?.OwnerParticipantId ??
                   runtimeEvent.EntitySnapshot?.OwnerParticipantId ?? runtimeEvent.ParticipantId;
        return explicitParticipantId > 0 ? explicitParticipantId : runtimeEvent.ParticipantId;
    }

    private static int ResolveTeamId(RuntimeEventContext runtimeEvent)
    {
        if (runtimeEvent.Entity != null && runtimeEvent.Entity.TeamId > 0)
            return runtimeEvent.Entity.TeamId;
        if (runtimeEvent.EntitySnapshot != null && runtimeEvent.EntitySnapshot.TeamId > 0)
            return runtimeEvent.EntitySnapshot.TeamId;
        if (runtimeEvent.Participant != null && runtimeEvent.Participant.TeamId > 0)
            return runtimeEvent.Participant.TeamId;
        if (runtimeEvent.AreaEntity != null && runtimeEvent.AreaEntity.TeamId > 0)
            return runtimeEvent.AreaEntity.TeamId;
        if (runtimeEvent.AreaEntitySnapshot != null && runtimeEvent.AreaEntitySnapshot.TeamId > 0)
            return runtimeEvent.AreaEntitySnapshot.TeamId;
        if (runtimeEvent.SourceEntity != null && runtimeEvent.SourceEntity.TeamId > 0)
            return runtimeEvent.SourceEntity.TeamId;
        if (runtimeEvent.SourceEntitySnapshot != null && runtimeEvent.SourceEntitySnapshot.TeamId > 0)
            return runtimeEvent.SourceEntitySnapshot.TeamId;
        return 0;
    }

    private string ResolveValue(
        string literal,
        string valueSource,
        RuntimeEventContext runtimeEvent,
        int participantId)
    {
        switch (Normalize(valueSource))
        {
            case "event-entity-definition":
                return runtimeEvent.Entity?.EntityDefinitionId ?? runtimeEvent.EntitySnapshot?.EntityDefinitionId ?? string.Empty;
            case "event-source-definition":
                return runtimeEvent.SourceEntity?.EntityDefinitionId ?? runtimeEvent.SourceEntitySnapshot?.EntityDefinitionId ?? string.Empty;
            case "event-entity-id":
                return runtimeEvent.EntityId.ToString(CultureInfo.InvariantCulture);
            case "event-participant-id":
                return participantId.ToString(CultureInfo.InvariantCulture);
            case "event-reason":
                return runtimeEvent.Reason ?? string.Empty;
            case "channel-id":
                return runtimeEvent.ChannelId ?? string.Empty;
            default:
                return literal ?? string.Empty;
        }
    }

    private static bool EventEntityHasAttribute(
        EntityRuntimeState entity,
        RuntimeEntityEventSnapshot snapshot,
        string attribute)
    {
        return entity?.Attributes?.Has(attribute) == true || snapshot?.HasAttribute(attribute) == true;
    }

    private static void MoveEntity(EntityRuntimeState entity, Vector3 destination)
    {
        Vector3 target = destination;
        target.y = entity.Position.y;
        entity.Position = target;
        entity.Destination = target;
        entity.InteractionTargetUnitId = -1;
        entity.Attack?.ClearTarget();
    }

    private static void StopEntity(EntityRuntimeState entity)
    {
        if (entity == null)
            return;
        entity.Destination = entity.Position;
        entity.InteractionTargetUnitId = -1;
        entity.Attack?.ClearTarget();
        if (entity.Worker != null)
        {
            entity.Worker.TargetResourceUnitId = -1;
            entity.Worker.ExtractionTimer = 0f;
            entity.Worker.IsExtracting = false;
        }
    }

    private static bool TryParseParticipantState(string value, out ParticipantLifeState state)
    {
        return Enum.TryParse(value, true, out state);
    }

    private static bool TryParseFatalResolution(string value, out FatalDamageResolution resolution)
    {
        string normalized = Normalize(value);
        if (normalized == "prevent" || normalized == "prevented")
        {
            resolution = FatalDamageResolution.Prevented;
            return true;
        }
        if (normalized == "death")
        {
            resolution = FatalDamageResolution.Death;
            return true;
        }
        if (normalized == "downed")
        {
            resolution = FatalDamageResolution.Downed;
            return true;
        }

        resolution = FatalDamageResolution.Death;
        return false;
    }

    public static bool IsSupportedConditionType(string value)
    {
        string normalized = Normalize(value);
        return normalized == "area-has-attribute" ||
               normalized == "entity-has-attribute" ||
               normalized == "source-entity-has-attribute" ||
               normalized == "entity-definition-is" ||
               normalized == "entity-team-is" ||
               normalized == "area-team-is" ||
               normalized == "entity-life-state-is" ||
               normalized == "entity-health-at-or-below" ||
               normalized == "damage-type-is" ||
               normalized == "participant-state-is" ||
               normalized == "entity-owner-state" ||
               normalized == "participant-control-is" ||
               normalized == "participant-has-attribute" ||
               normalized == "participant-lacks-attribute" ||
               normalized == "participant-variable-is" ||
               normalized == "rule-variable-is" ||
               normalized == "channel-id-is" ||
               normalized == "match-phase-is";
    }

    public static bool IsSupportedActionType(string value)
    {
        string normalized = Normalize(value);
        return normalized == "show-message" ||
               normalized == "prevent-death" ||
               normalized == "restore-health" ||
               normalized == "set-fatal-resolution" ||
               normalized == "set-participant-state" ||
               normalized == "set-participant-control-enabled" ||
               normalized == "add-participant-attribute" ||
               normalized == "remove-participant-attribute" ||
               normalized == "set-participant-variable" ||
               normalized == "set-rule-variable" ||
               normalized == "add-entity-attribute" ||
               normalized == "remove-entity-attribute" ||
               normalized == "set-entity-health" ||
               normalized == "set-entity-life-state" ||
               normalized == "move-entity-to-area" ||
               normalized == "destroy-participant-entities" ||
               normalized == "despawn-participant-entities" ||
               normalized == "start-channel" ||
               normalized == "cancel-channel" ||
               normalized == "give-resource" ||
               normalized == "remove-resource" ||
               normalized == "spawn-entity" ||
               normalized == "despawn-event-entity" ||
               normalized == "declare-victory" ||
               normalized == "declare-defeat" ||
               normalized == "declare-draw";
    }

    public static bool TryParseEventType(string value, out RuntimeEventType eventType)
    {
        switch (Normalize(value))
        {
            case "match-started": eventType = RuntimeEventType.MatchStarted; return true;
            case "entity-spawned": eventType = RuntimeEventType.EntitySpawned; return true;
            case "entity-despawned": eventType = RuntimeEventType.EntityDespawned; return true;
            case "entity-entered-area": eventType = RuntimeEventType.EntityEnteredArea; return true;
            case "entity-stayed-in-area": eventType = RuntimeEventType.EntityStayedInArea; return true;
            case "entity-exited-area": eventType = RuntimeEventType.EntityExitedArea; return true;
            case "entity-damaged": eventType = RuntimeEventType.EntityDamaged; return true;
            case "entity-fatal-damage": eventType = RuntimeEventType.EntityFatalDamage; return true;
            case "entity-died": eventType = RuntimeEventType.EntityDied; return true;
            case "channel-started": eventType = RuntimeEventType.ChannelStarted; return true;
            case "channel-completed": eventType = RuntimeEventType.ChannelCompleted; return true;
            case "channel-cancelled": eventType = RuntimeEventType.ChannelCancelled; return true;
            case "participant-state-changed": eventType = RuntimeEventType.ParticipantStateChanged; return true;
            case "participant-control-changed": eventType = RuntimeEventType.ParticipantControlChanged; return true;
            case "participant-attribute-changed": eventType = RuntimeEventType.ParticipantAttributeChanged; return true;
            case "participant-variable-changed": eventType = RuntimeEventType.ParticipantVariableChanged; return true;
            case "resource-changed": eventType = RuntimeEventType.ResourceChanged; return true;
            case "match-result-declared": eventType = RuntimeEventType.MatchResultDeclared; return true;
            default:
                eventType = RuntimeEventType.None;
                return false;
        }
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('_', '-').ToLowerInvariant();
    }

    private sealed class RuntimeRuleState
    {
        public ScenarioRuleDefinition Definition { get; }
        public RuntimeEventType EventType { get; }
        public bool Completed;
        public float NextAllowedTime;

        public RuntimeRuleState(ScenarioRuleDefinition definition, RuntimeEventType eventType)
        {
            Definition = definition;
            EventType = eventType;
        }
    }
}
