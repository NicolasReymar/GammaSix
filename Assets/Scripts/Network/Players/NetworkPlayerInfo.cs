using System;

[Serializable]
public class NetworkPlayerInfo
{
    public ulong ClientId;
    public string PlayerName;
    public int TeamId;
    public int ColorId;
    public bool IsReady;

    public NetworkPlayerInfo(ulong clientId, string playerName, int teamId, int colorId, bool isReady)
    {
        ClientId = clientId;
        PlayerName = playerName;
        TeamId = teamId;
        ColorId = colorId;
        IsReady = isReady;
    }
}
