using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Canalizaciones autoritativas y reutilizables. Una regla las inicia y otra
/// reacciona a channel-completed; el sistema no conoce el significado de la
/// actividad concreta.
/// </summary>
public sealed class RuntimeChannelSystem
{
    private readonly EntityWorld world;
    private readonly MatchParticipantRegistry participants;
    private readonly EntityAreaRuntimeSystem areas;
    private readonly RuntimeEventBus eventBus;
    private readonly Dictionary<string, RuntimeChannelState> active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeChannelState> completedWhileInside = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RuntimeChannelState> ActiveChannels => active.Values
        .OrderBy(item => item.ChannelId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.SourceEntityId)
        .ToList();

    public RuntimeChannelSystem(
        EntityWorld world,
        MatchParticipantRegistry participants,
        EntityAreaRuntimeSystem areas,
        RuntimeEventBus eventBus)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.participants = participants ?? throw new ArgumentNullException(nameof(participants));
        this.areas = areas ?? throw new ArgumentNullException(nameof(areas));
        this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public bool StartOrRefresh(
        string channelId,
        int sourceEntityId,
        int areaEntityId,
        int targetParticipantId,
        float duration,
        ParticipantLifeState? requiredParticipantState,
        string requiredParticipantAttribute,
        string reason,
        float elapsedTime,
        out string rejectionReason)
    {
        rejectionReason = null;
        if (string.IsNullOrWhiteSpace(channelId))
        {
            rejectionReason = "La canalización no tiene channelId.";
            return false;
        }
        if (duration <= 0f)
        {
            rejectionReason = "La duración de la canalización debe ser mayor que cero.";
            return false;
        }
        if (!world.TryGet(sourceEntityId, out EntityRuntimeState source) || source.Life?.CanAct != true)
        {
            rejectionReason = $"La entidad canalizadora {sourceEntityId} no está activa.";
            return false;
        }
        if (!world.TryGet(areaEntityId, out EntityRuntimeState area) || area.Area == null)
        {
            rejectionReason = $"La entidad {areaEntityId} no es un área válida.";
            return false;
        }
        if (!areas.IsEntityInsideArea(areaEntityId, sourceEntityId))
        {
            rejectionReason = "La entidad canalizadora ya no está dentro del área.";
            return false;
        }

        bool requiresParticipant = requiredParticipantState.HasValue ||
                                   !string.IsNullOrWhiteSpace(requiredParticipantAttribute);
        if (requiresParticipant && targetParticipantId <= 0)
        {
            rejectionReason = "La canalización requiere un participante objetivo válido.";
            return false;
        }

        MatchParticipantRuntimeState participant = null;
        if (targetParticipantId > 0 && !participants.TryGet(targetParticipantId, out participant))
        {
            rejectionReason = $"No existe el participante objetivo {targetParticipantId}.";
            return false;
        }
        if (participant != null && requiredParticipantState.HasValue &&
            participant.LifeState != requiredParticipantState.Value)
        {
            rejectionReason = $"El participante objetivo no está en estado {requiredParticipantState.Value}.";
            return false;
        }
        if (participant != null && !string.IsNullOrWhiteSpace(requiredParticipantAttribute) &&
            !participant.Attributes.Has(requiredParticipantAttribute))
        {
            rejectionReason = $"El participante objetivo no posee el atributo '{requiredParticipantAttribute}'.";
            return false;
        }

        string key = BuildKey(channelId, sourceEntityId, areaEntityId, targetParticipantId);
        if (completedWhileInside.ContainsKey(key) || active.ContainsKey(key))
            return true;

        RuntimeChannelState state = new()
        {
            Key = key,
            ChannelId = channelId.Trim(),
            SourceEntityId = sourceEntityId,
            AreaEntityId = areaEntityId,
            TargetParticipantId = targetParticipantId,
            Duration = duration,
            Elapsed = 0f,
            RequiredParticipantState = requiredParticipantState,
            RequiredParticipantAttribute = requiredParticipantAttribute,
            Reason = reason
        };
        active.Add(key, state);
        Publish(RuntimeEventType.ChannelStarted, state, elapsedTime, null);
        return true;
    }

