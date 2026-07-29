using System;

public sealed class RuntimeChannelState
{
    public string Key;
    public string ChannelId;
    public int SourceEntityId;
    public int AreaEntityId;
    public int TargetParticipantId;
    public float Duration;
    public float Elapsed;
    public ParticipantLifeState? RequiredParticipantState;
    public string RequiredParticipantAttribute;
    public string Reason;

    public float Progress => Duration <= 0f ? 1f : (float)Math.Min(1d, (double)Elapsed / Duration);
}
