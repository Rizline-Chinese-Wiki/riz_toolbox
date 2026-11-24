using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum ControllerPrefabResolutionStatus { Ready, NotReady, Ambiguous, Unsupported, ScanLimit, CleanupUnknown, ScanRequired }
internal sealed record ControllerPrefabResolution(ControllerPrefabResolutionStatus Status, string Reason,
    OwnedNativeControllerPrefab? Prefab = null, int ScannedElements = 0, int MatchingCards = 0,
    bool ConfirmedAbsent = false)
{
    internal string UnavailableReason => "Controller prefab is unavailable: " + Reason;
    internal OperationError? ConfirmedAbsenceError
        => Status == ControllerPrefabResolutionStatus.NotReady && Prefab is null && ConfirmedAbsent
            ? new(ErrorCode.NotReady, UnavailableReason, AudioContentCapabilityIds.LocalCatalogSelection, Retryable: true)
            : null;
}
internal interface IControllerPrefabReference : IDisposable { long Identity { get; } }
internal interface IControllerPrefabBackend : IDisposable
{
    void Initialize();
    int DiscoverSource(int source);
    IControllerPrefabReference? RetainCard(int source, int index);
    bool IsActiveLoadedCard(int source, IControllerPrefabReference card);
    IControllerPrefabReference? RetainPrefab(int source, IControllerPrefabReference card);
    void VerifyCardPrefab(int source, IControllerPrefabReference card, IControllerPrefabReference prefab);
    void CompleteDiscovery();
    int OutstandingRootRecords { get; }
}

/// <summary>
/// Resolves the two verified card-prefab fields by a complete, bounded discovery snapshot.
/// No prefab/stock GameObject is changed. A returned owner must remain alive through synchronous borrowing;
/// cross-frame callers must resolve again if they need a new global uniqueness observation.
/// </summary>
internal sealed class NativeControllerPrefabResolver
{
    internal const int MaximumScannedElements = 4096;
    private readonly IMainThreadDispatcher _main;
    private readonly Func<IControllerPrefabBackend> _factory;
    private bool _busy;
    internal NativeControllerPrefabResolver(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr gameAssembly)
        : this(main, () => new NativeControllerPrefabDiscovery(api, main, profile, gameAssembly)) { }
    internal NativeControllerPrefabResolver(IMainThreadDispatcher main, Func<IControllerPrefabBackend> backendFactory)
    { _main = main; _factory = backendFactory; }

    internal ControllerPrefabResolution Resolve() => Resolve(emptySourcesOnly: false);

    // Only the two verified array lengths are needed to prove an empty snapshot.
    internal ControllerPrefabResolution CheckEmptySources() => Resolve(emptySourcesOnly: true);

    private ControllerPrefabResolution Resolve(bool emptySourcesOnly)
    {
        _main.VerifyAccess();
        if (_busy) throw new InvalidOperationException("Reentrant prefab resolution is rejected.");
        _busy = true; OwnedNativeControllerPrefab? owner = null; var scanned = 0; var matching = 0;
        try
        {
            owner = OwnedNativeControllerPrefab.Reserve(_main, _factory());
            var backend = owner.Backend; backend.Initialize();
            var counts = new[] { backend.DiscoverSource(0), backend.DiscoverSource(1) };
            if (counts.Any(x => x < 0) || (long)counts[0] + counts[1] > MaximumScannedElements)
                return Refuse(ControllerPrefabResolutionStatus.ScanLimit, "The complete card source arrays exceed the declared 4096-element scan bound.");
            if (emptySourcesOnly && (counts[0] != 0 || counts[1] != 0))
            {
                backend.CompleteDiscovery();
                return Refuse(ControllerPrefabResolutionStatus.ScanRequired, "A verified card source contains elements.");
            }
            var ambiguous = false; long unique = 0;
            for (var source = 0; source < counts.Length; source++)
                for (var index = 0; index < counts[source]; index++)
                {
                    scanned++;
                    using var card = backend.RetainCard(source, index);
                    if (card is null || !backend.IsActiveLoadedCard(source, card)) continue;
                    using var prefab = backend.RetainPrefab(source, card);
                    if (prefab is null) continue;
                    backend.VerifyCardPrefab(source, card, prefab); matching++;
                    if (unique == 0)
                    {
                        // Keep separate final roots so the temporary card/prefab scopes below
                        // always have one clear disposal path, including ambiguous scans.
                        owner.RetainSelection(source, index, prefab.Identity); unique = owner.PrefabIdentity;
                    }
                    else if (prefab.Identity != unique) ambiguous = true;
                }
            backend.CompleteDiscovery();
            if (ambiguous) return Refuse(ControllerPrefabResolutionStatus.Ambiguous, "Active loaded cards reference more than one distinct controller prefab.");
            if (unique == 0) return Refuse(ControllerPrefabResolutionStatus.NotReady,
                "No active loaded LevelCardControl or WorkshopLevelCardControl supplies a verified prefab; enter the song list and retry.",
                confirmedAbsent: true);
            owner.VerifyStillCurrent();
            var result = new ControllerPrefabResolution(ControllerPrefabResolutionStatus.Ready,
                "Both card sources were scanned completely; all usable cards shared one exact live NormalModeInLevelController prefab.", owner, scanned, matching);
            owner = null; return result;
        }
        catch (Exception error)
        {
            var status = error is ControllerPrefabScanLimitException ? ControllerPrefabResolutionStatus.ScanLimit
                : error is TargetExpiredException ? ControllerPrefabResolutionStatus.NotReady : ControllerPrefabResolutionStatus.Unsupported;
            return Refuse(status, error.GetType().Name + ": " + error.Message);
        }
        finally { _busy = false; }

        ControllerPrefabResolution Refuse(ControllerPrefabResolutionStatus status, string reason, bool confirmedAbsent = false)
        {
            if (owner is not null)
                try { owner.Dispose(); owner = null; }
                catch (Exception cleanup) { status = ControllerPrefabResolutionStatus.CleanupUnknown; reason += " Root cleanup remains process-owned: " + cleanup.Message; }
            return new(status, reason, null, scanned, matching, confirmedAbsent && status == ControllerPrefabResolutionStatus.NotReady);
        }
    }
}

