using System;

[Serializable]
public sealed class WorkerEntityDefinition
{
    public float extractionTime = 1f;
    public bool repeatExtraction;
    public string resourceName;
    public int workerTier = 1;
    public string[] tools;
    public float interactionRange = 1.25f;
}
