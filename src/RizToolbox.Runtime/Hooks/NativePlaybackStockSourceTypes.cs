using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

// Exact independent prototype wire. These types are internal; they neither
// change the SDK nor confer first-install or production source authority.
internal enum NativePlaybackSourceKind : uint { Void0 = 1, Single1, Double1, Reference1, Boolean1, BooleanReturn0, UInt64_1, IcallSingle1 = 8 }
// SetTime advances the continuous Scheduling fence before its original store.
// It does not grant arbitrary Seek or complete lower-engine writer coverage.
internal enum NativePlaybackSourceRole : uint
{ SetPitch = 1, Pause, Replay, Resume, PlayScheduled, SetClip, ProgressPlay, PauseLeaf, ProgressMusicPlayer, SetTime, ProgressStartMoveNext, PlayerConstructor, AudioPlayHelper, AudioPlayDelayed, AudioPlay, AudioStop, AudioStopBoolean, AudioSetLoop, ProgressClip = 19, LowerSetPitch = 20, EngineSetPitch = 21, LowerSetTime = 22 }
internal enum NativePlaybackSourceStage : uint { Before = 1, OriginalReturned, Abnormal, PhysicalExit }
[Flags] internal enum NativePlaybackSourceFlags : uint
{ Armed = 1, Dormant = 2, Unknown = 4, OriginalReturned = 8, Abnormal = 16, PhysicalExited = 32, Withdrawn = 64, StockStop = 128,
    InitializationResponsibility = 256, LowerPitchChild = 512, EnginePitchChild = 1024, LowerTimeChild = 2048 }

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativePlaybackSourceFrame
{
    internal uint Version, Size;
    internal NativePlaybackSourceKind Kind;
    internal NativePlaybackSourceRole Role;
    internal uint Slot;
    internal NativePlaybackSourceStage Stage;
    internal uint ThreadId, Depth;
    internal IntPtr Owner;
    internal ulong OwnerEpoch, Sequence, Token, ParentSequence;
    internal IntPtr Self, IncomingMethod, Caller;
    internal ulong ArgBits, WriterSequence, SourceSequence, SchedulingSequence, Entered, Exited, InFlight;
    internal NativePlaybackSourceFlags Flags;
    internal uint Reserved;
    internal uint RawReturnByte, ReturnPresent;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativePlaybackSourceInformation
{
    internal uint Version, Size;
    internal NativePlaybackSourceKind Kind;
    internal NativePlaybackSourceRole Role;
    internal uint Slot;
    internal NativePlaybackSourceFlags Flags;
    internal uint OwnerThreadId, Reserved;
    internal IntPtr Owner;
    internal ulong OwnerEpoch;
    internal IntPtr Target, Original, Entry;
    internal ulong Entered, Exited, InFlight, WriterSequence, SourceSequence, SchedulingSequence, Faults;
    internal uint JournalCapacity, ConfiguredSlots;
}

internal sealed record NativePlaybackSourceFault(string Reason, Exception? Error = null);
internal sealed record NativePlaybackSourceHealth(bool Known, bool Armed, ulong Entered, ulong Exited,
    ulong InFlight, ulong WriterSequence, ulong SourceSequence, ulong SchedulingSequence,
    ulong NativeFaults, bool Unknown, string? FaultReason)
{
    internal bool ProductionAdmission => false;
}

/// <summary>Only an actual native current scope can create this object. Scalar
/// fields are diagnostic projections, never a replacement for the private owner.</summary>
internal sealed class NativePlaybackSourceObservation : IDisposable
{
    private readonly NativePlaybackStockSourceBridge _owner;
    private readonly NativePlaybackSourceFrame _frame;
    private readonly NativePlaybackStockSourceHub.EnginePitchAssociation? _engineAssociation;
    private int _releaseRequested, _released;
    private NativePlaybackSourceObservation(NativePlaybackStockSourceBridge owner, NativePlaybackSourceFrame frame,
        NativePlaybackStockSourceHub.EnginePitchAssociation? association)
        => (_owner, _frame, _engineAssociation) = (owner, frame, association);
    internal static NativePlaybackSourceObservation CaptureActual(NativePlaybackStockSourceBridge owner,
        NativePlaybackSourceFrame expected, object issuer)
    {
        owner.VerifyIssuer(issuer);
        var actual = owner.ReadOperationCurrentActual(expected);
        if (actual.Stage != NativePlaybackSourceStage.Before)
            throw new UnsupportedSignatureException("Only the actual source Before scope can issue an observation.");
        var association = actual.Role == NativePlaybackSourceRole.EngineSetPitch
            ? NativePlaybackStockSourceHub.EnginePitchAssociation.CaptureActual(owner, actual) : null;
        return new(owner, actual, association);
    }
    internal NativePlaybackSourceKind Kind => _frame.Kind;
    internal NativePlaybackSourceRole Role => _frame.Role;
    internal IntPtr Self => _frame.Self;
    internal IntPtr EngineWrapper => _engineAssociation?.Wrapper
        ?? throw new UnsupportedSignatureException("Only an actual engine scope can carry its private rooted wrapper association.");
    internal void VerifyEngineAssociationCurrent() => _engineAssociation?.VerifyCurrent();
    internal IntPtr IncomingMethod => _frame.IncomingMethod;
    internal IntPtr Caller => _frame.Caller;
    internal ulong OwnerEpoch => _frame.OwnerEpoch;
    internal ulong Sequence => _frame.Sequence;
    internal ulong ParentSequence => _frame.ParentSequence;
    internal ulong ArgBits => _frame.ArgBits;
    internal ulong UInt64Argument => Kind == NativePlaybackSourceKind.UInt64_1 ? _frame.ArgBits
        : throw new UnsupportedSignatureException("This source declaration has no UInt64 argument.");
    internal ulong WriterSequence => _frame.WriterSequence;
    internal ulong SourceSequence => _frame.SourceSequence;
    internal ulong SchedulingSequence => _frame.SchedulingSequence;
    // Only the native current-frame comparison can issue this fact. It is not
    // a caller-settable permission to ignore an arbitrary nested Stop.
    internal bool IsExactStockStop => Role == NativePlaybackSourceRole.AudioStop
        && (_frame.Flags & NativePlaybackSourceFlags.StockStop) != 0;
    // A private native current-frame fact, never a managed delegation switch.
    // This call still owns its own Before/return/physical-exit journal row.
    internal bool IsExactLowerPitchChild => Role == NativePlaybackSourceRole.LowerSetPitch
        && Kind == NativePlaybackSourceKind.IcallSingle1
        && (_frame.Flags & NativePlaybackSourceFlags.LowerPitchChild) != 0;
    internal bool IsExactEnginePitchChild => Role == NativePlaybackSourceRole.EngineSetPitch
        && Kind == NativePlaybackSourceKind.IcallSingle1
        && (_frame.Flags & NativePlaybackSourceFlags.EnginePitchChild) != 0;
    // The authenticated native frame belongs to the hub's configured time pair.
    // Its child projection retains the issued Before and physical journal row.
    internal bool IsExactLowerTimeChild => Role == NativePlaybackSourceRole.LowerSetTime
        && Kind == NativePlaybackSourceKind.IcallSingle1
        && (_frame.Flags & NativePlaybackSourceFlags.LowerTimeChild) != 0;
    internal uint ThreadId => _frame.ThreadId;
    internal uint Depth => _frame.Depth;
    internal float SingleArgument => BitConverter.Int32BitsToSingle(unchecked((int)_frame.ArgBits));
    internal double DoubleArgument => BitConverter.Int64BitsToDouble(unchecked((long)_frame.ArgBits));
    internal IntPtr ReferenceArgument => new(unchecked((long)_frame.ArgBits));
    internal bool IsReleased => Volatile.Read(ref _released) != 0;
    internal bool ReleaseRequested => Volatile.Read(ref _releaseRequested) != 0;
    internal bool BelongsTo(NativePlaybackStockSourceBridge owner) => ReferenceEquals(_owner, owner);
    internal void VerifyCurrent() { Open(); _owner.ReadCurrentActual(_frame, requireBefore: false); }
    internal void VerifyOperationCurrent() { Open(); _owner.ReadOperationCurrentActual(_frame, requireBefore: false); }
    internal bool TryReadCompletion(out NativePlaybackSourceCompletion? completion)
    { Open(); return _owner.TryReadCompletionActual(this, _frame, out completion); }
    internal bool TryReadOperationCompletion(out NativePlaybackSourceCompletion? completion)
    { Open(); return _owner.TryReadOperationCompletionActual(this, _frame, out completion); }
    internal NativePlaybackSourceFrame ActualOrigin(NativePlaybackStockSourceBridge owner)
    {
        Open();
        if (!BelongsTo(owner)) throw new UnsupportedSignatureException("A source observation belongs to another native owner.");
        return _frame;
    }
    internal void MarkReleased(NativePlaybackStockSourceBridge owner)
    {
        if (!BelongsTo(owner)) throw new UnsupportedSignatureException("Only this exact source owner can release its receipt.");
        Volatile.Write(ref _released, 1);
    }
    private void Open()
    { if (IsReleased) throw new ObjectDisposedException(nameof(NativePlaybackSourceObservation)); }
    public void Dispose()
    {
        if (IsReleased) return;
        Interlocked.Exchange(ref _releaseRequested, 1);
        _owner.ReleaseWhenExited(this); // In-flight responsibility remains with the actual native journal.
    }
    internal void DisposeOperation()
    {
        if (IsReleased) return;
        Interlocked.Exchange(ref _releaseRequested, 1);
        _owner.ReleaseOperationWhenExited(this);
    }
}

/// <summary>The callback form describes original return only. Physical exit
/// is separately read from the native finally journal, using the private Before.</summary>
internal sealed class NativePlaybackSourceCompletion
{
    private readonly NativePlaybackStockSourceBridge _owner;
    private readonly NativePlaybackSourceFrame _frame;
    internal NativePlaybackSourceObservation Observation { get; }
    private NativePlaybackSourceCompletion(NativePlaybackStockSourceBridge owner,
        NativePlaybackSourceObservation observation, NativePlaybackSourceFrame frame)
        => (_owner, Observation, _frame) = (owner, observation, frame);
    internal bool Returned => (_frame.Flags & NativePlaybackSourceFlags.OriginalReturned) != 0;
    internal bool Abnormal => (_frame.Flags & NativePlaybackSourceFlags.Abnormal) != 0;
    internal bool PhysicalExited => (_frame.Flags & NativePlaybackSourceFlags.PhysicalExited) != 0;
    internal bool Unknown => (_frame.Flags & NativePlaybackSourceFlags.Unknown) != 0;
    internal NativePlaybackSourceStage Stage => _frame.Stage;
    internal bool HasRawReturnByte => _frame.ReturnPresent == 1;
    internal byte RawReturnByte => HasRawReturnByte ? checked((byte)_frame.RawReturnByte)
        : throw new UnsupportedSignatureException("This physical return has no raw AL evidence.");
    internal ulong Sequence => _frame.Sequence;
    internal ulong WriterSequence => _frame.WriterSequence;
    internal ulong SourceSequence => _frame.SourceSequence;
    internal ulong SchedulingSequence => _frame.SchedulingSequence;
    internal static NativePlaybackSourceCompletion CaptureReturnedActual(NativePlaybackStockSourceBridge owner,
        NativePlaybackSourceObservation observation, NativePlaybackSourceFrame expected, object issuer)
    {
        owner.VerifyIssuer(issuer);
        var actual = owner.ReadOperationCurrentActual(expected, requireBefore: false, allowWithdrawnCompletion: true);
        var before = observation.ActualOrigin(owner);
        if (!NativePlaybackStockSourceBridge.SameCall(before, actual)
            || actual.Stage is not (NativePlaybackSourceStage.OriginalReturned or NativePlaybackSourceStage.Abnormal))
            throw new UnsupportedSignatureException("The actual returned scope differs from its private source Before.");
        return new(owner, observation, actual);
    }
    internal static NativePlaybackSourceCompletion CapturePhysicalActual(NativePlaybackStockSourceBridge owner,
        NativePlaybackSourceObservation observation, NativePlaybackSourceFrame origin, object issuer)
    {
        owner.VerifyIssuer(issuer);
        return new(owner, observation, owner.ReadReceiptActual(origin));
    }
    internal static NativePlaybackSourceCompletion CaptureOperationPhysicalActual(NativePlaybackStockSourceBridge owner,
        NativePlaybackSourceObservation observation, NativePlaybackSourceFrame actual, object issuer)
    {
        owner.VerifyIssuer(issuer);
        var origin = observation.ActualOrigin(owner);
        if (!NativePlaybackStockSourceBridge.SameCall(origin, actual) || actual.Stage != NativePlaybackSourceStage.PhysicalExit)
            throw new UnsupportedSignatureException("The running physical journal differs from its exact retained Before.");
        return new(owner, observation, actual);
    }
    internal void VerifyCurrent()
    {
        var origin = Observation.ActualOrigin(_owner);
        // Clean logical withdrawal ends new source authority, while a captured
        // original return still owns its completion and eventual journal row.
        _owner.VerifyKnown();
        if (PhysicalExited) _owner.ReadReceiptActual(origin);
        else _owner.ReadCurrentActual(_frame, requireBefore: false, allowWithdrawnCompletion: true);
    }
    internal void VerifyAuthority() { VerifyCurrent(); _owner.VerifyAuthority(); }
    internal void VerifyOperationCurrent()
    {
        var origin = Observation.ActualOrigin(_owner);
        _owner.VerifyOperationKnown();
        if (PhysicalExited) _owner.ReadOperationReceiptActual(origin);
        else _owner.ReadOperationCurrentActual(_frame, requireBefore: false, allowWithdrawnCompletion: true);
    }
    internal void VerifyOperationAuthority() { VerifyOperationCurrent(); _owner.VerifyOperationAuthority(); }
    internal NativePlaybackSourceFrame ActualWriterReturn()
    {
        _owner.VerifyIssuedOperationReturn(this); return _frame;
    }
}
