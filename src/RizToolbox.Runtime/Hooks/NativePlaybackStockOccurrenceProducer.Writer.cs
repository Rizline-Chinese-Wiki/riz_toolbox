namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockOccurrenceProducer
{
    private readonly object _writerIssuer = new();
    private NativePlaybackStockSourceHub.WriterReservation? _pendingWriter;
    private ContinuationReceipt? _writerCurrent;
    private readonly HashSet<NativePlaybackSourceObservation> _ownedWriterCalls = new();
    private readonly HashSet<NativePlaybackStockSourceHub.WriterReservation> _writerResponsibilities = new();

    internal bool OwnsReservedTimeBefore(NativePlaybackSourceObservation observation)
    {
        VerifyThread();
        return !_disposed && _unknown is null && observation.Role == NativePlaybackSourceRole.SetTime
            && _pendingWriter is { Role: NativePlaybackSourceRole.SetTime } writer
            && writer.MatchesCurrentBefore(observation);
    }

    internal ContinuationReceipt WritePitch(ExecutionReceipt root, float pitch)
        => WriteCore(root, pitch, NativePlaybackSourceRole.SetPitch, null);
    internal ContinuationReceipt WriteTime(ExecutionReceipt root, float seconds)
        => WriteCore(root, seconds, NativePlaybackSourceRole.SetTime, null);

    private ContinuationReceipt WriteCore(ExecutionReceipt root, float value, NativePlaybackSourceRole role, ContinuationReceipt? predecessor)
    {
        VerifyThread();
        if (!float.IsFinite(value) || (role == NativePlaybackSourceRole.SetTime ? value < 0 : value <= 0) || _pendingWriter is not null)
            throw new UnsupportedSignatureException("The native value must be finite and in range for its non-reentrant private writer.");
        if (predecessor is null) root.VerifyCurrent(); else predecessor.VerifyCurrent();
        if (role == NativePlaybackSourceRole.SetTime && (root.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay)
            || !root.RootExitedFor(this) || !_group.HasLowerTime))
            throw new UnsupportedSignatureException("A time write requires this actual exited Pause and the exact lower-time physical pair.");
        var observation = root.WriterRoot(this);
        var health = _group.ReadSourceOperationHealth();
        var reservation = _group.ReserveOperationWriter(observation, _source, unchecked((uint)BitConverter.SingleToInt32Bits(value)),
            health.WriterSequence, health.SourceSequence, health.SchedulingSequence, predecessor?.ReservationFor(this), role);
        _writerResponsibilities.Add(reservation);
        try
        {
            _pendingWriter = reservation;
            reservation.RunOperation();
        }
        finally
        {
            _pendingWriter = null;
            if (_disposed) { reservation.Dispose(); PumpCompleted(); }
        }
        if (_unknown is not null || !ReferenceEquals(_issued, root))
            throw new UnsupportedSignatureException("The logical source changed during its reserved native writer attempt.");
        VerifyReservedWriter(reservation);
        var next = ContinuationReceipt.Issue(this, root, reservation, value, _writerIssuer);
        var previous = _writerCurrent;
        _writerCurrent = next; next.VerifyCurrent();
        previous?.ReleaseFor(this, _writerIssuer); PumpOperationCompleted(); return next;
    }

    /// <summary>An exact native attempt and post fence succeed the immutable S2
    /// receipt. This is writer execution evidence; rooted DSP/pitch readback and
    /// Clock publication are separate consumers and grant no implicit admission.</summary>
    internal sealed class ContinuationReceipt
    {
        private readonly NativePlaybackStockOccurrenceProducer _owner;
        private readonly ExecutionReceipt _root;
        private readonly NativePlaybackStockSourceHub.WriterReservation _reservation;
        private ContinuationReceipt(NativePlaybackStockOccurrenceProducer owner, ExecutionReceipt root,
            NativePlaybackStockSourceHub.WriterReservation reservation, float value)
        {
            (_owner, _root, _reservation, WrittenValue) = (owner, root, reservation, value);
            if (root.Schedule is { } schedule) Schedule = ScheduleWitness.Derive(this, schedule);
        }
        internal static ContinuationReceipt Issue(NativePlaybackStockOccurrenceProducer owner, ExecutionReceipt root,
            NativePlaybackStockSourceHub.WriterReservation reservation, float pitch, object issuer)
        {
            if (!ReferenceEquals(owner._writerIssuer, issuer))
                throw new UnsupportedSignatureException("Only the producer's completed native writer can issue its successor.");
            return new(owner, root, reservation, pitch);
        }
        internal NativePlaybackSourceRole WrittenRole => _reservation.Role;
        internal float WrittenValue { get; }
        internal float Pitch => WrittenRole == NativePlaybackSourceRole.SetPitch ? WrittenValue
            : throw new UnsupportedSignatureException("This continuation records a time write, not an observed pitch value.");
        internal float TimeSeconds => WrittenRole == NativePlaybackSourceRole.SetTime ? WrittenValue
            : throw new UnsupportedSignatureException("This continuation records a pitch write, not an observed time value.");
        internal ulong RootSequence => _root.Sequence;
        internal NativePlaybackSourceRole Role => _root.Role;
        internal ScheduleWitness? Schedule { get; }
        internal ulong WriterSequence => _reservation.Completed.PostWriter;
        internal ulong SourceSequence => _reservation.Completed.PostSource;
        internal ulong SchedulingSequence => _reservation.Completed.PostScheduling;
        internal bool ProductionAdmission => false;
        internal bool RootPhysicalExited { get { VerifyCurrent(); return _root.RootExitedFor(_owner); } }
        internal void VerifyCurrent()
        {
            _owner.VerifyThread();
            if (_owner._disposed || _owner._unknown is not null || !ReferenceEquals(_owner._writerCurrent, this))
                throw new UnsupportedSignatureException("The writer continuation belongs to an ended logical source.");
            _root.WriterRoot(_owner); _reservation.VerifyOperationCurrent(); _owner.VerifyReservedWriter(_reservation);
        }
        internal ContinuationReceipt WritePitch(float pitch)
        { VerifyCurrent(); return _owner.WriteCore(_root, pitch, NativePlaybackSourceRole.SetPitch, this); }
        internal ContinuationReceipt WriteTime(float seconds)
        { VerifyCurrent(); return _owner.WriteCore(_root, seconds, NativePlaybackSourceRole.SetTime, this); }
        internal NativePlaybackStockSourceHub.WriterReservation ReservationFor(NativePlaybackStockOccurrenceProducer owner)
        {
            if (!ReferenceEquals(owner, _owner)) throw new UnsupportedSignatureException("Another producer cannot consume this writer predecessor.");
            VerifyCurrent(); return _reservation;
        }
        internal void ReleaseFor(NativePlaybackStockOccurrenceProducer owner, object issuer)
        {
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(owner._writerIssuer, issuer))
                throw new UnsupportedSignatureException("Only the actual producer can retire its predecessor reservation.");
            _reservation.DisposeOperation();
        }
        internal bool Contains(NativePlaybackSourceObservation observation)
            => _root.Contains(observation) || _reservation.Contains(observation);
        internal sealed class ScheduleWitness
        {
            private readonly ContinuationReceipt _continuation;
            private ScheduleWitness(ContinuationReceipt continuation, ExecutionReceipt.ScheduleWitness original)
            {
                _continuation = continuation; RawDoubleBits = original.RawDoubleBits;
                Source = original.Source; ParentSequence = original.ParentSequence; LeafSequence = original.LeafSequence;
            }
            internal static ScheduleWitness Derive(ContinuationReceipt continuation, ExecutionReceipt.ScheduleWitness original)
                => new(continuation, original);
            internal ulong RawDoubleBits { get; }
            internal double ActualStartDsp => BitConverter.Int64BitsToDouble(unchecked((long)RawDoubleBits));
            internal IntPtr Source { get; }
            internal ulong ParentSequence { get; }
            internal ulong LeafSequence { get; }
            internal void VerifyCurrent()
            {
                if (!ReferenceEquals(_continuation.Schedule, this))
                    throw new UnsupportedSignatureException("A copied writer schedule witness is not its retained continuation.");
                _continuation.VerifyCurrent();
            }
        }
    }
}
