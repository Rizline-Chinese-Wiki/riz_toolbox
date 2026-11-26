namespace RizToolbox.Runtime.Hooks;

/// <summary>Execution evidence only. It owns no run, roots, rate writer reservation,
/// clock publication or production admission. S3 must combine these exact receipts
/// with NativePlaybackActualSource's seven roots/readback and its own native reservation.
/// Only the real group callbacks create occurrences; there is no Begin/End API.</summary>
internal sealed partial class NativePlaybackStockOccurrenceProducer : IDisposable
{
    private readonly PlaybackSourceGroupLease _group;
    private readonly IntPtr _player, _source, _progress;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly object _issuer = new();
    private readonly Action<ExecutionReceipt>? _returned;
    private readonly Dictionary<NativePlaybackSourceObservation, Call> _calls = new();
    private readonly List<Call> _responsibilities = new();
    private Occurrence? _active;
    private ExecutionReceipt? _issued;
    private RejectedNoEffectReturn? _rejectedNoEffectReturn;
    private NativePlaybackSourceObservation? _resumeCountdown;
    private string? _unknown;
    private bool _disposed;
    internal string? LastRejection { get; private set; }
    internal string? LogicalFault { get { VerifyThread(); return _unknown; } }
    internal bool ProductionAdmission => false;
    internal void WithdrawLogical(string reason) { VerifyThread(); Fail(reason); }
    internal bool OwnResponsibilitiesDrained
    { get { VerifyThread(); PumpCompleted(); return _responsibilities.Count == 0 && _writerResponsibilities.Count == 0; } }

    internal ExecutionReceipt? Current { get { VerifyThread(); _issued?.VerifyCurrent(); return _issued; } }

