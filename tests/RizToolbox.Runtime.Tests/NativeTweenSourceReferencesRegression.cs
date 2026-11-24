using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Controlled CLR semantics only. The transport below models callback
/// TLS with fixture-owned copies; it is not native/game production evidence.</summary>
internal static class NativeTweenSourceReferencesRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Controlled source references preserve the independent V1 wire and never issue production authority", Wire),
        ("Controlled source references reserve and record every new root before Target or official metadata", RootBeforeInspection),
        ("Controlled source reference metadata failure retains every root and later unknown After references", MetadataFailure),
        ("Controlled source reference copies stale addresses and rejected TLS never authorize a root", RejectedTls),
        ("Controlled source reference workers stop before notice transport or Root and retain their callback fault", Worker),
        ("Controlled nested source reference scopes restore the actual outer receipt without mixing occurrence records", Nested),
        ("Controlled source reference consumer exceptions stay inside the callback and retain all responsibility", ConsumerFailure),
        ("Controlled repeated source identities share only their retained live roots and preserve every role occurrence", SharedRoots),
        ("Controlled uncertain source Root calls preserve their slots root other references and never retry raw tokens", AllocationFailure),
        ("Controlled Decision source references complete their source phase at BeforeCommitted without an invented After", Decision),
        ("Controlled source completion rejects missing or all-null before and after reference publications", EmptyPhases)
    };

    private static void Wire()
    {
        NativeTweenSourceReferences.ValidateLayout();
        Check((uint)NativeTweenSourceReferenceRole.Manager == 1 && (uint)NativeTweenSourceReferenceRole.Event == 32
            && (uint)NativeTweenSourceReferencePhase.References == 0 && (uint)NativeTweenSourceReferencePhase.Abnormal == 3);
        Check(Marshal.OffsetOf<NativeTweenSourceReferencesInformation>(nameof(NativeTweenSourceReferencesInformation.SourceEpoch)).ToInt32() == 32
            && Marshal.OffsetOf<NativeTweenSourceReferencesInformation>(nameof(NativeTweenSourceReferencesInformation.RejectedReads)).ToInt32() == 96);
        using var f = new Fixture(); Check(!f.Owner.Production && NativeTweenSourceReferences.InformationShape(f.Native.Info, 7));
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References, f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager)) == 0);
        var receipt = f.Receipts.Single(); Check(!receipt.Production && receipt.Known);
        receipt.VerifyIssuedBy(f.Owner); Throws<InvalidOperationException>(() => receipt.VerifyCurrent(f.Owner));
        using var foreign = new Fixture(); Throws<InvalidOperationException>(() => receipt.VerifyIssuedBy(foreign.Owner));
        Throws<InvalidOperationException>(() => new NativeTweenSourceReferences.Receipt(f.Owner, new object(),
            new NativeTweenSourceReferences.Scope(f.Origin(1)), f.Origin(1), default,
            Array.Empty<NativeTweenSourceReferences.RootedReference>(), Array.Empty<NativeTweenSourceReferences.RootedReference>()));
    }

    private static void RootBeforeInspection()
    {
        using var f = new Fixture();
        f.OnRoot = _ => Check(f.Owner.ReservedRootRecords == 3 && f.TargetReads == 0 && f.ValidationReads == 0);
        f.OnValidate = (_, _) => Check(f.Owner.RootRecords == 3 && f.RootAttempts == 3);
        var rows = new[] { f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager), f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween, 2),
            f.Ref(f.Array, NativeTweenSourceReferenceRole.Array), f.Ref(f.Tween, NativeTweenSourceReferenceRole.Prior, 3),
            f.Ref(IntPtr.Zero, NativeTweenSourceReferenceRole.Prior, 4) };
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References, rows) == 0);
        var receipt = f.Receipts.Single();
        Check(f.RootAttempts == 3 && f.Owner.RootRecords == 3 && f.ValidationReads == 4 && f.Freed == 0
            && receipt.References.Count == 5 && receipt.ScopeReferences.Count == 5
            && receipt.References[4].Root is null && receipt.References[4].Identity == IntPtr.Zero
            && receipt.References[1].SourceIndex == 2 && receipt.References[3].SourceIndex == 3
            && ReferenceEquals(receipt.References[1].Root, receipt.References[3].Root));
        Check(f.Log.Take(3).All(value => value == "Root") && f.Log.Skip(3).Any(value => value == "Target"));
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References,
            new[] { f.Ref(f.Array, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 0);
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 0);
        Check(f.Receipts[0].Phase == NativeTweenSourceReferencePhase.References
            && f.Receipts[1].Phase == NativeTweenSourceReferencePhase.BeforeCommitted
            && f.Receipts[2].Phase == NativeTweenSourceReferencePhase.References
            && f.Receipts[3].Phase == NativeTweenSourceReferencePhase.AfterCommitted
            && f.Receipts[3].ScopeReferences.Count == 6 && f.Owner.RetainedScopeRecords == 0);
    }

    private static void MetadataFailure()
    {
        using var f = new Fixture();
        f.OnValidate = (_, _) => throw new UnsupportedSignatureException("Controlled changed official metadata.");
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager), f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween),
            f.Ref(f.Array, NativeTweenSourceReferenceRole.Array)) == 5);
        Check(f.Owner.Unknown && f.Owner.RootRecords == 3 && f.Owner.ReservedRootRecords == 3
            && f.RootAttempts == 3 && f.ValidationReads == 1 && f.Freed == 0 && f.Receipts.Count == 0);
        f.OnValidate = null;
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 5);
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References,
            new[] { f.Ref(f.NewArray, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 5);
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 5);
        Check(f.Owner.RootRecords == 4 && f.RootAttempts == 4 && f.Freed == 0 && f.Owner.RetainedScopeRecords == 1
            && f.Receipts.Count == 3 && f.Receipts.All(receipt => !receipt.Known));
        Throws<UnsupportedSignatureException>(() => f.Receipts[^1].VerifyIssuedBy(f.Owner));
    }

    private static void RejectedTls()
    {
        var diagnostics = new RecordingLogger();
        using var copied = new Fixture(diagnostics);
        Check(copied.Native.Deliver(copied.Origin(1), NativeTweenSourceReferencePhase.References,
            new[] { copied.Ref(copied.Array, NativeTweenSourceReferenceRole.Array) }, 1, copyNotice: true) == 5);
        Check(copied.Native.ReadCalls == 1 && copied.Native.PointerReads == 0 && copied.RootAttempts == 0
            && copied.Owner.RootRecords == 0 && copied.Owner.Unknown);
        var diagnostic = diagnostics.Entries.Single();
        Check(diagnostic.Level == LogLevel.Error && diagnostic.Message.Contains("native notice copy")
            && diagnostic.Message.Contains("Source identity was unavailable") && !diagnostic.Message.Contains("ReadyPopFront")
            && ReferenceEquals(diagnostic.Error, copied.Owner.FirstFailure) && diagnostic.Error!.Message.Contains("status=3"));
        using var stale = new Fixture();
        var staleNotice = new NativeTweenSourceReferenceNotice { Version = 1, Size = 56,
            Phase = NativeTweenSourceReferencePhase.References, ReferenceCount = 1, ObserverEpoch = 11,
            ScopeId = 1, DeliverySequence = 1, Origin = new(1), References = new(1) };
        Check(stale.Native.InvokeOutside(staleNotice) == 5);
        Check(stale.Native.ReadCalls == 1 && stale.Native.PointerReads == 0 && stale.RootAttempts == 0 && stale.Owner.Unknown);
        using var rejected = new Fixture(); rejected.Native.RejectCurrentRead = true;
        Check(rejected.Deliver(1, NativeTweenSourceReferencePhase.References,
            rejected.Ref(rejected.Array, NativeTweenSourceReferenceRole.Array)) == 5);
        Check(rejected.Native.ReadCalls == 1 && rejected.Native.PointerReads == 0 && rejected.RootAttempts == 0);
    }

    private static void Worker()
    {
        using var f = new Fixture(); uint callbackStatus = 0; Exception? readFailure = null;
        var thread = new Thread(() => {
            callbackStatus = f.Native.InvokeRaw(new IntPtr(1));
            try { _ = f.Owner.RootRecords; } catch (Exception error) { readFailure = error; }
        });
        thread.Start(); thread.Join();
        Check(callbackStatus == 5 && readFailure is InvalidOperationException && f.Native.ReadCalls == 0
            && f.RootAttempts == 0 && f.Owner.CallbackFaults == 1 && f.Owner.Unknown && f.Owner.FirstFailure is InvalidOperationException);
        using var rooted = new Fixture();
        Check(rooted.Deliver(1, NativeTweenSourceReferencePhase.References,
            rooted.Ref(rooted.Tween, NativeTweenSourceReferenceRole.Tween)) == 0);
        var receipt = rooted.Receipts.Single(); Exception? receiptFailure = null;
        var reader = new Thread(() => { try { receipt.VerifyIssuedBy(rooted.Owner); } catch (Exception error) { receiptFailure = error; } });
        reader.Start(); reader.Join(); Check(receiptFailure is InvalidOperationException && !rooted.Owner.Unknown);
    }

    private static void Nested()
    {
        using var f = new Fixture(); NativeTweenSourceReferences.Receipt? outer = null, inner = null;
        f.Consume = receipt => {
            if (receipt.Phase != NativeTweenSourceReferencePhase.References
                || receipt.Origin.Stage != NativeTweenSourceStage.Before) return;
            if (receipt.ScopeId == 1)
            {
                outer = receipt;
                Check(f.Deliver(2, NativeTweenSourceReferencePhase.References,
                    f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween), f.Ref(f.Array, NativeTweenSourceReferenceRole.Array)) == 0);
                receipt.VerifyCurrent(f.Owner); Check(receipt.ScopeReferences.Count == 2);
                Throws<InvalidOperationException>(() => inner!.VerifyCurrent(f.Owner));
            }
            else { inner = receipt; Check(receipt.ScopeId == 2 && receipt.ScopeReferences.Count == 2); }
        };
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager), f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween)) == 0);
        Check(outer is not null && inner is not null && f.Owner.RootRecords == 3 && f.RootAttempts == 3 && f.Native.MaximumDepth == 2);
        outer!.VerifyIssuedBy(f.Owner); inner!.VerifyIssuedBy(f.Owner);
        Throws<InvalidOperationException>(() => outer.VerifyCurrent(f.Owner));
        Check(f.Deliver(2, NativeTweenSourceReferencePhase.BeforeCommitted) == 0
            && f.Deliver(2, NativeTweenSourceReferencePhase.References,
                new[] { f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween) }, NativeTweenSourceStage.After) == 0
            && f.Deliver(2, NativeTweenSourceReferencePhase.AfterCommitted) == 0
            && f.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0
            && f.Deliver(1, NativeTweenSourceReferencePhase.References,
                new[] { f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween) }, NativeTweenSourceStage.After) == 0
            && f.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 0);
        Check(f.Owner.RetainedScopeRecords == 0 && !f.Owner.Unknown && f.Freed == 0);
    }

    private static void ConsumerFailure()
    {
        using var f = new Fixture(); var expected = new InvalidOperationException("Controlled observer failure.");
        f.Consume = _ => throw expected;
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager), f.Ref(f.Array, NativeTweenSourceReferenceRole.Array)) == 5);
        Check(f.Owner.RootRecords == 2 && f.Freed == 0 && ReferenceEquals(f.Owner.FirstFailure, expected));
        var failedReceipt = f.Receipts.Single();
        Throws<InvalidOperationException>(() => failedReceipt.VerifyCurrent(f.Owner));
        Throws<UnsupportedSignatureException>(() => failedReceipt.VerifyIssuedBy(f.Owner));
        f.Consume = null;
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.Abnormal) == 5);
        Check(f.Owner.CallbackFaults == 2 && f.Owner.RootRecords == 2 && f.Owner.RetainedScopeRecords == 1
            && f.Receipts[^1].Phase == NativeTweenSourceReferencePhase.Abnormal && !f.Receipts[^1].Known);
    }

    private static void SharedRoots()
    {
        using var f = new Fixture();
        for (ulong scope = 1; scope <= 64; ++scope)
        {
            Check(f.Deliver(scope, NativeTweenSourceReferencePhase.References, f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager),
                f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween, 1), f.Ref(f.Tween, NativeTweenSourceReferenceRole.Prior, 2)) == 0);
            Check(f.Deliver(scope, NativeTweenSourceReferencePhase.BeforeCommitted) == 0
                && f.Deliver(scope, NativeTweenSourceReferencePhase.References,
                    new[] { f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween) }, NativeTweenSourceStage.After) == 0
                && f.Deliver(scope, NativeTweenSourceReferencePhase.AfterCommitted) == 0);
        }
        Check(f.RootAttempts == 2 && f.Owner.RootRecords == 2 && f.Owner.ReservedRootRecords == 2 && f.Freed == 0
            && f.Receipts.Where(receipt => receipt.Phase == NativeTweenSourceReferencePhase.AfterCommitted)
                .All(receipt => receipt.ScopeReferences.Count == 4 && receipt.ScopeReferences[1].SourceIndex == 1
                    && receipt.ScopeReferences[2].SourceIndex == 2));
        var shared = f.Receipts[0].References[1].Root!; shared.Dispose();
        Check(f.Deliver(65, NativeTweenSourceReferencePhase.References, f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween),
            f.Ref(f.NewArray, NativeTweenSourceReferenceRole.Array)) == 5);
        Check(f.RootAttempts == 3 && f.Owner.Unknown && f.Owner.ReservedRootRecords == 3 && f.Freed == 1);
    }

    private static void AllocationFailure()
    {
        using var f = new Fixture();
        f.OnRoot = value => { if (value == f.Manager) throw new InvalidOperationException("Controlled uncertain Root allocation."); };
        Check(f.Deliver(1, NativeTweenSourceReferencePhase.References, f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager),
            f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween), f.Ref(f.Array, NativeTweenSourceReferenceRole.Array)) == 5);
        Check(f.RootAttempts == 3 && f.Owner.ReservedRootRecords == 3 && f.Owner.RootRecords == 2
            && f.TargetReads == 0 && f.ValidationReads == 0 && f.Freed == 0);
        f.OnRoot = null;
        Check(f.Deliver(2, NativeTweenSourceReferencePhase.References, f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager),
            f.Ref(f.NewArray, NativeTweenSourceReferenceRole.Array)) == 5);
        Check(f.RootAttempts == 4 && f.Owner.ReservedRootRecords == 4 && f.Owner.RootRecords == 3 && f.Freed == 0);
    }

    private static void Decision()
    {
        using var f = new Fixture(); var origin = f.Origin(1, NativeTweenSourceStage.Decision);
        origin.Site = NativeTweenSourceSite.TailDeadDecision;
        Check(f.Native.Deliver(origin, NativeTweenSourceReferencePhase.References,
            new[] { f.Ref(f.Manager, NativeTweenSourceReferenceRole.Manager), f.Ref(f.Tween, NativeTweenSourceReferenceRole.Tween),
                f.Ref(f.Array, NativeTweenSourceReferenceRole.Array) }, 1) == 0);
        Check(f.Native.Deliver(origin, NativeTweenSourceReferencePhase.BeforeCommitted, Array.Empty<NativeTweenSourceBorrowedReference>(), 1) == 0);
        var committed = f.Receipts[^1]; committed.VerifyIssuedBy(f.Owner);
        Check(committed.Origin.Stage == NativeTweenSourceStage.Decision && committed.Phase == NativeTweenSourceReferencePhase.BeforeCommitted
            && committed.ScopeReferences.Count == 3 && f.Owner.RetainedScopeRecords == 0 && f.Owner.RootRecords == 3 && !f.Owner.Unknown);
    }

    private static void EmptyPhases()
    {
        using var absent = new Fixture();
        Check(absent.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 5);
        Check(absent.Owner.Unknown && absent.Owner.RetainedScopeRecords == 1
            && absent.Owner.RootRecords == 0 && absent.Receipts.Count == 0 && absent.Freed == 0);

        using var allNull = new Fixture();
        Check(allNull.Deliver(1, NativeTweenSourceReferencePhase.References,
            allNull.Ref(IntPtr.Zero, NativeTweenSourceReferenceRole.Manager)) == 0);
        Check(allNull.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 5);
        Check(allNull.Owner.Unknown && allNull.Owner.RetainedScopeRecords == 1
            && allNull.Owner.RootRecords == 0 && allNull.Freed == 0);

        using var missingAfter = new Fixture();
        Check(missingAfter.Deliver(1, NativeTweenSourceReferencePhase.References,
            missingAfter.Ref(missingAfter.Manager, NativeTweenSourceReferenceRole.Manager)) == 0);
        Check(missingAfter.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(missingAfter.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 5);
        Check(missingAfter.Owner.Unknown && missingAfter.Owner.RetainedScopeRecords == 1
            && missingAfter.Owner.RootRecords == 1 && missingAfter.Freed == 0);

        using var nullAfter = new Fixture();
        Check(nullAfter.Deliver(1, NativeTweenSourceReferencePhase.References,
            nullAfter.Ref(nullAfter.Manager, NativeTweenSourceReferenceRole.Manager)) == 0);
        Check(nullAfter.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(nullAfter.Deliver(1, NativeTweenSourceReferencePhase.References,
            new[] { nullAfter.Ref(IntPtr.Zero, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 0);
        Check(nullAfter.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 5);
        Check(nullAfter.Owner.Unknown && nullAfter.Owner.RetainedScopeRecords == 1
            && nullAfter.Owner.RootRecords == 1 && nullAfter.Freed == 0);
    }

    internal sealed class Transport : INativeTweenSourceReferencesTransport
    {
        internal NativeTweenSourceReferencesInformation Info = new() { Version = 1, Size = 104, ScopeCapacity = 128 };
        private NativeTweenSourceReferencesNotify? _notify;
        private IntPtr _user;
        private Frame? _current;
        private ulong _delivery;
        internal int ReadCalls, PointerReads, MaximumDepth;
        internal bool RejectCurrentRead;
        public uint Attach(ref NativeTweenSourceReferencesObserver observer)
        {
            if (Info.Attached != 0 || observer.Version != 1 || observer.Size != 24 || observer.Notify == IntPtr.Zero) return 2;
            _notify = Marshal.GetDelegateForFunctionPointer<NativeTweenSourceReferencesNotify>(observer.Notify); _user = observer.User;
            Info.Attached = 1; Info.SourceEpoch = 7; Info.ObserverEpoch = 11; return 0;
        }
        public uint GetInfo(ref NativeTweenSourceReferencesInformation information) { information = Info; return 0; }
        public uint ReadCurrentNotice(IntPtr actualNotice, ulong observerEpoch, ulong scopeId, ulong deliverySequence,
            IntPtr noticeCopy, IntPtr originCopy, IntPtr referencesCopy, uint referenceCapacity)
        {
            ++ReadCalls; var current = _current;
            if (RejectCurrentRead || current is null || actualNotice != current.Address || observerEpoch != current.Notice.ObserverEpoch
                || scopeId != current.Notice.ScopeId || deliverySequence != current.Notice.DeliverySequence
                || current.Origin.ThreadId != NativeTweenSourceReferences.CurrentThreadId()) return 3;
            if (referenceCapacity < current.References.Length) return 1;
            ++PointerReads;
            Marshal.StructureToPtr(current.Origin, originCopy, false);
            for (var i = 0; i < current.References.Length; ++i)
                Marshal.StructureToPtr(current.References[i], IntPtr.Add(referencesCopy, i * 16), false);
            var notice = current.Notice; notice.Origin = originCopy;
            notice.References = current.References.Length == 0 ? IntPtr.Zero : referencesCopy;
            Marshal.StructureToPtr(notice, noticeCopy, false); return 0;
        }
        internal uint Deliver(NativeTweenSourceReferenceOrigin origin, NativeTweenSourceReferencePhase phase,
            NativeTweenSourceBorrowedReference[] references, ulong scope, bool copyNotice = false)
        {
            var address = Marshal.AllocHGlobal(56); var originAddress = Marshal.AllocHGlobal(80);
            var referencesAddress = Marshal.AllocHGlobal(Math.Max(16, references.Length * 16)); IntPtr copied = IntPtr.Zero;
            var previous = _current;
            try
            {
                Marshal.StructureToPtr(origin, originAddress, false);
                for (var i = 0; i < references.Length; ++i) Marshal.StructureToPtr(references[i], IntPtr.Add(referencesAddress, i * 16), false);
                var notice = new NativeTweenSourceReferenceNotice { Version = 1, Size = 56, Phase = phase,
                    ReferenceCount = checked((uint)references.Length), ObserverEpoch = 11, ScopeId = scope, DeliverySequence = ++_delivery,
                    Origin = originAddress, References = references.Length == 0 ? IntPtr.Zero : referencesAddress };
                Marshal.StructureToPtr(notice, address, false);
                _current = new Frame(address, notice, origin, references, previous);
                var depth = 0; for (var cursor = _current; cursor is not null; cursor = cursor.Parent) ++depth;
                MaximumDepth = Math.Max(MaximumDepth, depth);
                if (!copyNotice) return InvokeRaw(address);
                copied = Marshal.AllocHGlobal(56); notice.Origin = new(1); notice.References = new(1);
                Marshal.StructureToPtr(notice, copied, false); return InvokeRaw(copied);
            }
            finally
            {
                _current = previous; if (copied != IntPtr.Zero) Marshal.FreeHGlobal(copied);
                Marshal.FreeHGlobal(referencesAddress); Marshal.FreeHGlobal(originAddress); Marshal.FreeHGlobal(address);
            }
        }
        internal uint InvokeOutside(NativeTweenSourceReferenceNotice notice)
        {
            var pointer = Marshal.AllocHGlobal(56);
            try { Marshal.StructureToPtr(notice, pointer, false); return InvokeRaw(pointer); }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        internal uint InvokeRaw(IntPtr address) => (_notify ?? throw new InvalidOperationException("Controlled observer is not attached."))(address, _user);
        private sealed record Frame(IntPtr Address, NativeTweenSourceReferenceNotice Notice, NativeTweenSourceReferenceOrigin Origin,
            NativeTweenSourceBorrowedReference[] References, Frame? Parent);
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly NativeThreadAccess _thread = new();
        private readonly Dictionary<uint, IntPtr> _roots = new();
        private uint _next;
        internal readonly IntPtr Manager = new(0x1000), Tween = new(0x2000), Array = new(0x3000), NewArray = new(0x4000);
        internal readonly Transport Native = new();
        internal NativeTweenSourceReferences Owner { get; private set; } = null!;
        internal readonly NativeTweenSourceReferences.ControlledConsumerRegistration ConsumerRegistration;
        internal readonly List<NativeTweenSourceReferences.Receipt> Receipts = new();
        internal readonly List<string> Log = new();
        internal Action<IntPtr>? OnRoot;
        internal Action<IntPtr>? OnFree;
        internal Action<NativeRuntime.RootHandle, NativeTweenSourceObjectKind>? OnValidate;
        internal Action<NativeTweenSourceReferences.Receipt>? Consume;
        internal int RootAttempts, TargetReads, ValidationReads, Freed, FreeAttempts;
        internal Fixture(ILogger? diagnostics = null)
        {
            _thread.BindCurrentThread();
            Owner = NativeTweenSourceReferences.CreateControlledForTests(Native, 7, _thread.Verify, Root,
                (root, kind) => { ++ValidationReads; Log.Add("Validate"); OnValidate?.Invoke(root, kind); return root.Target; },
                receipt => { receipt.VerifyCurrent(Owner); Receipts.Add(receipt); Consume?.Invoke(receipt); }, diagnostics);
            ConsumerRegistration = Owner.RegisterControlledConsumerForTests();
        }
        internal void VerifyAccess() => _thread.Verify();
        internal NativeTweenSourceBorrowedReference Ref(IntPtr value, NativeTweenSourceReferenceRole role, int index = -1)
            => new() { Value = value, Role = role, SourceIndex = index };
        internal NativeTweenSourceReferenceOrigin Origin(ulong scope, NativeTweenSourceStage stage = NativeTweenSourceStage.Before)
            => new() { Version = 1, Size = 80, Operation = NativeTweenSourceOperation.Update,
                ThreadId = NativeTweenSourceReferences.CurrentThreadId(), Depth = 1, Site = NativeTweenSourceSite.ReadyPopFront,
                Stage = stage, OriginRoles = (NativeTweenSourceReferenceRole)63, SourceEpoch = 7,
                OperationSequence = 100 + scope, Transaction = 1000 + scope, SourcePc = new(0x5000), RelayReturnPc = new(0x6000) };
        internal uint Deliver(ulong scope, NativeTweenSourceReferencePhase phase, params NativeTweenSourceBorrowedReference[] references)
            => Deliver(scope, phase, references, phase == NativeTweenSourceReferencePhase.AfterCommitted ? NativeTweenSourceStage.After : NativeTweenSourceStage.Before);
        internal uint Deliver(ulong scope, NativeTweenSourceReferencePhase phase, NativeTweenSourceBorrowedReference[] references, NativeTweenSourceStage stage)
            => Native.Deliver(Origin(scope, stage), phase, references, scope);
        internal NativeRuntime.RootHandle Root(IntPtr value)
        {
            _thread.Verify(); ++RootAttempts; Log.Add("Root"); OnRoot?.Invoke(value);
            var id = ++_next; _roots.Add(id, value);
            return new(id, key => { ++FreeAttempts; OnFree?.Invoke(value); _roots.Remove(key); ++Freed; }, key => {
                ++TargetReads; Log.Add("Target"); return _roots.GetValueOrDefault(key);
            }, _thread.Verify);
        }
        public void Dispose() { /* This fixture deliberately retains every process root responsibility. */ }
    }
    internal sealed class RecordingLogger : ILogger
    {
        internal readonly List<(LogLevel Level, string Message, Exception? Error)> Entries = new();
        internal bool ThrowOnLog;
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            Entries.Add((level, message, exception));
            if (ThrowOnLog) throw new IOException("Controlled diagnostic sink failure.");
        }
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Source reference publication invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
