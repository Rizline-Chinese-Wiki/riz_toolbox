using System.Collections.Concurrent;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Storage;

/// <summary>Plugin data/config/cache ownership and quotas. Does not access game saves or game settings.</summary>
public sealed class PluginStorageManager
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ChangeNotifications> _notifications = new(StringComparer.Ordinal);
    private readonly string _root;
    private readonly long _quota;
    private readonly int _maximumFileBytes;

    private sealed class ChangeNotifications
    {
        internal readonly Queue<(View Source, PluginStorageChanged Change)> Pending = new();
        internal long Revision;
        internal bool Publishing;
    }

    public PluginStorageManager(string dataRoot, long quotaBytes = 16 * 1024 * 1024, int maximumFileBytes = 1024 * 1024)
    {
        if (quotaBytes <= 0 || maximumFileBytes <= 0 || maximumFileBytes > 16 * 1024 * 1024 || maximumFileBytes > quotaBytes)
            throw new ArgumentOutOfRangeException(nameof(quotaBytes));
        _root = Path.GetFullPath(dataRoot); _quota = quotaBytes; _maximumFileBytes = maximumFileBytes;
    }

    public IPluginStorageService ForPlugin(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.MainThread.VerifyAccess();
        var view = new View(this, context, StoragePaths.PluginDirectory(_root, context.PluginId));
        return context.Own(view);
    }

    /// <summary>Host-only resource composition uses the same validated data root as scoped storage.</summary>
    public string GetDataDirectory(string pluginId)
    {
        var directory = Path.Combine(StoragePaths.PluginDirectory(_root, pluginId), "data");
        StoragePaths.VerifyNoLinks(directory);
        return directory;
    }

    private sealed class View : IPluginStorageService, IDisposable
    {
        private readonly PluginStorageManager _manager; private readonly IPluginContext _context; private readonly string _directory;
        private readonly int _thread = Environment.CurrentManagedThreadId; private bool _disposed;
        private object Gate => Gates.GetOrAdd(_directory, _ => new());
        internal View(PluginStorageManager manager, IPluginContext context, string directory) => (_manager, _context, _directory) = (manager, context, directory);

        private void Verify()
        {
            if (Environment.CurrentManagedThreadId != _thread) throw new StorageException(ErrorCode.WrongThread, "Plugin storage calls require the verified main thread.");
            _context.MainThread.VerifyAccess();
            if (_disposed || _context is IPluginScope { IsActive: false }) throw new ObjectDisposedException("Plugin storage scope has ended.");
        }

        private string AreaRoot(PluginStorageArea area)
        {
            if (!Enum.IsDefined(typeof(PluginStorageArea), area)) throw new StorageException(ErrorCode.InvalidArgument, "Unknown storage area.");
            return Path.Combine(_directory, area.ToString().ToLowerInvariant());
        }
        private string FilePath(PluginStorageArea area, string name)
        {
            name = StoragePaths.LogicalName(name, area == PluginStorageArea.Configuration);
            var path = Path.Combine(AreaRoot(area), name.Replace('/', Path.DirectorySeparatorChar) + (area == PluginStorageArea.Configuration ? ".json" : ""));
            StoragePaths.VerifyNoLinks(path); return path;
        }
        private void BinaryArea(PluginStorageArea area)
        { if (area == PluginStorageArea.Configuration) throw new StorageException(ErrorCode.InvalidArgument, "Use schema-aware configuration methods for the configuration area."); _ = AreaRoot(area); }
        private long Used() => StoragePaths.EnumerateFiles(_directory).Sum(path => new FileInfo(path).Length);
        private byte[] ReadBounded(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > _manager._maximumFileBytes) throw new StorageException(ErrorCode.QuotaExceeded, "File exceeds the plugin file-size budget.");
            var bytes = new byte[(int)input.Length]; input.ReadExactlyCompat(bytes); return bytes;
        }
        private void Budget(string path, long bytes, bool preserveOriginal = false)
        {
            if (bytes > _manager._maximumFileBytes) throw new StorageException(ErrorCode.QuotaExceeded, "File exceeds the plugin file-size budget.");
            long previous = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (Used() - (preserveOriginal ? 0 : previous) + bytes > _manager._quota)
                throw new StorageException(ErrorCode.QuotaExceeded, "Plugin storage quota would be exceeded; previous files are preserved.");
        }
        private void Changed(PluginStorageArea area, string name, PluginStorageChangeKind kind)
        {
            // Callers hold the namespace gate. Reentrant notifications wait for delivery of the current revision.
            var notifications = _manager._notifications.GetOrAdd(_context.PluginId, static _ => new());
            notifications.Pending.Enqueue((this, new(_context.PluginId, area, name, kind, ++notifications.Revision)));
            if (notifications.Publishing) return;

            notifications.Publishing = true;
            try
            {
                while (notifications.Pending.TryDequeue(out var pending))
                {
                    var source = pending.Source;
                    if (!source._disposed && source._context is not IPluginScope { IsActive: false })
                        source._context.Events.Publish(pending.Change);
                }
            }
            finally
            {
                notifications.Pending.Clear();
                notifications.Publishing = false;
            }
        }

        private AtomicSchemaStore ConfigurationStore(string path) => new(path, _manager._maximumFileBytes, (length, migrating) =>
        {
            try
            {
                Verify();
                StoragePaths.VerifyNoLinks(path);
                Budget(path, length, preserveOriginal: migrating);
                return null;
            }
            catch (Exception ex) { return StoragePaths.Error(ex); }
        });

        private string? ValidateConfiguration<T>(T value, Func<T, string?>? validate)
        {
            Verify();
            var error = validate?.Invoke(value);
            Verify();
            return error;
        }

        public Result<ConfigurationSnapshot<T>> LoadConfiguration<T>(string name, int schemaVersion, Func<T> defaults,
            IReadOnlyList<ConfigurationMigration>? migrations = null, Func<T, string?>? validate = null)
        {
            lock (Gate)
            {
                try
                {
                    Verify(); var path = FilePath(PluginStorageArea.Configuration, name);
                    var steps = migrations?.Select(x => new JsonSchemaMigration(x.FromVersion, x.ToVersion, input =>
                        {
                            Verify();
                            var result = x.Transform(input);
                            Verify();
                            return result;
                        })).ToArray();
                    Verify();
                    var loaded = ConfigurationStore(path).Load(schemaVersion, defaults, steps, value => ValidateConfiguration(value, validate));
                    Verify();
                    if (!loaded.Success) return Result<ConfigurationSnapshot<T>>.Fail(loaded.Error!);
                    if (loaded.Value!.Migrated) Changed(PluginStorageArea.Configuration, name, PluginStorageChangeKind.Migrated);
                    return Result<ConfigurationSnapshot<T>>.Ok(new(loaded.Value.SchemaVersion, loaded.Value.Value, loaded.Value.Migrated));
                }
                catch (Exception ex) { return Result<ConfigurationSnapshot<T>>.Fail(StoragePaths.Error(ex)); }
            }
        }

        public Result SaveConfiguration<T>(string name, int schemaVersion, T value, Func<T, string?>? validate = null)
        {
            lock (Gate)
            {
                try
                {
                    Verify(); var path = FilePath(PluginStorageArea.Configuration, name);
                    var saved = ConfigurationStore(path).Save(schemaVersion, value, item => ValidateConfiguration(item, validate));
                    Verify();
                    if (saved.Success) Changed(PluginStorageArea.Configuration, name, PluginStorageChangeKind.Saved);
                    return saved;
                }
                catch (Exception ex) { return Result.Fail(StoragePaths.Error(ex)); }
            }
        }

        public Result<byte[]> Read(PluginStorageArea area, string name)
        {
            lock (Gate)
            {
                try
                {
                    Verify(); BinaryArea(area); var path = FilePath(area, name);
                    if (!File.Exists(path)) return Result<byte[]>.Fail(new(ErrorCode.TargetMissing, "Plugin data file does not exist."));
                    return Result<byte[]>.Ok(ReadBounded(path));
                }
                catch (Exception ex) { return Result<byte[]>.Fail(StoragePaths.Error(ex)); }
            }
        }

        public Result Write(PluginStorageArea area, string name, byte[] data)
        {
            lock (Gate)
            {
                string? temporary = null;
                try
                {
                    Verify(); BinaryArea(area); var path = FilePath(area, name);
                    if (data is null) throw new StorageException(ErrorCode.InvalidArgument, "File bytes are required.");
                    Budget(path, data.Length); var snapshot = data.ToArray();
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); StoragePaths.VerifyNoLinks(path);
                    temporary = Path.Combine(Path.GetDirectoryName(path)!, ".write-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(snapshot); output.Flush(true); }
                    Verify(); StoragePaths.VerifyNoLinks(path); File.Move(temporary, path, true);
                    Changed(area, name, PluginStorageChangeKind.Saved); return Result.Ok();
                }
                catch (Exception ex) { return Result.Fail(StoragePaths.Error(ex)); }
                finally { if (temporary is not null) try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }

        public Result Delete(PluginStorageArea area, string name)
        {
            lock (Gate)
            {
                try { Verify(); BinaryArea(area); var path = FilePath(area, name); if (File.Exists(path)) { File.Delete(path); Changed(area, name, PluginStorageChangeKind.Deleted); } return Result.Ok(); }
                catch (Exception ex) { return Result.Fail(StoragePaths.Error(ex)); }
            }
        }

        public Result<IReadOnlyList<PluginStorageEntry>> List(PluginStorageArea area)
        {
            lock (Gate)
            {
                try
                {
                    Verify(); var root = AreaRoot(area);
                    var files = StoragePaths.EnumerateFiles(root).Select(path => new PluginStorageEntry(area, Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length)).ToArray();
                    return Result<IReadOnlyList<PluginStorageEntry>>.Ok(Array.AsReadOnly(files));
                }
                catch (Exception ex) { return Result<IReadOnlyList<PluginStorageEntry>>.Fail(StoragePaths.Error(ex)); }
            }
        }

        public Result<PluginStorageQuota> GetQuota()
        {
            lock (Gate)
            {
                try { Verify(); return Result<PluginStorageQuota>.Ok(new(Used(), _manager._quota, _manager._maximumFileBytes)); }
                catch (Exception ex) { return Result<PluginStorageQuota>.Fail(StoragePaths.Error(ex)); }
            }
        }
        public Result<IDisposable> Subscribe(Action<PluginStorageChanged> handler)
        {
            try
            {
                Verify(); ArgumentNullException.ThrowIfNull(handler);
                return Result<IDisposable>.Ok(_context.Events.Subscribe<PluginStorageChanged>(change => { if (!_disposed && change.PluginId == _context.PluginId) handler(change); }));
            }
            catch (Exception ex) { return Result<IDisposable>.Fail(StoragePaths.Error(ex)); }
        }
        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != _thread) throw new StorageException(ErrorCode.WrongThread, "Storage scope release requires the verified main thread.");
            _disposed = true;
        }
    }
}
