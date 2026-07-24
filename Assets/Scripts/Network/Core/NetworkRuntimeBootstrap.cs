using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkRuntimeBootstrap : MonoBehaviour
{
    public static NetworkRuntimeBootstrap Instance { get; private set; }

    public NetworkManager NetworkManager { get; private set; }
    public UnityTransport Transport { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        CreateNetworkRuntime();
    }

    private void CreateNetworkRuntime()
    {
        NetworkManager = GetComponent<NetworkManager>();
        if (NetworkManager == null)
            NetworkManager = gameObject.AddComponent<NetworkManager>();

        Transport = GetComponent<UnityTransport>();
        if (Transport == null)
            Transport = gameObject.AddComponent<UnityTransport>();

        NetworkManager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = Transport,
            EnableSceneManagement = true,
            ConnectionApproval = false
        };

        Debug.Log("[Network] Runtime de Netcode preparado.");
    }
}
