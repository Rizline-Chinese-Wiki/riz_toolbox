using System.Text.RegularExpressions;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Storage;

internal sealed class StorageException : Exception
{
    public ErrorCode Code { get; }
    public StorageException(ErrorCode code, string message) : base(message) => Code = code;
}

internal static class StoragePaths
{
    internal static string PluginDirectory(string root, string pluginId)
    {
        if (!Regex.IsMatch(pluginId, "^[a-z0-9][a-z0-9._-]{0,127}$") || pluginId.EndsWith('.')) throw new ArgumentException("Invalid plugin ID.", nameof(pluginId));
        return Path.GetFullPath(Path.Combine(root, "storage", "plugin-" + pluginId));
    }

    internal static string LogicalName(string name, bool configuration = false)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 256 || name.Contains('\\') || name.Contains(':') || Path.IsPathRooted(name))
            throw new StorageException(ErrorCode.InvalidArgument, "Storage names must be relative logical names using '/' without drive names or traversal.");
        var segments = name.Split('/');
        if (configuration && segments.Length != 1) throw new StorageException(ErrorCode.InvalidArgument, "Configuration names cannot contain directory separators.");
        foreach (var segment in segments)
        {
            string device = segment.Split('.')[0].ToUpperInvariant();
            if (!Regex.IsMatch(segment, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$") || segment.EndsWith('.') ||
                device is "CON" or "PRN" or "AUX" or "NUL" || Regex.IsMatch(device, "^(COM|LPT)[0-9]$"))
                throw new StorageException(ErrorCode.InvalidArgument, "Storage name contains an invalid or reserved segment.");
        }
        return string.Join('/', segments);
    }

    internal static void VerifyNoLinks(string path)
    {
        for (var candidate = path; !string.IsNullOrEmpty(candidate); candidate = Path.GetDirectoryName(candidate))
            try { if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) throw new StorageException(ErrorCode.InvalidArgument, "Storage paths cannot contain symbolic links or reparse points."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
    }

    internal static IEnumerable<string> EnumerateFiles(string root, int depth = 0)
    {
        VerifyNoLinks(root);
        if (!Directory.Exists(root)) yield break;
        if (depth > 16) throw new StorageException(ErrorCode.InvalidArgument, "Storage directory depth exceeds 16 levels.");
        foreach (var path in Directory.GetFileSystemEntries(root).OrderBy(x => x, StringComparer.Ordinal))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new StorageException(ErrorCode.InvalidArgument, "Storage paths cannot contain symbolic links or reparse points.");
            if (Directory.Exists(path)) { foreach (var file in EnumerateFiles(path, depth + 1)) yield return file; }
            else yield return path;
        }
    }

    internal static OperationError Error(Exception ex)
        => new(ex is StorageException storage ? storage.Code : ex is ObjectDisposedException ? ErrorCode.ScopeExpired :
            ex is UnauthorizedAccessException or IOException or System.Text.Json.JsonException ? ErrorCode.ConfigInvalid : ErrorCode.InvalidArgument, ex.Message);
}
