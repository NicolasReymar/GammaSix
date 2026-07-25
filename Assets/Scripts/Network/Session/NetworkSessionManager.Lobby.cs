using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public partial class NetworkSessionManager
{
    public void ToggleLocalReady()
    {
        if (!IsConnectedClient) return;

        NetworkPlayerInfo local = GetLocalPlayer();
        bool nextReady = local == null || !local.IsReady;

        if (IsHost)
        {
            if (local != null)
                UpsertPlayer(local.ClientId, local.PlayerName, local.TeamId, local.ColorId, nextReady);
            BroadcastRoster();
            return;
        }

        using FastBufferWriter writer = new(sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(nextReady);
        Manager.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer);
    }

    public bool RequestColorChange(ulong targetClientId, int colorId)
    {
        colorId = PlayerColorPalette.Normalize(colorId);
        NetworkPlayerInfo local = GetLocalPlayer();
        if (local == null)
            return false;

        if (!IsHost && targetClientId != local.ClientId)
        {
            SetStatus("Solo el host puede cambiar el color de otro jugador.");
            return false;
        }

        if (!IsHost && FixedColors)
        {
            SetStatus("Los colores están fijados por el host.");
            return false;
        }

        if (IsHost)
            return ApplyColorChange(targetClientId, colorId, Manager.LocalClientId);

        ColorRequestPayload request = new() { TargetClientId = targetClientId, ColorId = colorId };
        string json = JsonUtility.ToJson(request);
        FixedString512Bytes payload = json;
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(ColorRequestMessage, NetworkManager.ServerClientId, writer);
        return true;
    }

    public bool RequestTeamChange(ulong targetClientId, int teamId)
    {
        NetworkPlayerInfo local = GetLocalPlayer();
        if (local == null)
            return false;

        teamId = Mathf.Clamp(teamId, 1, Mathf.Max(1, SelectedScenarioMaxTeams));

        if (FixedTeams)
        {
            SetStatus("Los equipos están fijados por la configuración de la partida.");
            return false;
        }

        if (!IsHost && targetClientId != local.ClientId)
        {
            SetStatus("Solo el host puede cambiar el equipo de otro jugador.");
            return false;
        }

        if (!IsHost && local.IsReady)
        {
            SetStatus("Debes marcarte como no listo antes de cambiar de equipo.");
            return false;
        }

        if (IsHost)
            return ApplyTeamChange(targetClientId, teamId, Manager.LocalClientId);

        TeamRequestPayload request = new() { TargetClientId = targetClientId, TeamId = teamId };
        FixedString512Bytes payload = JsonUtility.ToJson(request);
        using FastBufferWriter writer = new(FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        Manager.CustomMessagingManager.SendNamedMessage(TeamRequestMessage, NetworkManager.ServerClientId, writer);
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
        {
            SetStatus("Colores fijos está controlado por un override del contenido. Desactiva el override para modificarlo.");
            ApplyEnabledOverrides();
            BroadcastLobbySettings();
            return false;
        }

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

        if (FixedTeamsForcedByScenario)
        {
            SetStatus("Los equipos están fijados por el escenario seleccionado.");
            ApplyEnabledOverrides();
            BroadcastLobbySettings();
            return false;
        }

        ActiveSettingOverride activeOverride = activeOverrides.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Key, "fixedTeams", StringComparison.OrdinalIgnoreCase));
        if (activeOverride != null)
        {
            SetStatus("Equipos fijos está controlado por un override del contenido. Desactiva el override para modificarlo.");
            ApplyEnabledOverrides();
            BroadcastLobbySettings();
            return false;
        }

        hostFixedTeamsSetting = fixedTeams;
        FixedTeams = fixedTeams;
        BroadcastLobbySettings();
        LobbySettingsChanged?.Invoke();
        SetStatus(FixedTeams ? "Equipos fijos activados." : "Equipos fijos desactivados.");
        return true;
    }

    private void HandleReadyMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost) return;

        reader.ReadValueSafe(out bool isReady);
        NetworkPlayerInfo current = players.FirstOrDefault(p => p.ClientId == senderClientId);
        if (current == null) return;

        UpsertPlayer(senderClientId, current.PlayerName, current.TeamId, current.ColorId, isReady);
        BroadcastRoster();
    }

    private void HandleColorRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost) return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        ColorRequestPayload request = JsonUtility.FromJson<ColorRequestPayload>(payload.ToString());
        ApplyColorChange(request.TargetClientId, request.ColorId, senderClientId);
    }

    private void HandleTeamRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (!IsHost) return;

        reader.ReadValueSafe(out FixedString512Bytes payload);
        TeamRequestPayload request = JsonUtility.FromJson<TeamRequestPayload>(payload.ToString());
        ApplyTeamChange(request.TargetClientId, request.TeamId, senderClientId);
    }

    private bool ApplyTeamChange(ulong targetClientId, int requestedTeamId, ulong requesterClientId)
    {
        NetworkPlayerInfo requester = players.FirstOrDefault(p => p.ClientId == requesterClientId);
        NetworkPlayerInfo target = players.FirstOrDefault(p => p.ClientId == targetClientId);
        if (requester == null || target == null)
            return false;

        bool requesterIsHost = requesterClientId == Manager.LocalClientId;

        if (FixedTeams)
        {
            SetStatus("Los equipos están fijados por la configuración de la partida.");
            return false;
        }

        if (!requesterIsHost && targetClientId != requesterClientId)
        {
            SetStatus("Un jugador no puede cambiar el equipo de otro jugador.");
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
        target.IsReady = false;

        if (FixedColors)
        {
            int configuredColor = GetConfiguredTeamColorId(requestedTeamId);
            if (configuredColor >= 0)
                target.ColorId = configuredColor;
        }

        BroadcastRoster();
        SetStatus($"Equipo de {target.PlayerName}: Equipo {target.TeamId}.");
        return true;
    }

    private bool ApplyColorChange(ulong targetClientId, int requestedColorId, ulong requesterClientId)
    {
        NetworkPlayerInfo requester = players.FirstOrDefault(p => p.ClientId == requesterClientId);
        NetworkPlayerInfo target = players.FirstOrDefault(p => p.ClientId == targetClientId);
        if (requester == null || target == null)
            return false;

        bool requesterIsHost = requesterClientId == Manager.LocalClientId;
        if (!requesterIsHost && targetClientId != requesterClientId)
        {
            SetStatus("Un jugador no puede cambiar el color de otro jugador.");
            return false;
        }

        if (!requesterIsHost && FixedColors)
        {
            SetStatus("Los colores están fijados por el host.");
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

        NetworkPlayerInfo occupied = players.FirstOrDefault(p => p.ColorId == requestedColorId);
        if (!requesterIsHost && occupied != null && occupied.ClientId != target.ClientId)
        {
            SetStatus("Ese color ya está ocupado.");
            return false;
        }

        int previousTargetColor = target.ColorId;
        target.ColorId = requestedColorId;
        target.IsReady = false;

        if (requesterIsHost && occupied != null && occupied.ClientId != target.ClientId)
        {
            occupied.ColorId = previousTargetColor;
            occupied.IsReady = false;
        }

        BroadcastRoster();
        SetStatus($"Color de {target.PlayerName}: {PlayerColorPalette.GetName(target.ColorId)}.");
        return true;
    }

    private void UpsertPlayer(ulong clientId, string playerName, int teamId, int colorId, bool isReady)
    {
        NetworkPlayerInfo existing = players.FirstOrDefault(p => p.ClientId == clientId);
        if (existing == null)
            players.Add(new NetworkPlayerInfo(clientId, playerName, teamId, colorId, isReady));
        else
        {
            existing.PlayerName = playerName;
            existing.TeamId = teamId;
            existing.ColorId = colorId;
            existing.IsReady = isReady;
        }

        players.Sort((a, b) => a.TeamId.CompareTo(b.TeamId));
        PlayersChanged?.Invoke();
    }

    private int GetNextTeamId()
    {
        return LobbyRuleService.GetNextTeamId(players, MaxTeams);
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
