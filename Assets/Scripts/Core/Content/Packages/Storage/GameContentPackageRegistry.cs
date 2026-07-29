using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class GameContentPackageRegistry
{
    public static IReadOnlyList<InstalledGameContentPackage> LoadAll()
    {
        InstalledGameContentPackageRegistry registry = LoadRegistry();
        return registry.Packages
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.PackageId))
            .OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryGet(string packageId, out InstalledGameContentPackage package)
    {
        package = LoadRegistry().Packages.FirstOrDefault(item =>
            item != null && string.Equals(item.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        return package != null;
    }

    public static void Upsert(InstalledGameContentPackage package)
    {
        if (package == null || string.IsNullOrWhiteSpace(package.PackageId))
            throw new ArgumentException("El paquete instalado no tiene PackageId.", nameof(package));

        InstalledGameContentPackageRegistry registry = LoadRegistry();
        registry.Packages.RemoveAll(item =>
            item != null && string.Equals(item.PackageId, package.PackageId, StringComparison.OrdinalIgnoreCase));
        registry.Packages.Add(package);
        SaveRegistry(registry);
    }

    public static bool Remove(string packageId)
    {
        InstalledGameContentPackageRegistry registry = LoadRegistry();
        int removed = registry.Packages.RemoveAll(item =>
            item != null && string.Equals(item.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        if (removed <= 0)
            return false;
        SaveRegistry(registry);
        return true;
    }

    private static InstalledGameContentPackageRegistry LoadRegistry()
    {
        Directory.CreateDirectory(GameContentRepository.PackagesPath);
        string path = GameContentRepository.PackageRegistryPath;
        if (!File.Exists(path))
            return new InstalledGameContentPackageRegistry();

        try
        {
            InstalledGameContentPackageRegistry registry =
                JsonUtility.FromJson<InstalledGameContentPackageRegistry>(File.ReadAllText(path));
            registry ??= new InstalledGameContentPackageRegistry();
            registry.Packages ??= new List<InstalledGameContentPackage>();
            return registry;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[GameContentPackageRegistry] Registro inválido: {exception.Message}");
            return new InstalledGameContentPackageRegistry();
        }
    }

    private static void SaveRegistry(InstalledGameContentPackageRegistry registry)
    {
        Directory.CreateDirectory(GameContentRepository.PackagesPath);
        string temporary = GameContentRepository.PackageRegistryPath + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(registry, true));
        if (File.Exists(GameContentRepository.PackageRegistryPath))
            File.Delete(GameContentRepository.PackageRegistryPath);
        File.Move(temporary, GameContentRepository.PackageRegistryPath);
    }
}
