using System.Reflection;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

// Non-Fresh accessor ownership exercised through synthetic IL2CPP exports.
internal static class NativePlaybackActualSourceLifetimeChecks
{
    internal static IEnumerable<(string Name, Action Run)> All
    {
        get
        {
            yield return ("actual accessor copied and worker open or dispose leave the owner's seven roots usable", OwnerCleanup);
            foreach (var call in Calls())
                yield return ("actual accessor " + call.Name + " defers reentrant disposal until native return", () => DeferredDispose(call));
            yield return ("actual accessor deferred cleanup remembers unknown native free without retry", DeferredFreeFailure);
            yield return ("actual accessor deferred cleanup releases owned roots after the native graph expires", ExpiredGraphCleanup);
            yield return ("actual accessor reentrant disposal preserves an entered native invocation failure", NativeInvokeFailure);
        }
    }

    private sealed record Call(string Name, string Method, Func<FakeNative, IntPtr> Receiver,
        Action<NativePlaybackActualSource, FakeNative> Run);

    private static IEnumerable<Call> Calls() => new Call[]
    {
        new("graph clip getter", "get_clip", n => n.GameplaySource, (a, _) => a.VerifyGraph()),
        new("graph controller active getter", "get_isActiveAndEnabled", n => n.GameplayController, (a, _) => a.VerifyGraph()),
        new("graph source active getter", "get_isActiveAndEnabled", n => n.GameplaySource, (a, _) => a.VerifyGraph()),
        new("graph clip alive getter", "op_Implicit", _ => IntPtr.Zero, (a, _) => a.VerifyGraph()),
        new("sample graph clip getter", "get_clip", n => n.GameplaySource, (a, _) => a.Read(17, 23)),
        new("sample graph controller active getter", "get_isActiveAndEnabled", n => n.GameplayController, (a, _) => a.Read(17, 23)),
        new("sample graph source active getter", "get_isActiveAndEnabled", n => n.GameplaySource, (a, _) => a.Read(17, 23)),
        new("sample graph clip alive getter", "op_Implicit", _ => IntPtr.Zero, (a, _) => a.Read(17, 23)),
        new("sample playing getter", "get_isPlaying", n => n.GameplaySource, (a, _) => a.Read(17, 23)),
        new("sample DSP getter", "get_DspTime", _ => IntPtr.Zero, (a, _) => a.Read(17, 23)),
        new("sample media getter", "get_time", n => n.GameplaySource, (a, _) => a.Read(17, 23)),
        new("sample clip length getter", "get_length", n => n.GameplayClip, (a, _) => a.Read(17, 23)),
        new("sample pitch getter", "get_pitch", n => n.GameplaySource, (a, _) => a.Read(17, 23)),
        new("active getter", "get_isActiveAndEnabled", n => n.GameplaySource, (a, _) => a.ReadActive()),
        new("playing getter", "get_isPlaying", n => n.GameplaySource, (a, _) => a.ReadPlaying()),
        new("DSP getter", "get_DspTime", _ => IntPtr.Zero, (a, _) => a.ReadDsp()),
        new("media getter", "get_time", n => n.GameplaySource, (a, _) => a.ReadMedia()),
        new("Pause invocation", "Pause", n => n.GameplaySource, (a, _) => a.InvokeAudioPause()),
        new("Stop invocation", "Stop", n => n.GameplaySource, (a, _) => a.InvokeStop()),
        new("time setter", "set_time", n => n.GameplaySource, (a, _) => a.InvokeTime(5)),
        new("schedule invocation", "PlayScheduled", n => n.GameplaySource, (a, _) => a.InvokeSchedule(101)),
        PitchCall(),
        new("clip setter", "set_clip", n => n.GameplaySource, (a, n) => a.InvokeClip(n.GameplayClip))
    };
    private static Call PitchCall() => new("pitch setter", "set_pitch", n => n.GameplaySource, (a, _) => a.InvokePitch(2));

