using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeTweenSourceSite : uint
{
    ReadyPopFront, ReadyPopAppend, PlayingInsertFront, PlayingInsertAppend, PlayingMoveWrite, PlayingMoveClear,
    PlayingTailClear, ScheduledPushFront, ScheduledPushTail, ScheduledSetItem, ScheduledRemoveAdvance,
    ScheduledRemoveTail, ScheduledRemoveCurrent, TailDeadDecision, PlayingExecuteFront, PlayingExecuteExisting,
    PlayingExecuteAppend, UpdateOnStartFront, UpdateOnStartAppend, PlayOnStart, PlayOnComplete, ExecuteOnComplete
}
internal enum NativeTweenSourceOperation : uint { Update, Play, Execute }
internal enum NativeTweenSourceStage : uint { Before, After, Decision }
[Flags] internal enum NativeTweenSourceFlags : uint
{ Known = 1, PopSucceeded = 2, ExactDead = 4, Redirected = 8, ParentMatched = 16, Unknown = 32, Abnormal = 64, ReadFault = 128, Paired = 256 }

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceRecord
{
    internal uint Version, Size;
    internal NativeTweenSourceSite Site;
    internal NativeTweenSourceStage Stage;
    internal NativeTweenSourceFlags Flags;
    internal uint ThreadId, Depth;
    internal NativeTweenSourceOperation Operation;
    // Wire names retained from V1: these are actual operation sequences, as
    // selected by Operation. An independent Play never acquires a fake Update.
    internal ulong Epoch, UpdateSequence, ParentUpdateSequence, EventSequence, Transaction;
    internal IntPtr SourcePc, RelayReturnPc, Manager, Container, ArrayBefore, ArrayAfter, Tween, Prior;
    internal int SourceIndex, DestinationIndex, CountBefore, CountAfter, CapacityBefore, CapacityAfter, VersionBefore, VersionAfter;
    internal ulong RawResult, Occurrence, Reserved1;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceInformation
{
    internal uint Version, Size, Configured, Unknown, BindingCount, JournalCapacity, ActiveUpdates, MetadataBound;
    internal ulong Epoch, Events, Transactions, CompletedTransactions, Redirects, Faults, PendingTransactions, RetainedRecords;
}
internal sealed record NativeTweenSourceRootReference(NativeRuntime.RootHandle Root, NativeTweenSourceObjectKind Kind,
    bool OwnershipAnchor = true);
internal interface INativeTweenSourceOwner { void VerifyTweenSourceOwner(); }
internal interface INativeTweenSourceTransport
{
    uint GetInfo(ref NativeTweenSourceInformation information);
    uint ReadRecord(ulong epoch, ulong eventSequence, ref NativeTweenSourceRecord record);
}

/// <summary>A non-consuming journal reader over independently rooted identities.
/// New production references arrive through the same producer's synchronous
/// source observer. Neither path grants operation or effects completion.</summary>
internal sealed partial class NativeTweenSource
{
    internal const uint SiteCount = 22, BindingCount = 43, JournalCapacity = 8192;
    private static readonly HashSet<NativeTweenSource> ProcessOwners = new();
    private readonly object _issuer = new();
    private readonly INativeTweenSourceTransport _native;
    private readonly Action _verify;
    private readonly Func<IntPtr, NativeRuntime.RootHandle> _adopt;
    private readonly Func<NativeRuntime.RootHandle, NativeTweenSourceObjectKind, IntPtr> _validate;
    private readonly List<NativeTweenSourceRootSet> _sets = new();
    private readonly NativeFirstInstall.TweenSourceReady? _production;
    private readonly ulong _epoch;
    private readonly int _managedThread = Environment.CurrentManagedThreadId;
    private readonly uint _osThread = NativeTweenSourceReferences.CurrentThreadId();
    private AvailableRead? _availableRead;
    private ulong _lastEvents, _lastTransactions, _lastCompleted;
    private int _unknown;
    internal bool Production => _production is not null;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0 || _references?.Unknown == true;
    internal int RootRecords { get { _verify(); return _sets.Sum(set => set.RootRecords); } }

    private NativeTweenSource(INativeTweenSourceTransport native, Action verify,
        Func<IntPtr, NativeRuntime.RootHandle> adopt, Func<NativeRuntime.RootHandle, NativeTweenSourceObjectKind, IntPtr> validate,
        NativeFirstInstall.TweenSourceReady? production = null)
    {
        if (production is not null && (native is not LoadedTransport loaded || !production.IsIssuedBy(loaded.Producer)))
            throw new InvalidOperationException("A production source reader requires its exact loaded transport owner.");
        (_native, _verify, _adopt, _validate) = (native, verify, adopt, validate);
        _production = production;
        ValidateLayout(); var info = ReadInformation(); _epoch = info.Epoch;
    }

    internal static NativeTweenSource CreateControlledForTests(INativeTweenSourceTransport native, Action verify,
        Func<IntPtr, NativeRuntime.RootHandle> adopt, Func<NativeRuntime.RootHandle, NativeTweenSourceObjectKind, IntPtr> validate)
    {
        ArgumentNullException.ThrowIfNull(native); ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(adopt); ArgumentNullException.ThrowIfNull(validate); verify();
        var reader = new NativeTweenSource(native, verify, adopt, validate);
        lock (ProcessOwners) ProcessOwners.Add(reader); return reader;
    }

    /// <summary>Actual helper reads remain diagnostic until a private complete
    /// publication owner exists. This never loads, patches or configures a DLL.</summary>
    internal static NativeTweenSource OpenLoadedReadOnly(NativeRuntime runtime, Action verify)
    {
        verify(); var ready = NativeFirstInstall.RequireReady(); ready.VerifyReady();
        var metadata = runtime.InspectTweenSourcesMetadata(ready.Module);
        var reader = new NativeTweenSource(new LoadedTransport(ready), verify, runtime.Root,
            (root, kind) => runtime.ValidateRootedTweenSourceObject(root, kind, metadata, ready.Module));
        lock (ProcessOwners) ProcessOwners.Add(reader); return reader;
    }
    internal static NativeTweenSource BindProduction(NativeRuntime runtime, Action verify)
    {
        ArgumentNullException.ThrowIfNull(runtime); ArgumentNullException.ThrowIfNull(verify); verify();
        var ready = NativeFirstInstall.RequireReady(); var source = ready.RequireTweenSourceReady();
        var metadata = runtime.InspectTweenSourcesMetadata(source.Module);
        var admission = runtime.InspectTweenAdmissionMetadata(source.Module);
        source.BindMetadata(metadata, admission.CachedPointerField.Field);
        var reader = new NativeTweenSource(new LoadedTransport(ready), verify, runtime.Root,
            (root, kind) => runtime.ValidateRootedTweenSourceObject(root, kind, metadata, source.Module), source);
        lock (ProcessOwners) ProcessOwners.Add(reader);
        reader.AttachProductionReferences(runtime, source); return reader;
    }

    internal NativeTweenSourceRootSet RetainRootedObjects(INativeTweenSourceOwner owner,
        IReadOnlyList<NativeTweenSourceRootReference> references)
    {
        _verify(); ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(references);
        RequireKnown(); owner.VerifyTweenSourceOwner();
        if (_withdrawals.Count != 0) throw new InvalidOperationException("A withdrawing source reader cannot enroll a new root set.");
        if (_sets.Count >= 32 || references.Count is < 1 or > 2048)
            throw new InvalidOperationException("Tween source root responsibility exceeds its fixed complete bound.");
        var set = new NativeTweenSourceRootSet(this, owner, _issuer); _sets.Add(set);
        _referenceRegistration?.AddSet(set);
        try
        {
            foreach (var reference in references)
            {
                ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(reference.Root);
                var value = reference.Root.Target;
                if (set.TryGetKind(value, out var existing))
                {
                    if (existing != reference.Kind) throw new UnsupportedSignatureException("One source object was assigned incompatible retained roles.");
                    if (reference.OwnershipAnchor) set.MarkInitialOwnershipAnchor(value, _issuer);
                    continue;
                }
                // The borrowed root already exists. Record the independent root
                // before class/field validation can throw, and never reclaim it
                // after an unknown read or unfinished source transaction.
                var entry = set.ReserveRoot(value, reference.Kind, _issuer, reference.OwnershipAnchor);
                var retained = _adopt(value); set.RecordRoot(entry, retained, _issuer);
                if (_validate(retained, reference.Kind) != value)
                    throw new UnsupportedSignatureException("The independently rooted source object changed its exact identity.");
            }
            var info = ReadInformation(); owner.VerifyTweenSourceOwner();
            // No earlier journal row can be retroactively turned into a rooted
            // source capture by retaining a raw pointer after the operation.
            set.CompleteRetention(info.Events, _issuer); return set;
        }
        catch { Fault(); throw; }
    }

    internal NativeTweenSourceBatch ReadAvailable(NativeTweenSourceRootSet roots, ulong afterSequence, int maximum)
    {
        VerifyAvailableThread();
        var scope = new AvailableRead(this);
        if (Interlocked.CompareExchange(ref _availableRead, scope, null) is not null)
        {
            Fault();
            throw new InvalidOperationException("A source journal batch cannot be reentered.");
        }
        try
        {
            _verify(); VerifySet(roots); RequireKnown();
            if (maximum is < 1 or > 8192 || afterSequence != roots.LastReadSequence)
                throw new ArgumentOutOfRangeException(nameof(afterSequence), "Read the next bounded continuous journal interval from this retained root set.");
            try
            {
                scope.Open();
                NativeTweenSourceBatch batch;
                try { batch = ReadAvailableCore(roots, afterSequence, maximum, scope); }
                catch (Exception readFailure)
                {
                    try { scope.VerifyExit(); }
                    catch (Exception verificationFailure)
                    { throw new AggregateException("The source journal read and its publication exit check failed.", readFailure, verificationFailure); }
                    throw;
                }
                scope.VerifyExit(); RequireKnown(); VerifySet(roots);
                roots.Advance(batch.LastSequence, _issuer);
                return batch;
            }
            catch { Fault(); throw; }
        }
        finally { Volatile.Write(ref _availableRead, null); }
    }

    private NativeTweenSourceBatch ReadAvailableCore(NativeTweenSourceRootSet roots, ulong afterSequence, int maximum, AvailableRead scope)
    {
        roots.Owner.VerifyTweenSourceOwner(); VerifyRoots(roots);
        var before = ReadInformation(scope);
        if (afterSequence > before.Events) throw new UnsupportedSignatureException("Source journal sequence regressed before reading.");
        if (before.Events - afterSequence > before.RetainedRecords)
            throw new UnsupportedSignatureException("The source reader fell behind the actual retained journal window.");
        var end = roots.Withdrawal is { } withdrawal ? Math.Min(before.Events, withdrawal.JournalCutoff) : before.Events;
        if (afterSequence > end) throw new UnsupportedSignatureException("The frozen source reader advanced past its actual withdrawal cutoff.");
        var count = Math.Min((ulong)maximum, end - afterSequence);
        var records = new List<NativeTweenSourceBoundRecord>();
        for (ulong offset = 1; offset <= count; ++offset)
        {
            scope.VerifyCurrent();
            var sequence = checked(afterSequence + offset); var record = new NativeTweenSourceRecord { Version = 1, Size = 192 };
            if (_native.ReadRecord(_epoch, sequence, ref record) != 0 || !RecordShape(record, _epoch, sequence))
                throw new UnsupportedSignatureException("A retained source row is missing, foreign or unknown.");
            _production?.VerifyAvailableRecordBinding(record, scope);
            roots.ObserveUnownedTweenOperation(record, _issuer);
            // Foreign operations advance the global journal cursor without
            // dereferencing any of their borrowed object identities.
            if (!IsRelevant(roots, record)) continue;
            foreach (var pointer in ObjectTokens(record))
                if (pointer != IntPtr.Zero && !roots.HasRetainedObjectAt(pointer, sequence))
                    throw new UnsupportedSignatureException("A source row references an object not rooted before this journal interval.");
            records.Add(new(this, roots, record, _issuer));
        }
        var after = ReadInformation(scope); VerifyRoots(roots); roots.Owner.VerifyTweenSourceOwner();
        if (after.Events < before.Events) throw new UnsupportedSignatureException("The non-consuming source journal regressed.");
        var last = checked(afterSequence + count);
        return new(this, roots, _epoch, afterSequence, last, records, _issuer);
    }

    private void VerifyAvailableThread()
    {
        if (_managedThread != Environment.CurrentManagedThreadId || _osThread != NativeTweenSourceReferences.CurrentThreadId())
            throw new InvalidOperationException("The source journal batch requires its original managed and native thread.");
    }

    internal sealed class AvailableRead
    {
        private readonly NativeTweenSource _reader;
        private readonly LoadedTransport? _loaded;
        private bool _open;

        internal AvailableRead(NativeTweenSource reader) => (_reader, _loaded) = (reader, reader._native as LoadedTransport);

        internal void Open()
        {
            VerifyOwner();
            _loaded?.Producer.VerifyReady();
            _open = true;
        }

        internal void VerifyExit()
        {
            VerifyOwner();
            _loaded?.Producer.VerifyReady();
        }

        private void VerifyOwner()
        {
            _reader.VerifyAvailableThread();
            if (!ReferenceEquals(Volatile.Read(ref _reader._availableRead), this))
                throw new InvalidOperationException("The source journal read lost its exact synchronous scope.");
        }

        internal void VerifyCurrent()
        {
            VerifyOwner();
            if (!_open) throw new InvalidOperationException("The source journal read has no verified publication scope.");
            _reader.RequireKnown();
        }

        internal void VerifyRecordOwner(NativeFirstInstall.TweenSourceReady producer)
        {
            VerifyCurrent();
            if (!ReferenceEquals(producer, _reader._production))
                throw new InvalidOperationException("The source journal row belongs to another publication producer.");
        }

        internal uint ReadInformation(ref NativeTweenSourceInformation information)
        {
            VerifyCurrent();
            return _loaded is null ? _reader._native.GetInfo(ref information) : _loaded.GetInfoWithinAvailableRead(ref information);
        }
    }

    private static bool IsRelevant(NativeTweenSourceRootSet roots, NativeTweenSourceRecord record)
        => record.Manager != IntPtr.Zero && roots.TryGetAnchorKindAt(record.Manager, record.EventSequence, out var manager) && manager == NativeTweenSourceObjectKind.Manager
            || record.Tween != IntPtr.Zero && roots.TryGetAnchorKindAt(record.Tween, record.EventSequence, out var tween) && tween == NativeTweenSourceObjectKind.Tween;
    private static IEnumerable<IntPtr> ObjectTokens(NativeTweenSourceRecord record)
    { yield return record.Manager; yield return record.Container; yield return record.ArrayBefore; yield return record.ArrayAfter; yield return record.Tween; yield return record.Prior; }
    private void VerifyRoots(NativeTweenSourceRootSet roots)
    {
        foreach (var row in roots.Entries)
        {
            row.ReferenceBorrow?.VerifyRetained(row.Root ?? throw new TargetExpiredException("A source-root borrow lost its handle."));
            row.OperationAnchor?.VerifyRetained(this, roots, row.Identity);
            if (row.ReferenceReceipt is { } receipt)
            {
                receipt.VerifyIssuedBy(_references ?? throw new InvalidOperationException("The source reference observer was lost."));
                if (row.ReferenceCapture is null || !receipt.References.Contains(row.ReferenceCapture)
                    || row.ReferenceCapture.Identity != row.Identity || !ReferenceEquals(row.ReferenceCapture.Root, row.Root))
                    throw new UnsupportedSignatureException("A dynamic source root lost its actual synchronous capture occurrence.");
            }
            if (row.Root is null || row.Root.Target != row.Identity || _validate(row.Root, row.Kind) != row.Identity)
                throw new UnsupportedSignatureException("A pre-rooted source object changed during journal observation.");
        }
    }
    private void VerifySet(NativeTweenSourceRootSet roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (!ReferenceEquals(roots.Reader, this) || !_sets.Contains(roots) || !roots.Ready || roots.Retired || roots.Releasing)
            throw new InvalidOperationException("The source root set is foreign or its retention is incomplete.");
    }
    private NativeTweenSourceInformation ReadInformation(AvailableRead? scope = null)
    {
        _verify(); var info = new NativeTweenSourceInformation { Version = 1, Size = 96 };
        var status = scope is null ? _native.GetInfo(ref info) : scope.ReadInformation(ref info);
        if (status != 0 || !InformationShape(info) || _epoch != 0 && info.Epoch != _epoch
            || info.Events < _lastEvents || info.Transactions < _lastTransactions || info.CompletedTransactions < _lastCompleted)
            throw new UnsupportedSignatureException("The actual source journal is unavailable, regressed or permanently unknown.");
        (_lastEvents, _lastTransactions, _lastCompleted) = (info.Events, info.Transactions, info.CompletedTransactions); return info;
    }
    private void RequireKnown() { if (Unknown) throw new InvalidOperationException("Source journal responsibility is permanently unknown."); }
    private void Fault() => Interlocked.Exchange(ref _unknown, 1);
    internal bool IsIssuer(object issuer) => ReferenceEquals(issuer, _issuer);
    internal static bool InformationShape(NativeTweenSourceInformation info)
        => info.Version == 1 && info.Size == 96 && info.Configured == 1 && info.MetadataBound == 1 && info.Unknown == 0
            && info.BindingCount == BindingCount && info.JournalCapacity == JournalCapacity && info.Epoch != 0 && info.Faults == 0
            && info.ActiveUpdates <= 32 && info.RetainedRecords == Math.Min(info.Events, JournalCapacity)
            && info.CompletedTransactions <= info.Transactions && info.PendingTransactions <= info.Transactions;
    internal static bool RecordShape(NativeTweenSourceRecord record, ulong epoch, ulong sequence)
        => record.Version == 1 && record.Size == 192 && (uint)record.Site < SiteCount && (uint)record.Operation <= 2
            && (uint)record.Stage <= 2 && record.Epoch == epoch && record.EventSequence == sequence && sequence != 0
            && record.UpdateSequence != 0 && record.Transaction != 0 && record.ThreadId != 0 && record.Depth is > 0 and <= 16
            && record.Reserved1 == 0 && record.SourcePc != IntPtr.Zero && record.RelayReturnPc != IntPtr.Zero
            && (record.Flags & (NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched)) == (NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched)
            && (record.Flags & (NativeTweenSourceFlags.Unknown | NativeTweenSourceFlags.Abnormal | NativeTweenSourceFlags.ReadFault)) == 0
            && ((uint)record.Flags & ~511u) == 0
            && (record.Site == NativeTweenSourceSite.TailDeadDecision ? record.Stage == NativeTweenSourceStage.Decision : record.Stage != NativeTweenSourceStage.Decision)
            && (record.Operation != NativeTweenSourceOperation.Update || record.Manager != IntPtr.Zero)
            && ((uint)record.Site <= 18 ? record.Operation == NativeTweenSourceOperation.Update
                : record.Site is NativeTweenSourceSite.PlayOnStart or NativeTweenSourceSite.PlayOnComplete
                    ? record.Operation == NativeTweenSourceOperation.Play : record.Operation == NativeTweenSourceOperation.Execute);
    internal static void ValidateLayout()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<NativeTweenSourceRecord>() != 192 || Marshal.SizeOf<NativeTweenSourceInformation>() != 96
            || Marshal.OffsetOf<NativeTweenSourceRecord>(nameof(NativeTweenSourceRecord.Operation)).ToInt32() != 28
            || Marshal.OffsetOf<NativeTweenSourceRecord>(nameof(NativeTweenSourceRecord.Epoch)).ToInt32() != 32
            || Marshal.OffsetOf<NativeTweenSourceRecord>(nameof(NativeTweenSourceRecord.SourcePc)).ToInt32() != 72
            || Marshal.OffsetOf<NativeTweenSourceRecord>(nameof(NativeTweenSourceRecord.Manager)).ToInt32() != 88
            || Marshal.OffsetOf<NativeTweenSourceRecord>(nameof(NativeTweenSourceRecord.SourceIndex)).ToInt32() != 136
            || Marshal.OffsetOf<NativeTweenSourceRecord>(nameof(NativeTweenSourceRecord.RawResult)).ToInt32() != 168
            || Marshal.OffsetOf<NativeTweenSourceInformation>(nameof(NativeTweenSourceInformation.MetadataBound)).ToInt32() != 28)
            throw new UnsupportedSignatureException("The source journal differs from its fixed V1 wire.");
    }

    private sealed class LoadedTransport : INativeTweenSourceTransport
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(ref NativeTweenSourceInformation info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint RecordNative(ulong epoch, ulong sequence, ref NativeTweenSourceRecord record);
        private readonly InfoNative _info; private readonly RecordNative _record;
        internal NativeFirstInstall Producer { get; }
        internal LoadedTransport(NativeFirstInstall ready)
        {
            ready.VerifyReady(); Producer = ready;
            T Export<T>(string name) where T : Delegate
            { var address = NativeLibrary.GetExport(ready.HelperModule, name); HookTargetValidation.VerifyExecutable(address, ready.HelperModule); return Marshal.GetDelegateForFunctionPointer<T>(address); }
            _info = Export<InfoNative>("RizTweenSourceContext_GetInfo"); _record = Export<RecordNative>("RizTweenSourceContext_ReadRecord");
        }
        public uint GetInfo(ref NativeTweenSourceInformation information)
        { Producer.VerifyReady(); return _info(ref information); }
        internal uint GetInfoWithinAvailableRead(ref NativeTweenSourceInformation information)
        { Producer.VerifyRuntimeChannelOwnership(); return _info(ref information); }
        public uint ReadRecord(ulong epoch, ulong eventSequence, ref NativeTweenSourceRecord record) => _record(epoch, eventSequence, ref record);
    }
}

