using System;

/// <summary>
/// Participante sincronizado de una partida.
///
/// El nombre de la clase se conserva para no romper todavía los sistemas de
/// gameplay que esperan NetworkPlayerInfo, pero ahora representa tanto humanos
/// conectados como participantes headless ejecutados por el servidor.
/// </summary>
[Serializable]
public class NetworkPlayerInfo
{
    public int ParticipantId;
    public string SlotId;
    public int SlotIndex;

    public ParticipantControllerKind ControllerKind;
    public string ControllerProfileId;
    public string ControllerSourceId;

    public bool ParticipantLocked;
    public bool TeamLocked;
    public bool ColorLocked;

    /// <summary>
    /// ClientId real para humanos. Para headless se conserva un identificador
    /// sintético únicamente por compatibilidad de red/visual; el gameplay valida
    /// propiedad mediante ParticipantId.
    /// </summary>
    public ulong ClientId;

    public string PlayerName;
    public int TeamId;
    public int ColorId;
    public bool IsReady;

    public bool IsHuman => ControllerKind == ParticipantControllerKind.Human;
    public bool IsHeadless => ControllerKind == ParticipantControllerKind.Headless;
    public bool RequiresReady => IsHuman;

    public NetworkPlayerInfo() { }

    public NetworkPlayerInfo(
        int participantId,
        string slotId,
        int slotIndex,
        ulong clientId,
        string playerName,
        int teamId,
        int colorId,
        bool isReady)
    {
        ParticipantId = participantId;
        SlotId = slotId;
        SlotIndex = slotIndex;
        ControllerKind = ParticipantControllerKind.Human;
        ClientId = clientId;
        PlayerName = playerName;
        TeamId = teamId;
        ColorId = colorId;
        IsReady = isReady;
    }

    public static NetworkPlayerInfo CreateHeadless(
        int participantId,
        string slotId,
        int slotIndex,
        string playerName,
        int teamId,
        int colorId,
        string controllerProfileId,
        string controllerSourceId,
        bool participantLocked,
        bool teamLocked,
        bool colorLocked)
    {
        return new NetworkPlayerInfo
        {
            ParticipantId = participantId,
            SlotId = slotId,
            SlotIndex = slotIndex,
            ControllerKind = ParticipantControllerKind.Headless,
            ControllerProfileId = controllerProfileId,
            ControllerSourceId = controllerSourceId,
            ParticipantLocked = participantLocked,
            TeamLocked = teamLocked,
            ColorLocked = colorLocked,
            ClientId = CreateSyntheticClientId(participantId),
            PlayerName = playerName,
            TeamId = teamId,
            ColorId = colorId,
            IsReady = true
        };
    }

    public static ulong CreateSyntheticClientId(int participantId)
    {
        int safeId = Math.Max(1, participantId);
        return ulong.MaxValue - (ulong)safeId;
    }
}
