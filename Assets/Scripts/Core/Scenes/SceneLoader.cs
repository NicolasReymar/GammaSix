using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    public const string Bootstrap = "Bootstrap";
    public const string MainMenu = "MainMenu";
    public const string Lobby = "Lobby";
    public const string GameScene = "GameScene";

    public static void LoadMainMenu()
    {
        GameManager.Instance?.SetState(GameState.MainMenu);
        LoadLocalScene(MainMenu);
    }

    public static void LoadLobby()
    {
        GameManager.Instance?.SetState(GameState.Lobby);
        LoadLocalScene(Lobby);
    }

    public static void LoadGameScene()
    {
        GameManager.Instance?.SetState(GameState.Loading);
        LoadLocalScene(GameScene);
    }

    public static void LoadLocalScene(string sceneName)
    {
        Debug.Log($"[SceneLoader] Cargando escena local: {sceneName}");
        SceneManager.LoadScene(sceneName);
    }
}
