using System.Text.Json;
using System.Text.RegularExpressions;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

internal sealed class PluginConfiguration : IPluginConfiguration
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly Action _verifyActive;
    private readonly object _gate = new();

    public PluginConfiguration(string dataRoot, string pluginId, Action verifyActive)
    {
        _directory = Path.GetFullPath(Path.Combine(dataRoot, "config", "plugin-" + pluginId));
        _verifyActive = verifyActive;
    }

    public OperationResult<T> Load<T>(string name, Func<T> defaults)
    {
        var denied = Guard(name);
        if (denied is not null) return OperationResult<T>.Fail(denied);
        lock (_gate)
        {
            try
            {
                if (defaults is null) return OperationResult<T>.Fail(ErrorCode.InvalidArgument, "Configuration defaults factory is required.", targetId: name);
                var path = GetPath(name);
                VerifyNoLinks(path);
                if (!File.Exists(path))
                {
                    var fallback = defaults();
                    denied = Guard(name);
                    return denied is null ? OperationResult<T>.Ok(fallback) : OperationResult<T>.Fail(denied);
                }
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > MaximumBytes) return OperationResult<T>.Fail(ErrorCode.QuotaExceeded, "Configuration exceeds the 1 MiB limit.", targetId: name);
                var value = JsonSerializer.Deserialize<T>(input, JsonOptions);
                denied = Guard(name);
                if (denied is not null) return OperationResult<T>.Fail(denied);
                return value is null ? OperationResult<T>.Fail(ErrorCode.ConfigInvalid, "Configuration document contains null.", targetId: name) : OperationResult<T>.Ok(value);
            }
            catch (Exception ex) { return OperationResult<T>.Fail(Guard(name) ?? Error(ex, "Cannot load configuration", name)); }
        }
    }

    public OperationResult Save<T>(string name, T value)
    {
        var denied = Guard(name);
        if (denied is not null) return OperationResult.Fail(denied);
        lock (_gate)
        {
            string? temporary = null;
            try
            {
                var path = GetPath(name);
                VerifyNoLinks(path);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
                denied = Guard(name);
                if (denied is not null) return OperationResult.Fail(denied);
                if (bytes.Length > MaximumBytes) return OperationResult.Fail(ErrorCode.QuotaExceeded, "Configuration exceeds the 1 MiB limit.", targetId: name);
                Directory.CreateDirectory(_directory);
                VerifyNoLinks(path);
                temporary = Path.Combine(_directory, ".write-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes);
                    output.Flush(true);
                }
                VerifyNoLinks(path);
                File.Move(temporary, path, true);
                return OperationResult.Ok();
            }
            catch (Exception ex) { return OperationResult.Fail(Guard(name) ?? Error(ex, "Cannot save configuration", name)); }
            finally
            {
                if (temporary is not null)
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private OperationError? Guard(string name)
    {
        try { _verifyActive(); return null; }
        catch (ObjectDisposedException error) { return new(ErrorCode.ScopeExpired, error.Message, TargetId: name); }
        catch (InvalidOperationException error) { return new(ErrorCode.WrongThread, error.Message, TargetId: name); }
    }
    private static OperationError Error(Exception error, string action, string name)
        => new(error is ArgumentException ? ErrorCode.InvalidArgument : error is JsonException ? ErrorCode.ConfigInvalid
            : error is IOException or UnauthorizedAccessException ? ErrorCode.ResourceLoadFailed : ErrorCode.InternalError,
            action + ": " + error.Message, TargetId: name);

    private string GetPath(string name)
    {
        if (name is null || !Regex.IsMatch(name, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$"))
            throw new ArgumentException("Configuration name must be 1-64 ASCII letters, digits, hyphens or underscores and start with a letter or digit.", nameof(name));
        return Path.Combine(_directory, "config-" + name + ".json");
    }

    private static void VerifyNoLinks(string path)
    {
        for (var candidate = path; !string.IsNullOrEmpty(candidate); candidate = Path.GetDirectoryName(candidate))
        {
            try
            {
                if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Configuration paths cannot contain symbolic links or reparse points.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
