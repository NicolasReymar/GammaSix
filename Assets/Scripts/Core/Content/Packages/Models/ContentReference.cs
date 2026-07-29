using System;

/// <summary>
/// Referencia de contenido con namespace. "base:id" resuelve contenido legado
/// del juego; "package.id:id" resuelve contenido de un paquete instalado.
/// </summary>
public readonly struct ContentReference
{
    public const string BasePackageId = "base";

    public string PackageId { get; }
    public string LocalId { get; }
    public bool IsQualified => !string.IsNullOrWhiteSpace(PackageId);
    public bool IsBase => string.Equals(PackageId, BasePackageId, StringComparison.OrdinalIgnoreCase);

    public ContentReference(string packageId, string localId)
    {
        PackageId = packageId?.Trim();
        LocalId = localId?.Trim();
    }

    public static ContentReference Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ContentReference(null, null);

        string trimmed = value.Trim();
        int separator = trimmed.IndexOf(':');
        if (separator <= 0 || separator >= trimmed.Length - 1)
            return new ContentReference(null, trimmed);

        return new ContentReference(
            trimmed.Substring(0, separator),
            trimmed.Substring(separator + 1));
    }

    public static string Qualify(string packageId, string localId)
    {
        if (string.IsNullOrWhiteSpace(localId))
            return localId;
        if (Parse(localId).IsQualified || string.IsNullOrWhiteSpace(packageId))
            return localId.Trim();
        return $"{packageId.Trim()}:{localId.Trim()}";
    }

    public override string ToString()
    {
        return IsQualified ? $"{PackageId}:{LocalId}" : LocalId;
    }
}
