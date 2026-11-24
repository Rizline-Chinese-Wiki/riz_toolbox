using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Host-owned LOCAL identity and the bounded metadata needed by the native read model.</summary>
internal sealed record LocalNativeMetadataDescriptor(LocalContentId LocalId, long Revision, string ChartId,
    string Title, string Artist, string ChartName, string DifficultyText, string Designer, Rgba ThemeColor,
    int MaxHit, int MaxScore, float Difficulty = 0f, string Illustrator = "");

/// <summary>Copied values from all nine native ILevelMetadata getters; From is the game's original constant.</summary>
internal sealed record LocalNativeMetadataObservation(string From, string MusicName, string Artist,
    string LevelDisplayName, string LevelInternalName, string Lv, string Charter, int Status, Rgba ThemeColor);

/// <summary>
/// Constructs an isolated ordinary Level/LevelMetadata graph through exact constructors and exported
/// field metadata. This is a metadata read model: it cannot launch a level or enter stock save/retry paths.
/// </summary>
internal sealed class NativeLocalMetadataFactory
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private const string ColorMapType = "System.Collections.Generic.Dictionary<System.String,UnityEngine.Color>";
    private const string DifferenceListType = "System.Collections.Generic.List<LevelDifference>";
    private const string StringListType = "System.Collections.Generic.List<System.String>";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private bool _buildVerified;

    internal NativeLocalMetadataFactory(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr gameAssemblyModule)
        => (_api, _main, _profile, _module) = (api, main, profile, gameAssemblyModule);

    internal OwnedNativeLocalMetadata Create(LocalNativeMetadataDescriptor descriptor)
    {
        _main.VerifyAccess();
        descriptor = ValidateAndCopy(descriptor);
        RequireBuild();
        var graph = new LocalNativeMetadataRootGraph(_main);
        try
        {
            IntPtr GameClass(string name) => _api.Class("Assembly-CSharp.dll", "", name);
            var levelClass = GameClass("Level"); var musicClass = GameClass("MusicInfo");
            var chartClass = GameClass("ChartInfo"); var levelInfoClass = GameClass("LevelInfo");
            var illustrationClass = GameClass("IllustrationInfo"); var differenceClass = GameClass("LevelDifference");
            var metadataClass = GameClass("LevelMetadata"); var contract = GameClass("ILevelMetadata");
            _api.RequireAssignable(contract, metadataClass);

            IntPtr Method(IntPtr cls, string name, string[] parameters, string returns)
            {
                var method = _api.InspectDeclaredMethod(cls, name, parameters, returns, _module);
                if (method.Entry is null)
                    throw new UnsupportedSignatureException($"LOCAL metadata method {name} has no verified pinned executable entry: {method.EntryStatus}");
                return method.MethodInfo;
            }
            var differenceConstructor = Method(differenceClass, ".ctor", new[] { "ChartInfo", ColorMapType }, "System.Void");
            var colorMapClass = _api.InspectMethodParameterClass(differenceConstructor, 1, ColorMapType).Class;
            var differenceListClass = _api.InspectMetadataFieldClass(levelClass, "chartInfos", DifferenceListType).FieldClass.Class;
            var stringListClass = _api.InspectMetadataFieldClass(levelInfoClass, "chartIds", StringListType).FieldClass.Class;
            var setColor = Method(colorMapClass, "set_Item", new[] { "System.String", "UnityEngine.Color" }, "System.Void");
            var getColor = Method(colorMapClass, "get_Item", new[] { "System.String" }, "UnityEngine.Color");
            var addDifference = Method(differenceListClass, "Add", new[] { "LevelDifference" }, "System.Void");
            var addString = Method(stringListClass, "Add", new[] { "System.String" }, "System.Void");
            var differenceCount = Method(differenceListClass, "get_Count", Array.Empty<string>(), "System.Int32");
            var stringCount = Method(stringListClass, "get_Count", Array.Empty<string>(), "System.Int32");
            var getDifference = Method(differenceListClass, "get_Item", new[] { "System.Int32" }, "LevelDifference");
            var getString = Method(stringListClass, "get_Item", new[] { "System.Int32" }, "System.String");
            var chartDisplayName = Method(chartClass, "get_LevelName", Array.Empty<string>(), "System.String");
            var getters = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            foreach (var name in new[] { "From", "MusicName", "Artist", "LevelDisplayName", "LevelInternalName", "Lv", "Charter" })
                getters.Add(name, Method(metadataClass, "get_" + name, Array.Empty<string>(), "System.String"));
            getters.Add("Status", Method(metadataClass, "get_Status", Array.Empty<string>(), "System.Int32"));
            getters.Add("ThemeColor", Method(metadataClass, "get_ThemeColor", Array.Empty<string>(), "UnityEngine.Color"));
            Method(metadataClass, ".ctor", new[] { "Level", "LevelDifference" }, "System.Void");

            NativeRuntime.RootHandle New(IntPtr cls, string[]? parameters = null, params A[] args)
            {
                try { return graph.Allocate(() => _api.NewExact(cls, parameters ?? Array.Empty<string>(), args)); }
                finally { foreach (var argument in args) argument.Dispose(); } // Also consume if the graph rejects allocation before NewExact.
            }
            void Write(NativeRuntime.RootHandle owner, IntPtr cls, string field, string type, A value)
                => _api.WriteExact(owner, cls, field, type, value);
            void Text(NativeRuntime.RootHandle owner, IntPtr cls, string field, string value)
                => Write(owner, cls, field, "System.String", _api.Str(value));

            // Slashes cannot appear in any validated component, so these IDs remain injective.
            // Revision belongs to the descriptor/owner identity; the native strings remain stable.
            var levelId = "riztoolbox.local/" + descriptor.LocalId.Namespace + "/" + descriptor.LocalId.Id;
            var chartId = levelId + "/chart/" + descriptor.ChartId;
            var musicId = levelId + "/audio"; var coverId = levelId + "/cover";
            var level = New(levelClass); var music = New(musicClass); var chart = New(chartClass);
            var levelInfo = New(levelInfoClass); var illustration = New(illustrationClass);
            var colors = New(colorMapClass);

            Text(music, musicClass, "id", musicId);
            Text(music, musicClass, "musicName", descriptor.Title);
            Text(music, musicClass, "artist", descriptor.Artist);
            Write(music, musicClass, "previewStartTime", "System.Single", A.Value(0f));
            Write(music, musicClass, "previewOverTime", "System.Single", A.Value(0f));
            Write(music, musicClass, "themeUiColor", "UnityEngine.Color", A.Value(descriptor.ThemeColor));
            Text(chart, chartClass, "id", chartId);
            Text(chart, chartClass, "level", descriptor.ChartName);
            Text(chart, chartClass, "designer", descriptor.Designer);
            Text(chart, chartClass, "difficultyText", descriptor.DifficultyText);
            Write(chart, chartClass, "difficulty", "System.Single", A.Value(descriptor.Difficulty));
            Write(chart, chartClass, "maxHit", "System.Int32", A.Value(descriptor.MaxHit));
            Write(chart, chartClass, "maxScore", "System.Int32", A.Value(descriptor.MaxScore));
            Text(illustration, illustrationClass, "id", coverId);
            Text(illustration, illustrationClass, "artist", descriptor.Illustrator);
            Text(levelInfo, levelInfoClass, "id", levelId);
            Text(levelInfo, levelInfoClass, "musicId", musicId);
            Text(levelInfo, levelInfoClass, "illustrationId", coverId);
            Text(levelInfo, levelInfoClass, "discName", "LOCAL");
            Text(level, levelClass, "id", levelId);
            Write(level, levelClass, "serial", "System.Int32", A.Value(0));
            Write(level, levelClass, "musicInfo", "MusicInfo", A.Ref(music.Target));
            Write(level, levelClass, "levelInfo", "LevelInfo", A.Ref(levelInfo.Target));
            Write(level, levelClass, "illustrationInfo", "IllustrationInfo", A.Ref(illustration.Target));

            _api.Invoke(setColor, colors.Target, _api.Str(descriptor.ChartName), A.Value(descriptor.ThemeColor));
            if (_api.Unbox<Rgba>(_api.Invoke(getColor, colors.Target, _api.Str(descriptor.ChartName))) != descriptor.ThemeColor)
                throw new UnsupportedSignatureException("LOCAL metadata color map did not preserve the declared Color.");
            var difference = New(differenceClass, new[] { "ChartInfo", ColorMapType }, A.Ref(chart.Target), A.Ref(colors.Target));
            var differences = New(differenceListClass); var chartIds = New(stringListClass);
            _api.Invoke(addDifference, differences.Target, A.Ref(difference.Target));
            _api.Invoke(addString, chartIds.Target, _api.Str(chartId));
            Write(level, levelClass, "chartInfos", DifferenceListType, A.Ref(differences.Target));
            Write(levelInfo, levelInfoClass, "chartIds", StringListType, A.Ref(chartIds.Target));
            if (_api.Unbox<int>(_api.Invoke(differenceCount, differences.Target)) != 1
                || _api.Invoke(getDifference, differences.Target, A.Value(0)) != difference.Target
                || _api.Unbox<int>(_api.Invoke(stringCount, chartIds.Target)) != 1
                || _api.Text(_api.Invoke(getString, chartIds.Target, A.Value(0))) != chartId)
                throw new UnsupportedSignatureException("LOCAL metadata lists did not preserve the exact chart graph.");
            var metadata = New(metadataClass, new[] { "Level", "LevelDifference" }, A.Ref(level.Target), A.Ref(difference.Target));
            if (_api.ObjectClass(metadata.Target) != metadataClass || _api.ObjectClass(level.Target) != levelClass)
                throw new UnsupportedSignatureException("LOCAL metadata construction returned an unexpected concrete class.");
            _api.RequireAssignable(contract, _api.ObjectClass(metadata.Target));

            string? originalFrom = null;
            LocalNativeMetadataObservation Read()
            {
                _main.VerifyAccess(); graph.VerifyActive();
                if (_api.ObjectClass(metadata.Target) != metadataClass || _api.ObjectClass(level.Target) != levelClass
                    || _api.ObjectClass(music.Target) != musicClass || _api.ObjectClass(chart.Target) != chartClass
                    || _api.ObjectClass(difference.Target) != differenceClass
                    || _api.Read<IntPtr>(metadata.Target, "level") != level.Target
                    || _api.Read<IntPtr>(metadata.Target, "chartInfo") != difference.Target
                    || _api.Read<IntPtr>(difference.Target, "chartInfo") != chart.Target
                    || _api.Read<IntPtr>(level.Target, "musicInfo") != music.Target
                    || _api.Read<IntPtr>(level.Target, "levelInfo") != levelInfo.Target
                    || _api.Read<IntPtr>(level.Target, "illustrationInfo") != illustration.Target
                    || _api.Read<IntPtr>(level.Target, "chartInfos") != differences.Target
                    || _api.Read<IntPtr>(levelInfo.Target, "chartIds") != chartIds.Target
                    || _api.ReadString(level.Target, "id") != levelId || _api.ReadString(chart.Target, "id") != chartId
                    || _api.ReadString(music.Target, "id") != musicId || _api.ReadString(illustration.Target, "id") != coverId
                    || _api.ReadString(levelInfo.Target, "id") != levelId
                    || _api.ReadString(levelInfo.Target, "musicId") != musicId
                    || _api.ReadString(levelInfo.Target, "illustrationId") != coverId
                    || _api.Read<int>(chart.Target, "maxHit") != descriptor.MaxHit
                    || _api.Read<int>(chart.Target, "maxScore") != descriptor.MaxScore
                    || _api.Read<float>(chart.Target, "difficulty") != descriptor.Difficulty
                    || _api.ReadString(illustration.Target, "artist") != descriptor.Illustrator)
                    throw new UnsupportedSignatureException("LOCAL metadata identity or retained object graph changed.");
                string Get(string name)
                {
                    var value = _api.Text(_api.Invoke(getters[name], metadata.Target));
                    if (!BoundedText(value, 256, false))
                        throw new UnsupportedSignatureException("LOCAL metadata getter " + name + " returned an absent or unbounded string.");
                    return value;
                }
                var observed = new LocalNativeMetadataObservation(Get("From"), Get("MusicName"), Get("Artist"),
                    Get("LevelDisplayName"), Get("LevelInternalName"), Get("Lv"), Get("Charter"),
                    _api.Unbox<int>(_api.Invoke(getters["Status"], metadata.Target)),
                    _api.Unbox<Rgba>(_api.Invoke(getters["ThemeColor"], metadata.Target)));
                // The stock display getter maps four built-in difficulty tokens. Validate its
                // result against the same ChartInfo's own public getter instead of guessing tokens.
                var displayName = _api.Text(_api.Invoke(chartDisplayName, chart.Target));
                if (observed.MusicName != descriptor.Title || observed.Artist != descriptor.Artist
                    || observed.LevelDisplayName != displayName || observed.LevelInternalName != descriptor.ChartName
                    || observed.Lv != descriptor.DifficultyText || observed.Charter != descriptor.Designer
                    || observed.Status != 0 || observed.ThemeColor != descriptor.ThemeColor
                    || originalFrom is not null && observed.From != originalFrom)
                    throw new UnsupportedSignatureException("LOCAL metadata getters differ from their isolated descriptor or fresh unplayed state.");
                originalFrom ??= observed.From;
                return observed;
            }
            var result = new OwnedNativeLocalMetadata(_main, descriptor, levelId, chartId, graph, metadata, Read);
            result.Read(); // Publication requires all nine getters, including exact Int32 and Color, to pass.
            return result;
        }
        catch (Exception failure)
        {
            try { graph.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("LOCAL metadata creation failed and root cleanup is unconfirmed.", failure, cleanup); }
            throw;
        }
    }

    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("LOCAL metadata requires the pinned Windows x64 Unity 2022.3.62f2 build.");
        if (_buildVerified) return;
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var stream = File.OpenRead(file); using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
            throw new UnsupportedSignatureException("LOCAL metadata game image fingerprint differs from the pinned profile.");
        _buildVerified = true;
    }

    internal static LocalNativeMetadataDescriptor ValidateAndCopy(LocalNativeMetadataDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.LocalId is null || !Id(descriptor.LocalId.Namespace) || !Id(descriptor.LocalId.Id)
            || !Id(descriptor.ChartId) || descriptor.Revision <= 0)
            throw new ArgumentException("LOCAL metadata requires namespace/song/chart IDs of 1..128 safe characters and a positive revision.", nameof(descriptor));
        foreach (var text in new[] { descriptor.Title, descriptor.Artist, descriptor.ChartName, descriptor.DifficultyText, descriptor.Designer })
            if (!BoundedText(text, 256, false))
                throw new ArgumentException("LOCAL metadata text must be nonempty, bounded to 256 characters and contain no controls.", nameof(descriptor));
        if (!BoundedText(descriptor.Illustrator, 256, true) || descriptor.MaxHit is < 0 or > 1_000_000
            || descriptor.MaxScore is < 0 or > 1_000_000_000 || !float.IsFinite(descriptor.Difficulty)
            || descriptor.Difficulty is < 0 or > 1000 || !Color(descriptor.ThemeColor))
            throw new ArgumentException("LOCAL metadata geometry, difficulty, illustration credit or normalized Color is outside its declared bounds.", nameof(descriptor));
        return descriptor with { LocalId = descriptor.LocalId with { } };
    }
    private static bool Id(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
    private static bool BoundedText(string? value, int maximum, bool allowEmpty) => value is not null && value.Length <= maximum
        && (allowEmpty || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl);
    private static bool Color(Rgba value) => new[] { value.R, value.G, value.B, value.Alpha }.All(x => float.IsFinite(x) && x is >= 0 and <= 1);
}

