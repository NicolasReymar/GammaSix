using System;

[Serializable]
public sealed class CampaignDefinition
{
    public string contentType = "campaign";
    public string id;
    public string name;
    public string description;
    public CampaignStepDefinition[] steps;
}

[Serializable]
public sealed class CampaignStepDefinition
{
    public string id;
    public string type;
    public string scenarioId;
    public string resource;
}
