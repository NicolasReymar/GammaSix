using System;

/// <summary>
/// Perfil visible en el catálogo Headless del lobby. El perfil describe al
/// controlador; la inteligencia se ejecutará únicamente en host/servidor.
/// </summary>
[Serializable]
public sealed class HeadlessProfileDefinition
{
    public string Id;
    public string DisplayName;
    public string Description;
    public string SourceId;
    public string SourceLabel;
    public string GameModeId;
    public int MaximumInstances = 1;
    public bool BuiltIn;
    public bool RuntimeImplemented;
    public string RuntimeControllerId;
    public ScenarioHeadlessControllerSettings ControllerSettings;
}