    internal NativePlaybackStockOccurrenceProducer(PlaybackSourceGroupLease group, IntPtr player,
        IntPtr audioSource, IntPtr progress, Action<ExecutionReceipt>? returned = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        var callers = group.CaptureStockOccurrenceCallProfile();
        callers.RequireComplete(group.HasDirectAudioControls);
        if (player == IntPtr.Zero || audioSource == IntPtr.Zero || progress == IntPtr.Zero
            || new[] { player, audioSource, progress }.Distinct().Count() != 3)
            throw new UnsupportedSignatureException("Stock execution needs distinct exact player/source/progress receivers.");
        (_group, _player, _source, _progress, _returned) = (group, player, audioSource, progress, returned);
    }
    private sealed class Call
    {
        internal readonly NativePlaybackSourceObservation Before;
        internal NativePlaybackSourceCompletion? Returned;
        internal readonly List<Call> PhysicalChildren = new();
        internal Call? PhysicalParent;
        internal bool Contains(NativePlaybackSourceObservation observation) => ReferenceEquals(Before, observation)
            || PhysicalChildren.Any(value => value.Contains(observation));
        internal Call(NativePlaybackSourceObservation before) => Before = before;
    }
    private sealed class Occurrence
    {
        internal readonly Call Root;
        internal readonly List<Call> Children = new();
        internal string? Rejection;
        internal Occurrence(Call root) => Root = root;
    }
    // Negative dispatch evidence only. This private identity cannot stand in
    // for a leaf, execution receipt, source ticket, readback or Clock baseline.
    private sealed class RejectedNoEffectReturn
    {
        internal readonly Call Root;
        internal readonly NativePlaybackSourceCompletion Completion;
        internal readonly string Rejection;
        internal RejectedNoEffectReturn(Call root, NativePlaybackSourceCompletion completion, string rejection)
            => (Root, Completion, Rejection) = (root, completion, rejection);
    }
    internal void ObserveBefore(NativePlaybackSourceObservation observation)
    {
        VerifyThread(); _group.VerifyIssuedOperationBefore(observation);
        if (_calls.ContainsKey(observation)) throw new UnsupportedSignatureException("A stock Before cannot be delivered twice.");
        if (_disposed) { observation.DisposeOperation(); return; }
        _rejectedNoEffectReturn = null;
        _resumeCountdown = null;
        var call = new Call(observation); _calls.Add(observation, call); _responsibilities.Add(call);
        // A retired logical source still accepts its actual physical duties.
        // It cannot associate a new child with a semantic tree already draining.
        if (_unknown is not null) return;
        if (observation.IsExactLowerPitchChild) { RegisterPhysicalLowerPitchChild(call); return; }
        if (observation.IsExactEnginePitchChild) { RegisterPhysicalEnginePitchChild(call); return; }
        if (observation.IsExactLowerTimeChild) { RegisterPhysicalLowerTimeChild(call); return; }
        if (_pendingWriter is not null && _pendingWriter.TryObserveBefore(observation))
        { _ownedWriterCalls.Add(observation); return; }
        if (observation.ParentSequence == 0)
        {
            _writerCurrent?.ReleaseFor(this, _writerIssuer);
            _issued = null; _writerCurrent = null; LastRejection = null;
            if (_active is not null) { Fail("A second stock root entered before its actual parent returned."); return; }
            // An independent SetTime attempt ends the old receipt even for an
            // equal value or a store followed by an exception. It is not Seek.
            if (!IsRoot(observation.Role))
            { Fail("An unreserved external pitch/source/schedule/pause leaf ended stock execution authority."); return; }
            _active = new(call);
            if (observation.Depth != 1 || observation.Self != (observation.Role == NativePlaybackSourceRole.ProgressPlay ? _progress : _player))
                _active.Rejection = "The actual root has another receiver, thread depth or physical parent.";
            return;
        }
        var parent = _active;
        if (parent is null) { Fail("A leaf has no current actual stock parent."); return; }
        parent.Children.Add(call);
        var root = parent.Root.Before;
        if (observation.ParentSequence != root.Sequence || observation.ThreadId != root.ThreadId
            || observation.OwnerEpoch != root.OwnerEpoch || observation.Depth != root.Depth + 1 || observation.Self != _source)
        { parent.Rejection = "The leaf has a different exact source, physical parent, thread or depth."; return; }
        var allowed = root.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay
            ? observation.Role == NativePlaybackSourceRole.PauseLeaf
            : observation.Role is NativePlaybackSourceRole.SetPitch or NativePlaybackSourceRole.PlayScheduled
                || _group.HasDirectAudioControls && observation.IsExactStockStop
                || root.Role == NativePlaybackSourceRole.Resume && observation.Role == NativePlaybackSourceRole.SetTime;
        if (!allowed || observation.IncomingMethod != IntPtr.Zero
            || observation.Caller != _group.StockOperationLeafCaller(root.Role, observation.Role))
        { parent.Rejection = "The leaf did not enter from its exact pinned native stock CALL."; return; }
        if (observation.Role == NativePlaybackSourceRole.SetPitch && observation.ArgBits != 0x3f800000
            || observation.Role == NativePlaybackSourceRole.PlayScheduled && (!double.IsFinite(observation.DoubleArgument) || observation.DoubleArgument < 0))
            parent.Rejection = "The actual reset pitch or unclamped schedule argument is unsupported.";
    }
    internal void ObserveReturned(NativePlaybackSourceCompletion completion)
    {
        VerifyThread(); _group.VerifyIssuedOperationReturn(completion);
        if (_disposed) return;
        if (!_calls.TryGetValue(completion.Observation, out var call) || call.Returned is not null)
            throw new UnsupportedSignatureException("A stock return has no unique retained actual Before.");
        call.Returned = completion;
        // Terminal Pump may have requested a child's release before this
        // parent returned. Retired execution cannot validate or issue a new
        // semantic receipt from that draining subtree.
        if (_unknown is not null) return;
        VerifyPhysicalLowerPitchChildren(call);
        VerifyPhysicalEnginePitchChildren(call);
        VerifyPhysicalLowerTimeChildren(call);
        if (call.PhysicalParent is not null) return;
        if (_ownedWriterCalls.Contains(completion.Observation))
        {
            if (_pendingWriter is null || !_pendingWriter.TryObserveReturned(completion))
                throw new UnsupportedSignatureException("An owned writer return lost its exact native current reservation.");
            return;
        }
        var active = _active;
        if (active is null) return;
        if (completion.Abnormal || !completion.Returned)
            active.Rejection = "An actual original did not return normally.";
        if (!ReferenceEquals(active.Root, call)) return;
        _active = null;
        // Resume can issue a new schedule receipt only through these exact
        // physical children, including its pinned SetTime CALL before pitch.
        var expected = call.Before.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay
            ? new[] { NativePlaybackSourceRole.PauseLeaf }
            : call.Before.Role == NativePlaybackSourceRole.Resume
                ? new[] { NativePlaybackSourceRole.SetTime, NativePlaybackSourceRole.SetPitch, NativePlaybackSourceRole.PlayScheduled }
                : new[] { NativePlaybackSourceRole.SetPitch, NativePlaybackSourceRole.PlayScheduled };
        if (_group.HasDirectAudioControls && call.Before.Role is NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume)
            expected = new[] { NativePlaybackSourceRole.AudioStop }.Concat(expected).ToArray();
        // Capture eligibility before the missing-leaf rejection is attached.
        // A wrong root, abnormal return, unexpected child or any prior rejection
        // must never be reclassified as an ordinary empty return.
        var noEffectReturn = active.Rejection is null && active.Children.Count == 0 && call.PhysicalChildren.Count == 0
            && call.PhysicalParent is null && completion is { Returned: true, Abnormal: false, Unknown: false }
            && call.Before.ParentSequence == 0 && call.Before.Depth == 1 && IsRoot(call.Before.Role)
            && call.Before.Self == (call.Before.Role == NativePlaybackSourceRole.ProgressPlay ? _progress : _player);
        if (!active.Children.Select(value => value.Before.Role).SequenceEqual(expected))
        {
            // Keep a bounded description of the actual rejected occurrence.
            // A paused Boolean cannot explain whether the native root was a
            // no-op, a different operation, or a missing physical child.
            var root = call.Before;
            var children = string.Join(",", active.Children.Take(8).Select(value =>
                $"{value.Before.Role}/depth={value.Before.Depth}/parent={value.Before.ParentSequence}/callerRva=0x{value.Before.Caller.ToInt64() - _group.Module.ToInt64():x}"));
            active.Rejection = "The original returned without exactly one expected leaf sequence (no-op, missing or duplicate leaf). "
                + $"rootRole={root.Role}; argBits=0x{root.ArgBits:x}; sequence={root.Sequence}; depth={root.Depth}; "
                + $"callerRva=0x{root.Caller.ToInt64() - _group.Module.ToInt64():x}; expected=[{string.Join(",", expected)}]; "
                + $"childCount={active.Children.Count}; actual=[{children}]; priorRejection={active.Rejection ?? "none"}.";
        }
        foreach (var child in active.Children)
            if (child.Returned is not { Returned: true, Abnormal: false }
                || !child.Before.TryReadOperationCompletion(out var physical) || physical is not { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false })
                active.Rejection = "A nested leaf lacks its own normal return and actual physical exit.";
        var health = _group.ReadSourceOperationHealth();
        if (health.WriterSequence != call.Before.WriterSequence + (ulong)active.Children.Count(value => value.Before.Role == NativePlaybackSourceRole.SetPitch)
            || health.SourceSequence != call.Before.SourceSequence
            || health.SchedulingSequence != call.Before.SchedulingSequence + (ulong)active.Children.Count(value => value.Before.Role != NativePlaybackSourceRole.SetPitch))
            active.Rejection = "The actual source fences include an unpaired writer/source/scheduling attempt.";
        if (_unknown is not null) active.Rejection = _unknown;
        if (active.Rejection is not null)
        {
            LastRejection = active.Rejection;
            if (noEffectReturn && !_disposed && _unknown is null && _issued is null
                && _pendingWriter is null && _writerCurrent is null
                && health is { Known: true, Armed: true, Unknown: false, NativeFaults: 0 }
                && health.WriterSequence == call.Before.WriterSequence && health.SourceSequence == call.Before.SourceSequence
                && health.SchedulingSequence == call.Before.SchedulingSequence)
                _rejectedNoEffectReturn = new(call, completion, active.Rejection);
            return;
        }
        _issued = ExecutionReceipt.Issue(this, active, _issuer);
        _returned?.Invoke(_issued);
    }
    private static bool IsRoot(NativePlaybackSourceRole role) => role is NativePlaybackSourceRole.Pause
        or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume or NativePlaybackSourceRole.ProgressPlay;
    internal bool IsRejectedNoEffectReturn(NativePlaybackSourceCompletion completion)
    {
        VerifyThread();
        var rejected = _rejectedNoEffectReturn;
        if (_disposed || _unknown is not null || _active is not null || _issued is not null
            || _pendingWriter is not null || _writerCurrent is not null || rejected is null
            || !ReferenceEquals(rejected.Completion, completion) || !ReferenceEquals(LastRejection, rejected.Rejection)
            || !_calls.TryGetValue(completion.Observation, out var call) || !ReferenceEquals(call, rejected.Root)
            || !ReferenceEquals(call.Returned, completion) || call.Before.IsReleased
            || call.PhysicalParent is not null || call.PhysicalChildren.Count != 0)
            return false;
        // This selection is valid only in that channel's actual current return
        // callback. A retained journal projection or copied completion cannot
        // carry it past the physical call, nor can it clear any responsibility.
        _group.VerifyIssuedOperationReturn(completion);
        var health = _group.ReadSourceOperationHealth();
        return health is { Known: true, Armed: true, Unknown: false, NativeFaults: 0 }
            && health.WriterSequence == call.Before.WriterSequence && health.SourceSequence == call.Before.SourceSequence
            && health.SchedulingSequence == call.Before.SchedulingSequence;
    }