internal sealed partial class NativeTweenSourceRootSet
{
    internal sealed class Entry
    {
        internal IntPtr Identity { get; }
        internal NativeTweenSourceObjectKind Kind { get; }
        internal NativeRuntime.RootHandle? Root;
        internal ulong ReferenceAfterSequence;
        internal NativeTweenSourceReferences.Receipt? ReferenceReceipt;
        internal NativeTweenSourceReferences.RootedReference? ReferenceCapture;
        internal NativeTweenSource.OperationRootAnchor? OperationAnchor;
        internal bool InitialOwnershipAnchor;
        internal NativeTweenSourceRootRetention Retention = NativeTweenSourceRootRetention.ReaderOwned;
        internal NativeTweenSourceReferences.RootBorrow? ReferenceBorrow;
        internal bool FreeAttempted, Freed, BorrowWithdrawn;
        internal Exception? ReleaseFailure;
        internal NativeRuntime.RootHandle? ReleaseHandle;
        internal Entry(IntPtr identity, NativeTweenSourceObjectKind kind) => (Identity, Kind) = (identity, kind);
    }
    private readonly Dictionary<IntPtr, Entry> _entries = new();
    internal NativeTweenSource Reader { get; }
    internal INativeTweenSourceOwner Owner { get; }
    internal IEnumerable<Entry> Entries => _entries.Values;
    internal int RootRecords => _entries.Values.Count(entry => entry.Root is not null);
    internal bool Ready { get; private set; }
    internal ulong RetainedAfterSequence { get; private set; }
    internal ulong LastReadSequence { get; private set; }
    internal NativeTweenSourceRootSet(NativeTweenSource reader, INativeTweenSourceOwner owner, object issuer)
    { if (!reader.IsIssuer(issuer)) throw new InvalidOperationException("Foreign source root issuer."); (Reader, Owner) = (reader, owner); }
    internal Entry ReserveRoot(IntPtr identity, NativeTweenSourceObjectKind kind, object issuer, bool ownershipAnchor = false)
    { Verify(issuer); var entry = new Entry(identity, kind) { InitialOwnershipAnchor = ownershipAnchor }; _entries.Add(identity, entry); return entry; }
    internal void MarkInitialOwnershipAnchor(IntPtr identity, object issuer)
    { Verify(issuer); if (Ready) throw new InvalidOperationException("Initial source ownership cannot be added after retention."); _entries[identity].InitialOwnershipAnchor = true; }
    internal void RecordRoot(Entry entry, NativeRuntime.RootHandle root, object issuer)
    { Verify(issuer); entry.Root = root; }
    internal bool TryGetKind(IntPtr identity, out NativeTweenSourceObjectKind kind)
    { if (_entries.TryGetValue(identity, out var row)) { kind = row.Kind; return true; } kind = default; return false; }
    internal bool HasRetainedObject(long identity) => !Retired && !Releasing && _entries.TryGetValue(new IntPtr(identity), out var entry) && entry.Root is not null;
    internal void CompleteRetention(ulong watermark, object issuer)
    { Verify(issuer); RetainedAfterSequence = LastReadSequence = watermark; Ready = true; }
    internal void Advance(ulong sequence, object issuer) { Verify(issuer); LastReadSequence = sequence; }
    private void Verify(object issuer) { if (!Reader.IsIssuer(issuer)) throw new InvalidOperationException("Foreign source root issuer."); }
}
internal sealed class NativeTweenSourceBoundRecord
{
    internal NativeTweenSourceRecord Record { get; }
    internal NativeTweenSourceRootSet Roots { get; }
    internal bool Production => Roots.Reader.Production;
    internal NativeTweenSourceBoundRecord(NativeTweenSource reader, NativeTweenSourceRootSet roots, NativeTweenSourceRecord record, object issuer)
    { if (!reader.IsIssuer(issuer) || !ReferenceEquals(roots.Reader, reader)) throw new InvalidOperationException("Foreign source row issuer."); (Roots, Record) = (roots, record); }
}
internal sealed class NativeTweenSourceBatch
{
    internal NativeTweenSourceRootSet Roots { get; }
    internal ulong Epoch { get; }
    internal ulong AfterSequence { get; }
    internal ulong LastSequence { get; }
    internal IReadOnlyList<NativeTweenSourceBoundRecord> Records { get; }
    internal bool Production => Roots.Reader.Production;
    internal NativeTweenSourceBatch(NativeTweenSource reader, NativeTweenSourceRootSet roots, ulong epoch, ulong afterSequence,
        ulong lastSequence, IEnumerable<NativeTweenSourceBoundRecord> records, object issuer)
    {
        if (!reader.IsIssuer(issuer) || !ReferenceEquals(roots.Reader, reader)) throw new InvalidOperationException("Foreign source batch issuer.");
        (Roots, Epoch, AfterSequence, LastSequence, Records) = (roots, epoch, afterSequence, lastSequence, Array.AsReadOnly(records.ToArray()));
    }
}
