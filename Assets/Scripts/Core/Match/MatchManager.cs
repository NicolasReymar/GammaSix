using UnityEngine;

public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance { get; private set; }

    public MatchConfig CurrentMatchConfig { get; private set; }

    public bool HasActiveMatch => CurrentMatchConfig != null;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
    }

    public void CreateMatch(MatchConfig matchConfig)
    {
        if (matchConfig == null)
        {
            Debug.LogError("[MatchManager] No se puede crear una partida con configuración null.");
            return;
        }

        CurrentMatchConfig = matchConfig;

        Debug.Log($"[MatchManager] Partida creada. Modo: {matchConfig.Mode}, Escenario: {matchConfig.ScenarioId}");
    }

    public void ClearMatch()
    {
        CurrentMatchConfig = null;

        Debug.Log("[MatchManager] Partida limpiada.");
    }
}