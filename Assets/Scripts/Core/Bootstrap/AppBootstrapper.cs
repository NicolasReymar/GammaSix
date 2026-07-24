using UnityEngine;

public class AppBootstrapper : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        InitializeApplication();
        SceneLoader.LoadMainMenu();
    }

    private void InitializeApplication()
    {
        Application.targetFrameRate = 60;

        EnsureGameManagerExists();
        EnsureMatchManagerExists();
        EnsureNetworkRuntimeExists();
        EnsureNetworkSessionManagerExists();

        GameManager.Instance.SetState(GameState.MainMenu);
        Debug.Log("[Bootstrap] Aplicación inicializada");
    }

    private void EnsureGameManagerExists()
    {
        if (GameManager.Instance != null) return;
        new GameObject("GameManager").AddComponent<GameManager>();
    }

    private void EnsureMatchManagerExists()
    {
        if (MatchManager.Instance != null) return;
        new GameObject("MatchManager").AddComponent<MatchManager>();
    }

    private void EnsureNetworkRuntimeExists()
    {
        if (NetworkRuntimeBootstrap.Instance != null) return;
        new GameObject("NetworkRuntime").AddComponent<NetworkRuntimeBootstrap>();
    }

    private void EnsureNetworkSessionManagerExists()
    {
        if (NetworkSessionManager.Instance != null) return;
        new GameObject("NetworkSessionManager").AddComponent<NetworkSessionManager>();
    }
}