    internal bool IsStockResumeCountdown(NativePlaybackSourceObservation observation)
        => observation is { Role: NativePlaybackSourceRole.ProgressPlay, Kind: NativePlaybackSourceKind.Boolean1,
                ParentSequence: 0, Depth: 1, ArgBits: 1 }
            && observation.Self == _progress && observation.IncomingMethod == IntPtr.Zero
            && _group.IsStockResumeCountdownCaller(observation.Caller);

    internal bool TryRetainResumeCountdown(NativePlaybackSourceCompletion completion)
    {
        if (!IsRejectedNoEffectReturn(completion) || !IsStockResumeCountdown(completion.Observation)) return false;
        _resumeCountdown = completion.Observation;
        return true;
    }

    internal void VerifyResumeCountdown(NativePlaybackSourceObservation observation)
    {
        VerifyThread();
        if (_disposed || _unknown is not null || !ReferenceEquals(_resumeCountdown, observation))
            throw new UnsupportedSignatureException("The exact paused resume countdown ended or was superseded.");
        _group.VerifySourceOperationRole(NativePlaybackSourceRole.ProgressPlay);
        if (!observation.TryReadOperationCompletion(out var exit)
            || exit is not { Returned: true, Abnormal: false, Unknown: false, PhysicalExited: true }
            || exit.WriterSequence != observation.WriterSequence || exit.SourceSequence != observation.SourceSequence
            || exit.SchedulingSequence != observation.SchedulingSequence)
            throw new UnsupportedSignatureException("The resume countdown has no normal physical exit on its exact source fences.");
        exit.VerifyOperationAuthority();
        var health = _group.ReadSourceOperationHealth();
        if (health is not { Known: true, Armed: true, Unknown: false, InFlight: 0, NativeFaults: 0 }
            || health.WriterSequence != observation.WriterSequence || health.SourceSequence != observation.SourceSequence
            || health.SchedulingSequence != observation.SchedulingSequence)
            throw new UnsupportedSignatureException("The actual source fences advanced after the resume countdown.");
    }