    private static IEnumerable<Action> Entries(NativePlaybackActualSource actual, FakeNative native)
    {
        foreach (var call in Calls()) yield return () => call.Run(actual, native);
        yield return () => { _ = actual.ProgressRoot; };
        yield return () => { _ = actual.PlayerRoot; };
        yield return () => { _ = actual.AudioSourceRoot; };
        yield return () => { _ = actual.ClipRoot; };
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static void Refused(Action action, ErrorCode code)
    {
        var error = Throws<GameplayStateException>(action);
        Check(error.Code == code, "Expected " + code + "; got " + error.Code + ".");
    }

    private static void OwnerCleanup()
    {
        using var f = new Fixture();
        var copied = (NativePlaybackActualSource)typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Actual, null)!;
        var roots = f.Native.ActiveRoots;
        var attempts = f.Native.FreeHandleAttempts;
        var freed = f.Native.FreedRoots;
        var calls = f.Native.ActualSourceNativeCalls;
        var newRoots = f.Native.NewRoots;
        f.Native.BeginLowerIl2CppAudit();
        try
        {
            foreach (var entry in Entries(copied, f.Native)) Refused(entry, ErrorCode.TargetExpired);
            Refused(copied.Dispose, ErrorCode.TargetExpired);
            Task.Run(() =>
            {
                foreach (var entry in Entries(f.Actual, f.Native)) Refused(entry, ErrorCode.WrongThread);
                Refused(f.Actual.Dispose, ErrorCode.WrongThread);
            }).GetAwaiter().GetResult();
            Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0,
                "A copied or worker accessor entered a lower native export before rejection.");
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.ActiveRoots == roots && f.Native.FreeHandleAttempts == attempts
            && f.Native.FreedRoots == freed && f.Native.NewRoots == newRoots
            && f.Native.ActualSourceNativeCalls == calls && f.Native.GameplayPitchWrites == 0,
            "Rejected open or cleanup consumed the actual accessor's state or roots.");

