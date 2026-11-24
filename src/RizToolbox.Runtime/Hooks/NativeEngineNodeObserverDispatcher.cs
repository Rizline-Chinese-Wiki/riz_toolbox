using System.Collections.Concurrent;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Observation-only fan-out for already configured native engine
/// channels. It has no detour engine, MethodInfo, cancellation receipt, root
/// release or game Ready authority. Each physical invocation owns one immutable
/// observer snapshot and its captured cleanup duties, including after detach.</summary>
internal sealed class NativeEngineNodeObserverDispatcher
{
    private const int InvocationLimit = 4096, ObserverLimit = 32;
    private static readonly object OwnersGate = new();
    private static readonly List<NativeEngineNodeObserverDispatcher> Owners = new();
    [ThreadStatic] private static Invocation? _top;
    private sealed class Sink
    {
        internal Func<NativeEngineNodeFrame, IDisposable?> Capture = null!;
        internal Action<IDisposable, NativeEngineNodeFrame, NativeEngineNodeCompletion> Complete = null!;
        internal Action<NativeEngineNodeFrame, string> Fault = null!;
        internal Func<bool>? Admission;
        internal bool Active = true;
    }
    private sealed class Invocation
    {
        internal readonly NativeEngineNodeObserverDispatcher Owner;
        internal readonly NativeEngineNodeFrame Frame;
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal readonly Invocation? Parent = _top;
        internal readonly Sink[] Observers;
        internal readonly List<(Sink Sink, IDisposable Context)> Contexts = new();
        internal StartScopeProof? StartProof;
        internal bool Closing;
        internal Invocation(NativeEngineNodeObserverDispatcher owner, NativeEngineNodeFrame frame, Sink[] observers)
            => (Owner, Frame, Observers) = (owner, frame, observers);
    }

    // Issued only for the actual current physical invocation retained by this
    // dispatcher. It is not a scalar frame or a caller-created completion key.
    internal sealed class StartScopeProof
    {
        private readonly NativeEngineNodeObserverDispatcher _owner;
        private readonly Invocation _call;
        private StartScopeProof(NativeEngineNodeObserverDispatcher owner, Invocation call)
            => (_owner, _call) = (owner, call);
        internal NativeEngineNodeFrame Frame => _call.Frame;
        internal NativeEngineNodeChannelIdentity Identity => _owner.Identity;
        internal bool IsCurrent(NativeEngineInlineFrame frame)
        {
            var call = _call;
            if (_owner.Unknown || !ReferenceEquals(call.StartProof, this)
                || !ReferenceEquals(_top, call) || !ReferenceEquals(call.Owner, _owner)
                || call.Thread != Environment.CurrentManagedThreadId || !call.Frame.IsValid
                || call.Frame.Kind != NativeEngineNodeKind.Start || Identity.Kind != NativeEngineNodeKind.Start
                || frame.StartSlot != Identity.Slot || frame.StartSequence != call.Frame.Sequence
                || frame.ThreadId != NativeEngineInlinePlatform.CurrentOsThreadId
                || !_owner._invocations.Values.Any(value => ReferenceEquals(value, call))) return false;
            uint depth = 1; ulong parentSequence = 0;
            for (var at = call.Parent; at is not null; at = at.Parent)
                if (at.Frame.Kind == NativeEngineNodeKind.Start)
                { if (parentSequence == 0) parentSequence = at.Frame.Sequence; if (depth == uint.MaxValue) return false; ++depth; }
            return frame.Depth == depth && frame.ParentStartSequence == parentSequence;
        }
        internal bool IsFor(NativeEngineNodeFrame frame) => SameFrame(_call.Frame, frame);
        internal bool ContainsFrozenSink(NativeEngineNodeObserverDispatcher owner, object sink)
            => ReferenceEquals(_owner, owner) && _call.Frame.Kind == NativeEngineNodeKind.Start
                && _call.Observers.Any(value => ReferenceEquals(value, sink))
                && _owner._invocations.Values.Any(value => ReferenceEquals(value, _call));
        internal static StartScopeProof IssueCurrent(NativeEngineNodeObserverDispatcher owner, NativeEngineInlineFrame frame)
        {
            var call = _top;
            if (call is null || !ReferenceEquals(call.Owner, owner) || call.Frame.Kind != NativeEngineNodeKind.Start)
                throw new InvalidOperationException("Inline observation has no exact current physical Start invocation.");
            var proof = call.StartProof ??= new StartScopeProof(owner, call);
            if (!proof.IsCurrent(frame))
                throw new InvalidOperationException("Inline observation differs from the actual Start channel, parent or thread.");
            return proof;
        }
    }

