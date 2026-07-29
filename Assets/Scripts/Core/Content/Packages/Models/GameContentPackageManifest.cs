using System;

/// <summary>
/// Manifiesto raíz de un archivo .gsixpackage. La primera versión del formato
/// exige compatibilidad exacta con Application.version y contenido declarativo.
/// </summary>
[Serializable]
public sealed class GameContentPackageManifest
{
    public string packageId;
    public string packageVersion;
    public string displayName;
    public string description;
    public string author;
    public string requiredGameVersion;
    public int contentFormatVersion = 1;
    public string entryScenarioId;
    public string[] requiredFeatures;
}
