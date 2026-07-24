using UnityEngine;

[System.Serializable]
public class TeamSetup
{
    public int TeamId { get; }
    public string TeamName { get; }
    public Color TeamColor { get; }

    public TeamSetup(int teamId, string teamName, Color teamColor)
    {
        TeamId = teamId;
        TeamName = teamName;
        TeamColor = teamColor;
    }
}
