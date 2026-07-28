using System;
using System.Collections.Generic;
using System.Linq;

public sealed class ResourceRuntimeState
{
    public bool Infinite;
    public string OnResourcesSpentEntityId;
    public int ResourceTier;
    public string[] ExtractionTools = Array.Empty<string>();
    public float InteractionRange;
    public int AmountPerExtraction;
    public List<ResourceAmountRuntimeState> Resources = new();

    public bool IsSpent => !Infinite && Resources.All(resource => resource.Amount <= 0);
}

public sealed class ResourceAmountRuntimeState
{
    public string ResourceId;
    public int Amount;
}

public sealed class WorkerRuntimeState
{
    public float ExtractionTime;
    public bool RepeatExtraction;
    public string ResourceName;
    public int WorkerTier;
    public string[] Tools = Array.Empty<string>();
    public float InteractionRange;

    public int TargetResourceUnitId = -1;
    public float ExtractionTimer;
    public bool IsExtracting;
    public string CarriedResourceName;
    public int CarriedResourceAmount;
}
