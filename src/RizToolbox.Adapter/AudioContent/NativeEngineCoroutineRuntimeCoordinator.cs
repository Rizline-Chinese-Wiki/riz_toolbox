using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeEngineCoroutineRuntimeSnapshot(bool Installed, bool Unknown, long NativeInFlight,
    bool NativeKnown, int ReceiverRoots, NativeEngineCoroutineIdentityLedgerSnapshot? Identities, string? Failure,
    NativeEngineCoroutineReceiverLifetimeSnapshot? Receivers = null, bool ObserverRetirementReady = false,
    NativeEngineCoroutineInlineSnapshot? Inline = null, NativeEngineSchedulerLedgerSnapshot? Scheduler = null,
    NativeEngineWaitLedgerSnapshot? Wait = null);

/// <summary>Connects the process-wide four engine channels to this launch's three
/// real coroutine trackers. Retained receiver roots and native owner identities
/// are separate; an observed engine return never completes cancellation or frees
/// a receiver, enumerator, bundle or other consumer.</summary>
internal sealed partial class NativeEngineCoroutineRuntimeCoordinator
{
    private static readonly object Gate = new();
    private static readonly HashSet<NativeEngineCoroutineRuntimeCoordinator> Owners = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly NativeEngineRuntimeCoordinator _runtime;
    private readonly NativeLocalActiveDestructionTracker _activeDestruction;
    private readonly NativeLocalCutoutEventSource _cutoutEvents;
    private readonly NativeCoroutineCancellationObserver _cancellation;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[2];
    private readonly NativeUnityCoroutineOwner[] _pairs = new NativeUnityCoroutineOwner[2];
    private NativeEngineCoroutineIdentityLedger? _identities;
    private NativeEngineCoroutineInlineLedger? _inline;
    private NativeEngineSchedulerRowLedger? _scheduler;
    private NativeEngineSchedulerObserverDispatcher.Binding? _schedulerBinding;
    private NativeEngineWaitLedger? _wait;
    private NativeEngineWaitObserverDispatcher.Binding? _waitBinding;
    private NativeEngineCoroutineReceiverLifetime? _receivers;
    private NativeCoroutineCancellationIdentityProvider[]? _providers;
    private NativeEngineCoroutineObserverRetirementAuthority? _observerRetirement;
    private NativeEngineRuntimeCoordinator.Subscription? _subscription;
    private int _unknown;
    private bool _installed;
    private bool _observerRetirementReady;
    private string? _failure;

    internal sealed class CaptureException : InvalidOperationException
    {
        internal NativeEngineCoroutineRuntimeCoordinator Responsibility { get; }
        internal CaptureException(NativeEngineCoroutineRuntimeCoordinator owner, Exception error)
            : base("Engine/tracker adoption retains its partial subscription and receiver roots.", error) => Responsibility = owner;
    }

    private NativeEngineCoroutineRuntimeCoordinator(NativeRuntime api, IMainThreadDispatcher main,
        LocalRunLaunchCommitState launch, NativeEngineRuntimeCoordinator runtime,
        NativeLocalActiveDestructionTracker activeDestruction, NativeLocalCutoutEventSource cutoutEvents,
        NativeCoroutineCancellationObserver cancellation)
        => (_api, _main, _launch, _runtime, _activeDestruction, _cutoutEvents, _cancellation) =
            (api, main, launch, runtime, activeDestruction, cutoutEvents, cancellation);

