using System;

[Serializable]
public sealed class ScenarioWaveControllerDefinition
{
    public string id;
    public bool enabled = true;
    public bool autoStart = true;
    public float initialDelay;
    public float defaultInterWaveDelay = 2f;
    public string repeatMode = "none";
    public int repeatCount = 1;
    public ScenarioWaveDefinition[] waves;
}

[Serializable]
public sealed class ScenarioWaveDefinition
{
    public string id;
    public float preparationTime;
    public float delayAfterCompletion = -1f;
    public string completionCondition = "all-spawned-resolved";
    public ScenarioWaveGroupDefinition[] groups;
}

[Serializable]
public sealed class ScenarioWaveGroupDefinition
{
    public string id;
    public string entityId;
    public int count = 1;
    public float startDelay;
    public float spawnInterval;

    public int ownerParticipantId;
    public string ownerSlotId;
    public int teamId;
    public int colorId = -1;

    public string spawnAreaAttribute;
    public ScenarioVector3 position;
    public bool randomizePositionInArea;
    public float positionJitterRadius;
    public string[] attributes;
}

public static class ScenarioWaveRepeatModes
{
    public const string None = "none";
    public const string Loop = "loop";
}

public static class ScenarioWaveCompletionConditions
{
    public const string SpawnComplete = "spawn-complete";
    public const string AllSpawnedResolved = "all-spawned-resolved";
}
