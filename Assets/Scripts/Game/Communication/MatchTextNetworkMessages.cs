using System;

public static class MatchTextNetworkMessageNames
{
    public const string Submit = "GammaSix.MatchText.Submit";
    public const string Display = "GammaSix.MatchText.Display";
}

[Serializable]
public sealed class MatchTextSubmitPayload
{
    public string Text;
}

[Serializable]
public sealed class MatchTextDisplayPayload
{
    public string SenderName;
    public string Text;
    public bool IsSystem;
    public bool IsError;
}
