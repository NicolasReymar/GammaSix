using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public partial class NetworkSessionManager
{
    public void ToggleLocalReady()
    {
        if (!IsConnectedClient)
            return;

        NetworkPlayerInfo local = GetLocalPlayer();
        if (local == null || !local.IsHuman)
            return;

        bool nextReady = !local.IsReady;

        if (IsHost)
        {
            local.IsReady = nextReady;
            BroadcastRoster();
            return;
        }

        using FastBufferWriter writer = new(sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(nextReady);
        Manager.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer);
    }

    public bool RequestColorChange(int targetParticipantId, int colorId)
    {
        colorId = PlayerColorPalette.Normalize(colorId);
        NetworkPlayerInfo local = GetLocalPlayer();
        NetworkPlayerInfo target = GetParticipant(targetParticipantId);
        if (local == null || target == null)
            return false;

        if (!IsHost && target.ParticipantId != local.ParticipantId)
        {
            SetStatus("Solo el host puede cambiar el color de otro participante.");
            return false;
        }

        if (!IsHost && FixedColors)
        {
            SetStatus("Los colores están fijados por el host.");
            return false;
        }

        if (IsHost)
            return ApplyColorChange(targetParticipantId, colorId, Manager.LocalClientId);

        ColorRequestPayload request = new()
        {
            TargetParticipantId = targetParticipantId,
            ColorId = colorId
        };
        FixedString512Bytes payload = JsonUtility.ToJson(request);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(ColorRequestMessage, NetworkManager.ServerClientId, writer);
        return true;
    }

    public bool RequestTeamChange(int targetParticipantId, int teamId)
    {
        NetworkPlayerInfo local = GetLocalPlayer();
        NetworkPlayerInfo target = GetParticipant(targetParticipantId);
        if (local == null || target == null)
            return false;

        teamId = Mathf.Clamp(teamId, 1, Mathf.Max(1, SelectedScenarioMaxTeams));

        if (FixedTeams || target.TeamLocked)
        {
            SetStatus("Los equipos están fijados por la configuración de la partida.");
            return false;
        }

        if (!IsHost && target.ParticipantId != local.ParticipantId)
        {
            SetStatus("Solo el host puede cambiar el equipo de otro participante.");
            return false;
        }

        if (!IsHost && local.IsReady)
        {
            SetStatus("Debes marcarte como no listo antes de cambiar de equipo.");
            return false;
        }

        if (IsHost)
            return ApplyTeamChange(targetParticipantId, teamId, Manager.LocalClientId);

        TeamRequestPayload request = new()
        {
            TargetParticipantId = targetParticipantId,
            TeamId = teamId
        };
        FixedString512Bytes payload = JsonUtility.ToJson(request);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(TeamRequestMessage, NetworkManager.ServerClientId, writer);
        return true;
    }

    public bool AddHeadlessParticipant(string profileId)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede agregar participantes headless.");
            return false;
        }

        HeadlessProfileDefinition profile = availableHeadlessProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            SetStatus("El perfil headless no está disponible para el modo de juego activo.");
            return false;
        }

        int currentInstances = players.Count(item => item.IsHeadless &&
            string.Equals(item.ControllerProfileId, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (currentInstances >= Mathf.Max(1, profile.MaximumInstances))
        {
            SetStatus($"Ya se alcanzó el máximo de instancias para {profile.DisplayName}.");
            return false;
        }

        int slotIndex = FindFirstFreeSlotIndex(0, SelectedScenarioMaxParticipants);
        if (slotIndex < 0)
        {
            SetStatus("No quedan casillas disponibles para agregar un headless.");
            return false;
        }

        ResetHumanReadiness();
        int participantId = AllocateParticipantId();
        int teamId = GetNextTeamId();
        int colorId = GetInitialColorForTeam(teamId);
        NetworkPlayerInfo headless = NetworkPlayerInfo.CreateHeadless(
            participantId,
            GetDefaultSlotId(slotIndex),
            slotIndex,
            profile.DisplayName,
            teamId,
            colorId,
            profile.Id,
            profile.SourceId,
            participantLocked: false,
            teamLocked: false,
            colorLocked: false);

        players.Add(headless);
        SortParticipants();
        BroadcastRoster();

        string runtimeNote = profile.RuntimeImplemented
            ? string.Empty
            : " La inteligencia de gameplay se conectará en la siguiente etapa; esta actualización deja listo el participante y el matchmaking.";
        SetStatus($"Headless agregado: {profile.DisplayName}.{runtimeNote}");
        return true;
    }

    public bool RemoveHeadlessParticipant(int participantId)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede quitar participantes headless.");
            return false;
        }

        NetworkPlayerInfo participant = GetParticipant(participantId);
        if (participant == null || !participant.IsHeadless)
            return false;

        if (participant.ParticipantLocked)
        {
            SetStatus("Este participante headless es obligatorio para el escenario.");
            return false;
        }

        players.Remove(participant);
        ResetHumanReadiness();
        BroadcastRoster();
        SetStatus($"Headless eliminado: {participant.PlayerName}.");
        return true;
    }

    public bool SetFixedColors(bool fixedColors)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede fijar los colores.");
            return false;
        }

        ActiveSettingOverride activeOverride = activeOverrides.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Key, "fixedColors", StringComparison.OrdinalIgnoreCase));
        if (activeOverride != null)
            activeOverride.Enabled = false;

        FixedColors = fixedColors;
        BroadcastLobbySettings();
        LobbySettingsChanged?.Invoke();
        SetStatus(FixedColors ? "Colores fijos activados." : "Colores fijos desactivados.");
        return true;
    }

    public bool SetFixedTeams(bool fixedTeams)
    {
        if (!IsHost)
        {
            SetStatus("Solo el host puede fijar los equipos.");
            return false;
        }

        ActiveSettingOverride activeOverride = activeOverrides.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
        if (activeOverride != null)
            activeOverride.Enabled = false;

        FixedTeamsForcedByScenario = false;
        hostFixedTeamsSetting = fixedTeams;
        FixedTeams = fixedTeams;
        BroadcastLobbySettings();
        LobbySettingsChanged?.Invoke();
        SetStatus(FixedTeams ? "Equipos fijos activados." : "Equipos fijos desactivados.");
        return true;
    }

    private void HandleReadyMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost)
            return;

        reader.ReadValueSafe(out bool isReady);
        NetworkPlayerInfo current = players.FirstOrDefault(p => p.IsHuman && p.ClientId == senderClientId);
        if (current == null)
            return;

        current.IsReady = isReady;
        BroadcastRoster();
    }

    private void HandleColorRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost)
            return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        ColorRequestPayload request = JsonUtility.FromJson<ColorRequestPayload>(payload.ToString());
        if (request != null)
            ApplyColorChange(request.TargetParticipantId, request.ColorId, senderClientId);
    }

    private void HandleTeamRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost)
            return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        TeamRequestPayload request = JsonUtility.FromJson<TeamRequestPayload>(payload.ToString());
        if (request != null)
            ApplyTeamChange(request.TargetParticipantId, request.TeamId, senderClientId);
    }

    private bool ApplyTeamChange(int targetParticipantId, int requestedTeamId, ulong requesterClientId)
    {
        NetworkPlayerInfo requester = players.FirstOrDefault(p => p.IsHuman && p.ClientId == requesterClientId);
        NetworkPlayerInfo target = GetParticipant(targetParticipantId);
        if (requester == null || target == null)
            return false;

        bool requesterIsHost = requesterClientId == Manager.LocalClientId;

        if (FixedTeams || target.TeamLocked)
        {
            SetStatus("Los equipos están fijados por la configuración de la partida.");
            return false;
        }

        if (!requesterIsHost && target.ParticipantId != requester.ParticipantId)
        {
            SetStatus("Un jugador no puede cambiar el equipo de otro participante.");
            return false;
        }

        if (!requesterIsHost && target.IsReady)
        {
            SetStatus("Debes marcarte como no listo antes de cambiar de equipo.");
            return false;
        }

        requestedTeamId = Mathf.Clamp(requestedTeamId, 1, Mathf.Max(1, SelectedScenarioMaxTeams));
        if (target.TeamId == requestedTeamId)
            return true;

        target.TeamId = requestedTeamId;
        if (target.IsHuman)
            target.IsReady = false;

        BroadcastRoster();
        SetStatus($"Equipo de {target.PlayerName}: Equipo {target.TeamId}.");
        return true;
    }

    private bool ApplyColorChange(int targetParticipantId, int requestedColorId, ulong requesterClientId)
    {
        NetworkPlayerInfo requester = players.FirstOrDefault(p => p.IsHuman && p.ClientId == requesterClientId);
        NetworkPlayerInfo target = GetParticipant(targetParticipantId);
        if (requester == null || target == null)
            return false;

        bool requesterIsHost = requesterClientId == Manager.LocalClientId;
        if (!requesterIsHost && target.ParticipantId != requester.ParticipantId)
        {
            SetStatus("Un jugador no puede cambiar el color de otro participante.");
            return false;
        }

        if (target.ColorLocked || (!requesterIsHost && FixedColors))
        {
            SetStatus("El color está fijado por la configuración de la partida.");
            return false;
        }

        if (!requesterIsHost && target.IsReady)
        {
            SetStatus("Debes marcarte como no listo antes de cambiar de color.");
            return false;
        }

        requestedColorId = PlayerColorPalette.Normalize(requestedColorId);
        if (target.ColorId == requestedColorId)
            return true;

        NetworkPlayerInfo occupied = players.FirstOrDefault(p =>
            p.ColorId == requestedColorId && p.ParticipantId != target.ParticipantId);
        if (!requesterIsHost && occupied != null)
        {
            SetStatus("Ese color ya está ocupado.");
            return false;
        }

        int previousTargetColor = target.ColorId;
        target.ColorId = requestedColorId;
        if (target.IsHuman)
            target.IsReady = false;

        if (requesterIsHost && occupied != null)
        {
            occupied.ColorId = previousTargetColor;
            if (occupied.IsHuman)
                occupied.IsReady = false;
        }

        BroadcastRoster();
        SetStatus($"Color de {target.PlayerName}: {PlayerColorPalette.GetName(target.ColorId)}.");
        return true;
    }

    private NetworkPlayerInfo UpsertPlayer(ulong clientId, string playerName, int teamId, int colorId, bool isReady)
    {
        NetworkPlayerInfo existing = players.FirstOrDefault(p => p.IsHuman && p.ClientId == clientId);
        if (existing == null)
        {
            int slotIndex = FindFirstFreeSlotIndex(0, SelectedScenarioMaxParticipants);
            if (slotIndex < 0)
                return null;

            existing = new NetworkPlayerInfo(
                AllocateParticipantId(),
                GetDefaultSlotId(slotIndex),
                slotIndex,
                clientId,
                playerName,
                teamId,
                colorId,
                isReady);
            players.Add(existing);
        }
        else
        {
            existing.PlayerName = playerName;
            existing.TeamId = teamId;
            existing.ColorId = colorId;
            existing.IsReady = isReady;
        }

        SortParticipants();
        PlayersChanged?.Invoke();
        return existing;
    }

    private int AllocateParticipantId()
    {
        while (players.Any(item => item.ParticipantId == nextParticipantId))
            nextParticipantId++;

        return nextParticipantId++;
    }

    private int FindFirstFreeSlotIndex(int minimumInclusive, int maximumExclusive)
    {
        int upper = Mathf.Clamp(maximumExclusive, 1, MaxParticipants);
        int lower = Mathf.Clamp(minimumInclusive, 0, upper - 1);
        for (int slotIndex = lower; slotIndex < upper; slotIndex++)
        {
            if (players.All(item => item.SlotIndex != slotIndex))
                return slotIndex;
        }

        return -1;
    }

    private static string GetDefaultSlotId(int slotIndex)
    {
        return $"slot.{slotIndex + 1}";
    }

    private void SortParticipants()
    {
        players.Sort((a, b) =>
        {
            int bySlot = a.SlotIndex.CompareTo(b.SlotIndex);
            return bySlot != 0 ? bySlot : a.ParticipantId.CompareTo(b.ParticipantId);
        });
    }

    private void ResetHumanReadiness()
    {
        foreach (NetworkPlayerInfo participant in players)
        {
            if (participant.IsHuman)
                participant.IsReady = false;
        }
    }

    private int GetNextTeamId()
    {
        return LobbyRuleService.GetNextTeamId(players, SelectedScenarioMaxTeams);
    }

    private int GetNextAvailableColorId()
    {
        return LobbyRuleService.GetNextAvailableColorId(players);
    }

    private int GetInitialColorForTeam(int teamId)
    {
        int configured = GetConfiguredTeamColorId(teamId);
        return configured >= 0 ? configured : GetNextAvailableColorId();
    }

    private int GetConfiguredTeamColorId(int teamId)
    {
        ActiveSettingOverride colorOverride = activeOverrides.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Key, $"team{teamId}Color", StringComparison.OrdinalIgnoreCase));
        if (colorOverride == null)
            return -1;

        return ParseColorId(colorOverride.Value);
    }

    private static int ParseColorId(string value)
    {
        return LobbyRuleService.ParseColorId(value);
    }
}