    private void Fail(string reason)
    {
        _unknown ??= reason; LastRejection = reason; _issued = null; _rejectedNoEffectReturn = null; _resumeCountdown = null;
        if (_active is not null) _active.Rejection = reason;
    }
    private void VerifyThread()
    { if (_thread != Environment.CurrentManagedThreadId) throw new UnsupportedSignatureException("Stock execution consumption is owner-thread bound."); }
    private void Verify(ExecutionReceipt receipt, Occurrence occurrence)
    {
        VerifyThread();
        if (_disposed || _unknown is not null || !ReferenceEquals(receipt, _issued) || occurrence.Rejection is not null)
            throw new UnsupportedSignatureException("The private stock execution receipt was superseded, withdrawn or rejected.");
        _group.VerifySourceOperationOwner();
        var health = _group.ReadSourceOperationHealth();
        if (health.WriterSequence != receipt.WriterSequence || health.SourceSequence != receipt.SourceSequence
            || health.SchedulingSequence != receipt.SchedulingSequence)
            throw new UnsupportedSignatureException("The actual continuous source fences advanced after this stock execution.");
        VerifyRoot(receipt, occurrence);
    }
    private void VerifyRoot(ExecutionReceipt receipt, Occurrence occurrence)
    {
        VerifyThread();
        if (_disposed || _unknown is not null || !ReferenceEquals(receipt, _issued) || occurrence.Rejection is not null)
            throw new UnsupportedSignatureException("The writer root belongs to an ended, rejected or superseded logical occurrence.");
        _group.VerifySourceOperationRole(occurrence.Root.Before.Role);
        var before = occurrence.Root.Before;
        if (before.TryReadOperationCompletion(out var physical))
        {
            if (physical is not { Returned: true, Abnormal: false, Unknown: false, PhysicalExited: true })
                throw new UnsupportedSignatureException("The original has no normal actual physical-exit responsibility.");
            physical.VerifyOperationAuthority();
        }
        else occurrence.Root.Returned!.VerifyOperationAuthority();
        foreach (var child in occurrence.Children)
        {
            _group.VerifySourceOperationRole(child.Before.Role);
            if (!child.Before.TryReadOperationCompletion(out var exit) || exit is not { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false })
                throw new UnsupportedSignatureException("The actual nested leaf completion changed.");
            exit.VerifyOperationAuthority();
            VerifyPhysicalLowerPitchChildren(child);
            VerifyPhysicalEnginePitchChildren(child);
            VerifyPhysicalLowerTimeChildren(child);
        }
    }
    /// <summary>Only releases after the exact native finally journal proves exit.
    /// It never invents End or converts a scalar zero-in-flight into completion.</summary>
    internal void PumpCompleted()
    {
        VerifyThread();
        // Native can fault before issuing the managed Returned callback. That
        // ends semantic retention, but never supplies a physical-exit receipt.
        _group.PollNativeFaults();
        var terminal = _disposed || _unknown is not null || !_group.IsActive;
        if (terminal)
        {
            if (!_disposed && _unknown is null)
                Fail("The physical group ended; only actual physical duties remain.");
            _active = null;
            _writerCurrent = null;
            _rejectedNoEffectReturn = null;
            _resumeCountdown = null;
        }
        foreach (var writer in _writerResponsibilities.ToArray())
        {
            // Keep the registered reservation until native proves it can be
            // released. Do not clear an in-flight Run or its finally owner.
            if (terminal) writer.Dispose();
            writer.PumpRelease();
            if (writer.Released) _writerResponsibilities.Remove(writer);
        }
        foreach (var call in _responsibilities.ToArray())
        {
            if (!terminal && RetainsPhysicalCall(call)) continue;
            call.Before.Dispose();
            if (call.Before.IsReleased)
            {
                if (ReferenceEquals(_rejectedNoEffectReturn?.Root, call)) _rejectedNoEffectReturn = null;
                _calls.Remove(call.Before); _ownedWriterCalls.Remove(call.Before); _responsibilities.Remove(call);
            }
        }
        _group.PumpCompleted();
    }
    internal void PumpOperationCompleted()
    {
        VerifyThread(); _group.PollOperationNativeFaults();
        if (_disposed || _unknown is not null || !_group.IsActive)
        {
            // Terminal retirement keeps its existing complete physical path.
            // It is never borrowed as success by a continuing operation.
            PumpCompleted(); return;
        }
        foreach (var writer in _writerResponsibilities.ToArray())
        {
            writer.PumpOperationRelease();
            if (writer.Released) _writerResponsibilities.Remove(writer);
        }
        foreach (var call in _responsibilities.ToArray())
        {
            if (RetainsPhysicalCall(call)) continue;
            call.Before.DisposeOperation();
            if (call.Before.IsReleased)
            {
                if (ReferenceEquals(_rejectedNoEffectReturn?.Root, call)) _rejectedNoEffectReturn = null;
                _calls.Remove(call.Before); _ownedWriterCalls.Remove(call.Before); _responsibilities.Remove(call);
            }
        }
        _group.PumpOperationCompleted();
    }
    public void Dispose()
    {
        VerifyThread(); _disposed = true; _issued = null; _rejectedNoEffectReturn = null; _resumeCountdown = null;
        foreach (var writer in _writerResponsibilities) writer.Dispose();
        PumpCompleted();
    }