/// <summary>An owned metadata read model. Roots keep it alive; no resource Release, Destroy, launch or save API exists.</summary>
internal sealed class OwnedNativeLocalMetadata : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly LocalNativeMetadataRootGraph _graph;
    private readonly NativeRuntime.RootHandle _metadata;
    private readonly Func<LocalNativeMetadataObservation> _read;
    private LocalNativeMetadataObservation? _observation;
    internal OwnedNativeLocalMetadata(IMainThreadDispatcher main, LocalNativeMetadataDescriptor descriptor,
        string nativeLevelId, string nativeChartId, LocalNativeMetadataRootGraph graph, NativeRuntime.RootHandle metadata,
        Func<LocalNativeMetadataObservation> read)
        => (_main, Descriptor, NativeLevelId, NativeChartId, _graph, _metadata, _read) =
            (main, descriptor, nativeLevelId, nativeChartId, graph, metadata, read);
    internal LocalNativeMetadataDescriptor Descriptor { get; }
    internal string NativeLevelId { get; }
    internal string NativeChartId { get; }
    internal bool CanLaunch => false;
    internal bool IsDisposed => _graph.IsDisposed;
    internal bool CleanupUnknown => _graph.CleanupUnknown;
    /// <summary>Outstanding owner root records, including unknown frees. This is not a native heap/GC counter.</summary>
    internal int RootCount { get { _main.VerifyAccess(); return _graph.RootCount; } }
    internal IntPtr Pointer { get { _main.VerifyAccess(); _graph.VerifyActive(); return _metadata.Target; } }
    internal long NativeIdentity => Pointer.ToInt64();
    /// <summary>Last successful immutable copy. Call Read to collect another native observation.</summary>
    internal LocalNativeMetadataObservation Observation
    { get { _main.VerifyAccess(); _graph.VerifyActive(); return _observation ?? throw new InvalidOperationException("No valid metadata observation is available."); } }
    internal LocalNativeMetadataObservation Read()
    {
        _main.VerifyAccess(); _graph.VerifyActive(); _observation = null;
        return _observation = _read();
    }
    public void Dispose() { _main.VerifyAccess(); _observation = null; _graph.Dispose(); }
}