    internal StartScopeProof GetCurrentStartProof(NativeEngineInlineFrame frame)
    {
        return StartScopeProof.IssueCurrent(this, frame);
    }

    internal sealed class Binding : IDisposable
    {
        private readonly NativeEngineNodeObserverDispatcher _owner;
        private readonly Sink _sink;
        private readonly Action _detach;
        private int _disposed, _inlineClaimed;
        internal Binding(NativeEngineNodeObserverDispatcher owner, object issuer, object sink, Action detach)
        {
            lock (owner._gate)
            {
                if (!ReferenceEquals(issuer, owner._bindingIssuer) || sink is not Sink actual || !owner._sinks.Contains(actual))
                    throw new InvalidOperationException("Engine logical binding does not have its actual private dispatcher issuer and sink.");
                (_owner, _sink, _detach) = (owner, actual, detach);
            }
        }
        internal NativeEngineNodeChannelIdentity Identity => _owner.Identity;
        internal bool IsFor(NativeEngineNodeObserverDispatcher owner) => ReferenceEquals(_owner, owner);
        internal bool TryClaimInline(NativeEngineNodeObserverDispatcher owner)
            => IsFor(owner) && Volatile.Read(ref _disposed) == 0 && !MayHaveCapturedStart
                && Interlocked.CompareExchange(ref _inlineClaimed, 1, 0) == 0;
        internal bool WasCapturedBy(StartScopeProof proof) => proof.ContainsFrozenSink(_owner, _sink);
        internal bool IsCapturedByNearestStart
            => MatchesNearestCapturedStart(null);
        internal bool IsCapturedByNearestOpenStart => MatchesNearestCapturedStart(null, false);
        internal bool MatchesNearestCapturedStart(ulong? sequence, bool includeClosing = true)
        {
                // Called while a new Enter is evaluating admission: _top is
                // still its actual physical parent. Never borrow through the
                // nearest Start mask to an older matching ancestor.
                for (var at = _top; at is not null; at = at.Parent)
                    if (at.Frame.Kind == NativeEngineNodeKind.Start)
                        return !_owner.Unknown && ReferenceEquals(at.Owner, _owner) && at.Frame.IsValid
                            && (sequence is null || at.Frame.Sequence == sequence.Value)
                            && at.Thread == Environment.CurrentManagedThreadId
                            && at.Observers.Any(value => ReferenceEquals(value, _sink))
                            && (_owner._invocations.Values.Any(value => ReferenceEquals(value, at))
                                || includeClosing && at.Closing && _owner._closing.Values.Any(value => ReferenceEquals(value, at)));
                return false;
        }
        internal bool MayHaveCapturedStart => _owner._invocations.Values.Any(call => call.Frame.Kind == NativeEngineNodeKind.Start
            && call.Observers.Any(sink => ReferenceEquals(sink, _sink)));
        internal ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _owner.ReadActivity();
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _detach(); }
    }

    private readonly object _gate = new();
    private readonly object _bindingIssuer = new();
    private readonly INativeEngineNodeContextBridge _bridge;
    private readonly NativeEngineNodeChannelIdentity _identity;
    private readonly ILogger _log;
    private readonly List<Sink> _sinks = new();
    private readonly ConcurrentDictionary<nuint, Invocation> _invocations = new();
    private readonly ConcurrentDictionary<nuint, Invocation> _closing = new();
    private Sink[] _observers = Array.Empty<Sink>();
    private long _sequence, _inFlight;
    private int _unknown;
    internal NativeEngineNodeChannelIdentity Identity => _identity;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0;

    internal NativeEngineNodeObserverDispatcher(INativeEngineNodeContextBridge bridge, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(bridge); ArgumentNullException.ThrowIfNull(log);
        if (!bridge.Identity.IsComplete) throw new ArgumentException("An actual complete configured engine identity is required.", nameof(bridge));
        _bridge = bridge; _identity = bridge.Identity; _log = log;
        lock (OwnersGate) Owners.Add(this); // Callback and physical record lifetime is process-wide, also on Attach failure.
        try { bridge.Attach(Enter, Exit); }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }

    internal static NativeEngineNodeObserverDispatcher AdoptConfigured(string helperPath,
        NativeEngineNodeChannelIdentity identity, ILogger log)
        => new(NativeEngineNodeContextBridge.AdoptConfigured(helperPath, identity, log), log);

    internal static NativeEngineNodeObserverDispatcher AdoptPublished(NativeEngineFirstInstall owner,
        NativeEngineNodeKind kind, ILogger log)
        => new(NativeEngineNodeContextBridge.AdoptPublished(owner, kind, log), log);

    internal static NativeEngineNodeObserverDispatcher AdoptPublished(NativeEngineTransportFirstInstall owner,
        NativeEngineNodeKind kind, ILogger log)
        => new(NativeEngineNodeContextBridge.AdoptPublished(owner, kind, log), log);

    internal Binding Bind<T>(Func<NativeEngineNodeFrame, T?> capture,
        Action<T, NativeEngineNodeFrame, NativeEngineNodeCompletion> complete,
        Action<NativeEngineNodeFrame, string> fault, Func<bool>? admission = null) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(complete); ArgumentNullException.ThrowIfNull(fault);
        var sink = new Sink { Capture = frame => capture(frame), Complete = (context, frame, completion) => complete((T)context, frame, completion), Fault = fault,
            Admission = admission };
        lock (_gate)
        {
            if (_sinks.Count >= ObserverLimit) throw new InvalidOperationException("Engine node observer capacity exhausted.");
            _sinks.Add(sink); Publish();
        }
        return new(this, _bindingIssuer, sink, () => { lock (_gate) { sink.Active = false; _sinks.Remove(sink); Publish(); } });
    }

    private void Publish() => Volatile.Write(ref _observers, _sinks.Where(sink => sink.Active).ToArray());

    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        var retained = Math.Max(0, Volatile.Read(ref _inFlight));
        try
        {
            var activity = _bridge.ReadActivity();
            if (activity.InFlight < 0 || !activity.Known) Interlocked.Exchange(ref _unknown, 1);
            return new(Math.Max(retained, Math.Max(0, activity.InFlight)), activity.Known && !Unknown);
        }
        catch (Exception error)
        { FaultLog("Engine node native activity failed.", error); return new(retained, false); }
    }

    private nuint Enter(NativeEngineNodeFrame frame)
    {
        var observers = Volatile.Read(ref _observers);
        var counted = false; var registered = false; Invocation? call = null; nuint token = 0;
        try
        {
            // Freeze every admission before any capture callback can detach a
            // peer, activate a partial subscription or Bind another consumer.
            var admitted = new List<Sink>(observers.Length);
            foreach (var sink in observers)
                try { if (sink.Admission?.Invoke() != false) admitted.Add(sink); }
                catch (Exception error)
                { FaultLog("Engine node logical admission failed.", error); Faults(new[] { sink }, frame, "Engine node logical admission failed."); }
            observers = admitted.ToArray();
            counted = true;
            if (Interlocked.Increment(ref _inFlight) > InvocationLimit)
                throw new InvalidOperationException("Engine node physical record capacity exhausted.");
            var sequence = Interlocked.Increment(ref _sequence);
            if (sequence <= 0) throw new InvalidOperationException("Engine node managed sequence exhausted.");
            token = checked((nuint)sequence); call = new(this, frame, observers);
            if (!_invocations.TryAdd(token, call)) throw new InvalidOperationException("Engine node invocation token collision.");
            registered = true; _top = call;
            if (!frame.IsValid || frame.Kind != Identity.Kind)
            { Faults(observers, frame, "Engine node Enter differs from its exact native frame."); return token; }
            if (Unknown)
            { Faults(observers, frame, "Engine node observation remains Unknown."); return token; }
            // Membership is frozen at physical Enter. A peer detach or new Bind
            // cannot change this invocation's observer or cleanup membership.
            foreach (var sink in observers)
            {
                try
                {
                    var context = sink.Capture(frame);
                    if (context is not null)
                        try { call.Contexts.Add((sink, context)); }
                        catch { context.Dispose(); throw; }
                }
                catch (Exception error)
                { FaultLog("Engine node capture failed; original is preserved.", error); Faults(new[] { sink }, frame, "Engine node capture failed."); }
            }
            if ((frame.SnapshotFlags & NativeEngineNodeSnapshotFlags.Faulted) != 0)
                Faults(observers, frame, "Native engine node snapshot could not be read.");
            return token;
        }
        catch (Exception error)
        {
            FaultLog("Engine node Enter observation failed; original is preserved.", error);
            Faults(observers, frame, "Engine node physical entry could not be recorded.");
            if (registered) return token;
            // No captured context exists without a registered record. Unknown
            // remains permanent even after native physical activity drains.
            if (counted) Interlocked.Decrement(ref _inFlight);
            return 0;
        }
    }

    private void Exit(NativeEngineNodeFrame frame, nuint token, NativeEngineNodeCompletion completion)
    {
        if (token == 0 || !_invocations.TryGetValue(token, out var call))
        { Faults(Volatile.Read(ref _observers), frame, "Engine node completion has no exact invocation token."); return; }
        if (call.Thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_top, call)
            || !ReferenceEquals(call.Owner, this) || !SameFrame(call.Frame, frame))
        { Faults(call.Observers, frame, "Engine node completion crossed its exact frame, thread or physical parent."); return; }
        if (!_invocations.TryRemove(token, out _))
        { Faults(call.Observers, frame, "Engine node completion token was already consumed."); return; }
        call.Closing = true;
        if (!_closing.TryAdd(token, call))
        {
            call.Closing = false;
            Faults(call.Observers, frame, "Engine node closing responsibility token was already present.");
            return;
        }
        var valid = completion.IsValidFor(Identity.Kind);
        if (!valid) Faults(call.Observers, frame, "Engine node completion differs from its exact native result shape.");
        if ((completion.Flags & (NativeEngineNodeExitFlags.Abnormal | NativeEngineNodeExitFlags.EnterFaulted)) != 0)
            Faults(call.Observers, frame, "Engine node original or native observer exited abnormally.");
        if ((completion.OutputFlags & NativeEngineNodeOutputFlags.Faulted) != 0)
            Faults(call.Observers, frame, "Native engine Start output slot could not be read.");
        try
        {
            // Captured scopes retain completion duty after logical detach,
            // another sink failure or a native original exception.
            foreach (var item in call.Contexts)
                try { item.Sink.Complete(item.Context, frame, completion); }
                catch (Exception error)
                { FaultLog("Engine node completion observer failed.", error); Faults(new[] { item.Sink }, frame, "Engine node completion observer failed."); }
        }
        finally
        {
            for (var index = call.Contexts.Count - 1; index >= 0; --index)
                try { call.Contexts[index].Context.Dispose(); }
                catch (Exception error)
                { FaultLog("Engine node captured context cleanup failed.", error); Faults(new[] { call.Contexts[index].Sink }, frame, "Engine node captured context cleanup failed."); }
            _top = call.Parent; Interlocked.Decrement(ref _inFlight);
            call.Closing = false; _closing.TryRemove(token, out _);
        }
    }

    private void Faults(Sink[] observers, NativeEngineNodeFrame frame, string reason)
    {
        Interlocked.Exchange(ref _unknown, 1);
        foreach (var sink in observers)
            try { sink.Fault(frame, reason); }
            catch (Exception error) { FaultLog("Engine node fault observer failed.", error); }
    }
    private void FaultLog(string message, Exception error)
    { Interlocked.Exchange(ref _unknown, 1); try { _log.Log(LogLevel.Error, message, error); } catch { } }

    internal static bool SameFrame(NativeEngineNodeFrame left, NativeEngineNodeFrame right)
        => left.Version == right.Version && left.Size == right.Size && left.Kind == right.Kind && left.SnapshotFlags == right.SnapshotFlags
            && left.Arg0 == right.Arg0 && left.Arg1 == right.Arg1 && left.Arg2 == right.Arg2 && left.Arg3 == right.Arg3
            && left.Caller == right.Caller && left.Sequence == right.Sequence && left.Node == right.Node && left.Owner == right.Owner
            && left.EnumeratorHandle == right.EnumeratorHandle && left.EnumeratorCache == right.EnumeratorCache
            && left.EnumeratorType == right.EnumeratorType && left.ReferenceCount == right.ReferenceCount
            && left.Reserved0 == right.Reserved0 && left.Reserved1 == right.Reserved1;
}
