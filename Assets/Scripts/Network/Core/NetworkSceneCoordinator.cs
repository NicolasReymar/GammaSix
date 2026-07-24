using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NetworkSceneCoordinator
{
    public static bool LoadNetworkScene(string sceneName)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || !manager.IsListening)
        {
            Debug.LogError("[NetworkScene] No existe una sesión de red activa.");
            return false;
        }

        if (!manager.IsServer)
        {
            Debug.LogWarning("[NetworkScene] Solo el servidor puede cargar escenas sincronizadas.");
            return false;
        }

        manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        return true;
    }
}