    internal sealed partial class ExecutionReceipt
    {
        private readonly NativePlaybackStockOccurrenceProducer _owner;
        private readonly Occurrence _occurrence;
        private ExecutionReceipt(NativePlaybackStockOccurrenceProducer owner, Occurrence occurrence)
        {
            (_owner, _occurrence) = (owner, occurrence);
            var call = occurrence.Root.Before; var returned = owner._group.ReadSourceOperationHealth();
            Role = call.Role; Sequence = call.Sequence; ArgBits = call.ArgBits;
            WriterSequence = returned.WriterSequence; SourceSequence = returned.SourceSequence; SchedulingSequence = returned.SchedulingSequence;
            if (Role is NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume)
                Schedule = ScheduleWitness.Issue(this, owner._issuer);
        }
        internal static ExecutionReceipt Issue(NativePlaybackStockOccurrenceProducer owner, object candidate, object issuer)
        {
            if (!ReferenceEquals(owner._issuer, issuer) || candidate is not Occurrence occurrence
                || occurrence.Root.Returned is not { Returned: true, Abnormal: false })
                throw new UnsupportedSignatureException("Only the actual stock return observer can issue execution evidence.");
            return new(owner, occurrence);
        }
        internal NativePlaybackSourceRole Role { get; }
        internal ulong Sequence { get; }
        internal ulong ArgBits { get; }
        internal ulong WriterSequence { get; }
        internal ulong SourceSequence { get; }
        internal ulong SchedulingSequence { get; }
        internal ScheduleWitness? Schedule { get; }
        internal bool ProductionAdmission => false;