internal sealed class ControllerPrefabScanLimitException : InvalidOperationException
{ internal ControllerPrefabScanLimitException(string message) : base(message) { } }

/// <summary>Owns only discovery roots, never Unity assets. Dispose frees GC roots and does not Destroy/disable the prefab.</summary>
internal sealed class OwnedNativeControllerPrefab : IDisposable
{
    private const int MaximumOwners = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<OwnedNativeControllerPrefab> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    internal readonly IControllerPrefabBackend Backend;
    private IControllerPrefabReference? _card, _prefab;
    private int _source;
    private bool _busy, _disposeAttempted, _disposed, _cleanupUnknown;
    private OwnedNativeControllerPrefab(IMainThreadDispatcher main, IControllerPrefabBackend backend) => (_main, Backend) = (main, backend);
    internal static OwnedNativeControllerPrefab Reserve(IMainThreadDispatcher main, IControllerPrefabBackend backend)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend);
        var owner = new OwnedNativeControllerPrefab(main, backend);
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumOwners)
                throw new InvalidOperationException("Live/unknown prefab discovery owners exhaust process root capacity.");
            ProcessOwners.Add(owner);
        }
        return owner;
    }
    internal void RetainSelection(int source, int index, long expectedPrefab)
    {
        Guard();
        if (_card is not null || _prefab is not null) throw new InvalidOperationException("Prefab selection is one-shot.");
        _source = source;
        _card = Backend.RetainCard(source, index) ?? throw new TargetExpiredException("Selected card disappeared while retaining its owner.");
        _prefab = Backend.RetainPrefab(source, _card) ?? throw new TargetExpiredException("Selected card lost its prefab reference.");
        if (_prefab.Identity != expectedPrefab) throw new TargetExpiredException("The selected card changed prefab identity during discovery.");
        Backend.VerifyCardPrefab(_source, _card, _prefab);
    }
    internal long PrefabIdentity { get { Guard(); return _prefab?.Identity ?? 0; } }
    internal IntPtr BorrowPrefabPointer { get { VerifyStillCurrent(); return new IntPtr(_prefab!.Identity); } }
    internal long CardOwnerIdentity { get { Guard(); return _card?.Identity ?? 0; } }
    internal bool IsDisposed { get { _main.VerifyAccess(); return _disposed; } }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _cleanupUnknown; } }
    internal int OutstandingRootRecords { get { _main.VerifyAccess(); return Backend.OutstandingRootRecords; } }
    /// <summary>Revalidates the retained card/field/prefab. This does not claim a fresh global uniqueness scan.</summary>
    internal void VerifyStillCurrent()
    {
        Guard(); if (_card is null || _prefab is null) throw new InvalidOperationException("No verified card-prefab selection exists.");
        _busy = true;
        try { Backend.VerifyCardPrefab(_source, _card, _prefab); }
        finally { _busy = false; }
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return;
        if (_busy) throw new InvalidOperationException("Reentrant prefab cleanup is rejected.");
        if (_disposeAttempted) throw new InvalidOperationException("Unknown prefab root frees are not retried.");
        _disposeAttempted = true; _busy = true;
        try
        {
            Backend.Dispose(); _card = null; _prefab = null; _disposed = true;
            lock (ProcessGate) ProcessOwners.Remove(this);
        }
        catch { _cleanupUnknown = true; throw; }
        finally { _busy = false; }
    }
    private void Guard()
    {
        _main.VerifyAccess(); if (_disposed || _disposeAttempted) throw new ObjectDisposedException(nameof(OwnedNativeControllerPrefab));
        if (_busy) throw new InvalidOperationException("Reentrant prefab owner operation is rejected.");
    }
}
