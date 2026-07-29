using System;
using System.Collections.Generic;

[Serializable]
public sealed class InstalledGameContentPackage
{
    public string PackageId;
    public string PackageVersion;
    public string DisplayName;
    public string RequiredGameVersion;
    public int ContentFormatVersion;
    public string EntryScenarioId;
    public string ContentHash;
    public string RelativeInstallPath;
    public long InstalledUtcTicks;
}

[Serializable]
public sealed class InstalledGameContentPackageRegistry
{
    public List<InstalledGameContentPackage> Packages = new();
}
