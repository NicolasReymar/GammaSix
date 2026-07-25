using System.Collections.Generic;

/// <summary>
/// Grupo lógico utilizado por el inspector y el visor extendido.
/// Las entidades heroicas siempre forman un grupo individual.
/// </summary>
public sealed class SelectionInspectionGroup
{
    public string Key { get; }
    public string DisplayName { get; }
    public bool IsHeroic { get; }
    public IReadOnlyList<NetworkEntityView> Members { get; }
    public NetworkEntityView Representative { get; }
    public int Count => Members?.Count ?? 0;

    public SelectionInspectionGroup(
        string key,
        string displayName,
        bool isHeroic,
        IReadOnlyList<NetworkEntityView> members,
        NetworkEntityView representative)
    {
        Key = key;
        DisplayName = displayName;
        IsHeroic = isHeroic;
        Members = members;
        Representative = representative;
    }
}