    internal static NativeEngineCoroutineRuntimeCoordinator Capture(NativeRuntime api, IMainThreadDispatcher main,
        LocalRunLaunchCommitState launch, NativeRuntime.RootHandle actualController,
        NativeLocalInitializationCoroutineTracker initial, NativeLocalTerminalCoroutineTracker terminal,
        NativeLocalCutoutCoroutineTracker cutout, NativeEngineRuntimeCoordinator runtime,
        NativeLocalActiveDestructionTracker activeDestruction, NativeLocalCutoutEventSource cutoutEvents,
        NativeCoroutineCancellationObserver cancellation)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(actualController);
        ArgumentNullException.ThrowIfNull(initial); ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(cutout); ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(activeDestruction); ArgumentNullException.ThrowIfNull(cutoutEvents);
        ArgumentNullException.ThrowIfNull(cancellation);
        var state = launch.Read();
        if (state.ActivationAttempted || state.Controller == 0 || !initial.IsFor(launch) || !terminal.IsFor(launch)
            || !cutout.IsFor(launch) || initial.ControllerIdentity != state.Controller
            || terminal.ControllerIdentity != state.Controller || actualController.Target.ToInt64() != state.Controller
            || cutout.HostIdentity == 0 || cutout.HostIdentity == state.Controller
            || !activeDestruction.IsFor(launch) || !cutoutEvents.IsFor(launch) || !cancellation.IsFor(launch))
            throw new InvalidOperationException("Engine adoption requires the exact three preactivation tracker owners.");
        // Inline cleanup must have an exact physical observer before retaining
        // any launch-specific native roots or claiming local-play capability.
        runtime.RequireInlineObservation();
        var schedulerDispatcher = runtime.RequireSchedulerObservation();
        var schedulerImage = runtime.RequirePublishedSchedulerImage().ToInt64();
        var waitDispatcher = runtime.RequireWaitObservation();
        if (runtime.RequirePublishedWaitImage().ToInt64() != schedulerImage)
            throw new UnsupportedSignatureException("Wait and scheduler observation must share the same published fixed image.");
        var owner = new NativeEngineCoroutineRuntimeCoordinator(api, main, launch, runtime, activeDestruction, cutoutEvents, cancellation);
        lock (Gate)
        {
            if (Owners.Count >= 32 || Owners.Any(value => ReferenceEquals(value._launch, launch)))
                throw new InvalidOperationException("Engine launch observation is duplicated or its permanent ownership capacity is exhausted.");
            Owners.Add(owner); // Before Root, metadata or a possibly partial Bind.
        }
        try
        {
            if (!runtime.ReadActivity().Known) throw new UnsupportedSignatureException("Engine production observation is Unknown.");
            owner._roots[0] = api.Root(actualController.Target);
            owner._roots[1] = api.Root(new IntPtr(cutout.HostIdentity));
            owner._pairs[0] = api.ReadUnityCoroutineOwner(owner._roots[0]!, state.Controller);
            owner._pairs[1] = api.ReadUnityCoroutineOwner(owner._roots[1]!, cutout.HostIdentity);
            var destruction = activeDestruction.Read(); var source = cutoutEvents.Read();
            if (destruction.Session != launch.Identity || destruction.Identity is null || destruction.Unknown
                || source.Session != launch.Identity || source.Source is null || source.Unknown)
                throw new InvalidOperationException("Engine adoption has no exact independent receiver destruction sources.");
            owner._receivers = new(main, launch.Identity, owner._pairs, destruction.Identity, source.Source);
            owner.VerifyReceivers();
            owner._providers = new[] {
                new NativeCoroutineCancellationIdentityProvider(initial, initial.ControllerIdentity, initial.ReadCancellationIdentities, initial.IsCurrentCancellationIdentity),
                new NativeCoroutineCancellationIdentityProvider(terminal, terminal.ControllerIdentity, terminal.ReadCancellationIdentities, terminal.IsCurrentCancellationIdentity),
                new NativeCoroutineCancellationIdentityProvider(cutout, cutout.HostIdentity, cutout.ReadCancellationIdentities, cutout.IsCurrentCancellationIdentity)
            };
            var nodes = new NativeEngineCoroutineNodeLedger(main);
            owner._identities = new(main, nodes, owner._providers,
                owner._pairs.Select(pair => new NativeEngineCoroutineOwnerPair(pair.ManagedReceiver, pair.NativeOwner)).ToArray());
            owner._inline = new(main, owner._identities, nodes);
            owner._wait = new(main, owner._identities, nodes, waitDispatcher, owner.Fault);
            owner._identities.BindWaitUntilYieldSource(owner._wait, initial);
            owner._waitBinding = waitDispatcher.Bind(owner._wait.Capture, owner._wait.Complete,
                owner._wait.ObservePhase, owner._wait.ObserveFault);
            IntPtr Site(long rva) => new(checked(schedulerImage + rva));
            owner._scheduler = new(main, owner._identities, nodes, schedulerDispatcher, Site(0x78f5e0), Site(0x78f680),
                new(Site(0x447eed), Site(0x447f54), Site(0x4477f1), Site(0x447c1b), Site(0x447efc), Site(0x447866)),
                owner._wait.TryClaimCallback);
            owner._schedulerBinding = schedulerDispatcher.Bind(owner._scheduler.Capture, owner._scheduler.Complete,
                owner._scheduler.ObserveRow, owner.ObserveSchedulerFault);
            owner._subscription = runtime.BindWithInline(owner.CaptureFrame, owner.CompleteFrame, owner.ObserveFault,
                owner._inline.Capture, owner._inline.Observe, owner.ObserveInlineFault);
            owner.VerifyReceivers();
            var activity = runtime.ReadActivity();
            if (!activity.Known || activity.InFlight != 0 || owner.Unknown || !owner._receivers!.Read().MayCommit)
                throw new InvalidOperationException("Engine tracker subscription did not establish an idle known boundary.");
            owner._observerRetirement = NativeEngineCoroutineObserverRetirementAuthority.ForProduction(main, owner, launch,
                owner._identities, owner._providers, runtime.ReadActivity,
                () => !owner.Unknown, owner.Fault,
                selected => owner._scheduler.MayRetireNodeGeneration(selected) && owner._wait.MayRetireNodeGeneration(selected));
            initial.AttachEngineRetirementAuthority(owner, owner._observerRetirement);
            terminal.AttachEngineRetirementAuthority(owner, owner._observerRetirement);
            cutout.AttachEngineRetirementAuthority(owner, owner._observerRetirement);
            owner._installed = true; return owner;
        }
        catch (Exception error) { owner.Fault(error.Message); throw new CaptureException(owner, error); }
    }

    private sealed class NodeContext : IDisposable
    {
        internal readonly NativeEngineCoroutineIdentityLedger.Call Identity;
        internal readonly NativeEngineSchedulerRowLedger.CleanupContext? Cleanup;
        internal readonly NativeEngineWaitLedger.CleanupContext? WaitCleanup;
        internal NodeContext(NativeEngineCoroutineIdentityLedger.Call identity, NativeEngineSchedulerRowLedger.CleanupContext? cleanup,
            NativeEngineWaitLedger.CleanupContext? waitCleanup)
            => (Identity, Cleanup, WaitCleanup) = (identity, cleanup, waitCleanup);
        public void Dispose()
        {
            try { Identity.Dispose(); }
            finally
            {
                try { Cleanup?.Dispose(); }
                finally { WaitCleanup?.Dispose(); }
            }
        }
    }

    private void CompleteFrame(NodeContext context, NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion)
    {
        _identities!.Complete(context.Identity, frame, completion);
        try { context.Cleanup?.Complete(); }
        finally { context.WaitCleanup?.Complete(); }
    }

    private NodeContext? CaptureFrame(NativeEngineNodeFrame frame)
    {
        // Foreign worker calls do not inspect rooted Unity objects or access the
        // main-thread ledger. Owned worker calls retain Unknown responsibility.
        if (!_main.IsMainThread)
        {
            if (_identities!.IsRelevantFrame(frame)) ObserveFault(frame, "Owned engine call arrived outside the Unity main thread.");
            return null;
        }
        if (_identities!.IsRelevantFrame(frame))
        {
            _observerRetirementReady = false;
            VerifyReceivers();
            var nativeOwner = frame.Kind == NativeEngineNodeKind.Start ? frame.Arg0.ToInt64() : frame.Owner.ToInt64();
            if (nativeOwner == 0 && frame.Kind != NativeEngineNodeKind.Start)
            {
                // A previously detached exact generation may have cleared its
                // owner scalar. The node ledger still validates that contract;
                // this lookup only preserves the original receiver role.
                var nodes = _identities.Read().Nodes.Nodes.Where(node => node.Node == frame.Node.ToInt64()
                    && !node.FinalCleanupObserved).ToArray();
                if (nodes.Length == 1) nativeOwner = nodes[0].NativeOwner;
            }
            _receivers!.RequireNewOwnedEntry(nativeOwner);
        }
        // All main-thread physical Starts, including foreign ones, preserve the
        // exact parent mask used by the existing identity ledger.
        var call = _identities.Capture(frame);
        if (call.NodeCall is not null) _wait!.BindNodeCall(call.NodeCall);
        if (frame.Kind == NativeEngineNodeKind.Detach && call.Relevant)
            _cancellation.ObserveEngineDetach(call);
        var waitCleanup = frame.Kind == NativeEngineNodeKind.Release && call.NodeCall is not null
            ? _wait!.CaptureCleanup(call.NodeCall) : null;
        // Wait has the full physical parent mask, including ordinary Dispatch
        // inside a Pump. Its cleanup cannot borrow that older scheduler row.
        var cleanup = waitCleanup is null && !_wait!.Unknown && frame.Kind == NativeEngineNodeKind.Release && call.NodeCall is not null
            ? _scheduler!.CaptureCleanup(call.NodeCall) : null;
        if (_wait!.Unknown)
            Fault(_wait.FirstFailure ?? $"Wait observation is unconfirmed at {frame.Kind} sequence={frame.Sequence}, caller=0x{frame.Caller:x}.");
        return new(call, cleanup, waitCleanup);
    }

    internal bool IsIssuedObserverRetirementAuthority(NativeEngineCoroutineObserverRetirementAuthority authority,
        LocalRunLaunchCommitState launch, NativeEngineCoroutineIdentityLedger identities,
        IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers)
    {
        _main.VerifyAccess();
        return ReferenceEquals(_observerRetirement, authority) && ReferenceEquals(_launch, launch)
            && ReferenceEquals(_identities, identities) && _providers is not null
            && providers.Count == _providers.Length
            && providers.Where((provider, index) => !ReferenceEquals(provider, _providers[index])).Any() == false;
    }

    private void VerifyReceivers()
    {
        _main.VerifyAccess();
        for (var index = 0; index < _roots.Length; ++index)
        {
            RequireKnown();
            var actual = _api.ObserveUnityCoroutineOwner(_roots[index]!, _pairs[index].ManagedReceiver);
            RequireKnown(); _receivers!.ObserveReceiver((NativeEngineCoroutineReceiverRole)index, actual);
        }
        RequireKnown(); var controller = _roots[0]!.Target;
        RequireKnown(); var trigger = _roots[1]!.Target;
        RequireKnown(); var actualTrigger = _api.Read<IntPtr>(controller, "cutoutMaskEventTrigger"); RequireKnown();
        if (actualTrigger != trigger || controller.ToInt64() != _launch.Read().Controller
            || _roots[0]!.Target != controller || _roots[1]!.Target != trigger)
            throw new TargetExpiredException("The retained cutout coroutine receiver left its exact controller field.");
    }

    private void ObserveFault(NativeEngineNodeFrame frame, string reason)
    {
        if (_identities?.IsRelevantFrame(frame) == true)
        {
            _identities.ObserveFault(frame, reason);
            Fault(reason);
        }
    }
    private void ObserveInlineFault(NativeEngineInlineFrame frame, string reason)
    {
        _inline?.ObserveFault(frame, reason);
        Fault(reason);
    }
    private void ObserveSchedulerFault(NativeEngineSchedulerFrame frame, string reason)
    {
        _scheduler?.ObserveFault(frame, reason);
        Fault(reason);
    }
    private void Fault(string reason)
    {
        var bounded = reason.Length > 768 ? reason[..768] : reason;
        var first = Interlocked.CompareExchange(ref _failure, bounded, null) ?? bounded;
        Interlocked.Exchange(ref _unknown, 1);
        _launch.ObserveNativeFaultFromAnyThread("Engine coroutine observation: " + first);
    }
    internal bool Unknown => Volatile.Read(ref _unknown) != 0 || _runtime.Unknown || _identities?.Unknown == true
        || _inline?.Unknown == true || _scheduler?.Unknown == true || _wait?.Unknown == true || _receivers?.Unknown == true
        || _launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown;
    private void RequireKnown()
    { if (Unknown) throw new InvalidOperationException("Engine receiver observation remains Unknown."); }
    internal void RefreshTrackedIdentities()
    {
        _main.VerifyAccess(); _observerRetirementReady = false;
        try
        {
            if (Unknown || !_installed) throw new InvalidOperationException("Engine tracker observation remains unavailable or Unknown.");
            VerifyReceivers(); _identities!.RefreshTrackedIdentities();
            if (!_runtime.ReadActivity().Known || _identities.Unknown)
                throw new InvalidOperationException("Engine/tracker identity refresh is Unknown.");
        }
        catch (Exception error) { Fault(error.Message); }
    }

    /// <summary>Runs the actual independent sources even when a cached pointer
    /// has already become zero. Pending receiver death must never prevent the
    /// first later-frame destruction observation from being reached.</summary>
    internal bool PumpIndependentReceiverDeaths()
    {
        _main.VerifyAccess(); _observerRetirementReady = false;
        try
        {
            RequireKnown(); var activity = _runtime.ReadActivity(); RequireKnown();
            if (!activity.Known) throw new InvalidOperationException("Engine activity is unknown while observing receiver death.");
            if (activity.InFlight != 0) return false;
            var confirmed = PumpDeathSources(() => { _cutoutEvents.Tick(); RequireKnown(); },
                () => { RequireKnown(); return _activeDestruction.Tick(); });
            RequireKnown(); VerifyReceivers();
            _receivers!.ObserveEventTriggerDeath(_cutoutEvents.IsFor(_launch), _cutoutEvents.Read());
            _receivers.ObserveControllerDeath(_activeDestruction.IsFor(_launch), confirmed, _activeDestruction.Read());
            RequireKnown(); activity = _runtime.ReadActivity(); RequireKnown();
            if (!activity.Known) throw new InvalidOperationException("Engine activity changed during receiver death observation.");
            return confirmed && _receivers.Read().Receivers[0].Phase == NativeEngineCoroutineReceiverPhase.ConfirmedDead;
        }
        catch (Exception error) { Fault(error.Message); return false; }
    }

    // The production call above supplies the retained sources themselves. This
    // narrow sequencing seam grants no source identity or destruction authority.
    internal static bool PumpDeathSources(Action observeCutoutSource, Func<bool> observeControllerDestruction)
    {
        ArgumentNullException.ThrowIfNull(observeCutoutSource); ArgumentNullException.ThrowIfNull(observeControllerDestruction);
        observeCutoutSource(); return observeControllerDestruction();
    }

    internal bool VerifyReceiversReadyForCommit()
    {
        _main.VerifyAccess(); RefreshTrackedIdentities();
        return !Unknown && _receivers?.Read().MayCommit == true;
    }
    internal bool AwaitingIndependentReceiverDeath
    { get { _main.VerifyAccess(); return _receivers?.Read().AwaitingIndependentDeath != false; } }

    internal bool MayRetireTrackedObserverRoots()
    {
        _main.VerifyAccess(); _observerRetirementReady = false;
        try
        {
            if (Unknown || !_installed || _identities is null || _providers is null
                || _scheduler?.Read().Pending != false || _wait?.Read().Pending != false) return false;
            var current = new List<NativeCoroutineCancellationIdentity>();
            foreach (var provider in _providers)
            {
                RequireKnown(); var rows = provider.Read(); RequireKnown();
                if (rows is null || rows.Count > NativeEngineCoroutineIdentityLedger.MaximumIdentities - current.Count
                    || rows.Any(row => !ReferenceEquals(row.Owner, provider.Owner) || row.Receiver != provider.Receiver))
                    throw new InvalidOperationException("The retained observer provider lost its bounded exact receiver records.");
                current.AddRange(rows);
            }
            _observerRetirementReady = NativeEngineCoroutineObserverRetirement.MayRetire(_runtime.ReadActivity(), _identities.Read(), current);
            RequireKnown(); return _observerRetirementReady;
        }
        catch (Exception error) { Fault(error.Message); return false; }
    }
    internal NativeEngineCoroutineRuntimeSnapshot Read()
    {
        _main.VerifyAccess(); var activity = _runtime.ReadActivity();
        return new(_installed, Unknown || !activity.Known, activity.InFlight, activity.Known,
            _roots.Count(value => value is not null), _identities?.Read(), Volatile.Read(ref _failure),
            _receivers?.Read(), _observerRetirementReady && !Unknown, _inline?.Read(), _scheduler?.Read(), _wait?.Read());
    }
}
