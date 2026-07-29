using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class GameContentPackageHashService
{
    public static string ComputeFileSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        using SHA256 sha = SHA256.Create();
        return ToHex(sha.ComputeHash(stream));
    }

    public static string ComputeDirectorySha256(string directoryPath)
    {
        using SHA256 sha = SHA256.Create();
        using MemoryStream canonical = new();

        string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(directoryPath, file)
                .Replace('\\', '/')
                .ToLowerInvariant();
            if (string.Equals(relative, "package.hash", StringComparison.OrdinalIgnoreCase))
                continue;

            byte[] pathBytes = Encoding.UTF8.GetBytes(relative);
            canonical.Write(pathBytes, 0, pathBytes.Length);
            canonical.WriteByte(0);
            byte[] bytes = File.ReadAllBytes(file);
            canonical.Write(bytes, 0, bytes.Length);
            canonical.WriteByte(0);
        }

        canonical.Position = 0;
        return ToHex(sha.ComputeHash(canonical));
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder builder = new(bytes.Length * 2);
        foreach (byte value in bytes)
            builder.Append(value.ToString("x2"));
        return builder.ToString();
    }
}
