using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeTweenLocationKind { Playing, Scheduled, Ready }
internal readonly record struct NativeTweenVector2Bits(int X, int Y);
internal readonly record struct NativeTweenVector3Bits(int X, int Y, int Z);
internal readonly record struct NativeTweenVector4Bits(int X, int Y, int Z, int W);
internal sealed record NativeTweenEffectValues(bool UseFixedStart, bool AutoPlay, long Message, string MessageText,
    int StartValue, int Target, NativeTweenVector2Bits Target2, NativeTweenVector3Bits Target3,
    NativeTweenVector4Bits TargetColor, NativeTweenVector4Bits FixedStart, NativeTweenVector4Bits StartColor,
    NativeTweenVector2Bits StartValue2, NativeTweenVector3Bits StartValue3);
internal sealed record NativeTweenInfoObservation(long Object, long Class, long Target, long Curve,
    long OnStart, long OnComplete, long OnTween, int DurationBits, int DelayBits, int NowTimeBits,
    int Ease, int TweenType, bool UseOnStart, bool UseOnComplete, bool IsOver, NativeTweenEffectValues EffectValues);
internal sealed record NativeTweenContainerObservation(long Object, long Array, long Class, int Count, int Version,
    IReadOnlyList<long> CapacityItems);
internal sealed record NativeTweenManagerObservation(long Object, long Class, long Generation,
    int TrackSize, int WorkingCount, NativeTweenContainerObservation? Playing,
    NativeTweenContainerObservation? Scheduled, NativeTweenContainerObservation? Ready);
internal sealed record NativeTweenDelegateObservation(long Delegate, long InvocationList, long ActualClass,
    long Target, long MethodInfo, bool Supported);
internal sealed record NativeTweenWorkGraph(long AddTween, IReadOnlyList<NativeTweenDelegateObservation> AddTweenLeaves,
    IReadOnlyList<NativeTweenInfoObservation> Tweens, IReadOnlyList<NativeTweenManagerObservation> Managers);
internal sealed record NativeTweenPlaySourceSnapshot(int EventSlot, long Event, long InvocationGeneration,
    ulong ParentSequence, ulong ChildSequence, long Invokable, long Target, long MethodInfo, bool AmbiguousLeaf);
internal sealed record NativeTweenLocation(long Manager, long Generation, NativeTweenLocationKind Kind, int Index);
internal sealed record NativeTweenAdmissionSnapshot(long Admission, ulong NativeSequence, long Tween,
    long Manager, long ManagerGeneration, long? PlayObservation, bool ConfirmedAppend, bool Ambiguous,
    bool ObservedAbsentAfterExecute, NativeTweenLocation? AdmittedAt, IReadOnlyList<NativeTweenLocation> Locations, string Detail);
internal sealed record NativeTweenOperationSnapshot(long Observation, NativeTweenOperation Operation,
    NativeOperationFrame Frame, long? Parent, bool Captured, bool Completed, bool NativeReturned, bool Disposed,
    NativeTweenPlaySourceSnapshot? Source, NativeTweenWorkGraph? Before, NativeTweenWorkGraph? After,
    bool Skipped = false, uint ExitFlags = 0);
internal sealed record NativeTweenSourceAdmissionSnapshot(long Source, bool Known, NativeTweenAdmissionPhase Phase,
    uint Slot, ulong Epoch, long CachedNative, ulong IssuersInFlight, ulong ActiveEntries,
    ulong AdmittedIssuers, ulong DeniedIssuers, ulong DeniedDead);
internal sealed record NativeLocalTweenWorkSnapshot(int Sources, int Managers, int Roots, long NativeInFlight,
    bool NativeKnown, bool Pending, bool Unknown, bool AdmissionSealed, string? Failure,
    NativeTweenWorkGraph? Current, IReadOnlyList<NativeTweenOperationSnapshot> Operations,
    IReadOnlyList<NativeTweenAdmissionSnapshot> Admissions,
    IReadOnlyList<NativeTweenSourceAdmissionSnapshot>? SourceAdmissions = null, bool AdmissionSealRequested = false);

