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
        EnsureScenarioTerrain(MatchManager.Instance.CurrentMatchConfig.ScenarioId);
        EnsureMatchRuntime(MatchManager.Instance.CurrentMatchConfig);
        EnsureEntityCoordinator();
        EnsureMatchTextChannel();
        EnsureGameHud();
    }

    private void EnsureScenarioTerrain(string scenarioId)
    {
        ScenarioTerrainController existing = FindFirstObjectByType<ScenarioTerrainController>();
        if (existing != null)
        {
            existing.Initialize(scenarioId);
            return;
        }

        GameObject terrainObject = new("Scenario Terrain");
        ScenarioTerrainController controller = terrainObject.AddComponent<ScenarioTerrainController>();
        controller.Initialize(scenarioId);
    }

    private void EnsureMatchTextChannel()
    {
        if (MatchTextChannelController.Instance != null)
            return;

        GameObject channelObject = new("Match Text Channel");
        channelObject.AddComponent<MatchTextChannelController>();
    }

    private void EnsureGameHud()
    {
        if (GameHudController.Instance != null)
            return;

        GameObject hudObject = new("Game HUD");
        hudObject.AddComponent<GameHudController>();
    }

    private void EnsureMatchRuntime(MatchConfig matchConfig)
    {
        MatchRuntimeController controller = MatchRuntimeController.Instance;
        if (controller == null)
        {
            GameObject runtimeObject = new("Match Runtime");
            controller = runtimeObject.AddComponent<MatchRuntimeController>();
        }

        if (!controller.IsInitialized)
            controller.Initialize(matchConfig);
    }

    private void EnsureEntityCoordinator()
    {
        if (NetworkEntityCoordinator.Instance != null)
            return;

        // El coordinador conserva input, selección, vistas y adaptación de red.
        // En un jugador utiliza el mismo runtime autoritativo de forma local.
        GameObject unitSystemObject = new("Entity Coordinator");
        unitSystemObject.AddComponent<NetworkEntityCoordinator>();
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
