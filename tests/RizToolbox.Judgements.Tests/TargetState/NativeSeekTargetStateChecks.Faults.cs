using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    private static void SourceChanges()
    {
        foreach (var mode in new[] { "cache", "information", "note", "challenge", "array" })
        {
            using var f = new Fixture(); var p = Capture(f, 0);
            switch (mode)
            {
                case "cache": f.Native.TargetSet(f.Native.JudgeOwner, "_judgeTime", .125f); break;
                case "information": f.Native.TargetSet(f.Native.GameplayInformation, "badTime", .1875f); break;
                case "note": f.Native.TargetSet(f.Native.TargetNote(0), "realTime", .125f); break;
                case "challenge": f.Native.TargetSet(f.Native.TargetNote(0), "inChallengeTime", true); break;
                default: f.Native.NoteViewSwapAllSlots(); break;
            }
            f.Native.JudgeQueueRememberOtherStorage(); Expect<InvalidOperationException>(p.VerifySource);
            Check(f.Native.JudgeQueueWrites.Count == 0 && f.Native.JudgeQueueStorageEquals(f.Baseline), "A stale projection repaired or overwrote changed source instead of refusing it.");
        }
        using (var f = new Fixture())
        {
            var fired = false; f.Native.AfterTargetFieldRead = (name, owner) =>
            {
                if (fired || owner != f.Native.JudgeOwner || name != "_judgeTime") return; fired = true;
                f.Native.TargetSet(owner, "_judgeTime", .125f); f.Native.JudgeQueueRememberOtherStorage();
            };
            Expect<InvalidOperationException>(() => Capture(f, 0)); f.ClearCallbacks();
            Check(fired && f.Native.JudgeQueueWrites.Count == 0, "An in-flight cached-window change escaped the actual reader's two complete samples.");
        }
        using (var f = new Fixture())
        {
            var p = Capture(f, 0); f.State.ObservePause(true); Expect<InvalidOperationException>(p.VerifySource);
            Check(f.Native.JudgeQueueWrites.Count == 0, "A managed source revision retained reset authority.");
        }
    }
    private static void Rejections()
    {
        foreach (var target in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            using var f = new Fixture(); Expect<ArgumentOutOfRangeException>(() => Capture(f, target));
            Check(f.Native.JudgeQueueWrites.Count == 0, "A nonfinite target reached native writes.");
        }
        foreach (var fault in new[] { ("note", "realTime", "System.Int32", 0x34), ("note", "holdEndRealTime", "System.Single", 0x3c),
            ("judge", "_judgeTime", "System.Single", 0x58), ("info", "challengeCompletely", "System.Single", 0xbc) })
        {
            using var f = new Fixture(configure: n => n.TargetMetadata(fault.Item1, fault.Item2, fault.Item3, fault.Item4));
            Expect<UnsupportedSignatureException>(() => Capture(f, 0));
        }
        using (var f = new Fixture(configure: n => n.TargetSet(n.TargetNote(0), "realTime", float.NaN)))
            Expect<InvalidDataException>(() => Capture(f, 0));
        using (var f = new Fixture(configure: n => n.TargetSet(n.TargetNote(2), "holdEndRealTime", float.NaN)))
            Expect<InvalidDataException>(() => Capture(f, 0));
        using (var f = new Fixture(configure: n => n.TargetSet(n.TargetNote(0), "holdEndRealTime", float.NaN)))
            ReadOnly(f, Capture(f, 0));
        using (var f = new Fixture(configure: n => n.TargetSet(n.JudgeOwner, "badTime", float.PositiveInfinity)))
            Expect<InvalidDataException>(() => Capture(f, 0));
        using (var f = new Fixture(configure: n => { n.TargetSet(n.JudgeOwner, "_instantiateReady", false); n.TargetSetChallenges((float.NaN, 1)); }))
            Expect<InvalidDataException>(() => Capture(f, 0));
        using (var f = new Fixture(configure: n => n.TargetSetChallenges((2, 1))))
            ReadOnly(f, Capture(f, 0));
    }
    private static void Lifetime()
    {
        using (var f = new Fixture())
        {
            var fired = false; f.Native.TargetProfile.OnVerify = () =>
            {
                if (fired) return; fired = true; f.Native.BeginLowerIl2CppAudit();
                try { Expect<InvalidOperationException>(() => f.Run.CaptureSeekTargetStatePlan(f.Native.TargetProfile, new(0), new())); }
                finally { f.Native.EndLowerIl2CppAudit(); }
                Check(f.Native.LowerIl2CppCalls == 0, "Same-RunValue target constructor reentry dispatched an IL2CPP export.");
            };
            var p = Capture(f, 0); f.ClearCallbacks(); Check(fired, "Target constructor reentry injection did not fire."); p.VerifySource();
            f.Native.BeginLowerIl2CppAudit();
            try
            {
                Task.Run(() =>
                {
                    Expect<InvalidOperationException>(p.VerifySource); Expect<InvalidOperationException>(p.Dispose);
                    Expect<InvalidOperationException>(() => f.Run.CaptureSeekTargetStatePlan(f.Native.TargetProfile, new(0), new()));
                }).GetAwaiter().GetResult();
            }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0, "Worker target operations reached IL2CPP or consumed the owner's preparation.");
            p.VerifySource();
        }
        using (var f = new Fixture())
        {
            var p = Capture(f, 0); f.Run.Dispose(); var fired = false; var fields = -1; var invocations = -1;
            f.Native.AfterTargetFieldRead = (name, owner) =>
            {
                if (fired || owner != f.Native.JudgeOwner || name != "_judgeTime") return;
                fired = true; p.Dispose(); Check(f.Native.HasRootFor(owner), "Read callback disposed a native receiver before its active call returned.");
                fields = f.Native.FieldReads; invocations = f.Native.Invocations;
            };
            Expect<ObjectDisposedException>(p.VerifySource); f.ClearCallbacks();
            Check(fired && f.Native.ActiveRoots == 0 && f.Native.FieldReads == fields && f.Native.Invocations == invocations
                && f.Native.JudgeQueueWrites.Count == 0, "Callback disposal crossed a later high-level read or lost deferred target-source cleanup.");
        }
    }
    private static IEnumerable<object> Ownership(object owner, int depth = 0)
    {
        yield return owner; if (depth == 6 || owner is NativeRuntime.RootHandle) yield break;
        if (owner is System.Collections.IEnumerable values && owner is not string)
        {
            foreach (var value in values) if (value is not null) foreach (var nested in Ownership(value, depth + 1)) yield return nested;
            yield break;
        }
        foreach (var name in new[] { "_source", "_access", "_core", "_plan", "_chartPlan", "_chart", "_view", "_reader", "_inner", "_roots", "_unknownFrees" })
        {
            var field = owner.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(owner) is not { } value) continue;
            foreach (var nested in Ownership(value, depth + 1)) yield return nested;
        }
    }
    private static void UnknownFree()
    {
        using var f = new Fixture(); var p = Capture(f, 0); f.Run.Dispose(); var target = f.Native.TargetNote(0);
        var handle = f.Native.NoteViewHandlesFor(target).Last();
        var wrapper = Ownership(p).OfType<NativeRuntime.RootHandle>().Distinct().Single(root => (uint)Field(root, "_id") == handle);
        f.Native.FailFreeHandle = handle; var error = Expect<AggregateException>(p.Dispose); f.Retain(p, error);
        Check(ReferenceEquals(error.Data["SeekTargetStateCleanupOwner"], p) && p.CleanupFailure is not null
            && Ownership(p).Any(owner => ReferenceEquals(owner, wrapper))
            && f.Native.NoteViewFreeLog.Count(row => row.Handle == handle && row.Target == target && row.InjectedFailure) == 1,
            "Unknown target-source native free discarded its original root wrapper, exact handle/target or public cleanup owner.");
        var attempts = f.Native.FreeHandleAttempts; Expect<AggregateException>(p.Dispose);
        Check(f.Native.FreeHandleAttempts == attempts && f.Native.ActiveRoots == 0, "Unknown target-source free was retried or blocked remaining known cleanup.");
    }
}