/// <summary>Bounded process ownership survives an abandoned handle or unknown native free; failed frees are never retried.</summary>
internal sealed class LocalNativeMetadataRootGraph : IDisposable
{
    private const int MaximumGraphs = 32, MaximumRoots = 10;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<LocalNativeMetadataRootGraph> ProcessOwners = new();
    private static int _unknownGraphs;
    private readonly IMainThreadDispatcher _main;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[MaximumRoots];
    private int _count;
    private bool _disposeStarted;
    private Exception? _cleanupFailure;
    internal LocalNativeMetadataRootGraph(IMainThreadDispatcher main)
    {
        _main = main; _main.VerifyAccess();
        lock (ProcessGate)
        {
            if (_unknownGraphs != 0 || ProcessOwners.Count >= MaximumGraphs)
                throw new InvalidOperationException("LOCAL metadata root capacity is exhausted or cleanup remains unknown; a process restart is required after unknown cleanup.");
            ProcessOwners.Add(this);
        }
    }
    internal bool IsDisposed => _disposeStarted;
    internal bool CleanupUnknown => _cleanupFailure is not null;
    internal int RootCount => _roots.Count(x => x is not null);
    internal void VerifyActive()
    {
        _main.VerifyAccess();
        if (_disposeStarted) throw new ObjectDisposedException(nameof(OwnedNativeLocalMetadata));
    }
    internal NativeRuntime.RootHandle Allocate(Func<NativeRuntime.RootHandle> allocate)
    {
        VerifyActive();
        if (_count >= MaximumRoots) throw new InvalidOperationException("LOCAL metadata graph exceeds its declared root bound.");
        var value = allocate();
        _roots[_count++] = value; // The fixed array cannot allocate after ownership is returned by NewExact.
        return value;
    }
    public void Dispose()
    {
        _main.VerifyAccess();
        if (_cleanupFailure is not null)
            throw new InvalidOperationException("LOCAL metadata root cleanup is unknown and will not be retried.", _cleanupFailure);
        if (_disposeStarted) return;
        _disposeStarted = true; var failures = new List<Exception>();
        for (var i = _count - 1; i >= 0; i--)
        {
            var root = _roots[i]; if (root is null) continue;
            try { root.Dispose(); _roots[i] = null; }
            catch (Exception error) { failures.Add(error); } // Independent roots still receive their own free attempt.
        }
        if (failures.Count != 0)
        {
            _cleanupFailure = new AggregateException("LOCAL metadata root free outcome is unconfirmed; process ownership is retained.", failures);
            lock (ProcessGate) _unknownGraphs++;
            throw _cleanupFailure;
        }
        lock (ProcessGate) ProcessOwners.Remove(this);
    }
}
