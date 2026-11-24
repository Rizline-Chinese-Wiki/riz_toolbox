using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekJudgeQueuesChecks
{
    private sealed class CallbackProfile : ISeekChartNativeProfile
    {
        private readonly ISeekChartNativeProfile _basis;
        internal Action? OnAccess;
        internal CallbackProfile(ISeekChartNativeProfile basis) => _basis = basis;
        public IntPtr ChartClass { get { OnAccess?.Invoke(); return _basis.ChartClass; } }
        public IntPtr ProgressClass { get { OnAccess?.Invoke(); return _basis.ProgressClass; } }
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
                var error = Expect<InvalidOperationException>(() => f.Run.PrepareSeekJudgeQueues(f.Native.SeekProfile, f.Targets));
                Check(error.Message.Contains("reenter", StringComparison.OrdinalIgnoreCase) && before == Calls.Read(f.Native),
                    "Same-RunValue Judge constructor reentry reached native work or failed for an unrelated reason.");
            }
            var profile = new CallbackProfile(f.Native.SeekProfile);
            if (metadata) f.Native.AfterJudgeMetadata = (_, _) => Nested(); else profile.OnAccess = Nested;
            using var outer = f.Prepare(profile: profile); f.ClearCallbacks(); profile.OnAccess = null;
            Check(fired, "The Judge constructor/profile reentry injection did not fire."); outer.VerifyPrepared();
        }
        var failedProfile = new CallbackProfile(f.Native.SeekProfile) { OnAccess = () => throw new IOException("Synthetic queue profile failure.") };
        var roots = f.Native.ActiveRoots; Expect<IOException>(() => f.Prepare(profile: failedProfile));
        Check(f.Native.ActiveRoots == roots, "Failed Judge writer construction leaked native roots.");
        var p = f.Prepare(); var callback = false;
        f.Native.AfterJudgeQueueStore = _ =>
        {
            if (callback) return; callback = true; var before = Calls.Read(f.Native);
            Expect<InvalidOperationException>(p.Commit); Expect<InvalidOperationException>(p.Restore);
            Expect<InvalidOperationException>(p.VerifyPrepared); Expect<InvalidOperationException>(() => p.Confirm());
            Check(before == Calls.Read(f.Native), "Participant callback reentry performed native work.");
        };
        p.Commit(); f.ClearCallbacks();
        Check(callback && p.Confirm().Observed == true, "Rejected callback reentry disrupted the admitted queue commit."); Restored(f, p);
    }
    private static void CallbackDispose()
    {
        foreach (var writer in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); f.Run.Dispose(); var fired = false;
            var owned = (NativeSeekRuntimeNoteView)Field(Field(p, "_access"), "_view"); Calls? calls = null;
            void DisposeInside()
            {
                if (fired) return; fired = true; p.Dispose(); Expect<ObjectDisposedException>(owned.VerifySource);
                Check(f.Native.NoteViewHandlesFor(f.Native.JudgeLists[0]).Length != 0,
                    "An in-flight Judge writer receiver was released before its native getter/write returned.");
                calls = Calls.Read(f.Native);
            }
            if (writer) f.Native.AfterJudgeQueueStore = _ => DisposeInside();
            else f.Native.AfterNoteViewFieldRead = (name, owner) =>
            { if (name == "_size" && owner == f.Native.JudgeLists[0]) DisposeInside(); };
            Expect<ObjectDisposedException>(p.Commit); f.ClearCallbacks();
            Check(fired && calls == Calls.Read(f.Native) && f.Native.ActiveRoots == 0
                && p.AttemptedWrites == (writer ? 1 : 0) && f.Native.JudgeQueueWrites.Count == (writer ? 1 : 0),
                "Dispose callback crossed a later high-level native API or lost its deferred roots / attempted-write state.");
        }
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
                Expect<InvalidOperationException>(() => f.Run.PrepareSeekJudgeQueues(f.Native.SeekProfile, f.Targets));
            }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0,
            "Wrong-thread Judge queue admission reached a synthetic IL2CPP export.");
        p.Commit(); Check(p.Confirm().Observed == true, "Worker rejection consumed the owner-thread preparation."); Restored(f, p);
    }
    private static void SourceChanges()
    {
        foreach (var (key, value) in new[] { ("pause.paused", false), ("progress.ready", false),
                     ("progress.playing", true), ("info.over", true) })
        {
            using var f = new Fixture(); f.Native.JudgeQueueForeignSource(key, value); var roots = f.Native.ActiveRoots;
            Expect<InvalidOperationException>(() => f.Prepare());
            Check(f.Native.JudgeQueueWrites.Count == 0 && f.Native.ActiveRoots == roots,
                "A source outside ready-paused admitted queue writes or leaked preparation roots: " + key);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Native.NoteViewSwapAllSlots(); Expect<InvalidOperationException>(p.Commit);
            Check(p.AttemptedWrites == 0 && f.Native.JudgeQueueWrites.Count == 0,
                "Changed actual runtime-note storage reached a queue writer.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.State.ObservePause(true); f.Native.BeginLowerIl2CppAudit();
            try { Expect<InvalidOperationException>(p.Commit); }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(f.Native.LowerIl2CppCalls == 0 && p.AttemptedWrites == 0,
                "An expired managed run revision reached native queue getters or writers.");
        }
    }
    private static void InvalidMetadata()
    {
        foreach (var fault in new[] { FakeNative.JudgeMetadataFault.Namespace, FakeNative.JudgeMetadataFault.StorageOverlap,
                     FakeNative.JudgeMetadataFault.StaticField, FakeNative.JudgeMetadataFault.WrongVersionWidth })
        {
            using var f = new Fixture(); f.Native.JudgeMutateMetadata(fault); var roots = f.Native.ActiveRoots;
            Expect<UnsupportedSignatureException>(() => f.Prepare());
            Check(f.Native.JudgeQueueWrites.Count == 0 && f.Native.ActiveRoots == roots,
                "Invalid official closed List metadata reached a queue writer or leaked roots: " + fault);
        }
    }
}