    public int CancelMatching(
        string channelId,
        int sourceEntityId,
        int targetParticipantId,
        string reason,
        float elapsedTime)
    {
        int cancelled = 0;
        foreach (RuntimeChannelState state in active.Values.ToArray())
        {
            if (!string.IsNullOrWhiteSpace(channelId) &&
                !string.Equals(state.ChannelId, channelId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (sourceEntityId > 0 && state.SourceEntityId != sourceEntityId)
                continue;
            if (targetParticipantId > 0 && state.TargetParticipantId != targetParticipantId)
                continue;

            active.Remove(state.Key);
            completedWhileInside.Remove(state.Key);
            Publish(RuntimeEventType.ChannelCancelled, state, elapsedTime, reason ?? "rule-cancelled");
            cancelled++;
        }
        return cancelled;
    }

    public void Update(float deltaTime, float elapsedTime)
    {
        float safeDelta = Mathf.Max(0f, deltaTime);
        foreach (RuntimeChannelState state in active.Values.ToArray())
        {
            if (!ValidateContinuity(state, out string cancellationReason))
            {
                active.Remove(state.Key);
                Publish(RuntimeEventType.ChannelCancelled, state, elapsedTime, cancellationReason);
                continue;
            }

            state.Elapsed += safeDelta;
            if (state.Elapsed < state.Duration)
                continue;

            state.Elapsed = state.Duration;
            active.Remove(state.Key);
            completedWhileInside[state.Key] = state;
            Publish(RuntimeEventType.ChannelCompleted, state, elapsedTime, null);
        }

        foreach (RuntimeChannelState state in completedWhileInside.Values.ToArray())
        {
            if (!world.TryGet(state.SourceEntityId, out _) ||
                !world.TryGet(state.AreaEntityId, out _) ||
                !areas.IsEntityInsideArea(state.AreaEntityId, state.SourceEntityId))
            {
                completedWhileInside.Remove(state.Key);
            }
        }
    }

    private bool ValidateContinuity(RuntimeChannelState state, out string reason)
    {
        reason = null;
        if (!world.TryGet(state.SourceEntityId, out EntityRuntimeState source) || source.Life?.CanAct != true)
        {
            reason = "source-unavailable";
            return false;
        }
        if (!world.TryGet(state.AreaEntityId, out EntityRuntimeState area) || area.Area == null)
        {
            reason = "area-unavailable";
            return false;
        }
        if (!areas.IsEntityInsideArea(state.AreaEntityId, state.SourceEntityId))
        {
            reason = "source-left-area";
            return false;
        }
        if (state.TargetParticipantId > 0)
        {
            if (!participants.TryGet(state.TargetParticipantId, out MatchParticipantRuntimeState participant))
            {
                reason = "participant-unavailable";
                return false;
            }
            if (state.RequiredParticipantState.HasValue &&
                participant.LifeState != state.RequiredParticipantState.Value)
            {
                reason = "participant-state-changed";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(state.RequiredParticipantAttribute) &&
                !participant.Attributes.Has(state.RequiredParticipantAttribute))
            {
                reason = "participant-attribute-changed";
                return false;
            }
        }
        return true;
    }

    private void Publish(
        RuntimeEventType type,
        RuntimeChannelState state,
        float elapsedTime,
        string cancellationReason)
    {
        world.TryGet(state.SourceEntityId, out EntityRuntimeState source);
        world.TryGet(state.AreaEntityId, out EntityRuntimeState area);
        participants.TryGet(state.TargetParticipantId, out MatchParticipantRuntimeState participant);
        eventBus.Publish(new RuntimeEventContext
        {
            Type = type,
            ElapsedTime = elapsedTime,
            EntityId = source?.UnitId ?? state.SourceEntityId,
            Entity = source,
            AreaEntityId = area?.UnitId ?? state.AreaEntityId,
            AreaEntity = area,
            ParticipantId = participant?.ParticipantId ?? state.TargetParticipantId,
            Participant = participant,
            ChannelId = state.ChannelId,
            ChannelDuration = state.Duration,
            ChannelProgress = state.Progress,
            Reason = cancellationReason ?? state.Reason
        });
    }

    private static string BuildKey(
        string channelId,
        int sourceEntityId,
        int areaEntityId,
        int targetParticipantId)
    {
        return $"{channelId.Trim().ToLowerInvariant()}:{sourceEntityId}:{areaEntityId}:{targetParticipantId}";
    }
}
