using System.Collections.Concurrent;
using System.Text.Json;
using RizToolbox.Core.Storage;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

public sealed record JsonSchemaMigration(int FromVersion, int ToVersion, Func<JsonElement, JsonElement> Transform);
public sealed record SchemaDocument<T>(int SchemaVersion, T Value, bool Migrated = false, string? OriginalBackup = null);

/// <summary>Bounded JSON storage with same-directory atomic replace, serialized writers and pure, consecutive schema migrations.</summary>
public sealed class AtomicSchemaStore
{
    private sealed class DocumentGate
    {
        public bool InOperation;
    }

    private static readonly ConcurrentDictionary<string, DocumentGate> Gates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 32 };
    private readonly string _path;
    private readonly int _maximumBytes;
    private readonly Func<int, bool, OperationError?>? _checkWrite;

    public AtomicSchemaStore(string path, int maximumBytes = 1024 * 1024)
        : this(path, maximumBytes, null) { }

    internal AtomicSchemaStore(string path, int maximumBytes, Func<int, bool, OperationError?>? checkWrite)
    {
        _path = Path.GetFullPath(path);
        if (maximumBytes <= 0 || maximumBytes > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _maximumBytes = maximumBytes;
        _checkWrite = checkWrite;
    }

    /// <summary>All migration/validation callbacks finish before changing disk. Missing schemaVersion is legacy version zero.</summary>
    public Result<SchemaDocument<T>> Load<T>(int currentSchema, Func<T> defaults,
        IReadOnlyList<JsonSchemaMigration>? migrations = null, Func<T, string?>? validate = null)
    {
        var gate = Gates.GetOrAdd(_path, _ => new());
        lock (gate)
        {
            if (gate.InOperation) return Failure<SchemaDocument<T>>(ErrorCode.Conflict, "A configuration operation is already active for this document.");
            gate.InOperation = true;
            bool migrationStarted = false;
            try
            {
                if (currentSchema < 0 || defaults is null) return Failure<SchemaDocument<T>>(ErrorCode.InvalidArgument, "Invalid schema or defaults factory.");
                VerifyNoLinks(_path);
                if (!File.Exists(_path))
                {
                    var value = defaults(); var invalid = validate?.Invoke(value);
                    return invalid is null ? Result<SchemaDocument<T>>.Ok(new(currentSchema, value)) : Failure<SchemaDocument<T>>(ErrorCode.ConfigInvalid, invalid);
                }
                var original = ReadBounded();
                using var json = JsonDocument.Parse(original, new() { MaxDepth = 32 });
                var document = json.RootElement;
                var version = document.TryGetProperty("schemaVersion", out var versionProperty) ? versionProperty.GetInt32() : 0;
                var data = document.TryGetProperty("data", out var payload) ? payload.Clone() : document.Clone();
                if (version > currentSchema || version < 0) return Failure<SchemaDocument<T>>(ErrorCode.MigrationFailed, "Configuration schema is newer than supported or invalid; original file has been preserved.");
                int initialVersion = version;
                while (version < currentSchema)
                {
                    migrationStarted = true;
                    var steps = (migrations ?? Array.Empty<JsonSchemaMigration>()).Where(x => x.FromVersion == version).ToArray();
                    if (steps.Length != 1 || steps[0].ToVersion != version + 1 || steps[0].Transform is null)
                        return Failure<SchemaDocument<T>>(ErrorCode.MigrationFailed, $"No unique consecutive migration from schema {version}; original file has been preserved.");
                    data = steps[0].Transform(data).Clone();
                    version = steps[0].ToVersion;
                }
                var result = data.Deserialize<T>(Options);
                if (result is null) throw new JsonException("Configuration document contains null.");
                var validationError = validate?.Invoke(result);
                if (validationError is not null) return Failure<SchemaDocument<T>>(migrationStarted ? ErrorCode.MigrationFailed : ErrorCode.ConfigInvalid,
                    validationError + "; original file has been preserved.");
                string? backup = null;
                if (migrationStarted)
                {
                    var bytes = Encode(currentSchema, result);
                    if (CheckWrite(bytes.Length, migrating: true) is { } denied) return Result<SchemaDocument<T>>.Fail(denied);
                    VerifyNoLinks(_path);
                    if (!ReadBounded().SequenceEqual(original)) return Failure<SchemaDocument<T>>(ErrorCode.MigrationFailed, "Configuration changed during migration; original file has been preserved.");
                    backup = _path + $".schema-{initialVersion}.{Guid.NewGuid():N}.bak";
                    Replace(bytes, backup, original);
                }
                return Result<SchemaDocument<T>>.Ok(new(currentSchema, result, migrationStarted, backup));
            }
            catch (StorageException ex) when (_checkWrite is not null) { return Failure<SchemaDocument<T>>(ex.Code, ex.Message); }
            catch (Exception ex) { return Failure<SchemaDocument<T>>(migrationStarted ? ErrorCode.MigrationFailed : ErrorCode.ConfigInvalid,
                $"Configuration load/upgrade failed; original file has been preserved: {ex.Message}"); }
            finally { gate.InOperation = false; }
        }
    }

    public Result Save<T>(int schemaVersion, T value, Func<T, string?>? validate = null)
    {
        var gate = Gates.GetOrAdd(_path, _ => new());
        lock (gate)
        {
            if (gate.InOperation) return Result.Fail(new(ErrorCode.Conflict, "A configuration operation is already active for this document."));
            gate.InOperation = true;
            try
            {
                if (schemaVersion < 0) return Result.Fail(new(ErrorCode.InvalidArgument, "Schema version must be nonnegative."));
                VerifyNoLinks(_path);
                var error = validate?.Invoke(value);
                if (error is not null) return Result.Fail(new(ErrorCode.ConfigInvalid, error));
                var bytes = Encode(schemaVersion, value);
                if (CheckWrite(bytes.Length, migrating: false) is { } denied) return Result.Fail(denied);
                VerifyNoLinks(_path);
                if (File.Exists(_path))
                {
                    using var original = JsonDocument.Parse(ReadBounded());
                    if (original.RootElement.TryGetProperty("schemaVersion", out var version) && version.GetInt32() > schemaVersion)
                        return Result.Fail(new(ErrorCode.MigrationFailed, "Refusing to downgrade a newer configuration schema; original file has been preserved."));
                }
                Replace(bytes);
                return Result.Ok();
            }
            catch (Exception ex) { return Result.Fail(new(ErrorCode.ConfigInvalid, $"Cannot atomically save configuration; original file has been preserved: {ex.Message}")); }
            finally { gate.InOperation = false; }
        }
    }

    private static byte[] Encode<T>(int schema, T value)
        => JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = schema, data = value }, Options);

    private OperationError? CheckWrite(int length, bool migrating)
    {
        var denied = _checkWrite?.Invoke(length, migrating);
        if (denied is not null) return denied;
        if (length > _maximumBytes) throw new IOException($"Configuration exceeds {_maximumBytes} bytes.");
        return null;
    }
    private byte[] ReadBounded()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > _maximumBytes) throw new StorageException(ErrorCode.QuotaExceeded, $"Configuration exceeds {_maximumBytes} bytes.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactlyCompat(bytes); return bytes;
    }
    private void Replace(byte[] bytes, string? backup = null, byte[]? original = null)
    {
        string? temporary = null;
        bool removeBackup = false;
        try
        {
            VerifyNoLinks(_path);
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory); VerifyNoLinks(_path);
            temporary = Path.Combine(directory, ".schema-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            VerifyNoLinks(_path);
            if (backup is not null)
            {
                using var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                removeBackup = true;
                output.Write(original!);
                output.Flush(true);
            }
            File.Move(temporary, _path, true);
            removeBackup = false;
        }
        finally
        {
            if (temporary is not null) DeleteStagedFile(temporary);
            if (removeBackup) DeleteStagedFile(backup!);
        }
    }

    private static void DeleteStagedFile(string path)
    { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void VerifyNoLinks(string path)
    {
        for (var candidate = path; !string.IsNullOrEmpty(candidate); candidate = Path.GetDirectoryName(candidate))
            try { if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) throw new IOException("Storage paths cannot contain symbolic links or reparse points."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
    }
    private static Result<T> Failure<T>(ErrorCode code, string message) => Result<T>.Fail(new(code, message));
}

internal static class BoundedStreamRead
{
    internal static void ReadExactlyCompat(this Stream stream, byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
