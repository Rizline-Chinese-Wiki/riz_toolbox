using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasSourceChecks
{
    private static IntPtr[] ActualPhysicalSource(FakeNative n)
    {
        var lists = n.CanvasSourceLists.ToArray();
        return new[] { n.GameplayInformation, n.GameplayProgress, n.SeekChart, n.GeometryCanvasControl,
                n.GeometryCanvasArray, n.CanvasSourceMovesArray, n.CanvasSourceEaseArray }
            .Concat(n.CanvasSourceSlots(n.GeometryCanvasArray)).Concat(n.CanvasSourceMoves).Concat(lists)
            .Concat(lists.Select(n.CanvasSourceBacking)).Concat(lists.SelectMany(p => n.CanvasSourceSlots(n.CanvasSourceBacking(p))))
            .Concat(n.CanvasSourceSlots(n.CanvasSourceEaseArray)).Concat(n.CanvasSourceSamples).Distinct().ToArray();
    }
    private static void Publication()
    {
        using (var f = new Fixture())
        {
            var first = f.Native.NoteViewRootLog.Count; var physical = ActualPhysicalSource(f.Native);
            var p = f.Capture(); f.Run.Dispose(); p.VerifySource();
            Check(physical.All(f.Native.HasRootFor) && physical.All(pointer => f.Native.NoteViewRootLog.Skip(first).Any(row => row.Target == pointer)),
                "Actual source did not independently root every array slot, List backing, Ease row and initialized sample container before caller roots were released.");
            f.ReadOnly(); p.Dispose();
            Check(f.Native.ActiveRoots == 0, "Independent Canvas source retained known physical handles after safe owner-thread disposal.");
        }
        foreach (var kind in new[] { "canvas", "transform", "move", "point", "point-item", "list", "ease-row", "samples" })
        {
            using var f = new Fixture(); var n = f.Native; var foreign = n.CanvasSourceCloneObject(n.GameplayInformation);
            Check(!n.HasRootFor(foreign), "Foreign publication fixture was already rooted.");
            switch (kind)
            {
                case "canvas": n.CanvasSourceSet(n.GameplayInformation, "canvasControl", foreign); break;
                case "transform": n.CanvasSourceSlot(n.GeometryCanvasArray, 2, foreign); break;
                case "move": n.CanvasSourceSlot(n.CanvasSourceMovesArray, 2, foreign); break;
                case "point": n.CanvasSourceSlot(n.CanvasSourceBacking(n.CanvasSourceList(0, true)), 2, foreign); break;
                case "point-item": n.CanvasSourceOverrideItem(n.CanvasSourceList(0, true), 2, foreign); break;
                case "list": n.CanvasSourceSet(n.CanvasSourceMoves[0], "speedKeyPoints", foreign); break;
                case "ease-row": n.CanvasSourceSlot(n.CanvasSourceEaseArray, 1, foreign); break;
                default: n.CanvasSourceSet(n.CanvasSourceSlots(n.CanvasSourceEaseArray)[1], "progress", foreign); break;
            }
            var inspected = 0; var roots = n.ActiveRoots; var publications = n.NoteViewRootLog.Count;
            n.AfterNoteViewObjectClass = pointer =>
            {
                if (pointer != foreign) return;
                ++inspected; Check(n.HasRootFor(pointer), "Rejected actual reference was inspected before immediate publication/root adoption.");
            };
            Reject(() => f.Capture()); f.Clear();
            Check((kind == "point-item" || inspected > 0) && n.NoteViewRootLog.Skip(publications).Any(row => row.Target == foreign)
                && n.CanvasSourcePublished && n.ActiveRoots == roots,
                "Wrong-class actual publication was not inspected only after rooting and then safely freed: " + kind);
        }
    }
    private static void Revoke(Fixture f, string kind)
    {
        switch (kind)
        {
            case "revision": f.State.ObservePause(true); break;
            case "withdrawal": f.State.ControllerUnavailable(); break;
            case "backend": f.Backend.Dispose(); break;
            default: f.Run.Dispose(); break;
        }
    }
    private static void Withdrawal()
    {
        foreach (var at in new[] { "profile", "getter", "reference-getter", "range", "root", "array-slot-root", "array-pin-root", "samples-pin-root", "samples-size" })
        foreach (var kind in new[] { "revision", "withdrawal", "backend", "run" })
        {
            using var f = new Fixture(); f.Context = "Withdrawal/capture/" + at + "/" + kind; var n = f.Native; var fired = false;
            void Withdraw()
            {
                if (fired) return; fired = true; Revoke(f, kind);
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            }
            if (at == "profile") n.CanvasSourceProfile.OnVerify = Withdraw;
            else if (at == "getter") n.AfterCanvasSourceRead = (name, _) => { if (name == "downVector") Withdraw(); };
            else if (at == "reference-getter") n.AfterCanvasSourceRead = (name, _) => { if (name == "canvasMoveData") Withdraw(); };
            else if (at == "range") n.AfterCanvasSourceRange = Withdraw;
            else if (at == "root") n.AfterGeometryRoot = (_, pointer) => { if (pointer == n.CanvasSourceMovesArray) Withdraw(); };
            else if (at == "samples-size") n.AfterGeometryArrayExport = (name, pointer) => { if (name == "size" && pointer == n.CanvasSourceSamples[1]) Withdraw(); };
            else n.AfterCanvasSourceRoot = (_, pointer, pinned) =>
            {
                if (at == "array-slot-root" && !pinned && pointer == n.CanvasSourceSlots(n.GeometryCanvasArray)[0]
                    || at == "array-pin-root" && pinned && pointer == n.GeometryCanvasArray
                    || at == "samples-pin-root" && pinned && pointer == n.CanvasSourceSamples[1]) Withdraw();
            };
            Reject(() => f.Capture(), f.Context); f.Clear();
            Check(fired && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "A source callback's logical revocation was followed by a metadata/getter/root/Unity export instead of known cleanup only: " + at + "/" + kind);
        }
        foreach (var at in new[] { "profile", "getter", "reference-getter", "root-target", "array-pin-root", "samples-pin-root", "samples-size" })
        foreach (var kind in new[] { "revision", "withdrawal", "backend" })
        {
            using var f = new Fixture(); f.Context = "Withdrawal/verify/" + at + "/" + kind;
            var p = f.Capture(); var n = f.Native; var fired = false;
            void Withdraw()
            { if (fired) return; fired = true; Revoke(f, kind); n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true; }
            if (at == "profile") n.CanvasSourceProfile.OnVerify = Withdraw;
            else if (at == "getter") n.AfterCanvasSourceRead = (name, _) => { if (name == "rightVector") Withdraw(); };
            else if (at == "reference-getter") n.AfterCanvasSourceRead = (name, _) => { if (name == "canvasMoveData") Withdraw(); };
            else if (at == "root-target") n.DuringHandleTarget = pointer => { if (pointer == n.GeometryCanvasControl) Withdraw(); };
            else if (at == "samples-size") n.AfterGeometryArrayExport = (name, pointer) => { if (name == "size" && pointer == n.CanvasSourceSamples[1]) Withdraw(); };
            else n.AfterCanvasSourceRoot = (_, pointer, pinned) =>
            { if (pinned && pointer == (at == "samples-pin-root" ? n.CanvasSourceSamples[1] : n.GeometryCanvasArray)) Withdraw(); };
            Reject(p.VerifySource); f.Clear(); Check(fired && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "Retained source verification dispatched another export after callback withdrawal: " + at + "/" + kind);
            n.BeginLowerIl2CppAudit(); try { Reject(p.VerifySource); } finally { n.EndLowerIl2CppAudit(); }
            Check(n.LowerIl2CppCalls == 0, "An already-revoked public source reentered native verification.");
        }
        foreach (var kind in new[] { "revision", "withdrawal", "backend", "run" })
        {
            using var f = new Fixture(); f.Context = "Withdrawal/range-exception/" + kind; var n = f.Native; var fired = false;
            n.AfterCanvasSourceRange = () =>
            {
                if (fired) return; fired = true; Revoke(f, kind);
                n.CanvasSourceRangeException = n.OtherObject;
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(() => f.Capture(), f.Context); f.Clear();
            Check(fired && n.CanvasSourceRangeAttempts == 1 && n.CanvasSourceRangeExceptionWrites == 1
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "A revoked native range exception entered method-name/error/object inspection after callback return instead of known cleanup only: " + kind);
        }
        foreach (var at in new[] { "profile", "getter" })
        {
            using var f = new Fixture(); f.Context = "Withdrawal/cross-construction/" + at;
            var n = f.Native; var p = f.Capture(); var plan = p.Plan;
            var fired = false; var priorVerifiedInside = false; var intervals = n.CanvasSourceRangeAttempts;
            void DisposeLaterConstruction()
            {
                if (fired) return; fired = true; f.Run.Dispose();
                // The earlier independent owner may export while the later
                // construction is suspended in this callback. Audit only the
                // later construction's continuation after that verification.
                n.CanvasSourceAuditExports = false;
                try { p.VerifySource(); priorVerifiedInside = true; }
                finally { n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true; }
            }
            if (at == "profile") n.CanvasSourceProfile.OnVerify = DisposeLaterConstruction;
            else n.AfterCanvasSourceRead = (name, _) => { if (name == "downVector") DisposeLaterConstruction(); };
            Reject(() => f.Capture()); f.Clear();
            Check(fired && priorVerifiedInside && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "A later construction did not revoke locally after caller disposal, or expired an earlier independently retained source: " + at);
            p.VerifySource();
            Check(ReferenceEquals(plan, p.Plan) && n.CanvasSourceRangeAttempts == intervals
                && ActualPhysicalSource(n).All(n.HasRootFor),
                "A later cancelled construction consumed, retargeted or borrowed the earlier immutable Canvas owner: " + at);
            p.Dispose();
            Check(n.ActiveRoots == 0, "Cross-construction Canvas cancellation leaked known physical source roots: " + at);
        }
    }
    private static void NoNestedExports(FakeNative n, Action nested)
    {
        var lookups = n.RequestedExports.Count; var roots = n.ActiveRoots; var ranges = n.CanvasSourceRangeAttempts;
        n.BeginLowerIl2CppAudit();
        try { nested(); } finally { n.EndLowerIl2CppAudit(); }
        Check(n.LowerIl2CppCalls == 0 && n.RequestedExports.Count == lookups && n.ActiveRoots == roots && n.CanvasSourceRangeAttempts == ranges,
            "Rejected synchronous reentry reached a lower export, lazy lookup, source acquisition or official interval.");
    }
    private static void Reentry()
    {
        using var f = new Fixture(); var n = f.Native; n.BeforeExport = name => n.RequestedExports.Add(name); var fired = false;
        n.CanvasSourceProfile.OnVerify = () =>
        {
            if (fired) return; fired = true;
            NoNestedExports(n, () =>
            {
                Reject(() => f.Capture());
                Reject(() => f.Run.CaptureSeekCanvasTargetSource(null!, float.NaN, float.PositiveInfinity));
            });
        };
        var p = f.Capture(); f.Clear(); Check(fired, "Real source construction profile callback did not exercise same-RunValue reentry.");
        n.BeforeExport = name => n.RequestedExports.Add(name); var count = 0;
        n.AfterCanvasSourceRead = (name, _) =>
        {
            if (name != "downVector") return;
            ++count; NoNestedExports(n, () => { Reject(p.VerifySource); Reject(p.Dispose); });
        };
        p.VerifySource(); f.Clear(); Check(count > 0, "Retained source verification did not exercise busy wrapper reentry.");
        p.VerifySource(); Check(p.Plan.Rows.Count == 3, "Rejected nested verification/disposal consumed the successful outer owner.");
        n.AfterCanvasSourceRange = () => NoNestedExports(n, () => Reject(() => f.Capture()));
        var q = f.Capture(); f.Clear(); q.VerifySource();
        Check(q.Plan.Rows.Count == 3, "Rejected official range callback reentry damaged the outer source construction gate.");
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var p = f.Capture(); var n = f.Native; var plan = p.Plan;
        var roots = n.ActiveRoots; var frees = n.FreeHandleAttempts; var ranges = n.CanvasSourceRangeAttempts;
        n.BeforeExport = name => n.RequestedExports.Add(name); var lookups = n.RequestedExports.Count;
        var errors = new List<Exception>(); n.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() =>
            {
                foreach (Action action in new Action[] { () => f.Capture(), p.VerifySource, p.Dispose,
                    () => f.Run.CaptureSeekCanvasTargetSource(null!, float.NaN, float.PositiveInfinity) })
                    errors.Add(Reject(action));
            }).GetAwaiter().GetResult();
        }
        finally { n.EndLowerIl2CppAudit(); }
        Check(errors.Count == 4 && errors.All(e => e.GetType() == typeof(InvalidOperationException))
            && n.LowerIl2CppCalls == 0 && n.LowerWorkerIl2CppCalls == 0 && n.RequestedExports.Count == lookups
            && n.ActiveRoots == roots && n.FreeHandleAttempts == frees && n.CanvasSourceRangeAttempts == ranges,
            "Worker capture/verify/dispose, including malformed inputs, did not reject at the owner-thread gate before native/lazy access or responsibility changes.");
        f.Clear(); p.VerifySource(); Check(ReferenceEquals(plan, p.Plan), "Worker rejection consumed or retargeted the main-thread owner.");
        p.Dispose(); var q = f.Capture(); q.VerifySource();
        Check(q.Plan.Rows.Count == 3, "Worker rejection poisoned a later valid main-thread source construction.");
    }
    private static void UnknownCleanup()
    {
        using (var f = new Fixture())
        {
            var p = f.Capture(); f.Run.Dispose(); var n = f.Native; var point = n.CanvasSourcePoint(0, true, 2);
            var handle = n.NoteViewHandlesFor(point).Last(); n.FailFreeHandle = handle;
            var error = Reject(p.Dispose); f.Retain(p, error);
            var failures = n.NoteViewFreeLog.Where(row => row.InjectedFailure).ToArray();
            Check(failures.Length == 1 && failures[0].Handle == handle && failures[0].Target == point && n.ActiveRoots == 0,
                "Unknown source Dispose lost its selected actual root or stopped cleanup of known physical roots.");
            var attempts = n.FreeHandleAttempts; var retry = Reject(p.Dispose);
            Check(ReferenceEquals(retry.Data["SeekCanvasSourceCleanupOwner"], p)
                && retry.Data["SeekCanvasSourceCleanupFailure"] is Exception && n.FreeHandleAttempts == attempts,
                "Unknown public Canvas owner was discarded or native free retried on repeated disposal.");
        }
        foreach (var stage in new[] { "getter", "range" })
        {
            using var f = new Fixture(); var n = f.Native; var fired = false; uint handle = 0;
            var point = n.CanvasSourcePoint(0, true, 2);
            void Abort()
            {
                if (fired) return; fired = true; handle = n.NoteViewHandlesFor(point).Last();
                n.FailFreeHandle = handle; throw new IOException("Synthetic actual Canvas capture failure with unknown root cleanup.");
            }
            if (stage == "getter") n.AfterCanvasSourceRead = (name, owner) => { if (name == "floorPosition" && owner == point) Abort(); };
            else n.AfterCanvasSourceRange = Abort;
            var failure = Reject(() => f.Capture()); f.Clear();
            var retained = failure.Data["SeekCanvasSourceCleanupOwner"] as NativeSeekCanvasTargetCapture;
            Check(fired && handle != 0 && retained is not null && failure.Data["SeekCanvasSourceCleanupFailure"] is Exception
                && n.NoteViewFreeLog.Count(row => row.InjectedFailure && row.Handle == handle && row.Target == point) == 1,
                "Capture failure discarded the exact public Canvas wrapper or selected unknown-free responsibility: " + stage);
            f.Retain(retained!, failure); var attempts = n.FreeHandleAttempts; var retry = Reject(retained!.Dispose);
            Check(ReferenceEquals(retry.Data["SeekCanvasSourceCleanupOwner"], retained) && n.FreeHandleAttempts == attempts,
                "Retained public owner after construction failure retried an unknown native free.");
        }
    }
}
