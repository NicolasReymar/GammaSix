using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Punto central para persistir los ajustes de partida. Cada sistema de
/// configuración registra un escritor y "Guardar y volver" ejecuta todos los
/// escritores antes de forzar PlayerPrefs.Save().
/// </summary>
public static class GameSettingsPersistenceService
{
    public readonly struct SaveResult
    {
        public SaveResult(int providersSaved, int valuesWritten, int failedProviders)
        {
            ProvidersSaved = providersSaved;
            ValuesWritten = valuesWritten;
            FailedProviders = failedProviders;
        }

        public int ProvidersSaved { get; }
        public int ValuesWritten { get; }
        public int FailedProviders { get; }
        public bool Success => FailedProviders == 0;
    }

    private static readonly Dictionary<string, Func<int>> Writers = new(StringComparer.Ordinal);
    private static bool defaultsRegistered;

    /// <summary>
    /// Registra o reemplaza un escritor de ajustes. El retorno indica cuántos
    /// valores fueron escritos y se usa únicamente para diagnóstico.
    /// </summary>
    public static void RegisterWriter(string key, Func<int> writer)
    {
        if (string.IsNullOrWhiteSpace(key) || writer == null)
            return;

        Writers[key] = writer;
    }

    public static void UnregisterWriter(string key, Func<int> writer)
    {
        if (string.IsNullOrWhiteSpace(key) || writer == null)
            return;

        if (Writers.TryGetValue(key, out Func<int> registered) && registered == writer)
            Writers.Remove(key);
    }

    /// <summary>
    /// Ejecuta todos los escritores registrados y fuerza la escritura en disco.
    /// Si alguno falla, el resto igualmente se intenta guardar y el resultado
    /// informa el error para que la UI no abandone la pantalla silenciosamente.
    /// </summary>
    public static SaveResult SaveAll()
    {
        EnsureDefaultWriters();

        int providersSaved = 0;
        int valuesWritten = 0;
        int failedProviders = 0;

        foreach (KeyValuePair<string, Func<int>> entry in new List<KeyValuePair<string, Func<int>>>(Writers))
        {
            try
            {
                valuesWritten += Mathf.Max(0, entry.Value.Invoke());
                providersSaved++;
            }
            catch (Exception exception)
            {
                failedProviders++;
                Debug.LogError($"[GameSettingsPersistence] No se pudo guardar '{entry.Key}': {exception}");
            }
        }

        try
        {
            PlayerPrefs.Save();
        }
        catch (Exception exception)
        {
            failedProviders++;
            Debug.LogError($"[GameSettingsPersistence] No se pudo escribir PlayerPrefs en disco: {exception}");
        }

        Debug.Log(
            $"[GameSettingsPersistence] Proveedores guardados: {providersSaved}, " +
            $"valores escritos: {valuesWritten}, errores: {failedProviders}.");

        return new SaveResult(providersSaved, valuesWritten, failedProviders);
    }

    private static void EnsureDefaultWriters()
    {
        if (defaultsRegistered)
            return;

        defaultsRegistered = true;
        RegisterWriter("hud-layout", HudLayoutPersistenceService.WriteAllRegisteredPositions);
    }
}
