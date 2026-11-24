using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldStateChecks
{
    private sealed class CallbackProfile : ISeekHoldNativeProfile
    {
        private readonly ISeekHoldNativeProfile _basis; internal Action? OnAccess;
        internal CallbackProfile(ISeekHoldNativeProfile basis) => _basis = basis;
        public IntPtr ChartClass { get { OnAccess?.Invoke(); return _basis.ChartClass; } }
        public IntPtr ProgressClass => _basis.ProgressClass;
        public IntPtr InformationClass => _basis.InformationClass;
        public IntPtr NoteClass => _basis.NoteClass;
        public IntPtr JudgeClass => _basis.JudgeClass;
        public IntPtr HoldClass => _basis.HoldClass;
        public IntPtr ChallengeTimeClass => _basis.ChallengeTimeClass;
        public IntPtr NoteListClass => _basis.NoteListClass;
        public IntPtr ClearMethod => _basis.ClearMethod;
        public IntPtr AddMethod => _basis.AddMethod;
        public void VerifyCurrent() { OnAccess?.Invoke(); _basis.VerifyCurrent(); }
    }

    private static void Reentry()
    {
        using var f = new Fixture();
        foreach (var metadata in new[] { false, true })
        {
            var fired = false;
            void Nested()
            {
                if (fired) return; fired = true; var before = Calls.Read(f.Native);
                var error = Expect<InvalidOperationException>(() => f.Run.PrepareSeekHoldState(f.Native.HoldStateProfile, f.Targets));
                Check(error.Message.Contains("reenter", StringComparison.OrdinalIgnoreCase) && before == Calls.Read(f.Native),
                    "Same-RunValue Hold constructor reentry reached a native export or failed for an unrelated reason.");
            }
            var profile = new CallbackProfile(f.Native.HoldStateProfile);
            if (metadata) f.Native.AfterJudgeMetadata = (_, _) => Nested(); else profile.OnAccess = Nested;
            using var outer = f.Prepare(profile: profile); f.ClearCallbacks(); profile.OnAccess = null;
            Check(fired, "The Hold constructor/profile reentry injection did not execute."); outer.VerifyPrepared();
        }
        var bad = new CallbackProfile(f.Native.HoldStateProfile) { OnAccess = () => throw new IOException("Synthetic Hold profile failure.") };
        var roots = f.Native.ActiveRoots; Expect<IOException>(() => f.Prepare(profile: bad));
        Check(f.Native.ActiveRoots == roots, "Failed Hold source construction leaked independent roots.");
        var p = f.Prepare(); var callback = false;
        f.Native.AfterHoldStateStore = _ =>
        {
            if (callback) return; callback = true; var before = Calls.Read(f.Native);
            Expect<InvalidOperationException>(p.Commit); Expect<InvalidOperationException>(p.Restore);
            Expect<InvalidOperationException>(p.VerifyPrepared); Expect<InvalidOperationException>(() => p.Confirm());
            Expect<InvalidOperationException>(() => p.ConfirmRestored());
            Check(before == Calls.Read(f.Native), "Hold participant callback reentry crossed a native export.");
        };
        p.Commit(); f.ClearCallbacks(); Check(callback && p.Confirm().Observed == true, "Rejected callback reentry disrupted the actual Hold commit."); Restored(f, p);
    }

    private static void CallbackDispose()
    {
        foreach (var writer in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); f.Run.Dispose(); var fired = false; Calls? stopped = null;
            void Withdraw()
            {
                if (fired) return; fired = true; p.Dispose();
                Check(f.Native.NoteViewHandlesFor(f.Native.TargetHoldLists[0]).Length != 0,
                    "Callback withdrawal freed a retained Hold List receiver while its native call was active.");
                Expect<ObjectDisposedException>(p.VerifyPrepared); stopped = Calls.Read(f.Native);
            }
            if (writer) f.Native.AfterHoldStateStore = _ => Withdraw();
            else f.Native.AfterTargetFieldRead = (name, owner) =>
            { if (name == "_size" && owner == f.Native.TargetHoldLists[0]) Withdraw(); };
            var error = Expect<ObjectDisposedException>(p.Commit); f.ClearCallbacks();
            Check(fired && stopped == Calls.Read(f.Native) && p.AttemptedWrites == (writer ? 1 : 0)
                && f.Native.HoldStateWrites.Count == (writer ? 1 : 0),
                "Callback withdrawal crossed a later high-level native export or lost the exact attempted dispatch prefix.");
            if (writer)
            {
                Check(p.HasRetainedResponsibility && f.Native.ActiveRoots > 0 && ReferenceEquals(error.Data["SeekHoldStateCleanupOwner"], p),
                    "Interrupted actual Hold dispatch lost the exact participant and independent native roots."); f.RetainState(p, error);
            }
            else Check(f.Native.ActiveRoots == 0, "Read-only callback withdrawal failed to close its independent source roots.");
        }
    }

    private static void FinalDispatchGuard()
    {
        using var f = new Fixture(); var p = f.Prepare(); var fired = false;
        f.Native.AfterHoldStateRoot = (_, target) =>
        {
            if (fired || target != f.Native.TargetHoldLists[0] || !InMethod("InvokeCore", "NativeRuntime")) return;
            fired = true; f.State.ObservePause(true);
        };
        Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks();
        Check(fired && p.AttemptedWrites == 0 && f.Native.HoldStateWrites.Count == 0 && f.Native.HoldStateEquals(f.Baseline),
            "A managed source revision changed after native marshalling/rooting but passed the final actual Hold dispatch guard.");
    }

    private static void WrongThread()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Native.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() =>
            {
                Expect<InvalidOperationException>(p.VerifyPrepared); Expect<InvalidOperationException>(p.Commit);
                Expect<InvalidOperationException>(p.Restore); Expect<InvalidOperationException>(() => p.Confirm());
                Expect<InvalidOperationException>(() => p.ConfirmRestored()); Expect<InvalidOperationException>(p.Dispose);
                Expect<InvalidOperationException>(() => f.Run.PrepareSeekHoldState(f.Native.HoldStateProfile, f.Targets));
            }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0,
            "Worker Hold admission invoked a lower synthetic IL2CPP export.");
        p.Commit(); Check(p.Confirm().Observed == true, "Worker rejection consumed the owner-thread Hold preparation."); Restored(f, p);
    }

    private static void SourceChanges()
    {
        foreach (var (key, value) in new[] { ("pause.paused", false), ("progress.ready", false), ("progress.playing", true), ("info.over", true) })
        {
            using var f = new Fixture(); f.Native.HoldStateForeignSource(key, value); var roots = f.Native.ActiveRoots;
            Expect<InvalidOperationException>(() => f.Prepare());
            Check(f.Native.ActiveRoots == roots && f.Native.HoldStateWrites.Count == 0,
                "A source outside ready-paused admitted actual Hold writes or leaked roots: " + key);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Native.NoteViewSwapAllSlots(); Expect<InvalidOperationException>(p.Commit);
            Check(p.AttemptedWrites == 0 && f.Native.HoldStateWrites.Count == 0, "Changed complete actual NoteView reached Hold dispatch.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Native.TargetSet(f.Native.TargetHold, "levelInformation", f.Native.OtherObject); f.Native.HoldStateRememberOther();
            Expect<InvalidOperationException>(p.Commit);
            Check(p.AttemptedWrites == 0 && f.Native.HoldStateWrites.Count == 0, "Changed Hold/source backlink reached a native writer.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.State.ObservePause(true); f.Native.BeginLowerIl2CppAudit();
            try { Expect<InvalidOperationException>(p.Commit); }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(f.Native.LowerIl2CppCalls == 0 && p.AttemptedWrites == 0, "Expired managed run revision reached native Hold APIs.");
        }
    }

    private static void InvalidMetadata()
    {
        foreach (var fault in new[] { FakeNative.JudgeMetadataFault.Namespace, FakeNative.JudgeMetadataFault.StorageOverlap,
                     FakeNative.JudgeMetadataFault.StaticField, FakeNative.JudgeMetadataFault.WrongVersionWidth,
                     FakeNative.JudgeMetadataFault.ArrayElement, FakeNative.JudgeMetadataFault.ArrayRank })
        {
            using var f = new Fixture(); f.Native.JudgeMutateMetadata(fault); var roots = f.Native.ActiveRoots;
            Expect<UnsupportedSignatureException>(() => f.Prepare());
            Check(f.Native.HoldStateWrites.Count == 0 && f.Native.ActiveRoots == roots, "Malformed exact Hold List metadata reached a writer or leaked roots: " + fault);
        }
        foreach (var field in new[] { "isStaticNote", "lastFrameHoldLineLength", "_nowTime", "_speed", "_holdLineLength", "holdNotes", "runningHoldNotes" })
        foreach (var remove in new[] { false, true })
        {
            using var f = new Fixture(); var owner = field is "isStaticNote" or "lastFrameHoldLineLength" ? f.Native.HoldStateNotes[0] : f.Native.TargetHold;
            f.Native.HoldStateMetadata(owner, field, offset: 0x100, remove: remove); var roots = f.Native.ActiveRoots;
            ExpectFailure(() => f.Prepare());
            Check(f.Native.HoldStateWrites.Count == 0 && f.Native.ActiveRoots == roots,
                "Missing or wrongly placed exact Hold state field reached writes or leaked source roots: " + field);
        }
    }

    private static void UnknownFree()
    {
        using var f = new Fixture(); var p = f.Prepare(); p.VerifyPrepared(); f.Run.Dispose();
        var handle = f.Native.NoteViewHandlesFor(f.Native.TargetHoldLists[0]).Single(); f.Native.FailFreeHandle = handle;
        var error = Expect<AggregateException>(p.Dispose); var attempts = f.Native.FreeHandleAttempts;
        Check(ReferenceEquals(error.Data["SeekHoldStateCleanupOwner"], p) && p.HasRetainedResponsibility && p.CleanupFailure is not null
            && f.Native.NoteViewFreeLog.Count(row => row.Handle == handle && row.InjectedFailure) == 1,
            "Unknown native free discarded the exact Hold participant/root cleanup responsibility.");
        ExpectFailure(p.Dispose);
        Check(f.Native.FreeHandleAttempts == attempts, "Unknown Hold native free was retried on repeated Dispose."); f.RetainFree(p, error);
    }
}
