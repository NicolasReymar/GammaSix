using System;
using System.Collections.Generic;

/// <summary>
/// Cola común de órdenes autoritativas. Humanos, headless y reglas del
/// escenario ingresan por la misma ruta y se validan durante el tick.
/// </summary>
public sealed class MatchCommandBus
{
    private const int DefaultMaximumCommandsPerTick = 1024;

    private readonly Queue<MatchCommandEnvelope> pending = new();
    private long nextSequence = 1;

    public int PendingCount => pending.Count;

    public event Action<MatchCommandEnvelope, MatchCommandResult> CommandProcessed;

    public long Enqueue(
        MatchCommandIssuer issuer,
        MatchCommandType commandType,
        object payload)
    {
        long sequence = nextSequence++;
        pending.Enqueue(new MatchCommandEnvelope(sequence, issuer, commandType, payload));
        return sequence;
    }

    public int ProcessPending(
        Func<MatchCommandEnvelope, MatchCommandResult> handler,
        int maximumCommands = DefaultMaximumCommandsPerTick)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        int processed = 0;
        int safeMaximum = Math.Max(1, maximumCommands);
        while (pending.Count > 0 && processed < safeMaximum)
        {
            MatchCommandEnvelope envelope = pending.Dequeue();
            MatchCommandResult result;
            try
            {
                result = handler(envelope) ?? MatchCommandResult.Rejected("El comando no produjo un resultado.");
            }
            catch (Exception exception)
            {
                result = MatchCommandResult.Rejected($"Excepción al procesar comando: {exception.Message}");
            }

            CommandProcessed?.Invoke(envelope, result);
            processed++;
        }

        return processed;
    }

    public void Clear()
    {
        pending.Clear();
    }
}
