using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeCoroutineCancellationObserverSnapshot(NativeCoroutineCancellationSnapshot Ledger,
    long NativeInFlight, bool NativeKnown, bool Installed, bool Unknown);

/// <summary>Owns six real Stop request observation bindings for one launch.
/// No engine cancellation-completed or resource release authority is created.</summary>
internal sealed partial class NativeCoroutineCancellationObserver
{
    private static readonly object Gate = new();
    private static readonly HashSet<NativeCoroutineCancellationObserver> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly ExactHookBroker _broker;
    private readonly NativeCoroutineCancellationLedger _ledger;
    private readonly long[] _receivers;
    private readonly ExactHookBroker.Binding?[] _bindings = new ExactHookBroker.Binding?[6];
    private readonly VerifiedHookTarget?[] _targets = new VerifiedHookTarget?[6];
    private int _unknown;
    private bool _ready;
    internal sealed class CaptureException : InvalidOperationException
    {
        internal NativeCoroutineCancellationObserver Responsibility { get; }
        internal CaptureException(NativeCoroutineCancellationObserver owner, Exception error)
            : base("Coroutine stop observers retain their partial installation and request responsibility.", error) => Responsibility = owner;
    }
    private NativeCoroutineCancellationObserver(IMainThreadDispatcher main, LocalRunLaunchCommitState launch,
        ExactHookBroker broker, IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers)
    {
        (_main, _launch, _broker, _ledger) = (main, launch, broker, new(main, providers));
        _receivers = providers.Select(value => value.Receiver).Distinct().ToArray();
    }

