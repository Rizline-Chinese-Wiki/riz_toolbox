using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Judgements;

internal sealed record HoldStopMonitorSnapshot(ObservedJudgementRun Run, long Receiver, bool Retired,
    bool HasResponsibility, NativeCoroutineCancellationSnapshot Requests);
internal sealed record HoldStopsSnapshot(bool Installed, string? Failure, IReadOnlyList<HoldStopMonitorSnapshot> Monitors);

internal sealed partial class NativeJudgementBackend
{
    private readonly Dictionary<HoldCoroutineLifecycle.CancellationProvider, HoldCancellationMonitor> _holdStopMonitors = new();
    private HoldCancellationMonitor[] _holdStopFaultMonitors = Array.Empty<HoldCancellationMonitor>();
    private HoldStopObservation? _holdStopTop;
    private bool _holdStopsInstalled, _holdStopsInstallationFinished;
    private string? _holdStopsFailure;

    internal sealed class HoldCancellationMonitor
    {
        private static readonly object ResponsibilityGate = new();
        private static readonly HashSet<HoldCancellationMonitor> PendingResponsibilities = new();
        private int _retainedPending;
        internal readonly HoldCoroutineLifecycle.CancellationProvider Provider;
        internal readonly NativeCoroutineCancellationLedger Requests;
        internal readonly long Receiver;
        internal HoldCancellationMonitor(NativeJudgementBackend owner, HoldCoroutineLifecycle.CancellationProvider provider)
        {
            Provider = provider; Receiver = provider.Receiver;
            Requests = new(owner._holdMain!, new[] { provider.ScalarProvider }, maximumIdentities: HoldCoroutineLifecycle.MaximumRecords);
        }
        internal bool MustRetain => Volatile.Read(ref _retainedPending) != 0 || Requests.Unknown;
        internal void RetainResponsibility()
        {
            // No engine receipt exists here. Keep the original ledger/provider
            // after run retirement, without copying its request snapshots.
            Interlocked.Exchange(ref _retainedPending, 1);
            lock (ResponsibilityGate) PendingResponsibilities.Add(this);
        }
        internal void Fault(NativeCoroutineCancellationOperation operation, NativeOperationFrame frame, string reason)
        {
            Requests.ObserveFault(operation, frame, reason);
            RetainResponsibility(); Provider.Fault(reason);
        }
    }
    private void EnsureHoldCancellationMonitor(HoldCoroutineIdentity identity)
    {
        HoldLocal();
        var provider = HoldLifecycle.GetCancellationProvider(identity.Run, identity.Owner);
        if (_holdStopMonitors.ContainsKey(provider)) return;
        PruneHoldCancellationMonitors();
        if (_holdStopMonitors.Count >= HoldCoroutineLifecycle.MaximumRecords)
            throw new InvalidOperationException("Hold cancellation monitor capacity is exhausted.");
        _holdStopMonitors.Add(provider, new HoldCancellationMonitor(this, provider));
        PublishHoldStopFaultMonitors();
    }
    private void PublishHoldStopFaultMonitors()
        => Volatile.Write(ref _holdStopFaultMonitors, _holdStopMonitors.Values.ToArray());
    private void PruneHoldCancellationMonitors()
    {
        _holdMain!.VerifyAccess();
        foreach (var monitor in _holdStopMonitors.Values.ToArray())
            if (monitor.Provider.Retired && !monitor.Provider.HasResponsibility && !monitor.MustRetain)
                _holdStopMonitors.Remove(monitor.Provider);
        PublishHoldStopFaultMonitors();
    }
    internal void CompleteHoldStopInstallation()
    {
        HoldLocal();
        if (_holdStopsInstallationFinished) throw new InvalidOperationException("Hold stop installation is once-completed.");
        _holdStopsInstallationFinished = true; _holdStopsInstalled = true;
    }
    internal void MarkHoldStopsUnavailable(string reason)
    {
        _holdMain!.VerifyAccess();
        _holdStopsInstallationFinished = true; _holdStopsInstalled = false;
        _holdStopsFailure = string.IsNullOrWhiteSpace(reason) ? "The complete six Stop channels are unavailable." : reason;
        HoldLifecycle.Fault("StopUnavailable: " + _holdStopsFailure);
    }
    internal HoldStopsSnapshot ReadHoldStops()
    {
        _holdMain!.VerifyAccess();
        return new(_holdStopsInstalled, _holdStopsFailure, Array.AsReadOnly(_holdStopMonitors.Values.Select(monitor =>
            new HoldStopMonitorSnapshot(monitor.Provider.Run, monitor.Receiver, monitor.Provider.Retired,
                monitor.Provider.HasResponsibility, monitor.Requests.Read())).ToArray()));
    }
    internal sealed class HoldStopEntry
    {
        internal readonly HoldCancellationMonitor Monitor;
        internal readonly IDisposable Lease;
        internal NativeCoroutineCancellationLedger.RequestContext? Request;
        internal HoldStopEntry(HoldCancellationMonitor monitor, IDisposable lease) => (Monitor, Lease) = (monitor, lease);
    }
    internal sealed class HoldStopObservation : IDisposable
    {
        internal readonly NativeJudgementBackend Owner;
        internal readonly NativeOperationFrame Frame;
        internal readonly NativeCoroutineCancellationOperation Operation;
        internal readonly HoldStopObservation? Parent;
        internal readonly List<HoldStopEntry> Entries = new();
        internal bool Completed, Disposed;
        internal HoldStopObservation(NativeJudgementBackend owner, NativeOperationFrame frame, NativeCoroutineCancellationOperation operation)
            => (Owner, Frame, Parent, Operation) = (owner, frame, owner._holdStopTop, operation);
        internal void Verify(NativeJudgementBackend owner, NativeOperationFrame frame)
        {
            if (!ReferenceEquals(Owner, owner) || Disposed || Completed || !Frame.Equals(frame)
                || !ReferenceEquals(owner._holdStopTop, this))
                throw new InvalidOperationException("Hold Stop observation crossed its exact physical invocation.");
        }
        internal void Acquire(HoldCancellationMonitor monitor)
        {
            if (Entries.Any(entry => ReferenceEquals(entry.Monitor, monitor))) return;
            monitor.RetainResponsibility(); // Before lease acquisition can fail.
            try
            {
                var lease = monitor.Provider.AcquireObservation();
                try { Entries.Add(new HoldStopEntry(monitor, lease)); }
                catch { lease.Dispose(); throw; }
            }
            catch (Exception error)
            {
                // This monitor may not yet be in Entries. Preserve its exact
                // ledger and epoch even when the physical lease could not open.
                monitor.Fault(Operation, Frame, "Hold Stop lease capture is unknown: " + error.Message);
                throw;
            }
        }
        public void Dispose() => Owner.ReleaseHoldStop(this);
    }
    internal HoldStopObservation? CaptureHoldStop(CoroutineStopOperation operation, NativeOperationFrame frame)
    {
        HoldLocal();
        var mapped = HoldStopOperation(operation);
        if (!frame.IsValid || frame.Kind != CoroutineStopProfile.Get(operation).Kind)
            throw new InvalidOperationException("Hold Stop requires its exact physical operation frame.");
        var observation = new HoldStopObservation(this, frame, mapped);
        _holdStopTop = observation;
        try
        {
            var seekCall = _seekHoldEpoch?.ObserveStopCapture(observation) == true;
            // Preserve every old record which the physical original could stop
            // before Tick is allowed to retire its managed run's roots.
            foreach (var monitor in _holdStopMonitors.Values.ToArray())
                if (monitor.Receiver == frame.Self.ToInt64() && monitor.Provider.HasResponsibility)
                    observation.Acquire(monitor);
            if (observation.Parent is { } parent)
                foreach (var entry in parent.Entries) observation.Acquire(entry.Monitor);
            if (observation.Entries.Count == 0 && !seekCall)
            {
                _holdStopTop = observation.Parent; observation.Disposed = true; return null;
            }
            TickHoldLifecycle();
            // A retirement free may synchronously introduce the new run. Its
            // separate provider must not borrow an older receiver's identities.
            foreach (var monitor in _holdStopMonitors.Values.ToArray())
                if (monitor.Receiver == frame.Self.ToInt64() && monitor.Provider.HasResponsibility)
                    observation.Acquire(monitor);
            foreach (var entry in observation.Entries)
            {
                var monitor = entry.Monitor;
                var receiverCurrent = monitor.Receiver == frame.Self.ToInt64() && IsCurrentHoldStopReceiver(monitor);
                entry.Request = monitor.Requests.Capture(mapped, frame, receiverCurrent);
                if (entry.Request is { } request)
                    foreach (var matched in request.Matched) monitor.Provider.ObserveCancellation(matched);
                if (monitor.Requests.Unknown) monitor.Provider.Fault("A Hold Stop request has unknown scalar provenance.");
            }
        }
        catch (Exception error)
        {
            // Keep the physical context and its leases until the original's
            // Complete/Dispose even when capture can only preserve Unknown.
            FaultHoldStop(observation, "Hold Stop capture is unknown: " + error.Message);
        }
        return observation;
    }
    internal void CompleteHoldStop(HoldStopObservation observation, NativeOperationFrame frame, NativeOperationExitFlags flags)
    {
        _holdMain!.VerifyAccess(); observation.Verify(this, frame);
        observation.Completed = true;
        List<Exception>? failures = null;
        foreach (var entry in observation.Entries)
        {
            try
            {
                if (entry.Request is { } request) entry.Monitor.Requests.Complete(request, frame, flags);
                if (entry.Monitor.Requests.Unknown) entry.Monitor.Provider.Fault("A Hold Stop request did not complete with confirmed provenance.");
            }
            catch (Exception error)
            {
                entry.Monitor.Fault(observation.Operation, frame, "Hold Stop completion has unknown responsibility.");
                (failures ??= new()).Add(error);
            }
        }
        if (failures is not null)
        {
            throw new AggregateException("Hold Stop completion failed.", failures);
        }
        _seekHoldEpoch?.ObserveStopCompletion(observation, flags);
    }
    private void ReleaseHoldStop(HoldStopObservation observation)
    {
        _holdMain!.VerifyAccess();
        if (observation.Disposed) return;
        if (!ReferenceEquals(observation.Owner, this) || !ReferenceEquals(_holdStopTop, observation))
        {
            FaultHoldStop(observation, "Hold Stop cleanup crossed its exact physical parent.");
            throw new InvalidOperationException("Hold Stop cleanup crossed its exact physical parent.");
        }
        observation.Disposed = true; _holdStopTop = observation.Parent;
        if (!observation.Completed) FaultHoldStop(observation, "Hold Stop original completion is unknown.");
        List<Exception>? failures = null;
        for (var index = observation.Entries.Count - 1; index >= 0; --index)
            try { observation.Entries[index].Request?.Dispose(); }
            catch (Exception error)
            {
                observation.Entries[index].Monitor.Fault(observation.Operation, observation.Frame, "Hold Stop request cleanup is unknown.");
                (failures ??= new()).Add(error);
            }
        // All request scopes close before the first release can call a native
        // root free and reenter. A failed free is retained by the lifecycle.
        for (var index = observation.Entries.Count - 1; index >= 0; --index)
            try { observation.Entries[index].Lease.Dispose(); }
            catch (Exception error)
            {
                observation.Entries[index].Monitor.Fault(observation.Operation, observation.Frame, "Hold Stop lease cleanup is unknown.");
                (failures ??= new()).Add(error);
            }
        PruneHoldCancellationMonitors();
        if (failures is not null)
        {
            throw new AggregateException("Hold Stop cleanup failed without retry.", failures);
        }
    }
    private static void FaultHoldStop(HoldStopObservation observation, string reason)
    {
        observation.Owner._seekHoldEpoch?.ObserveStopFault(observation, reason);
        foreach (var entry in observation.Entries)
            entry.Monitor.Fault(observation.Operation, observation.Frame, reason);
    }
    internal void ObserveHoldStopFault(CoroutineStopOperation operation, NativeOperationFrame frame, string reason)
    {
        var mapped = HoldStopOperation(operation);
        // Exit faults precede Complete. An old original can have started a new
        // run on the same receiver: its exact captured scope owns the fault.
        // Even an empty scope stays exact (lease acquisition already faulted).
        if (_holdMain!.IsMainThread && _holdStopTop is { } observation
            && observation.Operation == mapped && observation.Frame.Equals(frame))
        {
            FaultHoldStop(observation, "Native Hold Stop transport: " + reason);
            return;
        }
        // Immutable monitor table + scalar ledger fault callbacks only. This
        // path may run on a worker and never reads provider or native objects.
        foreach (var monitor in Volatile.Read(ref _holdStopFaultMonitors))
        {
            monitor.Requests.ObserveFault(mapped, frame, reason);
            if (monitor.Requests.Unknown)
            {
                monitor.RetainResponsibility();
                monitor.Provider.Fault("Native Hold Stop transport: " + reason);
            }
        }
    }
    private bool IsCurrentHoldStopReceiver(HoldCancellationMonitor monitor)
    {
        var prior = monitor.Provider.Run;
        var now = _state.CaptureJudgementRun(prior.Information, prior.Controller);
        return !monitor.Provider.Retired && now is not null && prior.SameGeneration(now)
            && _state.MatchesCurrentGameplayRun(prior.Information, prior.Controller, prior.RunId, prior.SceneGeneration);
    }
    private static NativeCoroutineCancellationOperation HoldStopOperation(CoroutineStopOperation operation) => operation switch {
        CoroutineStopOperation.Enumerator => NativeCoroutineCancellationOperation.StopEnumerator,
        CoroutineStopOperation.Handle => NativeCoroutineCancellationOperation.StopHandle,
        CoroutineStopOperation.All => NativeCoroutineCancellationOperation.StopAll,
        CoroutineStopOperation.ManagedHandle => NativeCoroutineCancellationOperation.StopManagedHandle,
        CoroutineStopOperation.ManagedEnumerator => NativeCoroutineCancellationOperation.StopManagedEnumerator,
        CoroutineStopOperation.Named => NativeCoroutineCancellationOperation.StopNamed,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)) };
}
