using System.Text;
using System.Text.RegularExpressions;
using RizToolbox.Core.Storage;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Localization;

/// <summary>Host-selected language and activation-owned, per-plugin dictionaries. No game language/settings writes.</summary>
public sealed class PluginLocalizationManager
{
    private readonly Dictionary<string, Dictionary<string, Table>> _catalogs = new(StringComparer.Ordinal);
    private readonly HashSet<View> _views = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private string _language;
    private long _revision;
    private bool _changingLanguage;
    private sealed record Table(string Language, IReadOnlyDictionary<string, string> Messages, bool Fallback, View Owner);

    public PluginLocalizationManager(string defaultLanguage = "en") => _language = Language(defaultLanguage);

    public IPluginLocalizationService ForPlugin(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context); context.MainThread.VerifyAccess();
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Localization manager requires its verified main thread.");
        _ = StoragePaths.PluginDirectory(AppContext.BaseDirectory, context.PluginId); // Common safe namespace validation, without creating a directory.
        var view = new View(this, context); _views.Add(view);
        try { return context.Own(view); } catch { view.Dispose(); throw; }
    }

    public Result SetLanguage(string language)
    {
        try
        {
            if (Environment.CurrentManagedThreadId != _thread) throw new StorageException(ErrorCode.WrongThread, "Language changes require the verified main thread.");
            language = Language(language);
            if (language == _language) return Result.Ok();
            if (_changingLanguage) return Result.Fail(new(ErrorCode.Conflict, "A language change is already being delivered; apply the next selection after this notification completes."));
            _changingLanguage = true;
            try
            {
                _language = language; _revision++;
                foreach (var id in _views.Where(x => x.Active).Select(x => x.Id).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray()) Notify(id);
            }
            finally { _changingLanguage = false; }
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(StoragePaths.Error(ex)); }
    }

    private static string Language(string value)
    {
        if (value is null || !Regex.IsMatch(value, "^[a-zA-Z]{2,8}(-[a-zA-Z0-9]{1,8}){0,3}$"))
            throw new StorageException(ErrorCode.InvalidArgument, "Language must be a bounded language tag such as en or zh-CN.");
        return value.ToLowerInvariant();
    }
    private static string Key(string value)
    {
        if (value is null || !Regex.IsMatch(value, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$"))
            throw new StorageException(ErrorCode.InvalidArgument, "Language keys are local to this plugin and use 1-128 ASCII letters, digits, dots, underscores or hyphens.");
        return value;
    }
    private void Notify(string pluginId)
    {
        var view = _views.FirstOrDefault(x => x.Id == pluginId && x.Active);
        view?.Publish(new(pluginId, _language, _revision));
    }
    private void Remove(Table table)
    {
        if (_catalogs.TryGetValue(table.Owner.Id, out var languages) && languages.TryGetValue(table.Language, out var current) && ReferenceEquals(current, table))
        {
            languages.Remove(table.Language); if (languages.Count == 0) _catalogs.Remove(table.Owner.Id);
            _revision++; Notify(table.Owner.Id);
        }
    }

    private sealed class View : IPluginLocalizationService, IDisposable
    {
        private readonly PluginLocalizationManager _manager; private readonly IPluginContext _context;
        private readonly List<Table> _owned = new(); private bool _disposed;
        internal string Id => _context.PluginId;
        internal bool Active => !_disposed && (_context is not IPluginScope scope || scope.IsActive);
        internal View(PluginLocalizationManager manager, IPluginContext context) => (_manager, _context) = (manager, context);
        private void Verify()
        {
            if (Environment.CurrentManagedThreadId != _manager._thread) throw new StorageException(ErrorCode.WrongThread, "Localization calls require the verified main thread.");
            _context.MainThread.VerifyAccess();
            if (!Active) throw new ObjectDisposedException("Plugin localization scope has ended.");
        }
        internal void Publish(PluginLanguageChanged change)
        { if (Active) _context.Events.Publish(change); }

        public Result<IDisposable> Register(string language, IReadOnlyDictionary<string, string> messages, bool fallback = false)
        {
            Table? table = null;
            try
            {
                Verify(); language = Language(language); ArgumentNullException.ThrowIfNull(messages);
                if (messages.Count > 4096) throw new StorageException(ErrorCode.QuotaExceeded, "A language table may contain at most 4096 entries.");
                var snapshot = new Dictionary<string, string>(StringComparer.Ordinal); long bytes = 0;
                foreach (var pair in messages)
                {
                    string key = Key(pair.Key);
                    if (pair.Value is null || pair.Value.Length > 16384) throw new StorageException(ErrorCode.InvalidArgument, "Language message is null or exceeds 16384 characters.");
                    bytes += Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(pair.Value);
                    if (bytes > 1024 * 1024) throw new StorageException(ErrorCode.QuotaExceeded, "Language table exceeds its 1 MiB byte budget.");
                    snapshot.Add(key, pair.Value);
                }
                if (!_manager._catalogs.TryGetValue(Id, out var languages)) _manager._catalogs.Add(Id, languages = new(StringComparer.Ordinal));
                if (languages.ContainsKey(language) || (fallback && languages.Values.Any(x => x.Fallback)))
                    return Result<IDisposable>.Fail(new(ErrorCode.Conflict, "This plugin already owns that language table or a fallback language.", TargetId: Id + "/" + language));
                table = new(language, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(snapshot), fallback, this);
                languages.Add(language, table); _owned.Add(table);
                var lease = _context.Own(new Registration(this, table));
                _manager._revision++; _manager.Notify(Id);
                return Result<IDisposable>.Ok(lease);
            }
            catch (Exception ex) { if (table is not null) _manager.Remove(table); return Result<IDisposable>.Fail(StoragePaths.Error(ex)); }
        }

        public Result<string> Resolve(string localKey)
        {
            try
            {
                Verify(); localKey = Key(localKey);
                if (_manager._catalogs.TryGetValue(Id, out var languages))
                {
                    if (languages.TryGetValue(_manager._language, out var exact) && exact.Messages.TryGetValue(localKey, out var message)) return Result<string>.Ok(message);
                    var fallback = languages.Values.SingleOrDefault(x => x.Fallback);
                    if (fallback is not null && fallback.Messages.TryGetValue(localKey, out message)) return Result<string>.Ok(message);
                }
                return Result<string>.Fail(new(ErrorCode.TargetMissing, "Language key is absent in both this plugin's active and fallback language.", TargetId: Id + "/" + localKey));
            }
            catch (Exception ex) { return Result<string>.Fail(StoragePaths.Error(ex)); }
        }

        public Result<LocalizationState> GetState()
        {
            try
            {
                Verify(); _manager._catalogs.TryGetValue(Id, out var languages);
                return Result<LocalizationState>.Ok(new(_manager._language, languages?.Values.SingleOrDefault(x => x.Fallback)?.Language, _manager._revision));
            }
            catch (Exception ex) { return Result<LocalizationState>.Fail(StoragePaths.Error(ex)); }
        }

        public Result<IDisposable> Subscribe(Action<PluginLanguageChanged> handler)
        {
            try
            {
                Verify(); ArgumentNullException.ThrowIfNull(handler);
                return Result<IDisposable>.Ok(_context.Events.Subscribe<PluginLanguageChanged>(change => { if (Active && change.PluginId == Id) handler(change); }));
            }
            catch (Exception ex) { return Result<IDisposable>.Fail(StoragePaths.Error(ex)); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != _manager._thread) throw new StorageException(ErrorCode.WrongThread, "Localization scope release requires the verified main thread.");
            _disposed = true;
            foreach (var table in _owned.ToArray()) _manager.Remove(table);
            _owned.Clear(); _manager._views.Remove(this);
        }
        private sealed class Registration : IDisposable
        {
            private View? _owner; private readonly Table _table;
            internal Registration(View owner, Table table) => (_owner, _table) = (owner, table);
            public void Dispose()
            {
                var owner = _owner; if (owner is null) return;
                if (Environment.CurrentManagedThreadId != owner._manager._thread) throw new StorageException(ErrorCode.WrongThread, "Language table release requires the verified main thread.");
                _owner = null; owner._owned.Remove(_table); owner._manager.Remove(_table);
            }
        }
    }
}