    internal static NativeCoroutineCancellationObserver Capture(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch, NativeLocalInitializationCoroutineTracker initial,
        NativeLocalTerminalCoroutineTracker terminal, NativeLocalCutoutCoroutineTracker cutout)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(initial); ArgumentNullException.ThrowIfNull(terminal); ArgumentNullException.ThrowIfNull(cutout);
        var state = launch.Read();
        if (state.ActivationAttempted || state.Controller == 0 || !initial.IsFor(launch) || !terminal.IsFor(launch) || !cutout.IsFor(launch)
            || initial.ControllerIdentity != state.Controller || terminal.ControllerIdentity != state.Controller || cutout.HostIdentity == 0)
            throw new InvalidOperationException("Stop observation requires the same preactivation launch and all exact coroutine owners.");
        var providers = new[] {
            new NativeCoroutineCancellationIdentityProvider(initial, initial.ControllerIdentity, initial.ReadCancellationIdentities, initial.IsCurrentCancellationIdentity),
            new NativeCoroutineCancellationIdentityProvider(terminal, terminal.ControllerIdentity, terminal.ReadCancellationIdentities, terminal.IsCurrentCancellationIdentity),
            new NativeCoroutineCancellationIdentityProvider(cutout, cutout.HostIdentity, cutout.ReadCancellationIdentities, cutout.IsCurrentCancellationIdentity)
        };
        var owner = new NativeCoroutineCancellationObserver(main, launch, broker, providers);
        lock (Gate)
        {
            if (Owners.Count >= 32 || Owners.Any(value => ReferenceEquals(value._launch, launch)))
                throw new InvalidOperationException("Coroutine stop observation process responsibility is exhausted or duplicated.");
            Owners.Add(owner);
        }
        try
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || module == IntPtr.Zero
                || profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || profile.UnityVersion != "2022.3.62f2")
                throw new UnsupportedSignatureException("Coroutine stop observation requires the pinned Windows x64 Unity build.");
            using (var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll")))
            using (var sha = SHA256.Create())
                if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                    throw new UnsupportedSignatureException("Coroutine stop image differs from the pinned build.");
            foreach (var row in CoroutineStopProfile.All)
            {
                owner.RequireKnown(); owner._targets[(int)row.Operation] = api.ResolveCoroutineStopHook(module, row.Operation); owner.RequireKnown();
            }
            foreach (var row in CoroutineStopProfile.All)
            {
                owner.RequireKnown(); var operation = Map(row.Operation);
                owner._bindings[(int)row.Operation] = broker.BindNativeOperationObservation(owner._targets[(int)row.Operation]!, row.Kind,
                    frame => owner.CaptureRequest(operation, frame), owner.CompleteRequest,
                    (frame, failure) => owner.ObserveFault(operation, frame, failure));
                owner.RequireKnown();
            }
            owner._ready = true;
            var activity = owner.ReadActivity();
            if (!activity.Known || activity.InFlight != 0) throw new InvalidOperationException("Coroutine stop installation did not confirm idle observations.");
            return owner;
        }
        catch (Exception error) { owner.Fault(); throw new CaptureException(owner, error); }
    }
    private static NativeCoroutineCancellationOperation Map(CoroutineStopOperation operation) => operation switch {
        CoroutineStopOperation.Enumerator => NativeCoroutineCancellationOperation.StopEnumerator,
        CoroutineStopOperation.Handle => NativeCoroutineCancellationOperation.StopHandle,
        CoroutineStopOperation.All => NativeCoroutineCancellationOperation.StopAll,
        CoroutineStopOperation.ManagedHandle => NativeCoroutineCancellationOperation.StopManagedHandle,
        CoroutineStopOperation.ManagedEnumerator => NativeCoroutineCancellationOperation.StopManagedEnumerator,
        CoroutineStopOperation.Named => NativeCoroutineCancellationOperation.StopNamed,
        _ => throw new UnsupportedSignatureException("Unknown stop operation.") };
    private NativeCoroutineCancellationLedger.RequestContext? CaptureRequest(NativeCoroutineCancellationOperation operation, NativeOperationFrame frame)
    {
        try
        {
            // Publish the hold before consulting even scalar providers: their
            // callbacks can reenter a pump or a cleanup path synchronously.
            var owned = _receivers.Contains(frame.Self.ToInt64());
            if (owned) _launch.ObserveCoroutineCancellationRequestFromAnyThread();
            // Foreign worker calls have no main-thread physical parent to mask.
            // Owned workers retain Unknown without consulting any provider.
            if (!_main.IsMainThread)
            {
                if (owned) Fault();
                return null;
            }
            // Ledger filters unrelated receivers without reading native objects.
            var context = _ledger.Capture(operation, frame);
            if (_ledger.Read().Unknown) Fault();
            return context;
        }
        catch { Fault(); throw; }
    }
    private void CompleteRequest(NativeCoroutineCancellationLedger.RequestContext context, NativeOperationFrame frame, NativeOperationExitFlags flags)
    {
        if (!_main.IsMainThread)
        {
            if (context.IsOwnedBy(_ledger)) Fault();
            return;
        }
        _ledger.Complete(context, frame, flags);
        if (_ledger.Read().Unknown) Fault();
    }
    internal bool IsFor(LocalRunLaunchCommitState launch) => ReferenceEquals(_launch, launch);
    internal NativeCoroutineCancellationLedger CompletionLedger
    { get { _main.VerifyAccess(); return _ledger; } }
    internal void AcceptEngineCompletion(NativeCoroutineCancellationCompletionReceipt receipt)
    { _main.VerifyAccess(); RequireKnown(); _ledger.AcceptCompletion(receipt); }
    internal void ObserveEngineDetach(NativeEngineCoroutineIdentityLedger.Call call)
    {
        try
        {
            if (!_ready) { Fault(); return; }
            _ledger.ObserveEngineDetach(call);
            // Scalar property remains safe when the guard rejected a worker.
            if (_ledger.Unknown) Fault();
        }
        catch { Fault(); } // Never lose the engine Capture's existing token.
    }
    private void ObserveFault(NativeCoroutineCancellationOperation operation, NativeOperationFrame frame, string failure)
    {
        _ledger.ObserveFault(operation, frame, failure);
        // Ledger fault inspection on a worker must not build a snapshot or read
        // native state. Its scalar Unknown property is safe on every thread.
        if (_ledger.Unknown) Fault();
    }
    private void Fault()
    {
        Interlocked.Exchange(ref _unknown, 1);
        _launch.ObserveNativeFaultFromAnyThread("Coroutine stop observation is unknown.");
    }
    private void RequireKnown()
    {
        if (Volatile.Read(ref _unknown) != 0 || _ledger.Unknown || _launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown)
            throw new InvalidOperationException("Coroutine stop observation authority is unknown.");
    }
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        _main.VerifyAccess(); long active = 0;
        if (!_ready) return new(0, false);
        foreach (var binding in _bindings)
        {
            var value = binding?.ReadActivity();
            if (value is not { Known: true, InFlight: >= 0 } || active > long.MaxValue - value.InFlight)
            { Fault(); return new(active, false); }
            active += value.InFlight;
        }
        var scalar = _ledger.Read();
        return new(Math.Max(active, scalar.Outstanding), Volatile.Read(ref _unknown) == 0 && !scalar.Unknown);
    }
    internal bool MustRetainCoroutineResources()
    {
        _main.VerifyAccess(); var activity = ReadActivity(); var snapshot = _ledger.Read();
        if (!activity.Known || snapshot.Unknown) { Fault(); return true; }
        return RequiresRetention(activity, snapshot);
    }
    // Shared by the actual coordinator path and its finite scalar regressions.
    // Local request idle does not override persistent cancellation Pending.
    internal static bool RequiresRetention(ExactHookBroker.ManagedBoundaryActivity activity, NativeCoroutineCancellationSnapshot snapshot)
        => !activity.Known || activity.InFlight != 0 || snapshot.Unknown || snapshot.Outstanding != 0
            || !snapshot.CanConfirmRequestIdle || snapshot.Pending
            || (snapshot.HasObservedRequests || snapshot.ObservedRequests != 0) && !snapshot.EngineCancelled;
    internal NativeCoroutineCancellationObserverSnapshot Read()
    {
        _main.VerifyAccess(); var activity = ReadActivity(); var ledger = _ledger.Read();
        return new(ledger, activity.InFlight, activity.Known, _ready, Volatile.Read(ref _unknown) != 0 || ledger.Unknown);
    }
    internal bool Unknown => Volatile.Read(ref _unknown) != 0 || _ledger.Unknown;
}