internal interface INativeLocalTweenBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    IntPtr PlayMethodInfo { get; }
    void Initialize(NativeLocalTweenWorkOwner owner);
    NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after);
    NativeTweenManagerObservation CaptureSharedUpdate(NativeOperationFrame frame, bool after)
        => Capture(NativeTweenOperation.Update, frame, after).Managers.Single(row => row.Object == frame.Self.ToInt64());
    NativeTweenWorkGraph Refresh();
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}
internal sealed class NativeLocalTweenWorkException : InvalidOperationException
{
    internal NativeLocalTweenWorkOwner Responsibility { get; }
    internal NativeLocalTweenWorkException(NativeLocalTweenWorkOwner owner, Exception error)
        : base("Tween observation is unconfirmed; actual roots and work history remain retained.", error) => Responsibility = owner;
}

/// <summary>Owns actual admitted queue occurrences and native entry/exit history.
/// Actual native admission is a separate source boundary. Neither its seal,
/// isOver nor a later absent reference supplies a work/effects completion.</summary>
internal sealed partial class NativeLocalTweenWorkOwner : INativeTweenAdmissionOwner
{
    private const int MaximumOwners = 32, MaximumOperations = 1024, MaximumAdmissions = 512;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalTweenWorkOwner> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly INativeLocalTweenBackend _backend;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly NativeLocalUnityEventLedger? _listeners;
    private readonly NativeLocalRunLaunchCoordinator? _coordinator;
    private readonly List<OperationContext> _operations = new();
    private long _operationRevision;
    private readonly List<Admission> _admissions = new();
    private long[] _sources = Array.Empty<long>(), _managers = Array.Empty<long>();
    private long[] _activeUpdateManagers = Array.Empty<long>();
    private NativeTweenWorkGraph? _current;
    private OperationContext? _top;
    private bool _initializing, _ready, _reading, _sealRequested, _admissionSealed;
    private int _fault;
    private string? _failure;
    internal bool Unknown => Volatile.Read(ref _fault) != 0 || _backend.CleanupUnknown
        || _launch?.Read().Phase == LocalRunLaunchCommitPhase.Unknown || _listeners?.Unknown == true;
    // Worker fault classification uses copied identities only; no game/registry read.
    internal bool ObservationAuthorityUnknown => Volatile.Read(ref _fault) != 0;
    private NativeLocalTweenWorkOwner(IMainThreadDispatcher main, INativeLocalTweenBackend backend,
        LocalRunLaunchCommitState? launch, NativeLocalUnityEventLedger? listeners, NativeLocalRunLaunchCoordinator? coordinator)
        => (_main, _backend, _launch, _listeners, _coordinator) = (main, backend, launch, listeners, coordinator);

