using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class NativeTweenSourceDynamicReferencesRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Synchronous source receipts extend only matching pre-retained managers with the actual held root", NewReference),
        ("Source resize roots the new array after Before and admits only the actual subsequent journal row", Resize),
        ("A genuine synchronous source receipt cannot retroactively authorize an already-published raw object", Historical),
        ("Source reader rejects a different reference observer before changing retained responsibility", ForeignIssuer),
        ("Repeated source notices and matching owners share the same actual independent root handle", SharedRoot),
        ("Foreign source notices keep their own strong roots without extending unrelated journal owners", ForeignOperation),
        ("ReadyPopAppend Before preserves its first relevant owner failure and ignores an unrelated manager", ReadyPopOwnerFailure),
        ("A newly retained foreign Tween never becomes an ownership anchor for an older independent Play scope", DynamicIdentityIsNotOwnership),
        ("Historical scope references cannot be replayed as a new After capture for a later retained owner", ScopeHistoryIsNotCapture),
        ("A current retained operation input becomes a same-handle Tween ownership anchor before its first source row", OperationPromotion),
        ("A previously captured Tween root gains ownership only from its explicit current operation promotion", CapturedPromotion),
        ("Diagnostic shared-manager Tween roots never receive initial ownership anchors", DiagnosticRootIsNotOwnership),
        ("Operation promotion cannot repair either consumed or unread earlier foreign Play source rows", OperationPromotionCannotRepairHistory),
        ("A source promotion fault retains its already-borrowed root and never allocates a late root", OperationPromotionFault),
        ("A copied native input frame outside its real Before observer cannot issue a production root receipt", NoBorrowedReceiptFromScalars),
        ("A current Add receiver with an owned argument captures the new Manager's complete first Before and After source pair", ManagerPromotion),
        ("Current Manager promotion refuses a foreign argument wrong receiver and wrong native input shape without Root", ManagerPromotionAdmission),
        ("Manager ownership begins after its exact watermark and cannot authorize an already-published raw queue", ManagerPromotionIsNotRetroactive)
    };

    private static void NewReference()
    {
        using var f = new Fixture(); var roots = f.Retain();
        Check(f.Publish(f.References.NewArray) == 0);
        var actual = f.References.Receipts.Single().References.Single(row => row.Identity == f.References.NewArray).Root;
        Check(roots.RootRecords == 4 && ReferenceEquals(roots.Entries.Single(row => row.Identity == f.References.NewArray).Root, actual));
        var attempts = f.References.RootAttempts;
        f.Native.Add(f.Row(1, f.References.NewArray));
        var batch = f.Reader.ReadAvailable(roots, 0, 8);
        Check(batch.Records.Count == 1 && batch.LastSequence == 1 && !batch.Production && !f.Reader.Unknown
            && f.References.RootAttempts == attempts && f.References.Freed == 0);
    }

    private static void Resize()
    {
        using var f = new Fixture(); var roots = f.Retain();
        Check(f.Publish(f.References.Array) == 0);
        f.Native.Add(f.Row(1, f.References.Array));
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.References,
            new[] { f.References.Ref(f.References.NewArray, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 0);
        var row = f.Row(2, f.References.NewArray); row.Stage = NativeTweenSourceStage.After; row.Flags |= NativeTweenSourceFlags.Paired;
        f.Native.Add(row);
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 0);
        var batch = f.Reader.ReadAvailable(roots, 0, 8);
        var captured = roots.Entries.Single(entry => entry.Identity == f.References.NewArray);
        Check(batch.Records.Count == 2 && captured.ReferenceAfterSequence == 1 && roots.LastReadSequence == 2
            && captured.ReferenceReceipt!.Origin.Stage == NativeTweenSourceStage.After && f.References.Freed == 0);
    }

    private static void Historical()
    {
        using var f = new Fixture(); var roots = f.Retain();
        f.Native.Add(f.Row(1, f.References.NewArray));
        Check(f.Publish(f.References.NewArray) == 0);
        var attempts = f.References.RootAttempts;
        Throws<UnsupportedSignatureException>(() => f.Reader.ReadAvailable(roots, 0, 8));
        Check(f.Reader.Unknown && roots.LastReadSequence == 0 && roots.RootRecords == 4
            && f.References.RootAttempts == attempts && f.References.Freed == 0);
    }

    private static void ForeignIssuer()
    {
        using var f = new Fixture(); var roots = f.Retain();
        using var foreign = new NativeTweenSourceReferencesRegression.Fixture();
        foreign.Consume = f.Reader.ObserveSynchronousReferences;
        Check(foreign.Deliver(1, NativeTweenSourceReferencePhase.References,
            foreign.Ref(foreign.Manager, NativeTweenSourceReferenceRole.Manager),
            foreign.Ref(foreign.NewArray, NativeTweenSourceReferenceRole.Array)) == 5);
        Check(foreign.Owner.Unknown && foreign.Owner.RootRecords == 2 && roots.RootRecords == 3 && !f.Reader.Unknown);
        Throws<InvalidOperationException>(() => f.Reader.ObserveSynchronousReferences(foreign.Receipts.Single()));
        Check(foreign.Freed == 0 && f.References.Freed == 0);
    }

    private static void SharedRoot()
    {
        using var f = new Fixture(); var first = f.Retain(); var second = f.Retain();
        Check(f.Publish(f.References.NewArray) == 0);
        var left = first.Entries.Single(row => row.Identity == f.References.NewArray);
        var right = second.Entries.Single(row => row.Identity == f.References.NewArray);
        var attempts = f.References.RootAttempts;
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.References.Ref(f.References.NewArray, NativeTweenSourceReferenceRole.Array)) == 0);
        Check(ReferenceEquals(left.Root, right.Root) && f.Reader.RootRecords == 8
            && attempts == f.References.RootAttempts && f.References.Freed == 0);
    }

    private static void ForeignOperation()
    {
        using var f = new Fixture(); var roots = f.Retain();
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.References.Ref(new IntPtr(0x7000), NativeTweenSourceReferenceRole.Manager),
            f.References.Ref(new IntPtr(0x8000), NativeTweenSourceReferenceRole.Tween),
            f.References.Ref(f.References.NewArray, NativeTweenSourceReferenceRole.Array)) == 0);
        var row = f.Row(1, f.References.NewArray); row.Manager = new(0x7000); row.Tween = new(0x8000); f.Native.Add(row);
        var batch = f.Reader.ReadAvailable(roots, 0, 8);
        Check(batch.Records.Count == 0 && batch.LastSequence == 1 && roots.RootRecords == 3
            && f.References.Owner.RootRecords == 3 && !f.Reader.Unknown && f.References.Freed == 0);
    }

    private static void ReadyPopOwnerFailure()
    {
        var diagnostics = new NativeTweenSourceReferencesRegression.RecordingLogger { ThrowOnLog = true };
        using var f = new Fixture(diagnostics);
        var roots = f.Retain();
        var failure = new InvalidOperationException("The retained launch became unknown.");
        f.OwnerFailure = failure;

        var references = f.References;
        var foreign = references.Origin(1);
        foreign.Site = NativeTweenSourceSite.ReadyPopAppend;
        foreign.OriginRoles = NativeTweenSourceReferenceRole.Manager | NativeTweenSourceReferenceRole.Tween
            | NativeTweenSourceReferenceRole.Container | NativeTweenSourceReferenceRole.Array;
        var notice = new[] {
            references.Ref(new IntPtr(0x7000), NativeTweenSourceReferenceRole.Manager),
            references.Ref(new IntPtr(0x8000), NativeTweenSourceReferenceRole.Container),
            references.Ref(IntPtr.Zero, NativeTweenSourceReferenceRole.Tween)
        };
        Check(references.Native.Deliver(foreign, NativeTweenSourceReferencePhase.References, notice, 1) == 0);
        var foreignReceipt = references.Receipts.Single();
        Check(foreignReceipt.References[1].Kind == NativeTweenSourceObjectKind.ReadyStack
            && foreignReceipt.References[2].Root is null && roots.RootRecords == 3 && diagnostics.Entries.Count == 0);

        var owned = foreign;
        owned.OperationSequence = 62138;
        owned.Transaction = 2440;
        notice[0] = references.Ref(references.Manager, NativeTweenSourceReferenceRole.Manager);
        notice[1] = references.Ref(new IntPtr(0x9000), NativeTweenSourceReferenceRole.Container);
        Check(references.Native.Deliver(owned, NativeTweenSourceReferencePhase.References, notice, 2) == 5);
        Check(references.Native.Deliver(owned, NativeTweenSourceReferencePhase.Abnormal,
            System.Array.Empty<NativeTweenSourceBorrowedReference>(), 2) == 5);

        var diagnostic = diagnostics.Entries.Single();
        Check(diagnostic.Message.Contains("source reference consumer") && diagnostic.Message.Contains("ReadyPopAppend (Before)")
            && diagnostic.Message.Contains("Update sequence 62138") && diagnostic.Message.Contains("transaction 2440")
            && diagnostic.Message.Contains("scope 2, delivery 2, phase References") && ReferenceEquals(diagnostic.Error, failure));
        Check(ReferenceEquals(references.Owner.FirstFailure, failure) && references.Owner.CallbackFaults == 2
            && references.Owner.Unknown && f.Reader.Unknown && references.Owner.RetainedScopeRecords == 2
            && references.Owner.RootRecords == 4 && roots.RootRecords == 3 && references.Freed == 0);
    }

    private static void DynamicIdentityIsNotOwnership()
    {
        using var f = new Fixture(); var roots = f.Retain(); var foreignTween = new IntPtr(0x8000); var foreignEvent = new IntPtr(0x9000);
        var play = f.References.Origin(1); play.Operation = NativeTweenSourceOperation.Play; play.Site = NativeTweenSourceSite.PlayOnStart;
        Check(f.References.Native.Deliver(play, NativeTweenSourceReferencePhase.References, new[] {
            f.References.Ref(foreignTween, NativeTweenSourceReferenceRole.Tween),
            f.References.Ref(foreignEvent, NativeTweenSourceReferenceRole.Event) }, 1) == 0);
        var first = f.Row(1, IntPtr.Zero); first.Site = play.Site; first.Operation = play.Operation; first.Manager = IntPtr.Zero;
        first.ArrayBefore = IntPtr.Zero; first.Tween = foreignTween; f.Native.Add(first);
        Check(f.Reader.ReadAvailable(roots, 0, 8).Records.Count == 0);
        Check(f.References.Native.Deliver(play, NativeTweenSourceReferencePhase.BeforeCommitted,
            Array.Empty<NativeTweenSourceBorrowedReference>(), 1) == 0);

        Check(f.References.Deliver(2, NativeTweenSourceReferencePhase.References,
            f.References.Ref(f.References.Manager, NativeTweenSourceReferenceRole.Manager),
            f.References.Ref(foreignTween, NativeTweenSourceReferenceRole.Tween),
            f.References.Ref(f.References.NewArray, NativeTweenSourceReferenceRole.Array)) == 0);
        var owned = f.Row(2, f.References.NewArray); owned.Tween = foreignTween; owned.UpdateSequence = 102; owned.Transaction = 1002; f.Native.Add(owned);
        Check(f.Reader.ReadAvailable(roots, 1, 8).Records.Count == 1);
        play.Stage = NativeTweenSourceStage.After;
        Check(f.References.Native.Deliver(play, NativeTweenSourceReferencePhase.References,
            new[] { f.References.Ref(foreignEvent, NativeTweenSourceReferenceRole.Event) }, 1) == 0);
        var last = first; last.EventSequence = 3; last.Stage = NativeTweenSourceStage.After; last.Flags |= NativeTweenSourceFlags.Paired; f.Native.Add(last);
        Check(f.Reader.ReadAvailable(roots, 2, 8).Records.Count == 0 && !roots.HasRetainedObject(foreignEvent.ToInt64())
            && roots.RootRecords == 5 && !f.Reader.Unknown && !f.References.Owner.Unknown && f.References.Freed == 0);
    }

    private static void ScopeHistoryIsNotCapture()
    {
        using var f = new Fixture();
        Check(f.Publish(f.References.NewArray) == 0); // No root set existed in this real callback.
        f.Native.Add(f.Row(1, f.References.NewArray)); var roots = f.Retain();
        Check(roots.LastReadSequence == 1 && roots.RootRecords == 3);
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.References,
            new[] { f.References.Ref(f.References.Array, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 0);
        Check(roots.RootRecords == 3 && !roots.HasRetainedObject(f.References.NewArray.ToInt64()));
        var after = f.Row(2, f.References.NewArray); after.Stage = NativeTweenSourceStage.After; after.Flags |= NativeTweenSourceFlags.Paired; f.Native.Add(after);
        Throws<UnsupportedSignatureException>(() => f.Reader.ReadAvailable(roots, 1, 8));
        Check(f.Reader.Unknown && roots.RootRecords == 3 && roots.LastReadSequence == 1 && f.References.Freed == 0);
    }

    private static void OperationPromotion()
    {
        using var f = new Fixture(); var roots = f.Retain(); var tween = new IntPtr(0x8000); var child = new IntPtr(0x9000);
        var input = f.References.Root(tween); var attempts = f.References.RootAttempts;
        f.Reader.PromoteCurrentOperationTweenRoot(roots, input, f.PlayFrame(tween));
        var promoted = roots.Entries.Single(row => row.Identity == tween);
        Check(roots.HasTweenOwnershipAnchor(tween.ToInt64()) && ReferenceEquals(promoted.Root, input)
            && ReferenceEquals(promoted.OperationAnchor!.Root, input) && f.References.RootAttempts == attempts);
        var origin = f.References.Origin(1); origin.Operation = NativeTweenSourceOperation.Play; origin.Site = NativeTweenSourceSite.PlayOnStart;
        Check(f.References.Native.Deliver(origin, NativeTweenSourceReferencePhase.References, new[] {
            f.References.Ref(tween, NativeTweenSourceReferenceRole.Tween), f.References.Ref(child, NativeTweenSourceReferenceRole.Event) }, 1) == 0);
        f.Native.Add(f.PlayRow(1, tween, child));
        var batch = f.Reader.ReadAvailable(roots, 0, 8);
        Check(batch.Records.Count == 1 && batch.Records[0].Record.Tween == tween && !f.Reader.Unknown && f.References.Freed == 0);
        attempts = f.References.RootAttempts;
        f.Reader.PromoteCurrentOperationTweenRoot(roots, input, f.PlayFrame(tween));
        Check(roots.RootRecords == 5 && f.References.RootAttempts == attempts);
    }

    private static void CapturedPromotion()
    {
        using var f = new Fixture(); var roots = f.Retain(); var tween = new IntPtr(0x8000);
        Check(f.References.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.References.Ref(f.References.Manager, NativeTweenSourceReferenceRole.Manager),
            f.References.Ref(tween, NativeTweenSourceReferenceRole.Tween)) == 0);
        var captured = roots.Entries.Single(row => row.Identity == tween); var actual = captured.Root!;
        Check(!roots.HasTweenOwnershipAnchor(tween.ToInt64())); var attempts = f.References.RootAttempts;
        f.Reader.PromoteCurrentOperationTweenRoot(roots, actual, f.PlayFrame(tween));
        Check(roots.HasTweenOwnershipAnchor(tween.ToInt64()) && ReferenceEquals(captured.OperationAnchor!.Root, actual)
            && captured.ReferenceReceipt is not null && f.References.RootAttempts == attempts);
        f.Native.Add(f.PlayRow(1, tween, IntPtr.Zero));
        Check(f.Reader.ReadAvailable(roots, 0, 8).Records.Count == 1 && !f.Reader.Unknown && f.References.Freed == 0);
    }

    private static void DiagnosticRootIsNotOwnership()
    {
        using var f = new Fixture(); var tween = new IntPtr(0x8000); var input = f.References.Root(tween); var roots = f.Retain(input);
        Check(roots.HasRetainedObject(tween.ToInt64()) && !roots.HasTweenOwnershipAnchor(tween.ToInt64()));
        f.Native.Add(f.PlayRow(1, tween, IntPtr.Zero));
        Check(f.Reader.ReadAvailable(roots, 0, 8).Records.Count == 0 && !f.Reader.Unknown);
        var attempts = f.References.RootAttempts;
        Throws<UnsupportedSignatureException>(() => f.Reader.PromoteCurrentOperationTweenRoot(roots, input, f.PlayFrame(tween)));
        Check(f.Reader.Unknown && !roots.HasTweenOwnershipAnchor(tween.ToInt64()) && f.References.RootAttempts == attempts && f.References.Freed == 0);
    }

    private static void OperationPromotionCannotRepairHistory()
    {
        foreach (var consumed in new[] { false, true })
        {
            using var f = new Fixture(); var roots = f.Retain(); var tween = new IntPtr(0x8000);
            f.Native.Add(f.PlayRow(1, tween, IntPtr.Zero));
            if (consumed) Check(f.Reader.ReadAvailable(roots, 0, 8).Records.Count == 0);
            var input = f.References.Root(tween); var attempts = f.References.RootAttempts;
            Throws<UnsupportedSignatureException>(() => f.Reader.PromoteCurrentOperationTweenRoot(roots, input, f.PlayFrame(tween)));
            Check(f.Reader.Unknown && roots.RootRecords == 3 && !roots.HasTweenOwnershipAnchor(tween.ToInt64())
                && roots.LastReadSequence == (consumed ? 1UL : 0UL) && f.References.RootAttempts == attempts && f.References.Freed == 0);
        }
    }

    private static void OperationPromotionFault()
    {
        using var f = new Fixture(); var roots = f.Retain(); var tween = new IntPtr(0x8000); var input = f.References.Root(tween);
        f.FailPromotedValidation = true; var attempts = f.References.RootAttempts;
        Throws<UnsupportedSignatureException>(() => f.Reader.PromoteCurrentOperationTweenRoot(roots, input, f.PlayFrame(tween)));
        var retained = roots.Entries.Single(row => row.Identity == tween);
        Check(f.Reader.Unknown && roots.RootRecords == 4 && !roots.HasTweenOwnershipAnchor(tween.ToInt64())
            && ReferenceEquals(retained.Root, input) && ReferenceEquals(retained.OperationAnchor!.Root, input)
            && f.References.RootAttempts == attempts && f.References.Freed == 0);
        Throws<InvalidOperationException>(() => f.Reader.PromoteCurrentOperationTweenRoot(roots, input, f.PlayFrame(tween)));
        Check(f.References.RootAttempts == attempts && f.References.Freed == 0);
    }

    private static void NoBorrowedReceiptFromScalars()
    {
        using var f = new Fixture(); var tween = new IntPtr(0x8000); var input = f.References.Root(tween); var frame = f.PlayFrame(tween);
        Throws<InvalidOperationException>(() => ExactHookBroker.CaptureCurrentOperationRoot(frame, input, f, production: true));
        Throws<InvalidOperationException>(() => new ExactHookBroker.OperationRootReceipt(new object(), null!, null!, frame, input, f, true));
        Check(!f.Reader.Unknown && f.References.Freed == 0);
    }

    private static void ManagerPromotion()
    {
        using var f = new Fixture(); var roots = f.Retain(); var manager = new IntPtr(0x7000);
        var queue = new IntPtr(0xA000); var array = new IntPtr(0xB000); var foreignTween = new IntPtr(0xC000);
        var managerRoot = f.References.Root(manager); var tweenRoot = f.References.Root(f.References.Tween);
        var attempts = f.References.RootAttempts; var frame = f.ManagerFrame(manager, f.References.Tween);
        f.Reader.PromoteCurrentOperationManagerRoot(roots, managerRoot, tweenRoot, frame);
        var anchor = roots.Entries.Single(row => row.Identity == manager);
        Check(ReferenceEquals(anchor.Root, managerRoot) && ReferenceEquals(anchor.OperationAnchor!.Root, managerRoot)
            && roots.TryGetAnchorKind(manager, out var kind) && kind == NativeTweenSourceObjectKind.Manager && f.References.RootAttempts == attempts);
        // The real first pop Before names no Tween. Its new Manager anchor must
        // retain the queue/array and include this row before the After names it.
        var origin = f.References.Origin(1);
        Check(f.References.Native.Deliver(origin, NativeTweenSourceReferencePhase.References, new[] {
            f.References.Ref(manager, NativeTweenSourceReferenceRole.Manager),
            f.References.Ref(queue, NativeTweenSourceReferenceRole.Container),
            f.References.Ref(array, NativeTweenSourceReferenceRole.Array) }, 1) == 0);
        var before = f.ManagerRow(1, manager, queue, array, IntPtr.Zero); f.Native.Add(before);
        Check(f.References.Native.Deliver(origin, NativeTweenSourceReferencePhase.BeforeCommitted,
            Array.Empty<NativeTweenSourceBorrowedReference>(), 1) == 0);
        origin.Stage = NativeTweenSourceStage.After;
        Check(f.References.Native.Deliver(origin, NativeTweenSourceReferencePhase.References, new[] {
            f.References.Ref(manager, NativeTweenSourceReferenceRole.Manager),
            f.References.Ref(f.References.Tween, NativeTweenSourceReferenceRole.Tween),
            f.References.Ref(foreignTween, NativeTweenSourceReferenceRole.Prior) }, 1) == 0);
        var after = f.ManagerRow(2, manager, queue, array, f.References.Tween); after.Stage = NativeTweenSourceStage.After;
        after.Flags |= NativeTweenSourceFlags.Paired; f.Native.Add(after);
        Check(f.References.Native.Deliver(origin, NativeTweenSourceReferencePhase.AfterCommitted,
            Array.Empty<NativeTweenSourceBorrowedReference>(), 1) == 0);
        var batch = f.Reader.ReadAvailable(roots, 0, 8);
        Check(batch.Records.Count == 2 && batch.Records[0].Record.Tween == IntPtr.Zero
            && batch.Records[1].Record.Tween == f.References.Tween && roots.HasRetainedObject(foreignTween.ToInt64())
            && !roots.HasTweenOwnershipAnchor(foreignTween.ToInt64()) && !f.Reader.Unknown && f.References.Freed == 0);
        attempts = f.References.RootAttempts;
        f.Reader.PromoteCurrentOperationManagerRoot(roots, managerRoot, tweenRoot, frame);
        Check(f.References.RootAttempts == attempts && ReferenceEquals(anchor.OperationAnchor!.Root, managerRoot));
        f.Native.Add(f.PlayRow(3, foreignTween, IntPtr.Zero));
        Check(f.Reader.ReadAvailable(roots, 2, 8).Records.Count == 0 && !f.Reader.Unknown && f.References.Freed == 0);
    }

    private static void ManagerPromotionAdmission()
    {
        foreach (var mode in new[] { "foreign-tween", "wrong-receiver", "wrong-kind", "wrong-argument" })
        {
            using var f = new Fixture(); var roots = f.Retain(); var manager = new IntPtr(0x7000);
            var tween = mode == "foreign-tween" ? new IntPtr(0x8000) : f.References.Tween;
            var managerRoot = f.References.Root(manager); var tweenRoot = f.References.Root(tween);
            var frame = f.ManagerFrame(manager, tween);
            if (mode == "wrong-receiver") frame.Self = new(0xDEAD);
            if (mode == "wrong-kind") { frame.Kind = NativeOperationKind.Void0; frame.Arg0 = IntPtr.Zero; }
            if (mode == "wrong-argument") frame.Arg0 = new(0xBAD);
            var attempts = f.References.RootAttempts;
            Throws<InvalidOperationException>(() => f.Reader.PromoteCurrentOperationManagerRoot(roots, managerRoot, tweenRoot, frame));
            Check(f.Reader.Unknown && roots.RootRecords == 3 && !roots.TryGetAnchorKind(manager, out _)
                && f.References.RootAttempts == attempts && managerRoot.Target == manager && f.References.Freed == 0);
        }
    }

    private static void ManagerPromotionIsNotRetroactive()
    {
        using var f = new Fixture(); var roots = f.Retain(); var manager = new IntPtr(0x7000);
        var queue = new IntPtr(0xA000); var array = new IntPtr(0xB000);
        var managerRoot = f.References.Root(manager); var tweenRoot = f.References.Root(f.References.Tween);
        f.Native.Add(f.ManagerRow(1, manager, queue, array, IntPtr.Zero));
        var attempts = f.References.RootAttempts;
        f.Reader.PromoteCurrentOperationManagerRoot(roots, managerRoot, tweenRoot, f.ManagerFrame(manager, f.References.Tween));
        Check(roots.Entries.Single(row => row.Identity == manager).OperationAnchor!.AfterSequence == 1 && f.References.RootAttempts == attempts);
        // The old raw Before is skipped. Its borrowed queue was never rooted;
        // owning the Manager now cannot make a later unmatched After readable.
        Check(f.Reader.ReadAvailable(roots, 0, 8).Records.Count == 0 && roots.LastReadSequence == 1);
        var after = f.ManagerRow(2, manager, queue, array, f.References.Tween); after.Stage = NativeTweenSourceStage.After;
        after.Flags |= NativeTweenSourceFlags.Paired; f.Native.Add(after);
        Throws<UnsupportedSignatureException>(() => f.Reader.ReadAvailable(roots, 1, 8));
        Check(f.Reader.Unknown && roots.LastReadSequence == 1 && f.References.RootAttempts == attempts && f.References.Freed == 0);
    }

    private sealed class Fixture : IDisposable, INativeTweenSourceOwner
    {
        internal readonly NativeTweenSourceReferencesRegression.Fixture References;
        internal readonly Transport Native = new();
        internal readonly NativeTweenSource Reader;
        internal bool FailPromotedValidation;
        internal Exception? OwnerFailure;
        internal Fixture(NativeTweenSourceReferencesRegression.RecordingLogger? diagnostics = null)
        {
            References = new(diagnostics);
            Reader = NativeTweenSource.CreateControlledForTests(Native, References.VerifyAccess, References.Root, (root, _) => {
                if (FailPromotedValidation) throw new UnsupportedSignatureException("Controlled promoted Tween class changed.");
                return root.Target;
            });
            Reader.BindControlledReferencesForTests(References.Owner); References.Consume = Reader.ObserveSynchronousReferences;
        }
        public void VerifyTweenSourceOwner()
        {
            References.VerifyAccess();
            if (OwnerFailure is { } error) throw error;
        }
        internal NativeTweenSourceRootSet Retain(NativeRuntime.RootHandle? diagnosticTween = null)
        {
            var references = new List<NativeTweenSourceRootReference> {
                new(References.Root(References.Manager), NativeTweenSourceObjectKind.Manager),
                new(References.Root(References.Tween), NativeTweenSourceObjectKind.Tween),
                new(References.Root(References.Array), NativeTweenSourceObjectKind.TweenArray) };
            if (diagnosticTween is not null) references.Add(new(diagnosticTween, NativeTweenSourceObjectKind.Tween, OwnershipAnchor: false));
            return Reader.RetainRootedObjects(this, references);
        }
        internal NativeOperationFrame PlayFrame(IntPtr tween) => new() { Version = 1, Size = 64, Kind = NativeOperationKind.Void0,
            Self = tween, Caller = new(0x6000), Sequence = 103 };
        internal NativeOperationFrame ManagerFrame(IntPtr manager, IntPtr tween) => new() { Version = 1, Size = 64,
            Kind = NativeOperationKind.Reference1, Self = manager, Arg0 = tween, Caller = new(0x6000), Sequence = 104 };
        internal NativeTweenSourceRecord ManagerRow(ulong sequence, IntPtr manager, IntPtr queue, IntPtr array, IntPtr tween)
        {
            var row = Row(sequence, array); row.Manager = manager; row.Container = queue;
            row.ArrayBefore = array; row.Tween = tween; return row;
        }
        internal NativeTweenSourceRecord PlayRow(ulong sequence, IntPtr tween, IntPtr child)
        {
            var row = Row(sequence, IntPtr.Zero); row.Operation = NativeTweenSourceOperation.Play; row.Site = NativeTweenSourceSite.PlayOnStart;
            row.Manager = row.ArrayBefore = row.ArrayAfter = IntPtr.Zero; row.Container = child; row.Tween = tween;
            return row;
        }
        internal uint Publish(IntPtr array) => References.Deliver(1, NativeTweenSourceReferencePhase.References,
            References.Ref(References.Manager, NativeTweenSourceReferenceRole.Manager),
            References.Ref(References.Tween, NativeTweenSourceReferenceRole.Tween), References.Ref(array, NativeTweenSourceReferenceRole.Array));
        internal NativeTweenSourceRecord Row(ulong sequence, IntPtr array) => new() {
            Version = 1, Size = 192, Site = NativeTweenSourceSite.ReadyPopFront, Stage = NativeTweenSourceStage.Before,
            Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
            ThreadId = NativeTweenSourceReferences.CurrentThreadId(), Depth = 1, Epoch = 7,
            UpdateSequence = 101, Transaction = 1001, EventSequence = sequence, SourcePc = new(0x5000), RelayReturnPc = new(0x6000),
            Manager = References.Manager, Tween = References.Tween, ArrayBefore = References.Array, ArrayAfter = array };
        public void Dispose() => References.Dispose();
    }
    private sealed class Transport : INativeTweenSourceTransport
    {
        internal NativeTweenSourceInformation Info = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1,
            BindingCount = 43, JournalCapacity = 8192, Epoch = 7 };
        private readonly Dictionary<ulong, NativeTweenSourceRecord> _records = new();
        internal void Add(NativeTweenSourceRecord row) { _records.Add(row.EventSequence, row); Info.Events = row.EventSequence; Info.RetainedRecords = row.EventSequence; }
        public uint GetInfo(ref NativeTweenSourceInformation information) { information = Info; return 0; }
        public uint ReadRecord(ulong epoch, ulong eventSequence, ref NativeTweenSourceRecord record)
            => epoch == 7 && _records.TryGetValue(eventSequence, out record) ? 0u : 3u;
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Synchronous source root/journal binding failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
