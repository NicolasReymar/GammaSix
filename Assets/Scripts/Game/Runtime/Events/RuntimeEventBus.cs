using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cola de eventos autoritativos. Las publicaciones realizadas por una regla se
/// procesan después del evento actual para evitar recursión inmediata.
/// </summary>
public sealed class RuntimeEventBus
{
    private const int DefaultMaxEventsPerFlush = 512;
    private readonly Queue<RuntimeEventContext> pending = new();

    public int PendingCount => pending.Count;
    public event Action<RuntimeEventContext> EventPublished;

    public void Publish(RuntimeEventContext runtimeEvent)
    {
        if (runtimeEvent == null || runtimeEvent.Type == RuntimeEventType.None)
            return;

        runtimeEvent.CaptureEntitySnapshots();
        pending.Enqueue(runtimeEvent);
    }

    public int Flush(int maxEvents = DefaultMaxEventsPerFlush)
    {
        int safeLimit = Mathf.Max(1, maxEvents);
        int processed = 0;
        while (pending.Count > 0 && processed < safeLimit)
        {
            RuntimeEventContext runtimeEvent = pending.Dequeue();
            EventPublished?.Invoke(runtimeEvent);
            processed++;
        }

        if (pending.Count > 0)
        {
            Debug.LogError(
                $"[RuntimeEventBus] Se alcanzó el límite de {safeLimit} eventos por flush. " +
                $"Se descartaron {pending.Count} eventos para evitar un ciclo infinito.");
            pending.Clear();
        }

        return processed;
    }

    public void Clear()
    {
        pending.Clear();
    }
}
