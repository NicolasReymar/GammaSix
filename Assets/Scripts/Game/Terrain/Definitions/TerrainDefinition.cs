using System;

[Serializable]
public sealed class TerrainDefinition
{
    public string id;
    public string name;
    public string category;
    public string subCategory;
    public float tileSize = 1f;
    public string color = "#4FAE55";
    public bool walkable = true;
    public float movementCost = 1f;
    public string[] attributes;
}
