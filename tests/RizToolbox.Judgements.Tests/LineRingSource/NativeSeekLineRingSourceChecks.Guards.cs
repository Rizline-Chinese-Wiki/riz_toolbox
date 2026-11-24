using System.Reflection;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    private static void PublicationAndWithdrawal()
    {
        foreach (var scalar in new[] { false, true })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native; var fired = false;
            n.AfterCanvasSourceRead = (name, receiver) =>
            {
                if (fired || receiver != n.LineRingControl || name != (scalar ? "_nowTime" : "dynamicLines")) return;
                fired = true; source.Dispose(); n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(source.VerifyBaseline); f.Clear();
            Check(fired && source.GetJournal().Disposed && !source.HasRetainedResponsibility
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "Line/ring field callback withdrawal continued native reads or invented unknown cleanup.");
            basis.OriginalCacheOwner.VerifyPrepared(); basis.OriginalPositionOwner.VerifyPrepared(); basis.VerifyPrepared();
            NoNative(n, () => Reject(source.VerifySource)); NoWrites(n, "Read-only withdrawal entered a writer.");
        }
        // These callbacks run inside actual official exports. A value returned
        // by a withdrawn getter is still GC-owned until its handle is acquired.
        foreach (var at in new[] { "array-pin-root", "get_parent", "get_position", "get_localPosition",
            "get_localRotation", "get_localScale", "pose-box-target" })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
            var transform = n.LineRingLineTransform(n.LineRingOrphanDynamic);
            var roots = n.CanvasBasisCurrentRoots(); var publications = PublicationCounts.Read();
            var fired = false; var poseReturned = false; uint temporary = 0;
            void Withdraw()
            {
                fired = true; source.Dispose(); n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            }
            if (at == "array-pin-root") n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
            {
                if (fired || !pinned || pointer != n.LineRingDynamicArray) return;
                temporary = handle; Withdraw();
            };
            else if (at == "pose-box-target")
            {
                n.AfterGeometryGetter = (name, receiver) =>
                { if (name == "get_localPosition" && receiver == transform) poseReturned = true; };
                n.DuringHandleTarget = pointer =>
                {
                    // LineRingLastBox is assigned only after AfterGeometryGetter
                    // returns; this is the later gchandle_get_target boundary.
                    if (fired || !poseReturned || pointer == IntPtr.Zero || pointer != n.LineRingLastBox) return;
                    temporary = n.NoteViewHandlesFor(pointer).Single(); Withdraw();
                };
            }
            else n.AfterGeometryGetter = (name, receiver) =>
            {
                if (fired || name != at || receiver != transform) return;
                temporary = n.NoteViewHandlesFor(receiver).Last(); Withdraw();
            };
            Reject(source.VerifyBaseline); f.Clear(); var journal = source.GetJournal();
            Check(fired && temporary != 0 && !roots.ContainsKey(temporary)
                && n.NoteViewFreeLog.Count(row => row.Handle == temporary && !row.InjectedFailure) == 1
                && journal.Disposed && journal.CleanupFailure is null
                && !source.HasRetainedResponsibility && PublicationCounts.Read() == publications
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "Deep line/ring callback withdrawal consumed another export or lost known temporary cleanup: " + at);
            OriginalRootsRemain(n, roots, "Caller-only getter withdrawal released a sibling's original roots: " + at);
            basis.OriginalCacheOwner.VerifyPrepared(); basis.OriginalPositionOwner.VerifyPrepared(); basis.VerifyPrepared();
            NoNative(n, () => { Reject(source.VerifySource); Reject(source.VerifyBaseline); source.Dispose(); });
        }
        using var construction = new Fixture(); var value = construction.Prepare(); var native = construction.Native; var cancelled = false;
        native.AfterCanvasSourceRoot = (_, pointer, _) =>
        {
            if (cancelled || pointer != native.LineRingControl) return; cancelled = true; value.Dispose();
            native.CanvasSourceExportLog.Clear(); native.CanvasSourceAuditExports = true;
        };
        Reject(() => construction.Capture(value)); construction.Clear();
        Check(cancelled && native.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free") && native.ActiveRoots > 0,
            "Aggregate construction cancellation kept consuming line/ring source or lost the applicant Run roots.");
    }

    private static void WorkerAndSharedReentry()
    {
        using var f = new Fixture(); var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
        NoNative(n, () => Task.Run(() =>
        {
            foreach (Action action in new Action[] { source.VerifySource, source.VerifyBaseline, source.Dispose,
                () => source.GetJournal(), () => _ = source.Snapshot, () => _ = source.Coverage,
                () => basis.CaptureLineRingSource(n.LineRingSourceProfile) }) Reject(action);
        }).GetAwaiter().GetResult());
        var fired = false;
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (fired || receiver != n.LineRingControl || name != "_nowTime") return;
            fired = true; NoNative(n, () =>
            {
                Reject(source.VerifySource); Reject(source.VerifyBaseline); Reject(() => source.GetJournal());
                Reject(basis.OriginalCacheOwner.VerifyPrepared); Reject(basis.OriginalPositionOwner.VerifyPrepared);
                Reject(basis.VerifyPrepared); Reject(() => basis.CaptureLineRingSource(n.LineRingSourceProfile));
            });
        };
        source.VerifyBaseline(); f.Clear(); Check(fired, "No actual line/ring callback reentry was exercised.");
        source.VerifySource(); basis.VerifyPrepared(); NoWrites(n, "Rejected reader/child reentry wrote native values.");
    }

    private static void IndependentDisposal()
    {
        foreach (var readerFirst in new[] { false, true })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
            f.Run.Dispose(); var roots = n.CanvasBasisCurrentRoots();
            if (readerFirst)
            {
                source.Dispose(); OriginalRootsRemain(n, roots, "Reader disposal freed the active original Canvas children.");
                basis.VerifyPrepared(); basis.Dispose();
            }
            else
            {
                basis.Dispose(); OriginalRootsRemain(n, roots, "Aggregate disposal freed an independent reader hold.");
                source.VerifySource(); source.VerifyBaseline(); source.Dispose();
            }
            Check(n.ActiveRoots == 0 && !source.HasRetainedResponsibility, "The final known reader/aggregate hold did not free the original graph once.");
            NoNative(n, () => { source.Dispose(); basis.Dispose(); });
        }
    }

    private static void ProofsAndImmutableHistory()
    {
        using var f = new Fixture(); var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
        source.VerifyBaseline(); var prior = source.GetJournal(); var snapshot = source.Snapshot;
        Readonly(snapshot.DynamicLines, "Dynamic occurrences are writable."); Readonly(snapshot.Groups, "Physical groups are writable.");
        foreach (var group in snapshot.Groups) Readonly(group.Lines, "Group line occurrences are writable.");
        Readonly(source.Coverage.RequiredWork, "Required work records are writable.");
        n.LineRingSet(n.LineRingControl, "_speed", 99f); Reject(source.VerifyBaseline);
        Check(prior.SourceVerified && prior.BaselineVerified && !source.GetJournal().BaselineVerified && !source.GetJournal().SourceVerified,
            "A failed fresh baseline observation preserved stale proof or mutated a previous journal.");
        source.VerifySource(); Check(!source.GetJournal().BaselineVerified, "Source-only verification claimed the old baseline.");
        f.AllowBasisWrites = true; basis.Execute(); var position = basis.OriginalPositionOwner;
        var roots = n.CanvasBasisCurrentRoots(); source.Dispose();
        OriginalRootsRemain(n, roots, "Source disposal released entered Unity position effects.");
        var error = Reject(basis.Dispose);
        Check(position.HasRetainedResponsibility && ReferenceEquals(Data(error, "SeekCanvasPositionEffectsOwner"), position)
            && basis.GetJournal().ExternalEffectsUnclosed && source.GetJournal().AttemptedWrites == 0,
            "A read-only source closed the original position effects or claimed native writes.");
    }

    private static void BudgetsAndUnknownCleanup()
    {
        var type = typeof(NativeSeekLineRingSourceCapture);
        var owners = type.GetField("Owners", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        int Count() => (int)owners.GetType().GetProperty("Count")!.GetValue(owners)!;
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var added = new List<object>(); var initial = Count();
            var add = owners.GetType().GetMethod("Add")!; var remove = owners.GetType().GetMethod("Remove")!;
            try
            {
                while (Count() < 256) { var owner = Activator.CreateInstance(type, nonPublic: true)!; add.Invoke(owners, new[] { owner }); added.Add(owner); }
                NoNative(f.Native, () => Reject(() => f.Capture(basis)));
                Check(Count() == 256, "Capacity refusal consumed a line/ring owner before native acquisition.");
            }
            finally { foreach (var owner in added) remove.Invoke(owners, new[] { owner }); }
            Check(Count() == initial, "Capacity fixture changed an original live line/ring owner.");
        }
        // Reject an unconsumed get_parent token outside its publication
        // callback. Root pressure must not create a hidden unconfirmed token or
        // permanently expire a source whose original baseline can be restored.
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
            var core = Field(Field(Field(basis, "_basis"), "_source"), "_core");
            var limit = core.GetType().GetField("_rootLimit", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var originalLimit = (int)limit.GetValue(core)!; var retained = Field(core, "_roots");
            var count = (int)retained.GetType().GetProperty("Count")!.GetValue(retained)!;
            var transform = n.LineRingLineTransform(n.LineRingOrphanDynamic); var original = n.GeometryTransforms[transform];
            var parent = n.GeometryNewTransform(IntPtr.Zero, new(7, 8, 9), new(0, 0, 0, 1), new(1, 1, 1));
            var roots = n.CanvasBasisCurrentRoots(); var publications = PublicationCounts.Read();
            var fired = false; var rootsAtReturn = -1;
            try
            {
                limit.SetValue(core, count); n.GeometryTransforms[transform] = original with { Parent = parent };
                n.AfterGeometryGetter = (name, receiver) =>
                {
                    if (fired || name != "get_parent" || receiver != transform) return;
                    fired = true; rootsAtReturn = n.NewRoots;
                    n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
                };
                Reject(source.VerifySource); f.Clear(); var journal = source.GetJournal();
                Check(fired && n.NewRoots == rootsAtReturn && PublicationCounts.Read() == publications
                    && NativePublicationCall.UnconfirmedPublicationCount == publications.Unknown
                    && !journal.SourceExpired && !journal.HasRetainedResponsibility && journal.CleanupFailure is null
                    && !journal.SourceVerified && !journal.BaselineVerified
                    && !n.CanvasBasisCurrentRoots().Values.Contains(parent)
                    && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                    "A full actual root table published an unconfirmed parent token, allocated after refusal, or poisoned the holder.");
                OriginalRootsRemain(n, roots, "Managed line/ring root-budget refusal consumed original roots.");
            }
            finally
            {
                f.Clear(); limit.SetValue(core, originalLimit); n.GeometryTransforms[transform] = original;
            }
            source.VerifySource(); source.VerifyBaseline();
            basis.OriginalCacheOwner.VerifyPrepared(); basis.OriginalPositionOwner.VerifyPrepared(); basis.VerifyPrepared();
        }

        // The delegated common Canvas reader has its own adopter. A changed
        // list token must also be acknowledged before a managed budget refusal.
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native; var canvas = basis.SourceCapture;
            var core = Field(Field(Field(basis, "_basis"), "_source"), "_core");
            var limit = core.GetType().GetField("_rootLimit", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var originalLimit = (int)limit.GetValue(core)!; var retained = Field(core, "_roots");
            var count = (int)retained.GetType().GetProperty("Count")!.GetValue(retained)!;
            var roots = n.CanvasBasisCurrentRoots(); var publications = PublicationCounts.Read();
            var fired = false; var rootsAtReturn = -1;
            try
            {
                limit.SetValue(core, count); n.CanvasSourceReplaceList(0, true); var replacement = n.CanvasSourceList(0, true);
                n.AfterCanvasSourceRead = (name, receiver) =>
                {
                    if (fired || name != "speedKeyPoints" || receiver != n.CanvasSourceMoves[0]) return;
                    fired = true; rootsAtReturn = n.NewRoots;
                    n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
                };
                Reject(source.VerifySource); f.Clear(); var journal = source.GetJournal();
                Check(fired && n.NewRoots == rootsAtReturn && PublicationCounts.Read() == publications
                    && NativePublicationCall.UnconfirmedPublicationCount == publications.Unknown
                    && journal.SourceExpired && !journal.HasRetainedResponsibility && journal.CleanupFailure is null
                    && ReferenceEquals(journal.CanvasCapture, canvas) && ReferenceEquals(source.Snapshot.Identity.CanvasCapture, canvas)
                    && ReferenceEquals(basis.SourceCapture, canvas) && !n.CanvasBasisCurrentRoots().Values.Contains(replacement)
                    && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                    "Common Canvas root-budget refusal lost the original owner or retained an unconfirmed replacement list token.");
                OriginalRootsRemain(n, roots, "Common Canvas budget refusal consumed originally retained physical roots.");
                NoNative(n, () =>
                {
                    Reject(source.VerifySource); Reject(source.VerifyBaseline); Reject(basis.VerifyPrepared);
                    Reject(basis.OriginalCacheOwner.VerifyPrepared); Reject(basis.OriginalPositionOwner.VerifyPrepared);
                });
                Check(PublicationCounts.Read() == publications && !basis.GetJournal().HasRetainedResponsibility,
                    "Sticky common-source drift became a synthetic unknown cleanup or native publication retry.");
            }
            finally { f.Clear(); limit.SetValue(core, originalLimit); }
        }

        // A temporary pose receiver or result already owns native cleanup even
        // though this source can never enter a setter.
        foreach (var kind in new[] { "receiver", "box" })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
            var roots = n.CanvasBasisCurrentRoots(); var transform = n.LineRingLineTransform(n.LineRingOrphanDynamic);
            var fired = false; var poseReturned = false; uint failed = 0; var failedTarget = IntPtr.Zero;
            n.AfterGeometryGetter = (name, receiver) =>
            {
                if (fired || name != "get_localRotation" || receiver != transform) return;
                poseReturned = true;
                if (kind != "receiver") return;
                fired = true; failed = n.NoteViewHandlesFor(receiver).Last(); failedTarget = receiver; n.FailFreeHandle = failed;
            };
            if (kind == "box") n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
            {
                if (fired || !poseReturned || pinned || pointer == IntPtr.Zero || pointer != n.LineRingLastBox) return;
                fired = true; failed = handle; failedTarget = pointer; n.FailFreeHandle = handle;
            };
            var failure = Reject(source.VerifyBaseline); f.Clear(); f.AcceptUnknownPublication();
            Check(fired && poseReturned && !roots.ContainsKey(failed) && source.GetJournal().Captured,
                "The actual pose temporary unknown-free boundary did not fire: " + kind);
            RetainUnknownLineRingPublication(f, basis, source, failure, roots, failed, failedTarget);
        }

        // The public capture must survive both a reader factory that never
        // returns its reader and a later snapshot that never finishes capture.
        foreach (var readerConstruction in new[] { true, false })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var n = f.Native; var roots = n.CanvasBasisCurrentRoots();
            var transform = n.LineRingLineTransform(n.LineRingOrphanDynamic);
            var armed = false; var fired = false; uint failed = 0; var failedTarget = IntPtr.Zero;
            HashSet<uint>? controlRoots = null;
            if (readerConstruction)
            {
                n.AfterCanvasSourceRead = (name, receiver) =>
                {
                    if (armed || name != "_canvasControl" || receiver != n.LineRingControl) return;
                    armed = true; controlRoots = n.NoteViewHandlesFor(receiver).ToHashSet();
                };
                n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
                {
                    // VerifySource's next control root is the op_Implicit
                    // argument, not the already adopted permanent control root.
                    if (fired || !armed || pinned || pointer != n.LineRingControl || controlRoots!.Contains(handle)) return;
                    fired = true; failed = handle; failedTarget = pointer; n.FailFreeHandle = handle;
                };
            }
            else
            {
                n.AfterGeometryGetter = (name, receiver) =>
                { if (name == "get_localRotation" && receiver == transform) armed = true; };
                n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
                {
                    if (fired || !armed || pinned || pointer == IntPtr.Zero || pointer != n.LineRingLastBox) return;
                    fired = true; failed = handle; failedTarget = pointer; n.FailFreeHandle = handle;
                };
            }
            var failure = Reject(() => f.Capture(basis)); f.Clear(); f.AcceptUnknownPublication();
            var owner = Owner<NativeSeekLineRingSourceCapture>(failure, "SeekLineRingSourceCleanupOwner");
            Check(fired && armed && owner is not null && !owner.GetJournal().Captured && owner.GetJournal().Disposed
                && (type.GetField("_reader", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner) is null) == readerConstruction,
                "Half-constructed line/ring capture lost its public owner or did not fail at the intended reader/snapshot boundary.");
            RetainUnknownLineRingPublication(f, basis, owner!, failure, roots, failed, failedTarget);
        }
        foreach (var temporary in new[] { false, true })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
            var fired = false; uint failed = 0;
            if (temporary)
            {
                n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
                {
                    if (fired || !pinned || pointer != n.LineRingDynamicArray) return;
                    fired = true; failed = handle; n.FailFreeHandle = handle;
                };
                var error = Reject(source.VerifySource); f.Clear(); f.AcceptUnknownPublication();
                Check(fired && ReferenceEquals(Data(error, "SeekLineRingSourceCleanupOwner"), source)
                    && source.HasRetainedResponsibility && source.GetJournal().SourceCleanupOwner is object,
                    "Unknown line/ring array pin cleanup lost its exact original public/native owners.");
                NoNative(n, () => { Reject(source.VerifySource); Reject(source.Dispose); Reject(basis.OriginalCacheOwner.VerifyPrepared); });
            }
            else
            {
                f.Run.Dispose(); basis.Dispose(); failed = n.NoteViewHandlesFor(n.LineRingControl).Last(); n.FailFreeHandle = failed;
                var error = Reject(source.Dispose);
                Check(ReferenceEquals(Data(error, "SeekLineRingSourceCleanupOwner"), source) && source.HasRetainedResponsibility
                    && n.ActiveRoots == 0, "Terminal source free lost public responsibility or skipped independent known cleanup.");
            }
            var attempts = n.FreeHandleAttempts; NoNative(n, () => Reject(source.Dispose));
            Check(n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1 && n.FreeHandleAttempts == attempts,
                "An unknown original source/pin free was retried.");
        }
    }

    private static void RetainUnknownLineRingPublication(Fixture f, NativeSeekCanvasBasisCommands basis,
        NativeSeekLineRingSourceCapture source, Exception failure, IReadOnlyDictionary<uint, IntPtr> roots,
        uint failed, IntPtr failedTarget)
    {
        var n = f.Native; var publication = Owner<NativePublicationCall>(failure, "NativePublicationCleanupOwner");
        var journal = source.GetJournal(); var canvas = basis.SourceCapture;
        Check(failed != 0 && failedTarget != IntPtr.Zero && publication is not null
            && ReferenceEquals(Data(failure, "SeekLineRingSourceCleanupOwner"), source)
            && ReferenceEquals(Data(failure, "SeekLineRingSourceNativeCleanupOwner"), publication)
            && ReferenceEquals(Data(failure, "SeekCanvasBasisSourceCleanupOwner"), publication)
            && ReferenceEquals(Data(failure, "SeekCanvasBasisSourceOwner"), Field(basis, "_basis"))
            && ReferenceEquals(Data(failure, "SeekCanvasBasisSourceCapture"), canvas)
            && ReferenceEquals(journal.CanvasCapture, canvas) && ReferenceEquals(journal.CleanupOwner, source)
            && ReferenceEquals(journal.SourceCleanupOwner, publication) && ReferenceEquals(basis.GetJournal().SourceCleanupOwner, publication)
            && journal.HasRetainedResponsibility && journal.CleanupFailure is not null && journal.AttemptedWrites == 0
            && !journal.CompleteSeekPermitted && Data(failure, "NativePublicationCleanupFailure") is Exception
            && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.Target == failedTarget && row.InjectedFailure) == 1,
            "Unknown line/ring temporary free replaced the exact public/publication/shared-source cleanup owners.");
        OriginalRootsRemain(n, roots, "Unknown line/ring getter cleanup released original source roots.");
        f.Preserve(source, failure); f.Preserve(publication!, failure);
        var attempts = n.FreeHandleAttempts; Exception? repeated = null;
        NoNative(n, () =>
        {
            Reject(source.VerifySource); Reject(source.VerifyBaseline); repeated = Reject(source.Dispose);
            Reject(basis.OriginalCacheOwner.VerifyPrepared); Reject(basis.OriginalPositionOwner.VerifyPrepared);
            Reject(basis.VerifyPrepared); Reject(basis.Dispose);
        });
        Check(repeated is not null && ReferenceEquals(Data(repeated, "SeekLineRingSourceCleanupOwner"), source)
            && ReferenceEquals(Data(repeated, "SeekLineRingSourceNativeCleanupOwner"), publication)
            && ReferenceEquals(source.GetJournal().SourceCleanupOwner, publication) && n.FreeHandleAttempts == attempts,
            "An unknown getter cleanup was retried or its original native/public owner was replaced.");
        OriginalRootsRemain(n, roots, "Unknown cleanup refusal later consumed the original retained graph.");
    }

}
