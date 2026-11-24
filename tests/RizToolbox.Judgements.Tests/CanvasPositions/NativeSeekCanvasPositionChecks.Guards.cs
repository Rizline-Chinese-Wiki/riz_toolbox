using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasPositionChecks
{
    private static void IndependentLifetime()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var n = f.Native;
            var physical = new[] { n.GameplayInformation, n.GameplayProgress, n.SeekChart, n.GeometryCanvasControl,
                n.GeometryCanvasArray, n.CanvasSourceMovesArray, n.CanvasSourceEaseArray }
                .Concat(n.CanvasPositionTransforms).Concat(n.CanvasSourceMoves).Concat(n.CanvasSourceLists)
                .Concat(n.CanvasSourceLists.Select(n.CanvasSourceBacking)).Concat(n.CanvasSourceSamples).Distinct().ToArray();
            f.Run.Dispose(); p.VerifyPrepared();
            Check(physical.All(n.HasRootFor), "Position owner borrowed caller RunValue physical source/Transform roots.");
            f.Execute(p); Check(p.VerifyAppliedValues(), "Independently retained Canvas writer expired after applicant RunValue disposal.");
            p.RestoreSelectedValues(); Check(p.VerifyRestoredValues(), "Independent writer could not restore after applicant disposal.");
        }
        foreach (var at in new[] { "source-profile", "position-profile", "getter" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false; var verified = false;
            void CancelLaterConstruction()
            {
                if (fired) return; fired = true; f.Run.Dispose();
                n.CanvasSourceAuditExports = false;
                try { p.VerifyPrepared(); verified = true; }
                finally { n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true; }
            }
            if (at == "source-profile") n.CanvasSourceProfile.OnVerify = CancelLaterConstruction;
            else if (at == "position-profile") n.CanvasPositionProfile.OnVerify = CancelLaterConstruction;
            else n.AfterGeometryGetter = (_, _) => CancelLaterConstruction();
            Reject(() => f.Prepare()); f.Clear();
            Check(fired && verified && n.GeometryCalls.Count == 0
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "Later construction cancellation continued native exports or consumed an earlier independent writer: " + at);
            p.VerifyPrepared(); p.Dispose();
            Check(n.ActiveRoots == 0, "Cancelled later construction or safe earlier owner leaked physical roots: " + at);
        }
    }
    private static void CallbackWithdrawal()
    {
        foreach (var nativeException in new[] { false, true })
        foreach (var kind in new[] { "revision", "backend", "dispose" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false;
            if (nativeException) n.CanvasPositionGetterException = n.OtherObject;
            n.AfterGeometryGetter = (_, _) =>
            {
                if (fired) return; fired = true;
                if (kind == "dispose") p.Dispose(); else Revoke(f, kind);
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(p.VerifyPrepared); f.Clear();
            Check(fired && n.GeometryCalls.Count == 0 && p.GetJournal().AttemptedWrites == 0
                && (!nativeException || n.CanvasPositionGetterExceptionWrites == 1)
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "Getter callback revocation reached later metadata/object/error inspection instead of known cleanup only: " + kind + "/" + nativeException);
            NoNative(n, () => Reject(p.VerifyPrepared));
        }
        foreach (var nativeException in new[] { false, true })
        foreach (var kind in new[] { "revision", "backend", "dispose" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false;
            if (nativeException) n.CanvasPositionSetterException = n.OtherObject;
            n.AfterGeometryStore = _ =>
            {
                if (fired) return; fired = true;
                if (kind == "dispose") p.Dispose(); else Revoke(f, kind);
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            var failure = Reject(() => f.Execute(p)); f.Clear(); var j = p.GetJournal();
            Check(fired && n.GeometryCalls.Count == 1 && j.AttemptedWrites == 1 && j.Commands.Count == 1
                && j.Commands[0].NativeEntered && j.HasRetainedResponsibility && j.SideEffectsUnknown
                && ReferenceEquals(failure.Data["SeekCanvasPositionEffectsOwner"], p)
                && (!nativeException || n.CanvasPositionSetterExceptionWrites == 1)
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "Setter callback revocation inspected a native exception or continued getter/setter exports after callback return: " + kind + "/" + nativeException);
            NoNative(n, () => { Reject(p.Execute); Reject(p.RestoreSelectedValues); });
        }
    }
    private static void ThreadAndReentry()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var failures = new List<Exception>();
            NoNative(f.Native, () => Task.Run(() =>
            {
                foreach (Action action in new Action[] { () => f.Prepare(),
                    () => f.Run.PrepareSeekCanvasPositions(null!, null!, float.NaN, float.PositiveInfinity),
                    p.VerifyPrepared, p.Execute, () => p.VerifyAppliedValues(), p.RestoreSelectedValues,
                    () => p.VerifyRestoredValues(), p.Dispose, () => p.GetJournal(), () => _ = p.SideEffectsUnknown,
                    () => _ = p.HasRetainedResponsibility }) failures.Add(Reject(action));
            }).GetAwaiter().GetResult());
            Check(failures.Count == 11 && failures.All(e => e.GetType() == typeof(InvalidOperationException)),
                "Canvas worker admission or diagnostics did not reject before native validation.");
            p.VerifyPrepared(); Check(!p.GetJournal().ExecutionEntered && !p.GetJournal().Disposed,
                "Worker rejection consumed a main-thread writer or its once-only execution.");
        }
        using var outer = new Fixture(); var n = outer.Native; var preparing = false;
        n.CanvasPositionProfile.OnVerify = () =>
        {
            if (preparing) return; preparing = true;
            NoNative(n, () =>
            {
                Reject(() => outer.Prepare());
                Reject(() => outer.Run.PrepareSeekCanvasPositions(null!, null!, float.NaN, float.PositiveInfinity));
            });
        };
        var q = outer.Prepare(); outer.Clear(); Check(preparing, "Canvas construction reentry callback did not run.");
        var executing = false;
        n.BeforeGeometryCall = _ =>
        {
            if (executing) return; executing = true;
            NoNative(n, () =>
            {
                Reject(q.Execute); Reject(q.VerifyPrepared); Reject(() => q.VerifyAppliedValues());
                Reject(q.RestoreSelectedValues); Reject(() => q.VerifyRestoredValues()); Reject(() => q.GetJournal());
            });
        };
        outer.Execute(q); outer.Clear();
        Check(executing && n.GeometryCalls.Count == 3 && q.VerifyAppliedValues(),
            "Rejected synchronous command reentry damaged the outer complete actual sequence.");
    }
}
