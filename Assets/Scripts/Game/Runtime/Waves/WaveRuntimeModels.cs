using System;
using System.Collections.Generic;
using System.Linq;

public enum WaveControllerRuntimeStatus
{
    Idle,
    Preparing,
    Spawning,
    WaitingForResolution,
    Paused,
    Completed,
    Stopped
}

public sealed class WaveGroupRuntimeState
{
    public string GroupId { get; internal set; }
    public int RequestedCount { get; internal set; }
    public int QueuedCount { get; internal set; }
    public int FailedCount { get; internal set; }
    public float NextSpawnAt { get; internal set; }
    public bool Started { get; internal set; }
    public bool Completed { get; internal set; }
}

public sealed class WaveControllerRuntimeState
{
    private readonly HashSet<int> activeEntityIds = new();
    private readonly List<WaveGroupRuntimeState> groups = new();

    public string ControllerId { get; internal set; }
    public WaveControllerRuntimeStatus Status { get; internal set; } = WaveControllerRuntimeStatus.Idle;
    public WaveControllerRuntimeStatus StatusBeforePause { get; internal set; } = WaveControllerRuntimeStatus.Idle;
    public int CurrentWaveIndex { get; internal set; } = -1;
    public string CurrentWaveId { get; internal set; }
    public int Cycle { get; internal set; }
    public float PhaseEndsAt { get; internal set; }
    public float PausedAt { get; internal set; }
    public int TotalQueuedThisWave { get; internal set; }
    public int TotalFailedThisWave { get; internal set; }
    public int PendingSpawnCount { get; internal set; }
    public int ActiveEntityCount => activeEntityIds.Count;
    public IReadOnlyCollection<int> ActiveEntityIds => activeEntityIds;
    public IReadOnlyList<WaveGroupRuntimeState> Groups => groups;

    internal ScenarioWaveControllerDefinition Definition { get; set; }
    internal ScenarioWaveDefinition CurrentWaveDefinition { get; set; }
    internal List<WaveGroupRuntimeState> MutableGroups => groups;

    internal void AddActiveEntity(int entityId)
    {
        if (entityId > 0)
            activeEntityIds.Add(entityId);
    }

    internal void RemoveActiveEntity(int entityId)
    {
        if (entityId > 0)
            activeEntityIds.Remove(entityId);
    }

    internal void ClearForRestart()
    {
        activeEntityIds.Clear();
        groups.Clear();
        CurrentWaveIndex = -1;
        CurrentWaveId = null;
        CurrentWaveDefinition = null;
        Cycle = 0;
        PhaseEndsAt = 0f;
        PausedAt = 0f;
        TotalQueuedThisWave = 0;
        TotalFailedThisWave = 0;
        PendingSpawnCount = 0;
        StatusBeforePause = WaveControllerRuntimeStatus.Idle;
    }

    internal void BeginWaveState(ScenarioWaveDefinition wave, int waveIndex)
    {
        CurrentWaveDefinition = wave;
        CurrentWaveIndex = waveIndex;
        CurrentWaveId = string.IsNullOrWhiteSpace(wave?.id)
            ? $"wave.{waveIndex + 1}"
            : wave.id.Trim();
        groups.Clear();
        activeEntityIds.Clear();
        TotalQueuedThisWave = 0;
        TotalFailedThisWave = 0;
        PendingSpawnCount = 0;
    }

    public override string ToString()
    {
        string wave = CurrentWaveIndex >= 0
            ? $"{CurrentWaveIndex + 1}:{CurrentWaveId}"
            : "-";
        return $"{ControllerId} estado={Status} ciclo={Cycle} oleada={wave} " +
               $"spawn={TotalQueuedThisWave} activos={ActiveEntityCount} pendientes={PendingSpawnCount}";
    }
}

internal sealed class PendingWaveSpawn
{
    public string ScenarioInstanceId;
    public WaveControllerRuntimeState Controller;
    public string GroupId;
}
