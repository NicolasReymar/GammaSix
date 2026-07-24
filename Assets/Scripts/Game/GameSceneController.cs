using UnityEngine;

public class GameSceneController : MonoBehaviour
{
    private void Start()
    {
        bool networkMatchActive = NetworkSessionManager.Instance != null &&
                                  NetworkSessionManager.Instance.IsConnectedClient;

        if (MatchManager.Instance == null || !MatchManager.Instance.HasActiveMatch)
        {
            if (networkMatchActive)
            {
                string scenarioId = NetworkSessionManager.Instance.SelectedScenarioId;
                MatchManager.Instance?.CreateMatch(MatchConfigFactory.CreateMultiplayerDefault(scenarioId));
            }
            else
            {
                Debug.LogError("[GameScene] No existe una partida activa. Volviendo al menú principal.");
                SceneLoader.LoadMainMenu();
                return;
            }
        }

        GameManager.Instance?.SetState(GameState.Playing);
        LoadMatch(MatchManager.Instance.CurrentMatchConfig);
        EnsureNetworkUnitSystem();
        EnsureGameHud();
    }

    private void EnsureGameHud()
    {
        if (GameHudController.Instance != null)
            return;

        GameObject hudObject = new("Game HUD");
        hudObject.AddComponent<GameHudController>();
    }

    private void EnsureNetworkUnitSystem()
    {
        bool networkMatchActive = NetworkSessionManager.Instance != null &&
                                  NetworkSessionManager.Instance.IsConnectedClient;

        if (!networkMatchActive || NetworkUnitSystem.Instance != null)
            return;

        GameObject unitSystemObject = new("Network Unit System");
        unitSystemObject.AddComponent<NetworkUnitSystem>();
    }

    private void LoadMatch(MatchConfig matchConfig)
    {
        Debug.Log("========== PARTIDA CARGADA ==========");
        Debug.Log($"Modo: {matchConfig.Mode}");
        Debug.Log($"Escenario: {matchConfig.ScenarioId}");
        Debug.Log($"Cantidad de equipos: {matchConfig.Teams.Count}");

        foreach (TeamSetup team in matchConfig.Teams)
            Debug.Log($"Equipo {team.TeamId}: {team.TeamName}");

        if (NetworkSessionManager.Instance != null && NetworkSessionManager.Instance.IsConnectedClient)
        {
            NetworkPlayerInfo localPlayer = NetworkSessionManager.Instance.GetLocalPlayer();
            if (localPlayer != null)
                Debug.Log($"Jugador local: {localPlayer.PlayerName} | Equipo: {localPlayer.TeamId}");
        }

        Debug.Log("====================================");
    }
}
