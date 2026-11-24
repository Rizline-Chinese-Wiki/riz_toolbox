using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Explicit opt-in construction/read/retention proof; never hands metadata to a game controller.</summary>
internal sealed class LocalNativeMetadataProbe : IDisposable
{
    private static readonly HashSet<LocalNativeMetadataProbe> FailedCleanup = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly List<OwnedNativeLocalMetadata> _owned = new();
    private bool _attempted, _cleanupAttempted;

    internal LocalNativeMetadataProbe(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, IntPtr module)
        => (_api, _main, _log, _profile, _module) = (api, main, log, profile, module);

    internal void TryStart()
    {
        _main.VerifyAccess();
        if (_attempted) return;
        _attempted = true;
        try
        {
            if (FailedCleanup.Count >= 4)
                throw new InvalidOperationException("Local metadata probe cleanup capacity is exhausted.");
            var descriptors = Descriptors();
            var factory = new NativeLocalMetadataFactory(_api, _main, _profile, _module);
            foreach (var descriptor in descriptors)
            {
                var value = factory.Create(descriptor);
                try { _owned.Add(value); }
                catch { value.Dispose(); throw; }
            }
            if (_owned.Any(x => x.CanLaunch || x.IsDisposed || x.CleanupUnknown)
                || _owned.Select(x => x.NativeIdentity).Distinct().Count() != descriptors.Length)
                throw new InvalidDataException("Local metadata owners did not have separate, read-only native identities.");

            Collect();
            Log("created", new { count = _owned.Count, nativeGcCollectReturned = true,
                ownerRootRecords = _owned.Sum(x => x.RootCount), canLaunch = false });
            for (var index = 0; index < _owned.Count; index++)
            {
                var handle = _owned[index];
                var observation = handle.Read();
                Log("row", new { index, id = handle.Descriptor.LocalId, handle.Descriptor.Revision,
                    handle.Descriptor.ChartId, nativeIdentity = "0x" + unchecked((ulong)handle.NativeIdentity).ToString("X"),
                    observation });
            }

            // Withdraw one independent owner, collect again, and check that its neighbours
            // retain their original descriptor outputs. No game catalog/score object is used.
            var middle = _owned[1];
            middle.Dispose();
            if (!middle.IsDisposed || middle.CleanupUnknown || middle.RootCount != 0)
                throw new InvalidDataException("Middle metadata owner cleanup did not complete.");
            var disposedReadRejected = false;
            try { middle.Read(); }
            catch (ObjectDisposedException) { disposedReadRejected = true; }
            if (!disposedReadRejected)
                throw new InvalidDataException("Disposed metadata still permitted a native read.");
            Collect();
            _owned[0].Read(); _owned[2].Read();
            if (_owned[0].Descriptor.LocalId != _owned[2].Descriptor.LocalId
                || _owned[0].Descriptor.Revision == _owned[2].Descriptor.Revision)
                throw new InvalidDataException("Probe descriptors did not cover independent song revisions.");
            Dispose();
            if (_owned.Any(x => !x.IsDisposed || x.CleanupUnknown || x.RootCount != 0))
                throw new InvalidDataException("Metadata probe roots were not all released.");
            Log("verified", new { owners = 3, sameTitleCrossProvider = true, sameIdDifferentRevision = true,
                getterReadsAfterNativeGc = true, disposedReadRejected, remainingOwnersSurvivedWithdrawal = true,
                allOwnerRootRecordsReleased = true, canLaunch = false, gameCatalogModified = false,
                gameResultOrSavePathInvoked = false });
        }
        catch (Exception error)
        {
            try { _log.Log(LogLevel.Warning, "LocalMetadataProbe unavailable: " + error.GetType().Name + ": " + error.Message); } catch { }
            Dispose();
        }
    }

    private void Collect()
    {
        var gc = _api.Class("mscorlib.dll", "System", "GC");
        var method = _api.Method(gc, "Collect", 0, true, Array.Empty<string>());
        if (_api.DescribeReturnType(method) != "System.Void")
            throw new UnsupportedSignatureException("System.GC.Collect return signature changed.");
        _api.Invoke(method, IntPtr.Zero);
    }

    private static LocalNativeMetadataDescriptor[] Descriptors()
    {
        var first = new LocalNativeMetadataDescriptor(new("probe.metadata.a", "same-song"), 1, "chart-a",
            "LOCAL Metadata Sentinel", "Artist A", "LOCAL_A", "2.5", "Designer A",
            new Rgba(.15f, .35f, .65f, 1f), 8, 900000, 2.5f, "Illustrator A");
        var second = first with { LocalId = new("probe.metadata.b", "same-song"), Artist = "Artist B",
            ChartName = "LOCAL_B", Designer = "Designer B", ThemeColor = new(.2f, .7f, .3f, 1f) };
        var revision = first with { Revision = 2, Artist = "Artist A revision 2", ChartName = "LOCAL_A_R2",
            DifficultyText = "3.5", Difficulty = 3.5f, ThemeColor = new(.6f, .4f, .2f, 1f) };
        return new[] { first, second, revision };
    }

    private void Log(string phase, object value)
    {
        var text = JsonSerializer.Serialize(value);
        if (text.Length > 1800) throw new InvalidDataException("Metadata probe row exceeds the structured log bound.");
        _log.Log(LogLevel.Info, "LocalMetadataProbe " + phase + " " + text);
    }

    public void Dispose()
    {
        _main.VerifyAccess();
        if (_cleanupAttempted) return;
        _cleanupAttempted = true;
        var errors = new List<Exception>();
        foreach (var handle in _owned.AsEnumerable().Reverse())
            try { handle.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count == 0) return;
        FailedCleanup.Add(this);
        try { _log.Log(LogLevel.Error, "LocalMetadataProbe cleanup is unconfirmed; responsibility retained until process exit.", new AggregateException(errors)); } catch { }
    }
}
