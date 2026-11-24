using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

[Flags]
internal enum NativeTweenSourceReferenceRole : uint
{ Manager = 1, Tween = 2, Container = 4, Array = 8, Prior = 16, Event = 32 }
internal enum NativeTweenSourceReferencePhase : uint
{ References, BeforeCommitted, AfterCommitted, Abnormal }

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceReferenceOrigin
{
    internal uint Version, Size;
    internal NativeTweenSourceOperation Operation;
    internal uint ThreadId, Depth;
    internal NativeTweenSourceSite Site;
    internal NativeTweenSourceStage Stage;
    internal NativeTweenSourceReferenceRole OriginRoles;
    internal ulong SourceEpoch, OperationSequence, ParentOperationSequence, Transaction;
    internal IntPtr SourcePc, RelayReturnPc;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceBorrowedReference
{
    internal IntPtr Value;
    internal NativeTweenSourceReferenceRole Role;
    internal int SourceIndex;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceReferenceNotice
{
    internal uint Version, Size;
    internal NativeTweenSourceReferencePhase Phase;
    internal uint ReferenceCount;
    internal ulong ObserverEpoch, ScopeId, DeliverySequence;
    internal IntPtr Origin, References;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceReferencesObserver
{
    internal uint Version, Size;
    internal IntPtr Notify, User;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceReferencesTicket
{
    internal uint Version, Size, ThreadId, Reserved;
    internal ulong ObserverEpoch, ScopeId, SourceEpoch, OperationSequence, Transaction;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceReferencesInformation
{
    internal uint Version, Size, Attached, Unknown, ScopeCapacity, ActiveScopes, RetainedScopes, CallbackInFlight;
    internal ulong SourceEpoch, ObserverEpoch, ScopesBegun, ScopesEnded, ScopesCompleted;
    internal ulong DeliveryAttempts, DeliveriesCompleted, CallbackFaults, RejectedReads;
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint NativeTweenSourceReferencesNotify(IntPtr actualNotice, IntPtr user);

/// <summary>The controlled implementation is a semantic fixture. Only the private
/// loaded transport below reaches the actual FirstInstall observer attach gate.</summary>
internal interface INativeTweenSourceReferencesTransport
{
    uint Attach(ref NativeTweenSourceReferencesObserver observer);
    uint GetInfo(ref NativeTweenSourceReferencesInformation information);
    uint ReadCurrentNotice(IntPtr actualNotice, ulong observerEpoch, ulong scopeId, ulong deliverySequence,
        IntPtr noticeCopy, IntPtr originCopy, IntPtr referencesCopy, uint referenceCapacity);
}

/// <summary>Receives synchronous borrowed references from actual source scopes.
/// It holds independent roots for the process; there is no release, replacement,
/// cancellation or source-completion authority. References delivery means only
/// that references were rooted, never that the source operation succeeded. The
/// before and after committed phases are invalid unless their matching stage
/// first published at least one non-null reference.</summary>
internal sealed partial class NativeTweenSourceReferences
{
    internal const int MaximumReferencesPerNotice = 16, MaximumScopes = 128;
    private const int MaximumReferencesPerScope = 32768;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<NativeTweenSourceReferences> ProcessOwners = new();
    [ThreadStatic] private static CallbackFrame? _current;
    private readonly object _issuer = new();
    private readonly INativeTweenSourceReferencesTransport _native;
    private readonly Action _verify;
    private readonly Func<IntPtr, NativeRuntime.RootHandle> _adopt;
    private readonly Func<NativeRuntime.RootHandle, NativeTweenSourceObjectKind, IntPtr> _validate;
    private readonly Action<Receipt> _consume;
    private readonly ILogger? _diagnostics;
    private readonly NativeFirstInstall.TweenSourceReady? _production;
    private readonly NativeTweenSourceReferencesNotify _notify;
    // Only live generations are indexed by address. Every capture retains its
    // immutable record; released generations can never alias a later address.
    private readonly Dictionary<IntPtr, RootRecord> _roots = new();
    private readonly Dictionary<ulong, Scope> _scopes = new();
    private readonly int _managedThreadId;
    private readonly uint _threadId;
    private readonly ulong _sourceEpoch;
    private ulong _observerEpoch, _lastDelivery;
    private int _unknown;
    private long _callbackFaults;
    private Exception? _firstFailure;

    internal bool Production => _production is not null;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal long CallbackFaults => Interlocked.Read(ref _callbackFaults);
    internal Exception? FirstFailure => Volatile.Read(ref _firstFailure);
    internal int RootRecords { get { VerifyAccess(); return _roots.Values.Count(row => row.Root is not null); } }
    internal int ReservedRootRecords { get { VerifyAccess(); return _roots.Count; } }
    internal int RetainedScopeRecords { get { VerifyAccess(); return _scopes.Count; } }

    private NativeTweenSourceReferences(INativeTweenSourceReferencesTransport native, ulong sourceEpoch, Action verify,
        Func<IntPtr, NativeRuntime.RootHandle> adopt,
        Func<NativeRuntime.RootHandle, NativeTweenSourceObjectKind, IntPtr> validate, Action<Receipt> consume,
        ILogger? diagnostics, NativeFirstInstall.TweenSourceReady? production = null)
    {
        (_native, _sourceEpoch, _verify, _adopt, _validate, _consume, _diagnostics, _production)
            = (native, sourceEpoch, verify, adopt, validate, consume, diagnostics, production);
        _managedThreadId = Environment.CurrentManagedThreadId; _threadId = CurrentThreadId(); _notify = Notify;
    }

    internal static NativeTweenSourceReferences BindProduction(NativeRuntime runtime,
        NativeFirstInstall.TweenSourceReady ready, Action verify, Action<Receipt> consume)
    {
        ArgumentNullException.ThrowIfNull(runtime); ArgumentNullException.ThrowIfNull(ready);
        ArgumentNullException.ThrowIfNull(verify); ArgumentNullException.ThrowIfNull(consume); verify(); ready.VerifyReady();
        var metadata = runtime.InspectTweenSourcesMetadata(ready.Module);
        var admission = runtime.InspectTweenAdmissionMetadata(ready.Module);
        ready.BindMetadata(metadata, admission.CachedPointerField.Field);
        var owner = new NativeTweenSourceReferences(new LoadedTransport(ready), ready.GetSourceEpoch(), verify,
            runtime.Root, (root, kind) => runtime.ValidateRootedTweenSourceObject(root, kind, metadata, ready.Module),
            consume, runtime.Diagnostics, ready);
        // Native attachment is permanent, including an attach that throws after
        // publishing. Retain the owner and callback delegate BEFORE that call.
        lock (ProcessGate) ProcessOwners.Add(owner);
        owner.Attach(); return owner;
    }

    internal static NativeTweenSourceReferences CreateControlledForTests(INativeTweenSourceReferencesTransport native,
        ulong sourceEpoch, Action verify, Func<IntPtr, NativeRuntime.RootHandle> adopt,
        Func<NativeRuntime.RootHandle, NativeTweenSourceObjectKind, IntPtr> validate, Action<Receipt> consume,
        ILogger? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(native); ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(adopt); ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(consume); verify();
        if (sourceEpoch == 0) throw new ArgumentOutOfRangeException(nameof(sourceEpoch));
        var owner = new NativeTweenSourceReferences(native, sourceEpoch, verify, adopt, validate, consume, diagnostics);
        lock (ProcessGate) ProcessOwners.Add(owner);
        owner.Attach(); return owner;
    }

    private void Attach()
    {
        VerifyAccess();
        try
        {
            ValidateLayout();
            var observer = new NativeTweenSourceReferencesObserver { Version = 1, Size = 24,
                Notify = Marshal.GetFunctionPointerForDelegate(_notify) };
            var expected = observer;
            if (_native.Attach(ref observer) != 0 || !observer.Equals(expected))
                throw new UnsupportedSignatureException("Actual source reference observer attachment failed or changed its immutable input.");
            var info = NewInformation();
            if (_native.GetInfo(ref info) != 0 || !InformationShape(info, _sourceEpoch) || info.Unknown != 0)
                throw new UnsupportedSignatureException("The attached source reference observer has no matching known native identity.");
            _observerEpoch = info.ObserverEpoch;
        }
        catch (Exception error) { Fault(error); throw; }
    }

    private unsafe uint Notify(IntPtr actualNotice, IntPtr user)
    {
        Scope? scope = null;
        var step = "thread access";
        NativeTweenSourceReferenceNotice? copiedNotice = null;
        var origin = new NativeTweenSourceReferenceOrigin { Version = 1, Size = 80 };
        try
        {
            // A worker stops before reading even the notice scalar keys, native
            // transport access or Root. Its fault is still permanently recorded.
            VerifyAccess();
            step = "callback identity validation";
            if (user != IntPtr.Zero || actualNotice == IntPtr.Zero)
                throw new UnsupportedSignatureException("The source reference callback has a foreign observer identity.");
            // These 56 bytes are scalar values only. In particular, neither
            // Origin nor References is dereferenced before native TLS proof.
            step = "native notice copy";
            var keys = Marshal.PtrToStructure<NativeTweenSourceReferenceNotice>(actualNotice);
            var notice = new NativeTweenSourceReferenceNotice { Version = 1, Size = 56 };
            NativeTweenSourceBorrowedReference* references = stackalloc NativeTweenSourceBorrowedReference[MaximumReferencesPerNotice];
            var status = _native.ReadCurrentNotice(actualNotice, keys.ObserverEpoch, keys.ScopeId, keys.DeliverySequence,
                (IntPtr)(&notice), (IntPtr)(&origin), (IntPtr)references, MaximumReferencesPerNotice);
            if (status != 0)
                throw new UnsupportedSignatureException($"The native transport rejected the current source reference notice copy (status={status}).");
            step = "copied notice validation";
            if (!NoticeShape(notice) || notice.Version != keys.Version || notice.Size != keys.Size
                || notice.ObserverEpoch != _observerEpoch || notice.ObserverEpoch != keys.ObserverEpoch
                || notice.ScopeId != keys.ScopeId || notice.DeliverySequence != keys.DeliverySequence
                || notice.Phase != keys.Phase || notice.ReferenceCount != keys.ReferenceCount
                || notice.Origin != (IntPtr)(&origin)
                || notice.References != (notice.ReferenceCount == 0 ? IntPtr.Zero : (IntPtr)references)
                || notice.DeliverySequence <= _lastDelivery)
                throw new UnsupportedSignatureException("The validated native notice did not return its exact bounded caller-owned copies.");
            copiedNotice = notice;
            _lastDelivery = notice.DeliverySequence;
            step = "scope reservation";
            if (!_scopes.TryGetValue(notice.ScopeId, out scope))
            {
                if (notice.ScopeId <= _highestScopeIdentity)
                    throw new UnsupportedSignatureException("A completed source scope identity cannot be reused.");
                _highestScopeIdentity = notice.ScopeId;
                scope = new Scope(origin) { ScopeId = notice.ScopeId, Consumers = SnapshotConsumers() };
                _scopes.Add(notice.ScopeId, scope);
            }
            step = "root retention";
            var rows = new RootedReference[notice.ReferenceCount];
            var newRoots = new List<RootRecord>();
            // Reserve ALL responsibility slots before the first Root call.
            // Nulls and repeated roles remain separate occurrence records.
            for (var i = 0; i < rows.Length; ++i)
            {
                var borrowed = references[i]; RootRecord? root = null;
                if (borrowed.Value != IntPtr.Zero && scope.Consumers.Count != 0 && !_roots.TryGetValue(borrowed.Value, out root))
                {
                    root = new RootRecord(borrowed.Value, checked(++_rootGeneration)); _roots.Add(borrowed.Value, root); newRoots.Add(root);
                }
                if (root is not null && scope.Roots.Add(root)) root.Scopes.Add(scope);
                rows[i] = new RootedReference(this, _issuer, borrowed, origin, notice.ObserverEpoch, notice.ScopeId,
                    notice.DeliverySequence, notice.Phase, root);
                scope.References.Add(rows[i]);
            }
            Exception? allocationFailure = null;
            foreach (var record in newRoots)
            {
                // An uncertain allocation is never retried later from a raw
                // journal address. Continue rooting the other direct references.
                record.Attempted = true;
                try
                {
                    var root = _adopt(record.Identity); record.Root = root;
                    if (root is null) throw new TargetExpiredException("Source reference Root returned no independent handle.");
                }
                catch (Exception error) { record.Failure = error; allocationFailure ??= error; }
            }
            if (allocationFailure is not null) throw allocationFailure;

            // Every returned Root is now recorded. Only here may origin binding,
            // role metadata, object class or any GC-handle Target be inspected.
            step = "source origin and scope validation";
            if (_scopes.Count > MaximumScopes || scope.References.Count > MaximumReferencesPerScope)
                throw new UnsupportedSignatureException("The source reference observer exceeded its complete scope or reference responsibility bound.");
            ValidateOrigin(origin); ValidateScope(scope, origin, notice.Phase,
                rows.Any(row => row.Raw.Value != IntPtr.Zero));
            _production?.VerifyReferenceOrigin(origin);
            step = "rooted reference validation";
            foreach (var row in rows)
            {
                var role = (uint)row.Raw.Role;
                if (role == 0 || (role & (role - 1)) != 0 || (role & ~(uint)origin.OriginRoles) != 0)
                    throw new UnsupportedSignatureException("A source reference role is not one direct role declared by its actual origin.");
                var kind = ReferenceKind(row.Raw.Role, origin.Site);
                if (row.Raw.Value != IntPtr.Zero && scope.Consumers.Count != 0)
                {
                    var record = row.Record ?? throw new TargetExpiredException("Source reference generation was lost.");
                    var root = record.Root ?? throw new TargetExpiredException("A prior source reference root allocation is permanently unknown.");
                    if (root.Target != row.Raw.Value || _validate(root, kind) != row.Raw.Value || root.Target != row.Raw.Value)
                        throw new UnsupportedSignatureException("A synchronously rooted source reference changed its exact identity or official object class.");
                }
                row.CompleteValidation(kind, _issuer);
            }
            if (notice.Phase == NativeTweenSourceReferencePhase.Abnormal)
            { scope.Failed = true; Interlocked.Exchange(ref _unknown, 1); }
            step = "source reference consumer";
            var receipt = new Receipt(this, _issuer, scope, origin, notice, rows, scope.References.ToArray());
            var frame = new CallbackFrame(this, receipt, _current); _current = frame;
            try { _consume(receipt); }
            finally { _current = frame.Parent; }
            step = "delivery acknowledgment";
            if (Unknown || scope.Failed)
                throw new UnsupportedSignatureException("The source reference observer or this scope is permanently unknown; subsequent notices retain responsibility only.");
            receipt.Acknowledge(_issuer);
            // End belongs to the native source. Dropping this successful CLR
            // scope does not free a root or assert any listener/game completion.
            if (notice.Phase == NativeTweenSourceReferencePhase.AfterCommitted
                || notice.Phase == NativeTweenSourceReferencePhase.BeforeCommitted && origin.Stage == NativeTweenSourceStage.Decision)
            {
                scope.Completed = true; _scopes.Remove(notice.ScopeId); _completedScopes.Add(scope);
            }
            return 0;
        }
        catch (Exception error)
        {
            if (scope is not null) scope.Failed = true;
            if (Fault(error)) ReportCallbackFailure(step, copiedNotice, origin, error);
            return 5;
        }
    }

    private void ValidateOrigin(NativeTweenSourceReferenceOrigin origin)
    {
        if (origin.Version != 1 || origin.Size != 80 || (uint)origin.Operation > 2 || origin.ThreadId != _threadId
            || origin.Depth is < 1 or > 16 || (uint)origin.Site >= NativeTweenSource.SiteCount || (uint)origin.Stage > 2
            || ((uint)origin.OriginRoles & ~63u) != 0 || origin.SourceEpoch != _sourceEpoch
            || origin.OperationSequence == 0 || origin.OperationSequence == origin.ParentOperationSequence || origin.Transaction == 0
            || origin.SourcePc == IntPtr.Zero || origin.RelayReturnPc == IntPtr.Zero
            || (origin.Site == NativeTweenSourceSite.TailDeadDecision) != (origin.Stage == NativeTweenSourceStage.Decision)
            || ((uint)origin.Site <= 18 ? origin.Operation != NativeTweenSourceOperation.Update
                : (uint)origin.Site <= 20 ? origin.Operation != NativeTweenSourceOperation.Play : origin.Operation != NativeTweenSourceOperation.Execute))
            throw new UnsupportedSignatureException("The actual copied source reference origin has a foreign operation, thread, epoch or source shape.");
    }
    private static void ValidateScope(Scope scope, NativeTweenSourceReferenceOrigin origin,
        NativeTweenSourceReferencePhase phase, bool hasReference)
    {
        var initial = scope.Origin; var same = origin; same.Stage = initial.Stage;
        if (!same.Equals(initial) || initial.Stage == NativeTweenSourceStage.After)
            throw new UnsupportedSignatureException("A source reference scope crossed its actual immutable source origin.");
        var decision = initial.Stage == NativeTweenSourceStage.Decision;
        switch (phase)
        {
            case NativeTweenSourceReferencePhase.References:
                if (origin.Stage != initial.Stage && (decision || origin.Stage != NativeTweenSourceStage.After || !scope.Before))
                    throw new UnsupportedSignatureException("Source references crossed their actual source phase.");
                if (origin.Stage == initial.Stage)
                    scope.BeforeReferences = scope.BeforeReferences || hasReference;
                else
                    scope.AfterReferences = scope.AfterReferences || hasReference;
                break;
            case NativeTweenSourceReferencePhase.BeforeCommitted:
                if (scope.Before || !scope.BeforeReferences || origin.Stage != initial.Stage)
                    throw new UnsupportedSignatureException("Source BeforeCommitted has no preceding actual reference or is repeated or foreign.");
                scope.Before = true; break;
            case NativeTweenSourceReferencePhase.AfterCommitted:
                if (decision || !scope.Before || !scope.AfterReferences || scope.After
                    || origin.Stage != NativeTweenSourceStage.After)
                    throw new UnsupportedSignatureException("Source AfterCommitted has no matching actual before and after references.");
                scope.After = true; break;
            case NativeTweenSourceReferencePhase.Abnormal:
                if (scope.Abnormal) throw new UnsupportedSignatureException("Source Abnormal is repeated.");
                scope.Abnormal = true; break;
        }
    }
    private static NativeTweenSourceObjectKind ReferenceKind(NativeTweenSourceReferenceRole role, NativeTweenSourceSite site) => role switch
    {
        NativeTweenSourceReferenceRole.Manager => NativeTweenSourceObjectKind.Manager,
        NativeTweenSourceReferenceRole.Tween or NativeTweenSourceReferenceRole.Prior => NativeTweenSourceObjectKind.Tween,
        NativeTweenSourceReferenceRole.Array => NativeTweenSourceObjectKind.TweenArray,
        NativeTweenSourceReferenceRole.Event => NativeTweenSourceObjectKind.UnityEvent,
        NativeTweenSourceReferenceRole.Container when (uint)site is 0 or 1 or 7 or 8 => NativeTweenSourceObjectKind.ReadyStack,
        NativeTweenSourceReferenceRole.Container when (uint)site is >= 9 and <= 12 => NativeTweenSourceObjectKind.ScheduledList,
        _ => throw new UnsupportedSignatureException("A source reference role has no exact official object class at this source site.")
    };
    private void VerifyAccess()
    {
        _verify();
        if (Environment.CurrentManagedThreadId != _managedThreadId || CurrentThreadId() != _threadId)
            throw new InvalidOperationException("Source reference observation requires its verified Unity lifecycle OS thread.");
    }
    private bool Fault(Exception error)
    {
        Interlocked.Exchange(ref _unknown, 1); Interlocked.Increment(ref _callbackFaults);
        return Interlocked.CompareExchange(ref _firstFailure, error, null) is null;
    }
    private void ReportCallbackFailure(string step, NativeTweenSourceReferenceNotice? copiedNotice,
        NativeTweenSourceReferenceOrigin origin, Exception error)
    {
        if (_diagnostics is null) return;
        try
        {
            // The copies passed native TLS and buffer checks. Log only scalars,
            // including when a subsequent managed origin or root check failed.
            var context = copiedNotice is { } notice
                ? $" {origin.Site} ({origin.Stage}), {origin.Operation} sequence {origin.OperationSequence}, parent sequence {origin.ParentOperationSequence}, transaction {origin.Transaction}, scope {notice.ScopeId}, delivery {notice.DeliverySequence}, phase {notice.Phase}."
                : " Source identity was unavailable.";
            _diagnostics.Log(LogLevel.Error, $"Tween source reference callback failed during {step}.{context}", error);
        }
        catch { /* The recorded failure must be returned to the native source even if logging fails. */ }
    }
    internal static uint CurrentThreadId() => OperatingSystem.IsWindows() ? GetCurrentThreadId() : unchecked((uint)Environment.CurrentManagedThreadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    internal static NativeTweenSourceReferencesInformation NewInformation() => new() { Version = 1, Size = 104 };
    internal static bool InformationShape(NativeTweenSourceReferencesInformation info, ulong sourceEpoch)
        => info.Version == 1 && info.Size == 104 && info.Attached == 1 && info.Unknown <= 1 && info.ScopeCapacity == MaximumScopes
            && info.SourceEpoch == sourceEpoch && sourceEpoch != 0 && info.ObserverEpoch != 0
            && info.ActiveScopes <= info.RetainedScopes && info.RetainedScopes <= MaximumScopes
            && info.CallbackInFlight <= info.ActiveScopes && info.ScopesCompleted <= info.ScopesEnded && info.ScopesEnded <= info.ScopesBegun
            && info.ScopesBegun - info.ScopesEnded == info.ActiveScopes && info.ScopesBegun - info.ScopesCompleted == info.RetainedScopes
            && info.DeliveriesCompleted <= info.DeliveryAttempts && info.CallbackFaults <= info.DeliveryAttempts;
    private static bool NoticeShape(NativeTweenSourceReferenceNotice notice)
        => notice.Version == 1 && notice.Size == 56 && (uint)notice.Phase <= 3 && notice.ReferenceCount <= MaximumReferencesPerNotice
            && notice.ObserverEpoch != 0 && notice.ScopeId != 0 && notice.DeliverySequence != 0
            && (notice.Phase == NativeTweenSourceReferencePhase.References ? notice.ReferenceCount != 0 : notice.ReferenceCount == 0);
    internal static void ValidateLayout()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<NativeTweenSourceReferenceOrigin>() != 80
            || Marshal.SizeOf<NativeTweenSourceBorrowedReference>() != 16 || Marshal.SizeOf<NativeTweenSourceReferenceNotice>() != 56
            || Marshal.SizeOf<NativeTweenSourceReferencesObserver>() != 24 || Marshal.SizeOf<NativeTweenSourceReferencesTicket>() != 56
            || Marshal.SizeOf<NativeTweenSourceReferencesInformation>() != 104
            || Marshal.OffsetOf<NativeTweenSourceReferenceNotice>(nameof(NativeTweenSourceReferenceNotice.ObserverEpoch)).ToInt32() != 16
            || Marshal.OffsetOf<NativeTweenSourceReferenceNotice>(nameof(NativeTweenSourceReferenceNotice.Origin)).ToInt32() != 40
            || Marshal.OffsetOf<NativeTweenSourceReferenceOrigin>(nameof(NativeTweenSourceReferenceOrigin.SourceEpoch)).ToInt32() != 32
            || Marshal.OffsetOf<NativeTweenSourceReferenceOrigin>(nameof(NativeTweenSourceReferenceOrigin.SourcePc)).ToInt32() != 64)
            throw new UnsupportedSignatureException("Source reference V1 wire layout differs from the actual native side ABI.");
    }

    internal sealed class RootRecord
    {
        internal readonly IntPtr Identity;
        internal readonly ulong Generation;
        internal readonly HashSet<Scope> Scopes = new();
        internal readonly HashSet<RootBorrow> Borrowers = new();
        internal NativeRuntime.RootHandle? Root;
        internal bool Attempted, FreeAttempted, Freed;
        internal Exception? Failure;
        internal RootRecord(IntPtr identity, ulong generation) => (Identity, Generation) = (identity, generation);
    }
    internal sealed class Scope
    {
        internal readonly NativeTweenSourceReferenceOrigin Origin;
        internal readonly List<RootedReference> References = new();
        internal readonly HashSet<RootRecord> Roots = new();
        internal IReadOnlyList<object> Consumers = Array.Empty<object>();
        internal ulong ScopeId;
        internal bool BeforeReferences, AfterReferences, Before, After, Abnormal, Failed, Completed, NativeEnded;
        internal Scope(NativeTweenSourceReferenceOrigin origin) => Origin = origin;
    }
    private sealed record CallbackFrame(NativeTweenSourceReferences Owner, Receipt Receipt, CallbackFrame? Parent);

    internal sealed class RootedReference
    {
        private readonly NativeTweenSourceReferences _owner;
        private readonly NativeTweenSourceBorrowedReference _raw;
        private NativeTweenSourceObjectKind? _kind;
        private readonly NativeTweenSourceReferenceOrigin _origin;
        private readonly ulong _observerEpoch, _scopeId, _deliverySequence;
        private readonly NativeTweenSourceReferencePhase _phase;
        internal RootRecord? Record { get; }
        internal NativeTweenSourceBorrowedReference Raw { get { _owner.VerifyAccess(); return _raw; } }
        internal IntPtr Identity { get { _owner.VerifyAccess(); return _raw.Value; } }
        internal NativeTweenSourceReferenceRole Role { get { _owner.VerifyAccess(); return _raw.Role; } }
        internal int SourceIndex { get { _owner.VerifyAccess(); return _raw.SourceIndex; } }
        internal NativeRuntime.RootHandle? Root { get { _owner.VerifyAccess(); return _raw.Value == IntPtr.Zero ? null : _owner.ReadCaptureRoot(this); } }
        internal ulong RootGeneration { get { _owner.VerifyAccess(); return Record?.Generation ?? 0; } }
        internal NativeTweenSourceObjectKind Kind { get { _owner.VerifyAccess(); return _kind
            ?? throw new InvalidOperationException("This source reference responsibility has not completed official object validation."); } }
        internal NativeTweenSourceReferenceOrigin Origin { get { _owner.VerifyAccess(); return _origin; } }
        internal ulong ObserverEpoch { get { _owner.VerifyAccess(); return _observerEpoch; } }
        internal ulong ScopeId { get { _owner.VerifyAccess(); return _scopeId; } }
        internal ulong DeliverySequence { get { _owner.VerifyAccess(); return _deliverySequence; } }
        internal NativeTweenSourceReferencePhase Phase { get { _owner.VerifyAccess(); return _phase; } }
        internal RootedReference(NativeTweenSourceReferences owner, object issuer, NativeTweenSourceBorrowedReference raw,
            NativeTweenSourceReferenceOrigin origin, ulong observerEpoch, ulong scopeId, ulong deliverySequence, NativeTweenSourceReferencePhase phase,
            RootRecord? record = null)
        {
            if (!ReferenceEquals(issuer, owner._issuer)) throw new InvalidOperationException("Foreign source reference root issuer.");
            (_owner, _raw, _origin, _observerEpoch, _scopeId, _deliverySequence, _phase)
                = (owner, raw, origin, observerEpoch, scopeId, deliverySequence, phase);
            Record = record;
        }
        internal void CompleteValidation(NativeTweenSourceObjectKind kind, object issuer)
        { if (!ReferenceEquals(issuer, _owner._issuer)) throw new InvalidOperationException("Foreign source reference validation issuer."); _kind = kind; }
    }

    /// <summary>Private-issuer receipt for one actual synchronous delivery. Scalar
    /// copies or caller-created DTOs cannot construct this class. VerifyCurrent
    /// is required at consumption; VerifyIssuedBy checks an acknowledged retained
    /// copy later and grants no current callback scope or journal retroactivity.</summary>
    internal sealed class Receipt
    {
        private readonly NativeTweenSourceReferences _owner;
        private readonly object _issuer;
        private readonly Scope _scope;
        private readonly NativeTweenSourceReferenceOrigin _origin;
        private readonly NativeTweenSourceReferenceNotice _notice;
        private readonly IReadOnlyList<RootedReference> _references, _scopeReferences;
        private bool _acknowledged;
        internal bool Production => _owner.Production;
        internal bool Known { get { _owner.VerifyAccess(); return !_owner.Unknown && !_scope.Failed; } }
        internal NativeTweenSourceReferenceOrigin Origin { get { _owner.VerifyAccess(); return _origin; } }
        internal NativeTweenSourceReferencePhase Phase { get { _owner.VerifyAccess(); return _notice.Phase; } }
        internal ulong ObserverEpoch { get { _owner.VerifyAccess(); return _notice.ObserverEpoch; } }
        internal ulong ScopeId { get { _owner.VerifyAccess(); return _notice.ScopeId; } }
        internal ulong DeliverySequence { get { _owner.VerifyAccess(); return _notice.DeliverySequence; } }
        internal IReadOnlyList<RootedReference> References { get { _owner.VerifyAccess(); return _references; } }
        internal IReadOnlyList<RootedReference> ScopeReferences { get { _owner.VerifyAccess(); return _scopeReferences; } }
        internal Receipt(NativeTweenSourceReferences owner, object issuer, Scope scope, NativeTweenSourceReferenceOrigin origin,
            NativeTweenSourceReferenceNotice notice, RootedReference[] references, RootedReference[] scopeReferences)
        {
            if (!ReferenceEquals(issuer, owner._issuer)) throw new InvalidOperationException("Foreign source reference receipt issuer.");
            // Only scalar keys survive the callback. The verified copy buffers
            // are stack-local and can never become a deferred pointer read seam.
            notice.Origin = notice.References = IntPtr.Zero;
            (_owner, _issuer, _scope, _origin, _notice) = (owner, issuer, scope, origin, notice);
            _references = Array.AsReadOnly(references); _scopeReferences = Array.AsReadOnly(scopeReferences);
        }
        internal void VerifyCurrent(NativeTweenSourceReferences owner)
        {
            VerifyOwner(owner);
            if (_current is not { } current || !ReferenceEquals(current.Owner, owner) || !ReferenceEquals(current.Receipt, this))
                throw new InvalidOperationException("A source reference receipt is outside its actual synchronous callback scope.");
        }
        internal void VerifyIssuedBy(NativeTweenSourceReferences owner)
        {
            VerifyOwner(owner);
            if (!_acknowledged || !Known)
                throw new UnsupportedSignatureException("The actual source reference delivery was not acknowledged as known.");
        }
        internal bool IncludesConsumer(object consumer, NativeTweenSourceRootSet? roots = null)
        {
            _owner.VerifyAccess();
            return _scope.Consumers.Any(value => ReferenceEquals(value, consumer)
                || value is ScopeReaderMembership membership && ReferenceEquals(membership.Registration, consumer)
                    && (roots is null || ReferenceEquals(membership.Roots, roots)));
        }
        private void VerifyOwner(NativeTweenSourceReferences owner)
        {
            _owner.VerifyAccess(); ArgumentNullException.ThrowIfNull(owner);
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(_issuer, owner._issuer))
                throw new InvalidOperationException("The source reference receipt has a foreign private issuer.");
        }
        internal void Acknowledge(object issuer)
        { if (!ReferenceEquals(issuer, _issuer)) throw new InvalidOperationException("Foreign source reference acknowledgment issuer."); _acknowledged = true; }
    }

    private sealed class LoadedTransport : INativeTweenSourceReferencesTransport
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetInfoNative(ref NativeTweenSourceReferencesInformation information);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadCurrentNoticeNative(IntPtr actualNotice,
            ulong observerEpoch, ulong scopeId, ulong deliverySequence, IntPtr noticeCopy, IntPtr originCopy, IntPtr referencesCopy, uint capacity);
        private readonly NativeFirstInstall.TweenSourceReady _ready;
        private readonly GetInfoNative _info;
        private readonly ReadCurrentNoticeNative _read;
        internal LoadedTransport(NativeFirstInstall.TweenSourceReady ready)
        {
            _ready = ready;
            var info = NativeLibrary.GetExport(ready.HelperModule, "RizTweenSourceReferences_GetInfo");
            var read = NativeLibrary.GetExport(ready.HelperModule, "RizTweenSourceReferences_ReadCurrentNotice");
            HookTargetValidation.VerifyExecutable(info, ready.HelperModule); HookTargetValidation.VerifyExecutable(read, ready.HelperModule);
            _info = Marshal.GetDelegateForFunctionPointer<GetInfoNative>(info);
            _read = Marshal.GetDelegateForFunctionPointer<ReadCurrentNoticeNative>(read);
        }
        public uint Attach(ref NativeTweenSourceReferencesObserver observer) { _ready.AttachReferences(ref observer); return 0; }
        public uint GetInfo(ref NativeTweenSourceReferencesInformation information) => _info(ref information);
        public uint ReadCurrentNotice(IntPtr actualNotice, ulong observerEpoch, ulong scopeId, ulong deliverySequence,
            IntPtr noticeCopy, IntPtr originCopy, IntPtr referencesCopy, uint capacity)
            => _read(actualNotice, observerEpoch, scopeId, deliverySequence, noticeCopy, originCopy, referencesCopy, capacity);
    }
}
