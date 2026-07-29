using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

/// <summary>
/// Importa .gsixpackage de forma transaccional. Los archivos se extraen a Temp,
/// se validan y solo entonces reemplazan la versión instalada del packageId.
/// </summary>
public static class GameContentPackageImporter
{
    private const string PackageExtension = ".gsixpackage";
    private const int MaxArchiveEntries = 2048;
    private const long MaxExpandedBytes = 128L * 1024L * 1024L;

    public static IReadOnlyList<GameContentPackageImportResult> ImportPendingPackages()
    {
        Directory.CreateDirectory(GameContentRepository.ImportPath);
        List<GameContentPackageImportResult> results = new();
        foreach (string archive in Directory.GetFiles(GameContentRepository.ImportPath, $"*{PackageExtension}"))
        {
            GameContentPackageImportResult result = ImportPackage(archive);
            results.Add(result);
            MoveProcessedArchive(archive, result.Success ? "Imported" : "Rejected");
        }
        return results;
    }

    public static GameContentPackageImportResult ImportPackage(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return GameContentPackageImportResult.Failed("No existe el archivo .gsixpackage seleccionado.");
        if (!string.Equals(Path.GetExtension(archivePath), PackageExtension, StringComparison.OrdinalIgnoreCase))
            return GameContentPackageImportResult.Failed($"El archivo debe utilizar la extensión {PackageExtension}.");

        Directory.CreateDirectory(GameContentRepository.TempPath);
        string temporaryRoot = Path.Combine(GameContentRepository.TempPath, $"import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            ExtractSecurely(archivePath, temporaryRoot);
            if (!GameContentPackageValidator.ValidateExtractedPackage(
                    temporaryRoot,
                    out GameContentPackageManifest manifest,
                    out List<string> errors))
            {
                return GameContentPackageImportResult.Failed("El paquete no superó la validación.", errors);
            }

            string contentHash = GameContentPackageHashService.ComputeDirectorySha256(temporaryRoot);
            string packageRoot = Path.Combine(
                GameContentRepository.PackagesPath,
                SanitizePathSegment(manifest.packageId));
            string installPath = Path.Combine(packageRoot, SanitizePathSegment(manifest.packageVersion));
            string backupPath = packageRoot + $".backup-{Guid.NewGuid():N}";

            Directory.CreateDirectory(GameContentRepository.PackagesPath);
            if (Directory.Exists(packageRoot))
                Directory.Move(packageRoot, backupPath);

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.Move(temporaryRoot, installPath);
                temporaryRoot = null;
                File.WriteAllText(Path.Combine(installPath, "package.hash"), contentHash);

                GameContentPackageRegistry.Upsert(new InstalledGameContentPackage
                {
                    PackageId = manifest.packageId.Trim(),
                    PackageVersion = manifest.packageVersion.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(manifest.displayName) ? manifest.packageId.Trim() : manifest.displayName.Trim(),
                    RequiredGameVersion = manifest.requiredGameVersion.Trim(),
                    ContentFormatVersion = manifest.contentFormatVersion,
                    EntryScenarioId = ContentReference.Qualify(manifest.packageId, ContentReference.Parse(manifest.entryScenarioId).LocalId),
                    ContentHash = contentHash,
                    RelativeInstallPath = Path.GetRelativePath(GameContentRepository.RootPath, installPath).Replace('\\', '/'),
                    InstalledUtcTicks = DateTime.UtcNow.Ticks
                });

                if (Directory.Exists(backupPath))
                    Directory.Delete(backupPath, true);
                PackageContentResolver.ClearCache();
                EntityDefinitionRepository.ClearCache();
                TerrainDefinitionRepository.ClearCache();

                Debug.Log($"[GameContentPackageImporter] Instalado {manifest.packageId} {manifest.packageVersion} ({contentHash}).");
                return new GameContentPackageImportResult
                {
                    Success = true,
                    PackageId = manifest.packageId,
                    PackageVersion = manifest.packageVersion,
                    ContentHash = contentHash,
                    Message = $"Paquete instalado: {manifest.displayName ?? manifest.packageId}."
                };
            }
            catch
            {
                if (Directory.Exists(packageRoot))
                    Directory.Delete(packageRoot, true);
                if (Directory.Exists(backupPath))
                    Directory.Move(backupPath, packageRoot);
                throw;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[GameContentPackageImporter] Error al importar '{archivePath}': {exception}");
            return GameContentPackageImportResult.Failed(exception.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryRoot) && Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, true);
        }
    }

    private static void ExtractSecurely(string archivePath, string destinationRoot)
    {
        string normalizedRoot = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException($"El paquete supera el máximo de {MaxArchiveEntries} entradas.");

        long expandedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            expandedBytes += entry.Length;
            if (expandedBytes > MaxExpandedBytes)
                throw new InvalidDataException($"El paquete expandido supera {MaxExpandedBytes / (1024 * 1024)} MB.");
            string destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destination.StartsWith(normalizedRoot, StringComparison.Ordinal))
                throw new InvalidDataException($"Ruta insegura dentro del paquete: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            entry.ExtractToFile(destination, true);
        }
    }

    private static void MoveProcessedArchive(string archivePath, string folderName)
    {
        try
        {
            string folder = Path.Combine(GameContentRepository.ImportPath, folderName);
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, Path.GetFileName(archivePath));
            if (File.Exists(target))
                target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(archivePath)}-{DateTime.UtcNow:yyyyMMddHHmmss}{PackageExtension}");
            File.Move(archivePath, target);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GameContentPackageImporter] No se pudo archivar '{archivePath}': {exception.Message}");
        }
    }

    private static string SanitizePathSegment(string value)
    {
        string invalid = new(Path.GetInvalidFileNameChars());
        return new string(value.Where(character => !invalid.Contains(character)).ToArray());
    }
}
