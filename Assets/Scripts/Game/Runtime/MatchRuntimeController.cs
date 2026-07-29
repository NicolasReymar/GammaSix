using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Hospeda el runtime común de la partida. En multijugador solo el servidor lo
/// crea; en un jugador se ejecuta localmente con la misma implementación.
/// </summary>
public sealed class MatchRuntimeController : MonoBehaviour
{
    public static MatchRuntimeController Instance { get; private set; }

    public AuthoritativeMatchRuntime Runtime { get; private set; }
    public bool IsAuthoritative { get; private set; }
    public int LocalParticipantId { get; private set; } = -1;
    public bool IsInitialized { get; private set; }

    private MatchConfig matchConfig;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Initialize(MatchConfig config)
    {
        if (config == null)
        {
            Debug.LogError("[MatchRuntimeController] No se puede inicializar sin MatchConfig.");
            return;
        }

        matchConfig = config;
        NetworkSessionManager session = NetworkSessionManager.Instance;
        bool networkActive = session != null && session.IsListening;
        bool serverAuthority = NetworkRuntimeBootstrap.Instance?.NetworkManager != null &&
                               NetworkRuntimeBootstrap.Instance.NetworkManager.IsServer;

        LocalParticipantId = networkActive
            ? session.GetLocalPlayer()?.ParticipantId ?? -1
            : 1;

        IsAuthoritative = !networkActive || serverAuthority;
        if (!IsAuthoritative)
        {
            IsInitialized = true;
            Debug.Log("[MatchRuntimeController] Cliente remoto: recibe el estado desde el servidor.");
            return;
        }

        ScenarioDefinition scenario = GameContentRepository.LoadScenario(config.ScenarioId);
        IReadOnlyList<MatchParticipantRuntimeState> participants = networkActive
            ? BuildNetworkParticipants(session)
            : BuildOfflineParticipants();

        Runtime = new AuthoritativeMatchRuntime();
        Runtime.Initialize(scenario, participants);
        IsInitialized = true;
    }

    private void Update()
    {
        if (IsAuthoritative && Runtime != null)
            Runtime.Update(Time.deltaTime);
    }

    public bool TryResolveHumanParticipant(
        ulong clientId,
        out MatchParticipantRuntimeState participant)
    {
        participant = null;
        return Runtime?.Participants != null &&
               Runtime.Participants.TryGetHumanByClientId(clientId, out participant);
    }

    public bool QueueEntitySpawn(EntitySpawnRequest request, out string rejectionReason)
    {
        if (!IsAuthoritative || Runtime == null)
        {
            rejectionReason = "Solo la autoridad de la partida puede crear entidades.";
            return false;
        }

        return Runtime.QueueEntitySpawn(request, out rejectionReason);
    }

    public bool QueueEntityDespawn(
        int entityId,
        EntityLifecycleReason reason,
        out string rejectionReason)
    {
        if (!IsAuthoritative || Runtime == null)
        {
            rejectionReason = "Solo la autoridad de la partida puede eliminar entidades.";
            return false;
        }

        return Runtime.QueueEntityDespawn(entityId, reason, out rejectionReason);
    }

    private static IReadOnlyList<MatchParticipantRuntimeState> BuildNetworkParticipants(
        NetworkSessionManager session)
    {
        return session.Players
            .Where(item => item != null)
            .OrderBy(item => item.SlotIndex)
            .ThenBy(item => item.ParticipantId)
            .Select(MatchParticipantRuntimeState.FromLobbyParticipant)
            .ToList();
    }

    private static IReadOnlyList<MatchParticipantRuntimeState> BuildOfflineParticipants()
    {
        return new List<MatchParticipantRuntimeState>
        {
            MatchParticipantRuntimeState.CreateOfflineHuman(
                1,
                "Jugador",
                1,
                PlayerColorPalette.Blue)
        };
    }
}
