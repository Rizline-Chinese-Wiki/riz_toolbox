using System.Security.Cryptography;

namespace RizToolbox.Adapter;

internal static class FileHash
{
    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
    }
}