        var sample = f.Actual.Read(17, 23);
        Check(ReferenceEquals(sample.Binding, f.Binding) && sample.AuthorityRevision == 17
            && sample.SchedulingRevision == 23 && sample.AudioPositionSeconds == f.Native.GameplayPosition
            && f.Native.AllGameplayReceiversRooted && f.Native.ActiveRoots == roots,
            "The owner could not read its original exact graph after foreign cleanup was rejected.");
        attempts = f.Native.FreeHandleAttempts;
        freed = f.Native.FreedRoots;
        f.Actual.Dispose();
        Check(f.Native.ActiveRoots == f.BaselineRoots && f.Native.FreeHandleAttempts == attempts + 7
            && f.Native.FreedRoots == freed + 7, "Owner cleanup did not release exactly seven independent roots.");
        VerifyOwnedReleasedOnce(f);
        f.Actual.Dispose();
        Check(f.Native.FreeHandleAttempts == attempts + 7 && f.Native.FreedRoots == freed + 7,
            "Repeated owner cleanup attempted a root release again.");
    }

    private static void DeferredFreeFailure() => DeferredDispose(PitchCall(), failFree: true);
    private static void ExpiredGraphCleanup() => DeferredDispose(PitchCall(), expireGraph: true);
    private static void NativeInvokeFailure() => DeferredDispose(PitchCall(), failInvoke: true);

    private static void DeferredDispose(Call call, bool failFree = false, bool expireGraph = false, bool failInvoke = false)
    {
        using var f = new Fixture();
        if (failFree) f.Native.FailFreeHandle = f.OwnedHandles[0];
        if (failInvoke) f.Native.OccurrenceFailPitchNumber = 1;
        var entered = 0;
        var returned = 0;
        var rootsInside = -1;
        var attemptsInside = -1;
        var freedInside = -1;
        var newRootsInside = -1;
        var callCountInside = -1;
        var callTraceInside = -1;
        var receiver = call.Receiver(f.Native);

        f.Native.DuringActualSourceNativeCall = (name, self) =>
        {
            if (name != call.Method || self != receiver || entered != 0) return;
            entered++;
            rootsInside = f.Native.ActiveRoots;
            attemptsInside = f.Native.FreeHandleAttempts;
            freedInside = f.Native.FreedRoots;
            newRootsInside = f.Native.NewRoots;
            callCountInside = f.Native.ActualSourceNativeCalls;
            callTraceInside = f.Native.ActualSourceCallTrace.Count;
            Check(rootsInside >= f.BaselineRoots + 7
                && f.OwnedHandles.All(f.Native.SnapshotActualSourceHandles().Contains),
                "Native entry lost an exact accessor-owned root before the callback.");
            f.Actual.Dispose();
            f.Actual.Dispose();
            f.Native.BeginLowerIl2CppAudit();
            try
            {
                foreach (var entry in Entries(f.Actual, f.Native)) Throws<ObjectDisposedException>(entry);
                Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0,
                    "A newly requested accessor entry reached a lower native export after reentrant disposal.");
            }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(f.Native.ActiveRoots == rootsInside && f.Native.FreeHandleAttempts == attemptsInside
                && f.Native.FreedRoots == freedInside && f.Native.NewRoots == newRootsInside
                && f.Native.ActualSourceNativeCalls == callCountInside
                && f.OwnedHandles.All(f.Native.SnapshotActualSourceHandles().Contains),
                "Reentrant disposal freed roots or allowed new native use inside a still-active getter or invocation.");
            Check(!f.Native.ActualSourceFreedHandles.Any(f.OwnedHandles.Contains),
                "An owned handle was attempted before its native getter or invocation returned.");
            if (expireGraph)
            {
                f.Native.GameplayActive = false;
                f.Native.SetGameplayField("progress.source", f.Native.GameplayClip);
            }
        };
        f.Native.BeforeActualSourceNativeReturn = (name, self) =>
        {
            if (name != call.Method || self != receiver || entered == 0) return;
            returned++;
            Check(f.Native.ActiveRoots == rootsInside && f.Native.FreeHandleAttempts == attemptsInside
                && f.Native.FreedRoots == freedInside && !f.Native.ActualSourceFreedHandles.Any(f.OwnedHandles.Contains)
                && f.OwnedHandles.All(f.Native.SnapshotActualSourceHandles().Contains),
                "Owned roots were released before the original native call finished.");
        };

        if (failFree)
        {
            var error = Throws<IOException>(() => call.Run(f.Actual, f.Native));
            Check(error.Message.Contains("unknown GC-root free outcome", StringComparison.Ordinal),
                "Deferred cleanup hid the actual unknown native root-free failure.");
            f.ExpectedReleaseFailure = error;
        }
        else if (failInvoke)
        {
            var error = Throws<IOException>(() => call.Run(f.Actual, f.Native));
            Check(error.Message.Contains("pitch changed before failure", StringComparison.Ordinal),
                "Reentrant disposal replaced the entered native invocation's original failure.");
        }
        else Throws<ObjectDisposedException>(() => call.Run(f.Actual, f.Native));
        f.Native.DuringActualSourceNativeCall = null;
        f.Native.BeforeActualSourceNativeReturn = null;

        Check(entered == 1 && returned == 1 && f.Native.AllGameplayReceiversRooted,
            "The selected NativeRuntime fixture call did not enter and return once with rooted receivers.");
        Check(f.Native.ActualSourceNativeCalls == callCountInside
            && f.Native.ActualSourceCallTrace.Count == callTraceInside
            && f.Native.ActualSourceCallTrace[^1] == (call.Method, receiver),
            "The closed operation invoked another native getter or writer after " + call.Name + " returned: "
            + string.Join(", ", f.Native.ActualSourceCallTrace.Skip(callTraceInside).Select(row => row.Method)));
        VerifyEnteredInvocationEffect(call, f.Native);
        Check(f.Native.ActiveRoots == f.BaselineRoots
            && f.Native.FreeHandleAttempts - attemptsInside == f.Native.NewRoots - newRootsInside + rootsInside - f.BaselineRoots,
            "Actual operation unwind leaked a temporary root or attempted an extra native free.");
        VerifyOwnedReleasedOnce(f);
        var attemptsAfter = f.Native.FreeHandleAttempts;
        var freedAfter = f.Native.FreedRoots;
        var nativeCallsAfter = f.Native.ActualSourceNativeCalls;
        Throws<ObjectDisposedException>(() => f.Actual.Read(17, 23));
        if (failFree)
        {
            Check(ReferenceEquals(Throws<IOException>(f.Actual.Dispose), f.ExpectedReleaseFailure)
                && ReferenceEquals(Throws<IOException>(f.Actual.Dispose), f.ExpectedReleaseFailure),
                "Repeated cleanup converted an unknown native free into successful retirement.");
        }
        else { f.Actual.Dispose(); f.Actual.Dispose(); }
        Check(f.Native.FreeHandleAttempts == attemptsAfter && f.Native.FreedRoots == freedAfter
            && f.Native.ActualSourceNativeCalls == nativeCallsAfter && f.Native.ActiveRoots == f.BaselineRoots,
            "Closed use or repeated disposal retried the released root responsibility.");
    }

    private static void VerifyOwnedReleasedOnce(Fixture f)
        => Check(f.OwnedHandles.Length == 7 && f.OwnedHandles.All(handle =>
            f.Native.ActualSourceFreedHandles.Count(attempt => attempt == handle) == 1),
            "Each of the seven original accessor-owned handles must receive exactly one native free attempt.");

    private static void VerifyEnteredInvocationEffect(Call call, FakeNative native)
    {
        var preserved = call.Method switch
        {
            "Pause" => native.OccurrencePauseCalls == 1 && !native.OccurrenceAudioPlaying
                && native.OccurrenceCalls.SequenceEqual(new[] { "Pause" }),
            "Stop" => native.OccurrenceStopCalls == 1 && !native.OccurrenceAudioPlaying
                && native.GameplayPosition == 0 && native.OccurrenceCalls.SequenceEqual(new[] { "Stop" }),
            "set_time" => native.GameplayPosition == 5 && native.OccurrenceCalls.SequenceEqual(new[] { "time:5" }),
            "PlayScheduled" => native.OccurrenceScheduleArguments.SequenceEqual(new[] { 101d })
                && native.OccurrenceCalls.Count == 1,
            "set_pitch" => native.GameplayPitchWrites == 1 && native.GameplayPitch == 2
                && native.GameplayPitchAttempts.SequenceEqual(new[] { 2f }) && native.OccurrenceCalls.Count == 1,
            "set_clip" => native.OccurrenceCalls.SequenceEqual(new[] { "clip" }),
            _ => native.OccurrenceCalls.Count == 0 && native.GameplayPitchWrites == 0
        };
        Check(preserved, "Reentrant disposal hid, changed or repeated the already-entered " + call.Name + " effect.");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly NativePlaybackBinding Binding;
        internal readonly NativePlaybackActualSource Actual;
        internal readonly int BaselineRoots;
        internal readonly uint[] OwnedHandles;
        internal Exception? ExpectedReleaseFailure;

        internal Fixture()
        {
            Native.ConfigureActualSourceLifetime();
            Binding = new("actual-accessor-run", 3, 1, 1, Native.GameplayController.ToInt64(),
                Native.GameplayInformation.ToInt64(), Native.GameplayProgress.ToInt64(),
                Native.GameplayPlayer.ToInt64(), Native.GameplaySource.ToInt64(), Native.GameplayClip.ToInt64(),
                Native.GameplayGetTimeMethod.ToInt64());
            BaselineRoots = Native.ActiveRoots;
            var baselineHandles = Native.SnapshotActualSourceHandles();
            Actual = new(Native.GameplayApi, Binding, Native.GameplayPause.ToInt64());
            OwnedHandles = Native.SnapshotActualSourceHandles().Except(baselineHandles).ToArray();
            Check(Native.ActiveRoots == BaselineRoots + 7 && OwnedHandles.Length == 7,
                "The actual independent accessor did not retain exactly seven native root handles.");
        }

        public void Dispose()
        {
            Native.DuringActualSourceNativeCall = null;
            Native.BeforeActualSourceNativeReturn = null;
            try
            {
                try { Actual.Dispose(); }
                catch (Exception error) when (ReferenceEquals(error, ExpectedReleaseFailure)) { }
                Check(Native.ActiveRoots == BaselineRoots, "An actual accessor retained roots after its final operation returned.");
            }
            finally { Native.Dispose(); }
        }
    }
}
