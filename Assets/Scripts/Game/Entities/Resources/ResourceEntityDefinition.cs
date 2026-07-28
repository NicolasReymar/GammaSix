using System;

[Serializable]
public sealed class ResourceEntityDefinition
{
    public bool infinite;
    public string onResourcesSpentEntityId;
    public ResourceAmountDefinition[] resources;
    public int resourceTier = 1;
    public string[] extractionTools;
    public float interactionRange = 1.25f;
    public int amountPerExtraction = 1;
}

[Serializable]
public sealed class ResourceAmountDefinition
{
    public string resourceId;
    public int amount;
}