    internal static NativeLocalTweenWorkOwner Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch,
        NativeLocalUnityEventLedger listeners, NativeLocalRunLaunchCoordinator coordinator)
    {
        main.VerifyAccess(); launch.VerifyPendingOwnership(launch.Registry);
        if (!listeners.IsFor(launch) || launch.Read().ActivationAttempted)
            throw new InvalidOperationException("Tween sources must be adopted from this exact listener owner before activation.");
        ArgumentNullException.ThrowIfNull(coordinator);
        return CaptureCore(main, new NativeLocalTweenBackend(api, main, profile, module, broker, launch, listeners), launch, listeners, coordinator);
    }
    internal static NativeLocalTweenWorkOwner CaptureBackend(IMainThreadDispatcher main, INativeLocalTweenBackend backend)
    {
        if (backend is NativeLocalTweenBackend) throw new InvalidOperationException("Controlled capture cannot confer production authority.");
        return CaptureCore(main, backend, null, null, null);
    }
    private static NativeLocalTweenWorkOwner CaptureCore(IMainThreadDispatcher main, INativeLocalTweenBackend backend,
        LocalRunLaunchCommitState? launch, NativeLocalUnityEventLedger? listeners, NativeLocalRunLaunchCoordinator? coordinator)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend);
        var owner = new NativeLocalTweenWorkOwner(main, backend, launch, listeners, coordinator);
        lock (Gate)
        {
            if (Owners.Count >= MaximumOwners || launch is not null && Owners.Any(row => ReferenceEquals(row._launch, launch)))
                throw new InvalidOperationException("Tween process responsibility capacity exhausted or this launch already owns its observation.");
            Owners.Add(owner);
        }
        owner._initializing = owner._reading = true;
        try
        {
            backend.Initialize(owner); owner.Known();
            owner._current = Freeze(backend.Refresh()); owner.RegisterGraph(owner._current);
            var activity = backend.ReadActivity(); owner.Known();
            if (!activity.Known || activity.InFlight != 0)
                throw new InvalidOperationException("Tween observation was installed with unknown or active native calls.");
            owner._ready = true; return owner;
        }
        catch (Exception error) { owner.Fault(error.Message); throw new NativeLocalTweenWorkException(owner, error); }
        finally { owner._initializing = owner._reading = false; }
    }
    internal void RegisterSource(long identity)
    {
        _main.VerifyAccess();
        if (!_initializing || identity == 0) throw new InvalidOperationException("Only actual preactivation target adoption registers a Tween source.");
        if (!_sources.Contains(identity)) Volatile.Write(ref _sources, _sources.Append(identity).ToArray());
    }
    private void RegisterGraph(NativeTweenWorkGraph graph)
    {
        foreach (var manager in graph.Managers)
            if (!_managers.Contains(manager.Object)) Volatile.Write(ref _managers, _managers.Append(manager.Object).ToArray());
        RegisterJournalManagers();
        // An owned queue slot needs the complete Tween graph. An otherwise idle
        // journal manager only needs its own queues at the two physical boundaries.
        Volatile.Write(ref _activeUpdateManagers, graph.Managers.Where(HasOwnedQueueEntry).Select(manager => manager.Object).ToArray());
    }
    private bool HasOwnedQueueEntry(NativeTweenManagerObservation manager)
        => _sources.Any(source => Locations(manager, source).Any());
    private bool Matches(NativeTweenOperation operation, NativeOperationFrame frame)
    {
        var owned = operation switch {
            NativeTweenOperation.Play => Volatile.Read(ref _sources).Contains(frame.Self.ToInt64()),
            NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween => Volatile.Read(ref _sources).Contains(frame.Arg0.ToInt64()),
            NativeTweenOperation.Update => Volatile.Read(ref _activeUpdateManagers).Contains(frame.Self.ToInt64()),
            NativeTweenOperation.Awake => Volatile.Read(ref _sources).Length != 0,
            _ => false };
        if (owned) return true;
        // Update observes a shared manager, so that manager frame alone cannot
        // make its unrelated queue members local sources. A new source needs
        // an actual owned Tween ancestor or a private current listener receipt.
        return !_sealRequested && !_admissionSealed && HasOwnedTweenParent() && (operation switch {
            NativeTweenOperation.Play => frame.Self != IntPtr.Zero,
            NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween
                => frame.Arg0 != IntPtr.Zero && frame.Self != IntPtr.Zero,
            _ => false
        });
    }

    private bool IsJournalManager(NativeTweenOperation operation, NativeOperationFrame frame)
        => operation == NativeTweenOperation.Update && Volatile.Read(ref _journalManagers).Contains(frame.Self.ToInt64());

    internal sealed class OperationContext : IDisposable
    {
        private readonly NativeLocalTweenWorkOwner _owner;
        internal readonly long Observation;
        internal readonly NativeTweenOperation Operation;
        internal readonly NativeOperationFrame Frame;
        internal readonly OperationContext? Parent;
        internal bool Captured, Completed, NativeReturned, Disposed, Skipped;
        internal bool ForeignMask;
        internal uint ExitFlags;
        internal NativeLocalUnityEventLedger.TweenPlaySource? Source;
        internal NativeLocalUnityEventLedger.TweenDynamicSource? DynamicSource;
        internal NativeTweenWorkGraph? Before, After;
        internal NativeTweenManagerObservation? SharedManagerBefore, SharedManagerAfter;
        internal OperationContext(NativeLocalTweenWorkOwner owner, NativeTweenOperation operation, NativeOperationFrame frame, long id)
            => (_owner, Operation, Frame, Observation, Parent) = (owner, operation, frame, id, owner._top);
        public void Dispose() => _owner.DisposeContext(this);
        internal NativeTweenOperationSnapshot Snapshot() => new(Observation, Operation, Frame, Parent?.Observation,
            Captured, Completed, NativeReturned, Disposed, Source?.Snapshot, Before, After, Skipped, ExitFlags);
    }
    private sealed class Admission
    {
        internal long Id, Tween, Manager, ManagerGeneration;
        internal ulong Sequence;
        internal long? Play;
        internal bool Confirmed, Ambiguous, AbsentAfterExecute;
        internal NativeTweenLocation? AdmittedAt;
        internal NativeTweenLocation[] Locations = Array.Empty<NativeTweenLocation>();
        internal string Detail = "Actual admission is not yet confirmed.";
        internal NativeTweenAdmissionSnapshot Snapshot() => new(Id, Sequence, Tween, Manager, ManagerGeneration, Play,
            Confirmed, Ambiguous, AbsentAfterExecute, AdmittedAt, Array.AsReadOnly(Locations), Detail);
    }
    internal OperationContext? CaptureOperation(NativeTweenOperation operation, NativeOperationFrame frame)
    {
        _main.VerifyAccess();
        if (RetirementStarted) return null;
        var matches = Matches(operation, frame);
        var sharedUpdate = !matches && IsJournalManager(operation, frame);
        var dynamicSource = CaptureDynamicListenerSource(operation, frame);
        if (!matches && !sharedUpdate && dynamicSource is null) return CaptureForeignTweenMask(operation, frame);
        if (!_ready || _operations.Count >= MaximumOperations)
        { Fault("Tween entry has no initialized observation or exhausted its retained operation history."); throw new InvalidOperationException(_failure); }
        var context = new OperationContext(this, operation, frame, checked(++_operationRevision));
        context.DynamicSource = dynamicSource;
        _operations.Add(context); _top = context;
        // Retain the actual completion duty before the first native root/read.
        try
        {
            Known();
            if (_reading || !frame.IsValid || frame.Kind != TweenOperationProfile.Get(operation).Kind)
                throw new InvalidOperationException("Tween entry has an invalid native frame or reentered graph capture.");
            _reading = true;
            if (sharedUpdate)
            {
                var manager = FreezeManager(_backend.CaptureSharedUpdate(frame, false)); Known();
                // A local occurrence can arrive between the host Tick and this physical Before.
                if (!HasOwnedQueueEntry(manager))
                {
                    context.SharedManagerBefore = manager;
                    context.Captured = true;
                    BeginSourceOperation(context);
                    return context;
                }
            }
            if (operation == NativeTweenOperation.Play && _listeners is not null)
                context.Source = _listeners.TryCaptureTweenPlaySource(this, _launch!, frame.Self.ToInt64(), _backend.PlayMethodInfo);
            context.Before = Freeze(_backend.Capture(operation, frame, false)); Known(); RegisterGraph(context.Before);
            context.Captured = true;
            AdoptDynamicSourceBefore(context);
            AdoptCurrentManagerSourceBefore(context);
            BeginSourceOperation(context);
        }
        catch (Exception error) { Fault(error.Message); }
        finally { _reading = false; }
        return context;
    }
    internal void CompleteOperation(OperationContext context, NativeOperationFrame frame, NativeOperationExitFlags flags)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_top, context) || context.Completed || !SameFrame(context.Frame, frame))
        { Fault("Tween completion crossed its retained native sequence or physical parent."); return; }
        context.Completed = true; context.ExitFlags = (uint)flags;
        context.Skipped = flags == NativeOperationExitFlags.Skipped;
        context.NativeReturned = flags == NativeOperationExitFlags.Returned;
        if (context.ForeignMask)
        {
            if (!context.NativeReturned && !context.Skipped) Fault("A foreign Tween scalar mask did not return through its exact native frame.");
            return;
        }
        if (context.Skipped)
        {
            // A skipped original has no native After graph or append. Preserve
            // Before and the exact disposition while releasing only this call's
            // scalar context duty. Existing queue/effect owners stay retained.
            if (context.Operation is not (NativeTweenOperation.Play or NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween))
                Fault("Only the three exact Tween admission/death lanes may skip an original.");
            CompleteSourceOperation(context);
            return;
        }
        if (!context.NativeReturned) { Fault("Tween original exited abnormally; its admitted work remains retained."); return; }
        if (!context.Captured || Unknown) return;
        try
        {
            _reading = true;
            if (context.SharedManagerBefore is not null)
            {
                context.SharedManagerAfter = FreezeManager(_backend.CaptureSharedUpdate(frame, true)); Known();
                if (_operationRevision == context.Observation && SameManager(context.SharedManagerBefore, context.SharedManagerAfter))
                {
                    CompleteSourceOperation(context);
                    return;
                }
            }
            context.After = Freeze(_backend.Capture(context.Operation, frame, true)); Known();
            if (context.SharedManagerAfter is { } observed
                && !SameManager(observed, context.After.Managers.SingleOrDefault(row => row.Object == observed.Object)))
                throw new InvalidOperationException("The shared manager changed between its queue read and full exit graph.");
            RegisterGraph(context.After); _current = context.After;
            if (context.Operation == NativeTweenOperation.AddTweenToWaitingList && _sources.Contains(frame.Arg0.ToInt64()))
            {
                RecordAdmission(context);
                ObserveSourceAdmission(context);
            }
            UpdateLocations(context.After, context.Operation == NativeTweenOperation.ExecuteTween ? frame.Arg0.ToInt64() : 0);
            CompleteSourceOperation(context);
        }
        catch (Exception error) { Fault(error.Message); }
        finally { _reading = false; }
    }
    private void DisposeContext(OperationContext context)
    {
        _main.VerifyAccess();
        if (context.Disposed || !context.Completed || !ReferenceEquals(_top, context))
        { Fault("Tween native context disposal did not follow its exact completion."); return; }
        context.Disposed = true; _top = context.Parent;
        if (context.SharedManagerAfter is not null && !Unknown) CompactSharedUpdate(context);
    }
    internal void ObserveFault(NativeTweenOperation operation, NativeOperationFrame frame, string reason)
    { if (Matches(operation, frame) || IsJournalManager(operation, frame) || CaptureDynamicListenerSource(operation, frame) is not null) Fault(reason); }

    private void RecordAdmission(OperationContext context)
    {
        if (_admissions.Count == MaximumAdmissions) throw new InvalidOperationException("Tween admission responsibility capacity exhausted.");
        var before = context.Before!.Managers.SingleOrDefault(row => row.Object == context.Frame.Self.ToInt64());
        var after = context.After!.Managers.SingleOrDefault(row => row.Object == context.Frame.Self.ToInt64());
        var tween = context.Frame.Arg0.ToInt64();
        var beforeInfo = context.Before.Tweens.SingleOrDefault(row => row.Object == tween);
        // The pinned Add body skips isOver=false. Verify the complete ordered
        // vectors instead of inferring a successful append from total counts.
        var scheduled = before is not null && after is not null && SamePlaying(before.Playing, after.Playing)
            && Append(before.Scheduled, after.Scheduled, tween) && SameContainer(before.Ready, after.Ready);
        var ready = before is not null && after is not null && SamePlaying(before.Playing, after.Playing)
            && Append(before.Ready, after.Ready, tween) && SameContainer(before.Scheduled, after.Scheduled);
        if (!scheduled && !ready && beforeInfo is { IsOver: false } && SameManager(before, after)) return;
        var row = new Admission { Id = _admissions.Count + 1L, Sequence = context.Frame.Sequence, Tween = tween,
            Manager = context.Frame.Self.ToInt64(), ManagerGeneration = after?.Generation ?? 0,
            Confirmed = beforeInfo is { IsOver: true } && before?.Generation == after?.Generation && (scheduled ^ ready) };
        for (var parent = context.Parent; parent is not null; parent = parent.Parent)
            if (parent.Operation == NativeTweenOperation.Play && parent.Frame.Self.ToInt64() == tween) { row.Play = parent.Observation; break; }
        row.Ambiguous = !row.Confirmed;
        if (row.Confirmed)
            row.AdmittedAt = new(row.Manager, row.ManagerGeneration,
                scheduled ? NativeTweenLocationKind.Scheduled : NativeTweenLocationKind.Ready,
                scheduled ? before!.Scheduled!.Count : before!.Ready!.Count);
        row.Detail = row.Confirmed ? "This native Add sequence appended one actual ordered queue occurrence; later duplicate transfers may remain ambiguous."
            : "The actual Add returned, but its complete queue transition is unconfirmed; this distinct admission duty remains retained.";
        _admissions.Add(row);
    }
    private void UpdateLocations(NativeTweenWorkGraph graph, long actualExecutedTween)
    {
        foreach (var row in _admissions)
        {
            row.Locations = Locations(graph, row.Tween).ToArray();
            var sameTarget = _admissions.Count(other => other.Tween == row.Tween);
            if (sameTarget > 1 || row.Locations.Length > 1) row.Ambiguous = true;
            if (row.Locations.Any(location => location.Manager != row.Manager || location.Generation != row.ManagerGeneration))
            { row.Ambiguous = true; row.Detail = "Manager identity/generation changed; prior work and all historical graph roots remain retained."; }
            // This is a diagnostic fact about an actual Execute After. It is
            // deliberately not a completion/source receipt, even for one item.
            if (actualExecutedTween == row.Tween && row.Locations.Length == 0)
                row.AbsentAfterExecute = true;
        }
    }
    private static IEnumerable<NativeTweenLocation> Locations(NativeTweenWorkGraph graph, long tween)
        => graph.Managers.SelectMany(manager => Locations(manager, tween));
    private static IEnumerable<NativeTweenLocation> Locations(NativeTweenManagerObservation manager, long tween)
    {
        foreach (var (kind, container) in new[] { (NativeTweenLocationKind.Playing, manager.Playing),
            (NativeTweenLocationKind.Scheduled, manager.Scheduled), (NativeTweenLocationKind.Ready, manager.Ready) })
        {
            if (container is null) continue;
            // A nested Execute can already occupy a higher playing slot before
            // Update writes its final working count.
            var count = kind == NativeTweenLocationKind.Playing ? container.CapacityItems.Count : container.Count;
            for (var index = 0; index < count; ++index)
                if (container.CapacityItems[index] == tween) yield return new(manager.Object, manager.Generation, kind, index);
        }
    }
    private static bool Append(NativeTweenContainerObservation? before, NativeTweenContainerObservation? after, long tween)
        => before is not null && after is not null && before.Object == after.Object && before.Class == after.Class
            && after.Count == before.Count + 1 && after.CapacityItems.Take(before.Count).SequenceEqual(before.CapacityItems.Take(before.Count))
            && after.CapacityItems[before.Count] == tween;
    private static bool SamePlaying(NativeTweenContainerObservation? a, NativeTweenContainerObservation? b) => SameContainer(a, b);
    private static bool SameContainer(NativeTweenContainerObservation? a, NativeTweenContainerObservation? b)
        => a is null && b is null || a is not null && b is not null && a.Object == b.Object && a.Array == b.Array
            && a.Class == b.Class && a.Count == b.Count && a.Version == b.Version && a.CapacityItems.SequenceEqual(b.CapacityItems);
    internal static bool SameManager(NativeTweenManagerObservation? a, NativeTweenManagerObservation? b)
        => a is not null && b is not null && a.Object == b.Object && a.Class == b.Class && a.Generation == b.Generation
            && a.TrackSize == b.TrackSize && a.WorkingCount == b.WorkingCount
            && SamePlaying(a.Playing, b.Playing) && SameContainer(a.Scheduled, b.Scheduled) && SameContainer(a.Ready, b.Ready);
    internal void Tick()
    {
        _main.VerifyAccess();
        if (!_ready || Unknown || _reading || _top is not null || RetirementStarted) return;
        try
        {
            var activity = _backend.ReadActivity(); Known();
            if (!activity.Known) throw new InvalidOperationException("Tween physical transport became Unknown during host pumping.");
            if (activity.InFlight != 0) return;
            _reading = true; _current = Freeze(_backend.Refresh()); Known(); RegisterGraph(_current);
            VerifyDynamicSourceRoots();
            UpdateLocations(_current, 0);
        }
        catch (Exception error) { Fault(error.Message); }
        finally { _reading = false; }
    }
    internal bool TrySeal()
    {
        _main.VerifyAccess();
        if (!_sealRequested || !_ready || Unknown || _reading || _top is not null
            || _backend is not NativeLocalTweenBackend production) return false;
        try
        {
            VerifyTweenAdmissionOwner();
            _admissionSealed = production.TrySealAdmissions(this);
            return _admissionSealed;
        }
        catch (Exception error) { Fault(error.Message); return false; }
    }
    internal void RequestAdmissionSeal(LocalRunLaunchCommitState launch, NativeLocalRunLaunchCoordinator coordinator)
    {
        _main.VerifyAccess();
        if (launch is null || coordinator is null || !ReferenceEquals(_launch, launch) || !ReferenceEquals(_coordinator, coordinator))
            throw new InvalidOperationException("Admission seal request belongs to another retained production run.");
        coordinator.RequireTweenAdmissionSealRequest(this, launch);
        VerifyTweenAdmissionOwner(); _sealRequested = true;
    }
    public void VerifyTweenAdmissionOwner()
    {
        _main.VerifyAccess();
        // A controlled backend can exercise the same private transaction, but
        // can never enter Capture's exact production backend path.
        if (_backend is not NativeLocalTweenBackend && _backend is INativeLocalTweenRetirementBackend controlled)
        {
            lock (Gate)
                if (!Owners.Contains(this) || !_initializing && !_ready || Unknown
                    || _launch is not null || _coordinator is not null || _retirementTransaction?.RootsMayBeReleased == true)
                    throw new InvalidOperationException("Controlled Tween admission owner is no longer retained.");
            controlled.VerifyTweenRetirementOwner(this); return;
        }
        lock (Gate)
            if (!Owners.Contains(this) || !_initializing && !_ready || Unknown
                || _retirementTransaction?.RootsMayBeReleased == true
                || _launch is null || _listeners is null || _coordinator is null || _backend is not NativeLocalTweenBackend
                || !_listeners.IsFor(_launch))
                throw new InvalidOperationException("Native admission requires its actual retained production Tween source owner.");
    }
    internal void RequireAdmissionSourceSet(IEnumerable<long> sources)
    {
        VerifyTweenAdmissionOwner();
        var actual = sources.OrderBy(value => value).ToArray();
        if (!actual.SequenceEqual(_sources.OrderBy(value => value)))
            throw new InvalidOperationException("Actual native claim leases do not cover this owner's complete adopted source set.");
    }
    internal bool CanRetireWork()
    {
        _main.VerifyAccess();
        return !Unknown && HasRetirementReceipt();
    }
    internal NativeLocalTweenWorkSnapshot Read()
    {
        _main.VerifyAccess(); var activity = RetiredRootsUnavailable
            ? new ExactHookBroker.ManagedBoundaryActivity(0, HasRetirementReceipt() && !Unknown) : _backend.ReadActivity();
        return new(_sources.Length, _managers.Length, _backend.RootRecords, activity.InFlight, activity.Known,
            !HasRetirementReceipt(), Unknown || !activity.Known, _admissionSealed, _failure, _current,
            Array.AsReadOnly(_operations.Select(row => row.Snapshot()).ToArray()),
            Array.AsReadOnly(_admissions.Select(row => row.Snapshot()).ToArray()),
            (_backend as NativeLocalTweenBackend)?.ReadCachedSourceAdmissions(), _sealRequested);
    }
    internal bool IsFor(LocalRunLaunchCommitState launch, NativeLocalUnityEventLedger listeners)
        => ReferenceEquals(_launch, launch) && ReferenceEquals(_listeners, listeners) && !Unknown;
    private void Known() { if (Unknown || _retirementTransaction?.RootsMayBeReleased == true) throw new InvalidOperationException("Tween source observation is Unknown or its retained roots have entered retirement."); }
    private void Fault(string reason)
    {
        var first = Interlocked.CompareExchange(ref _failure, reason, null) ?? reason;
        Interlocked.Exchange(ref _fault, 1);
        _launch?.ObserveNativeFaultFromAnyThread("Tween observation: " + first);
    }
    private static bool SameFrame(NativeOperationFrame a, NativeOperationFrame b)
        => a.Version == b.Version && a.Size == b.Size && a.Kind == b.Kind && a.Reserved == b.Reserved && a.Self == b.Self
            && a.Arg0 == b.Arg0 && a.Arg1 == b.Arg1 && a.Method == b.Method && a.Caller == b.Caller && a.Sequence == b.Sequence;
    private static NativeTweenManagerObservation FreezeManager(NativeTweenManagerObservation manager)
    {
        static NativeTweenContainerObservation? Container(NativeTweenContainerObservation? value)
        {
            if (value is null) return null;
            if (value.Object == 0 || value.Array == 0 || value.Class == 0 || value.Count < 0 || value.Version < 0
                || value.Count > value.CapacityItems.Count) throw new InvalidOperationException("Tween container has no complete bounded actual vector.");
            return value with { CapacityItems = Array.AsReadOnly(value.CapacityItems.ToArray()) };
        }
        return manager with { Playing = Container(manager.Playing), Scheduled = Container(manager.Scheduled), Ready = Container(manager.Ready) };
    }
    private static NativeTweenWorkGraph Freeze(NativeTweenWorkGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph with { AddTweenLeaves = Array.AsReadOnly(graph.AddTweenLeaves.ToArray()), Tweens = Array.AsReadOnly(graph.Tweens.ToArray()),
            Managers = Array.AsReadOnly(graph.Managers.Select(FreezeManager).ToArray()) };
    }
}
