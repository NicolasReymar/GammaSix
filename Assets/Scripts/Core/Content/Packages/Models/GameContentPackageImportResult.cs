using System;
using System.Collections.Generic;

[Serializable]
public sealed class GameContentPackageImportResult
{
    public bool Success;
    public string PackageId;
    public string PackageVersion;
    public string ContentHash;
    public string Message;
    public List<string> Errors = new();

    public static GameContentPackageImportResult Failed(string message, IEnumerable<string> errors = null)
    {
        GameContentPackageImportResult result = new()
        {
            Success = false,
            Message = message ?? "No se pudo importar el paquete."
        };
        if (errors != null)
            result.Errors.AddRange(errors);
        return result;
    }
}
