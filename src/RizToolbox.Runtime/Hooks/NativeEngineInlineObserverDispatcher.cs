using System.Collections.Concurrent;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Frozen membership for one actual native Start's side events. Logical
/// detach withdraws future Starts; captured contexts still receive End/exit and
/// Dispose. The ordinary node dispatcher retains Complete/Dispose authority.</summary>
internal sealed class NativeEngineInlineObserverDispatcher
{
    private const int ObserverLimit = 32, InvocationLimit = 4096;
    private static readonly object OwnersGate = new();
    private static readonly List<NativeEngineInlineObserverDispatcher> Owners = new();
    [ThreadStatic] private static Invocation? _top;
    private sealed class Sink
    {
        internal Func<Observation, IDisposable?> Capture = null!;
        internal Action<IDisposable, Observation> Observe = null!;
        internal Action<NativeEngineInlineFrame, string> Fault = null!;
        internal NativeEngineNodeObserverDispatcher.Binding StartBinding = null!;
        internal bool Active = true;
    }
    private sealed class Invocation
    {
        internal readonly NativeEngineInlineObserverDispatcher Owner;
        internal readonly NativeEngineNodeObserverDispatcher.StartScopeProof Proof;
        internal readonly NativeEngineInlineFrame Entered;
        internal readonly Invocation? Parent = _top;
        internal readonly Sink[] Sinks;
        internal readonly List<(Sink Sink, IDisposable Context)> Contexts = new();
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal bool Began, Ended;
        internal Invocation(NativeEngineInlineObserverDispatcher owner, NativeEngineNodeObserverDispatcher.StartScopeProof proof,
            NativeEngineInlineFrame entered, Sink[] sinks) => (Owner, Proof, Entered, Sinks) = (owner, proof, entered, sinks);
    }
    internal sealed class Binding : IDisposable
    {
        private readonly Action _detach; private int _disposed;
        internal Binding(Action detach) => _detach = detach;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _detach(); }
        // The actual Start dispatcher may have held this retired sink during
        // Dispose. Sweep again after that physical Start's final context ends.
        internal void DrainRetired() { if (Volatile.Read(ref _disposed) != 0) _detach(); }
    }
    internal sealed class Observation
    {
        private readonly NativeEngineInlineObserverDispatcher _owner;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal NativeEngineInlineFrame Frame { get; }
        internal NativeEngineNodeObserverDispatcher.StartScopeProof Start { get; }
        internal NativeEngineInlineChannelIdentity Identity => _owner.Identity;
        internal Observation(NativeEngineInlineObserverDispatcher owner, object issuer,
            NativeEngineNodeObserverDispatcher.StartScopeProof start, NativeEngineInlineFrame frame)
        {
            if (!ReferenceEquals(issuer, owner._receiptIssuer)) throw new InvalidOperationException("Inline observation issuer differs from its actual dispatcher.");
            (_owner, Start, Frame) = (owner, start, frame);
        }
        internal bool IsCurrent => ReferenceEquals(_owner._currentObservation, this)
            && _thread == Environment.CurrentManagedThreadId && Start.IsCurrent(Frame)
            && Start.Identity == Identity.Start;
    }
    private readonly object _gate = new();
    private readonly INativeEngineInlineContextBridge _bridge;
    private readonly NativeEngineInlineChannelIdentity _identity;
    private readonly NativeEngineNodeObserverDispatcher _start;
    private readonly ILogger _log;
    private readonly object _receiptIssuer = new();
    private Observation? _currentObservation;
    private readonly List<Sink> _sinks = new();
    private readonly ConcurrentDictionary<ulong, Invocation> _calls = new();
    private Sink[] _published = Array.Empty<Sink>();
    private ulong _lastEvent;
    private long _inFlight;
    private int _unknown;
    internal NativeEngineInlineChannelIdentity Identity => _identity;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0 || _start.Unknown;

    internal NativeEngineInlineObserverDispatcher(INativeEngineInlineContextBridge bridge,
        NativeEngineNodeObserverDispatcher actualStartDispatcher, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(bridge); ArgumentNullException.ThrowIfNull(actualStartDispatcher); ArgumentNullException.ThrowIfNull(log);
        if (!bridge.Identity.IsComplete || bridge.Identity.Start != actualStartDispatcher.Identity)
            throw new ArgumentException("Inline observation requires the same exact actual native Start dispatcher.");
        (_bridge, _identity, _start, _log) = (bridge, bridge.Identity, actualStartDispatcher, log);
        lock (OwnersGate) Owners.Add(this); // Before a possibly partial permanent Attach.
        try { bridge.Attach(Observe); }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }
    internal Binding Bind<T>(Func<Observation, T?> capture,
        Action<T, Observation> observe, Action<NativeEngineInlineFrame, string> fault,
        NativeEngineNodeObserverDispatcher.Binding actualStartBinding) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(observe); ArgumentNullException.ThrowIfNull(fault);
        ArgumentNullException.ThrowIfNull(actualStartBinding);
        if (actualStartBinding.Identity != Identity.Start || !actualStartBinding.IsFor(_start))
            throw new ArgumentException("Inline logical observation must use this family's exact actual Start binding.", nameof(actualStartBinding));
        var sink = new Sink { Capture = receipt => capture(receipt), Observe = (context, receipt) => observe((T)context, receipt), Fault = fault,
            StartBinding = actualStartBinding };
        lock (_gate)
        {
            Publish();
            if (Unknown) throw new UnsupportedSignatureException("Native inline dispatcher is permanently Unknown.");
            if (_sinks.Count == ObserverLimit) throw new InvalidOperationException("Native inline logical observer capacity exhausted.");
            if (!actualStartBinding.TryClaimInline(_start))
                throw new InvalidOperationException("An inline logical binding cannot replace, duplicate or adopt a Start already in progress.");
            _sinks.Add(sink); Publish();
        }
        return new(() => { lock (_gate) { sink.Active = false; Publish(); } });
    }
    private void Publish()
    {
        _sinks.RemoveAll(value => !value.Active && !value.StartBinding.MayHaveCapturedStart);
        Volatile.Write(ref _published, _sinks.ToArray());
    }

    private void Observe(NativeEngineInlineFrame frame)
    {
        Invocation? call = null;
        try
        {
            if (frame.Kind != NativeEngineInlineKind.StartEntered) _calls.TryGetValue(frame.StartSequence, out call);
            if (_bridge.Identity != Identity || !frame.IsValid || frame.ProbeSlot != Identity.Slot || frame.Epoch != Identity.Epoch
                || frame.StartSlot != Identity.Start.Slot || frame.ThreadId != Identity.MainThreadId
                || frame.ThreadId != NativeEngineInlinePlatform.CurrentOsThreadId || frame.EventSequence <= _lastEvent
                || (frame.Flags & NativeEngineInlineFlags.ParentMatched) == 0)
                throw new InvalidOperationException("Native inline event crossed its exact ABI, family, sequence or physical thread.");
            _lastEvent = frame.EventSequence;
            if (frame.Kind == NativeEngineInlineKind.StartEntered)
            {
                if (Unknown || frame.HasFault || _calls.ContainsKey(frame.StartSequence) || _calls.Count == InvocationLimit)
                    throw new InvalidOperationException("Native inline Start responsibility is duplicated, Unknown or exceeds capacity.");
                var proof = _start.GetCurrentStartProof(frame);
                // Membership was frozen at the earlier actual typed Enter.
                // A detach/new Bind in a typed capture cannot change which
                // logical consumer owns this same physical Start's side duty.
                call = new(this, proof, frame, Volatile.Read(ref _published).Where(sink => sink.StartBinding.WasCapturedBy(proof)).ToArray());
                if (!_calls.TryAdd(frame.StartSequence, call)) throw new InvalidOperationException("Native inline Start token collision.");
                _top = call; Interlocked.Increment(ref _inFlight);
                if (!HasNormalShape(frame, call))
                    throw new InvalidOperationException("Native inline StartEntered differs from its actual argument-only shape.");
                Deliver(call, frame, receipt => {
                    foreach (var sink in call.Sinks)
                        try
                        {
                            var context = sink.Capture(receipt);
                            if (context is not null)
                                try { call.Contexts.Add((sink, context)); }
                                catch { context.Dispose(); throw; }
                        }
                        catch (Exception error) { Fault(call.Sinks, frame, "Native inline Start capture failed.", error); }
                });
                return;
            }
            if (call is null || !ReferenceEquals(call.Owner, this) || !ReferenceEquals(_top, call)
                || call.Thread != Environment.CurrentManagedThreadId || !call.Proof.IsCurrent(frame)
                || call.Entered.Depth != frame.Depth || call.Entered.ParentStartSequence != frame.ParentStartSequence)
                throw new InvalidOperationException("Native inline event has no exact retained actual Start context.");
            if (!frame.HasFault && !HasNormalShape(frame, call))
                throw new InvalidOperationException("Native inline event has a different native scalar or completion shape.");
            if (frame.Kind is NativeEngineInlineKind.Begin or NativeEngineInlineKind.EndFree or NativeEngineInlineKind.EndRetained)
            {
                if (!Identity.MatchesSite(frame)) throw new InvalidOperationException("Native inline event came from a different proved instruction/relay return pair.");
                if (frame.Kind == NativeEngineInlineKind.Begin)
                { if (call.Began || call.Ended) throw new InvalidOperationException("Native inline Begin was repeated."); call.Began = true; }
                else
                { if (!call.Began || call.Ended) throw new InvalidOperationException("Native inline End has no unique Begin."); call.Ended = true; }
            }
            if (frame.HasFault || frame.Kind == NativeEngineInlineKind.StartExited && call.Began != call.Ended)
                Fault(call.Sinks, frame, "Native inline Start has an unconfirmed branch, missing End or native fault.");
            Deliver(call, frame, receipt => {
                foreach (var item in call.Contexts)
                    try { item.Sink.Observe(item.Context, receipt); }
                    catch (Exception error) { Fault(call.Sinks, frame, "Native inline context observation failed.", error); }
            });
            if (frame.Kind == NativeEngineInlineKind.StartExited) Finish(call, frame);
        }
        catch (Exception error)
        {
            Fault(call?.Sinks ?? Volatile.Read(ref _published), frame, "Native inline physical observation failed; responsibility remains.", error);
            // A malformed exit can still dispose the frozen logical context
            // when its scalar identity names the actual retained physical top.
            // No cleanup proof is issued: its fault is sticky before Dispose.
            if (frame.Kind == NativeEngineInlineKind.StartExited && call is not null
                && ReferenceEquals(_top, call) && call.Thread == Environment.CurrentManagedThreadId && call.Proof.IsCurrent(frame))
                Finish(call, frame);
        }
    }
    private bool HasNormalShape(NativeEngineInlineFrame frame, Invocation call)
    {
        if (frame.OutputSlot != call.Proof.Frame.Arg3 || frame.Owner != call.Proof.Frame.Arg0
            || frame.EnumeratorCache != call.Proof.Frame.Arg1) return false;
        var parent = NativeEngineInlineFlags.ParentMatched;
        var snapshot = NativeEngineInlineFlags.SnapshotRequested | NativeEngineInlineFlags.SnapshotValid;
        var output = NativeEngineInlineFlags.OutputRequested | NativeEngineInlineFlags.OutputRead;
        if (frame.Kind == NativeEngineInlineKind.StartEntered)
            return frame.Flags == parent && frame.Node == IntPtr.Zero && frame.EnumeratorHandle == 0
                && frame.EnumeratorType == 0 && frame.ReferenceCount == 0 && frame.SitePc == Identity.Start.Target
                && frame.RelayReturnPc == IntPtr.Zero && frame.StartExitFlags == 0 && frame.RawStartReturn == 0 && frame.StartOutputNode == IntPtr.Zero;
        if (frame.Kind == NativeEngineInlineKind.StartExited)
            return frame.Flags == (parent | output | (call.Began ? snapshot : 0))
                && frame.SitePc == Identity.Start.Target && frame.RelayReturnPc == IntPtr.Zero
                && frame.StartExitFlags == (NativeEngineNodeExitFlags.Returned | NativeEngineNodeExitFlags.ReturnByteValid)
                && (call.Began || frame.Node == IntPtr.Zero && frame.EnumeratorHandle == 0 && frame.EnumeratorType == 0 && frame.ReferenceCount == 0);
        return frame.Flags == (parent | snapshot) && frame.StartExitFlags == 0 && frame.StartOutputNode == IntPtr.Zero
            && (frame.Kind != NativeEngineInlineKind.Begin || frame.RawStartReturn == 0);
    }
    private void Deliver(Invocation call, NativeEngineInlineFrame frame, Action<Observation> deliver)
    {
        var before = _currentObservation;
        var receipt = new Observation(this, _receiptIssuer, call.Proof, frame);
        _currentObservation = receipt;
        try { deliver(receipt); }
        finally { _currentObservation = before; }
    }
    private void Finish(Invocation call, NativeEngineInlineFrame frame)
    {
        try
        {
            for (var index = call.Contexts.Count - 1; index >= 0; --index)
                try { call.Contexts[index].Context.Dispose(); }
                catch (Exception error) { Fault(call.Sinks, frame, "Native inline captured context Dispose failed.", error); }
        }
        finally { _calls.TryRemove(call.Entered.StartSequence, out _); _top = call.Parent; Interlocked.Decrement(ref _inFlight); }
    }
    private void Fault(Sink[] sinks, NativeEngineInlineFrame frame, string reason, Exception? error = null)
    {
        Interlocked.Exchange(ref _unknown, 1);
        if (error is not null) try { _log.Log(LogLevel.Error, reason, error); } catch { }
        foreach (var sink in sinks) try { sink.Fault(frame, reason); } catch { }
    }
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        var retained = Math.Max(0, Volatile.Read(ref _inFlight));
        try
        {
            var activity = _bridge.ReadActivity();
            if (!activity.Known || activity.InFlight < 0) Interlocked.Exchange(ref _unknown, 1);
            return new(Math.Max(retained, Math.Max(0, activity.InFlight)), activity.Known && !Unknown);
        }
        catch { Interlocked.Exchange(ref _unknown, 1); return new(retained, false); }
    }
}
