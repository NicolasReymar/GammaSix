using UnityEngine;

/// <summary>
/// Centraliza la representación visual de la relación entre el jugador local
/// y una entidad objetivo. Se usa tanto para selección como para confirmar
/// órdenes contextuales.
/// </summary>
public static class EntityRelationshipVisuals
{
    public static readonly Color Friendly = new(0.10f, 1.00f, 0.20f, 1f);
    public static readonly Color Neutral = new(1.00f, 0.85f, 0.10f, 1f);
    public static readonly Color Enemy = new(1.00f, 0.12f, 0.10f, 1f);

    public static EntityRelation GetLocalRelation(NetworkEntityView target)
    {
        if (target == null)
            return EntityRelation.Neutral;

        NetworkSessionManager session = NetworkSessionManager.Instance;
        NetworkPlayerInfo localPlayer = session?.GetLocalPlayer();
        ulong localClientId = localPlayer?.ClientId ?? ulong.MaxValue;
        int localTeamId = localPlayer?.TeamId ?? -1;

        if (target.TeamId == 0)
            return EntityRelation.Neutral;

        if (target.OwnerClientId == localClientId)
            return EntityRelation.Owned;

        if (localTeamId > 0 && target.TeamId == localTeamId)
            return EntityRelation.Allied;

        return EntityRelation.Enemy;
    }

    public static Color GetColor(NetworkEntityView target)
    {
        return GetColor(GetLocalRelation(target));
    }

    public static Color GetColor(EntityRelation relation)
    {
        return relation switch
        {
            EntityRelation.Self => Friendly,
            EntityRelation.Owned => Friendly,
            EntityRelation.Allied => Friendly,
            EntityRelation.Neutral => Neutral,
            EntityRelation.Enemy => Enemy,
            _ => Neutral
        };
    }
}