        // Execution consumption is a running contract: registered current
        // receipt, native physical returns and all continuous fences. Startup
        // and explicit group audits keep their separate complete image path.
        internal void VerifyCurrent() => _owner.Verify(this, _occurrence);
        internal bool RootExitedFor(NativePlaybackStockOccurrenceProducer owner)
        {
            WriterRoot(owner);
            return _occurrence.Root.Before.TryReadOperationCompletion(out var completion) && completion!.PhysicalExited;
        }
        internal NativePlaybackSourceObservation WriterRoot(NativePlaybackStockOccurrenceProducer owner)
        {
            if (!ReferenceEquals(owner, _owner)) throw new UnsupportedSignatureException("The writer root belongs to another producer.");
            _owner.VerifyRoot(this, _occurrence); return _occurrence.Root.Before;
        }
        internal bool PhysicalExited
        { get { VerifyCurrent(); return _occurrence.Root.Before.TryReadOperationCompletion(out var value) && value!.PhysicalExited; } }
        internal bool Contains(NativePlaybackSourceObservation observation) => _occurrence.Root.Contains(observation)
            || _occurrence.Children.Any(value => value.Contains(observation));
        internal sealed class ScheduleWitness
        {
            private readonly ExecutionReceipt _receipt;
            private ScheduleWitness(ExecutionReceipt receipt, NativePlaybackSourceObservation leaf)
            { _receipt = receipt; RawDoubleBits = leaf.ArgBits; Source = leaf.Self; ParentSequence = leaf.ParentSequence; LeafSequence = leaf.Sequence; }
            internal static ScheduleWitness Issue(ExecutionReceipt receipt, object issuer)
            {
                if (!ReferenceEquals(receipt._owner._issuer, issuer))
                    throw new UnsupportedSignatureException("Only the actual execution receipt can derive its private schedule witness.");
                return new(receipt, receipt._occurrence.Children.Single(value => value.Before.Role == NativePlaybackSourceRole.PlayScheduled).Before);
            }
            internal ulong RawDoubleBits { get; }
            internal double ActualStartDsp => BitConverter.Int64BitsToDouble(unchecked((long)RawDoubleBits));
            internal IntPtr Source { get; }
            internal ulong ParentSequence { get; }
            internal ulong LeafSequence { get; }
            internal void VerifyCurrent()
            {
                if (!ReferenceEquals(_receipt.Schedule, this))
                    throw new UnsupportedSignatureException("A copied schedule witness is not the execution receipt's retained witness.");
                _receipt.VerifyCurrent();
            }
        }
    }
}
