using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    private readonly object _writerIssuer = new();
    private readonly Dictionary<ulong, WriterReservation> _writerObjects = new();
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct WriterInformation
    {
        internal uint Version, Size, State, Reserved;
        internal ulong Owner, OwnerEpoch, Reservation, Nonce, RootSequence, RootToken, Predecessor, PredecessorNonce;
        internal ulong Source, Target, Caller, IncomingMethod, ArgBits;
        internal ulong ExpectedWriter, ExpectedSource, ExpectedScheduling, AttemptSequence, AttemptToken, ParentSequence;
        internal ulong PostWriter, PostSource, PostScheduling;
        internal uint Returned, Abnormal, PhysicalExited, Contended, Current, ExceptionCode, Reserved0, Reserved1;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReserveWriterNative(IntPtr owner, ref WriterInformation information);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WriterNative(IntPtr reservation, ulong nonce, ref WriterInformation information);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReleaseWriterNative(IntPtr reservation, ulong nonce);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int VerifyWriterNative(IntPtr reservation, ulong nonce,
        ref NativePlaybackSourceFrame frame, ref WriterInformation information);

    private NativePlaybackSourceFrame WriterOrigin(NativePlaybackSourceObservation observation)
    {
        var channel = _channels.Values.SingleOrDefault(value => observation.BelongsTo(value))
            ?? throw new UnsupportedSignatureException("A writer requires this hub's actual retained observation.");
        return channel.RetainedWriterOrigin(observation);
    }
    internal WriterReservation ReserveWriter(NativePlaybackSourceObservation root, IntPtr source, IntPtr target,
        IntPtr method, uint bits, ulong writer, ulong sourceFence, ulong scheduling, WriterReservation? predecessor)
        => ReserveWriterCore(root, source, target, method, bits, writer, sourceFence, scheduling, predecessor, NativePlaybackSourceRole.SetPitch, auditImage: true);
    internal WriterReservation ReserveTimeWriter(NativePlaybackSourceObservation root, IntPtr source, IntPtr target,
        IntPtr method, uint bits, ulong writer, ulong sourceFence, ulong scheduling, WriterReservation? predecessor)
        => ReserveWriterCore(root, source, target, method, bits, writer, sourceFence, scheduling, predecessor, NativePlaybackSourceRole.SetTime, auditImage: true);
    internal WriterReservation ReserveOperationWriter(NativePlaybackSourceObservation root, IntPtr source, IntPtr target,
        IntPtr method, uint bits, ulong writer, ulong sourceFence, ulong scheduling, WriterReservation? predecessor,
        NativePlaybackSourceRole role)
        => ReserveWriterCore(root, source, target, method, bits, writer, sourceFence, scheduling, predecessor, role, auditImage: false);
    private WriterReservation ReserveWriterCore(NativePlaybackSourceObservation root, IntPtr source, IntPtr target,
        IntPtr method, uint bits, ulong writer, ulong sourceFence, ulong scheduling, WriterReservation? predecessor,
        NativePlaybackSourceRole role, bool auditImage)
    {
        VerifyThread();
        if (auditImage) _image.Verify();
        else if (ReadOperationHealth() is not { Known: true, Armed: true })
            throw new UnsupportedSignatureException("The running native writer lost its exact armed owner.");
        if (Marshal.SizeOf<WriterInformation>() != 224 || Export<VersionNative>("RtPlaybackWriterVersion")() != 1)
            throw new UnsupportedSignatureException("The native private writer V1/224B protocol is required.");
        if (role == NativePlaybackSourceRole.SetTime && Export<VersionNative>("RtPlaybackTimeWriterVersion")() != 1)
            throw new UnsupportedSignatureException("The native paused-time writer V1 protocol is required.");
        var origin = WriterOrigin(root);
        var predecessorIdentity = predecessor is null ? (WriterInformation?)null
            : auditImage ? predecessor.IdentityFor(this) : predecessor.IdentityForOperation(this);
        var info = new WriterInformation { Version = 1, Size = 224, RootSequence = origin.Sequence, RootToken = origin.Token,
            Source = unchecked((ulong)source.ToInt64()), Target = unchecked((ulong)target.ToInt64()),
            IncomingMethod = unchecked((ulong)method.ToInt64()), ArgBits = bits,
            ExpectedWriter = writer, ExpectedSource = sourceFence, ExpectedScheduling = scheduling,
            Predecessor = predecessorIdentity?.Reservation ?? 0, PredecessorNonce = predecessorIdentity?.Nonce ?? 0 };
        var reserve = role == NativePlaybackSourceRole.SetTime ? "RtPlaybackTimeWriterReserve" : "RtPlaybackWriterReserve";
        if (Export<ReserveWriterNative>(reserve)(_owner, ref info) != 1 || info.State != 1
            || info.Owner != unchecked((ulong)_owner.ToInt64()) || info.OwnerEpoch != Epoch || info.Reservation == 0 || info.Nonce == 0)
            throw new UnsupportedSignatureException("The exact native root/fences did not admit one private writer reservation.");
        var actual = WriterReservation.Issue(this, root, info, role, _writerIssuer);
        _writerObjects.Add(info.Nonce, actual); return actual;
    }

    internal sealed class WriterReservation : IDisposable
    {
        private readonly NativePlaybackStockSourceHub _hub;
        private readonly NativePlaybackSourceObservation _root;
        private readonly WriterInformation _identity;
        internal NativePlaybackSourceRole Role { get; }
        private readonly WriterNative _run, _read;
        private readonly VerifyWriterNative _verify;
        private readonly ReleaseWriterNative _release;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private NativePlaybackSourceObservation? _attempt;
        private NativePlaybackSourceCompletion? _returned;
        private bool _runAttempted;
        private bool _releaseRequested, _released;
        internal bool Released => _released;
        internal NativePlaybackSourceObservation ActualAttempt
        { get { VerifyRegistered(); return _attempt ?? throw new UnsupportedSignatureException("No exact native writer attempt has entered."); } }
        internal WriterInformation Completed { get; private set; }
        private WriterReservation(NativePlaybackStockSourceHub hub, NativePlaybackSourceObservation root, WriterInformation identity,
            NativePlaybackSourceRole role)
        {
            (_hub, _root, _identity, Role) = (hub, root, identity, role);
            _run = hub.Export<WriterNative>("RtPlaybackWriterRun"); _read = hub.Export<WriterNative>("RtPlaybackWriterRead");
            _verify = hub.Export<VerifyWriterNative>("RtPlaybackWriterVerifyCurrent");
            _release = hub.Export<ReleaseWriterNative>("RtPlaybackWriterRelease");
        }
        internal static WriterReservation Issue(NativePlaybackStockSourceHub hub, NativePlaybackSourceObservation root,
            WriterInformation identity, NativePlaybackSourceRole role, object issuer)
        {
            if (!ReferenceEquals(hub._writerIssuer, issuer))
                throw new UnsupportedSignatureException("Only the actual native hub can issue its writer reservation.");
            return new(hub, root, identity, role);
        }
        internal WriterInformation IdentityFor(NativePlaybackStockSourceHub hub)
        {
            if (!ReferenceEquals(hub, _hub)) throw new UnsupportedSignatureException("A writer predecessor belongs to another native hub.");
            VerifyCurrent(); return _identity;
        }
        internal WriterInformation IdentityForOperation(NativePlaybackStockSourceHub hub)
        {
            if (!ReferenceEquals(hub, _hub)) throw new UnsupportedSignatureException("The running writer predecessor belongs to another native hub.");
            VerifyOperationCurrent(); return _identity;
        }
        internal bool TryObserveBefore(NativePlaybackSourceObservation observation)
        {
            if (!MatchesCurrentBefore(observation)) return false;
            if (_attempt is not null)
                throw new UnsupportedSignatureException("The native once-only writer issued another actual Before.");
            _attempt = observation; return true;
        }
        // The callback router selects the private writer by the actual native
        // reservation/nonce and retained current frame. Selection consumes no
        // observation; the producer independently records it on its own entry.
        internal bool MatchesCurrentBefore(NativePlaybackSourceObservation observation)
        {
            VerifyRegistered();
            var frame = _hub.WriterOrigin(observation); var info = new WriterInformation { Version = 1, Size = 224 };
            if (_verify(new(unchecked((long)_identity.Reservation)), _identity.Nonce, ref frame, ref info) != 1) return false;
            VerifyIdentity(info);
            if (observation.Role != Role || info.AttemptSequence != observation.Sequence || info.AttemptToken != frame.Token)
                throw new UnsupportedSignatureException("The native writer's current Before differs from its exact reserved attempt.");
            return true;
        }
        internal bool TryObserveReturned(NativePlaybackSourceCompletion completion)
        {
            VerifyRegistered();
            if (!ReferenceEquals(_attempt, completion.Observation)) return false;
            var frame = completion.ActualWriterReturn(); var info = new WriterInformation { Version = 1, Size = 224 };
            if (_verify(new(unchecked((long)_identity.Reservation)), _identity.Nonce, ref frame, ref info) != 1) return false;
            VerifyIdentity(info);
            if (_returned is not null) throw new UnsupportedSignatureException("The native writer returned more than once.");
            _returned = completion; return true;
        }
        internal void Run()
            => RunCore(auditImage: true);
        internal void RunOperation()
            => RunCore(auditImage: false);
        private void RunCore(bool auditImage)
        {
            VerifyRegistered();
            if (_runAttempted) throw new UnsupportedSignatureException("A private native writer reservation is once-only.");
            if (auditImage) _hub._image.Verify();
            else if (_hub.ReadOperationHealth() is not { Known: true, Armed: true })
                throw new UnsupportedSignatureException("The actual running writer owner is no longer armed.");
            _runAttempted = true; _hub.WriterOrigin(_root);
            var info = new WriterInformation { Version = 1, Size = 224 };
            var status = _run(new(unchecked((long)_identity.Reservation)), _identity.Nonce, ref info);
            Completed = info; VerifyIdentity(info);
            if (status != 1 || _attempt is null || _returned is not { Returned: true, Abnormal: false })
                throw new UnsupportedSignatureException("The native writer did not complete one exact uncontended normal attempt; responsibility is retained.");
            if (auditImage) VerifyCurrent(); else VerifyOperationCurrent();
        }
        private void VerifyIdentity(WriterInformation info)
        {
            if (info.Version != 1 || info.Size != 224 || info.Owner != _identity.Owner || info.OwnerEpoch != _identity.OwnerEpoch
                || info.Reservation != _identity.Reservation || info.Nonce != _identity.Nonce
                || info.RootSequence != _identity.RootSequence || info.RootToken != _identity.RootToken
                || info.Predecessor != _identity.Predecessor || info.PredecessorNonce != _identity.PredecessorNonce
                || info.Source != _identity.Source || info.Target != _identity.Target || info.IncomingMethod != _identity.IncomingMethod
                || info.ArgBits != _identity.ArgBits || info.ExpectedWriter != _identity.ExpectedWriter
                || info.ExpectedSource != _identity.ExpectedSource || info.ExpectedScheduling != _identity.ExpectedScheduling)
                throw new UnsupportedSignatureException("The actual native writer differs from its private reserved identity.");
        }
        internal void VerifyCurrent()
            => VerifyCurrentCore(auditImage: true);
        internal void VerifyOperationCurrent()
            => VerifyCurrentCore(auditImage: false);
        private void VerifyCurrentCore(bool auditImage)
        {
            VerifyRegistered();
            if (auditImage) _hub._image.Verify();
            else if (_hub.ReadOperationHealth() is not { Known: true, Armed: true })
                throw new UnsupportedSignatureException("The running writer no longer owns its actual native source authority.");
            var info = new WriterInformation { Version = 1, Size = 224 };
            if (_read(new(unchecked((long)_identity.Reservation)), _identity.Nonce, ref info) != 1)
                throw new UnsupportedSignatureException("The native writer reservation is no longer retained.");
            VerifyIdentity(info);
            if (info.State != 3 || info.Current != 1 || info.Returned != 1 || info.PhysicalExited != 1 || info.Abnormal != 0
                || info.Contended != 0 || info.PostWriter != info.ExpectedWriter + (Role == NativePlaybackSourceRole.SetPitch ? 1ul : 0ul)
                || info.PostSource != info.ExpectedSource
                || info.PostScheduling != info.ExpectedScheduling + (Role == NativePlaybackSourceRole.SetTime ? 1ul : 0ul)
                || _attempt is null || _returned is null)
                throw new UnsupportedSignatureException("The actual writer attempt/post fence was superseded, contended or did not physically exit.");
            NativePlaybackSourceCompletion? exit;
            var completed = auditImage ? _attempt.TryReadCompletion(out exit)
                : _attempt.TryReadOperationCompletion(out exit);
            if (!completed || exit is not { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false })
                throw new UnsupportedSignatureException("The actual writer attempt/post fence was superseded, contended or did not physically exit.");
            if (auditImage) exit.VerifyAuthority(); else exit.VerifyOperationAuthority();
            Completed = info;
        }
        internal bool Contains(NativePlaybackSourceObservation observation) => ReferenceEquals(_attempt, observation);
        private void VerifyRegistered()
        {
            if (_thread != Environment.CurrentManagedThreadId)
                throw new UnsupportedSignatureException("Native writer ownership is confined to its original thread.");
            if (_released || !_hub._writerObjects.TryGetValue(_identity.Nonce, out var actual) || !ReferenceEquals(actual, this))
                throw new UnsupportedSignatureException("A copied, released or foreign writer is not the hub's private issued reservation.");
        }
        public void Dispose()
        {
            if (_released) return;
            VerifyRegistered(); _releaseRequested = true; PumpRelease();
        }
        internal void DisposeOperation()
        {
            if (_released) return;
            VerifyRegistered(); _releaseRequested = true; PumpOperationRelease();
        }
        internal void PumpOperationRelease()
        {
            if (_released || !_releaseRequested) return;
            VerifyRegistered();
            // The native nonce-based release is the actual exit gate. A
            // withdrawal never substitutes zero activity for that receipt.
            if (_release(new(unchecked((long)_identity.Reservation)), _identity.Nonce) != 1) return;
            _released = true; _hub._writerObjects.Remove(_identity.Nonce);
        }
        internal void PumpRelease()
        {
            if (_released || !_releaseRequested) return;
            VerifyRegistered(); _hub._image.Verify();
            if (_release(new(unchecked((long)_identity.Reservation)), _identity.Nonce) != 1) return;
            _released = true; _hub._writerObjects.Remove(_identity.Nonce);
        }
    }
}
