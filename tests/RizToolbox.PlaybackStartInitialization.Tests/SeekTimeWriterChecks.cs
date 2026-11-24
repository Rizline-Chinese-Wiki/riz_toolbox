using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static readonly List<SeekTimeContext> RetainedSeekTimeContexts = new();
    private static readonly string[] SeekTimeModes =
    {
        "seek-time-prepare-reentry", "seek-time-write-reentry", "seek-time-restore-reentry",
        "seek-time-readback-once", "seek-time-readback-progress", "seek-time-readback-always",
        "seek-time-external-equal", "seek-time-release-inflight", "seek-time-normal",
        "seek-time-before-write-progress", "seek-time-before-write-media", "seek-time-before-next-write-media"
    };

    private sealed class SeekTimeContext : IDisposable
    {
        internal readonly FakeNative Api = new();
        internal readonly MainThreadDispatcher Main = new();
        internal readonly GameEventState State = new();
        internal readonly IntPtr Wrapper, Lower;
        internal readonly PlaybackSourceGroupTargets Targets;
        internal readonly ExactHookBroker Broker;
        internal readonly PlaybackSourceGroupLease Group;
        internal readonly NativePlaybackBinding Binding;
        internal readonly NativePlaybackStockRootedSource Source;
        internal readonly List<object> NativeEvents = new();
        private readonly LowerFixtureRead _readTime;
        private bool _disposed;

        internal SeekTimeContext(string[] arguments)
        {
            var wrapperPath = Path.GetFullPath(arguments[1]);
            var lowerPath = Path.Combine(Path.GetDirectoryName(wrapperPath)!, "RizToolbox.PlaybackLowerPitchFixture.dll");
            Check(Hash(wrapperPath) == OwnedPlaybackFixtureProfile.WrapperSha256
                && Hash(lowerPath) == OwnedPlaybackFixtureProfile.LowerSha256,
                "Seek time checks require the current exact two-image lower-time profile.");
            Wrapper = NativeLibrary.Load(wrapperPath); Lower = NativeLibrary.Load(lowerPath);
            Check(Export<LowerFixtureBind>(Wrapper, "StockFixtureBindLowerPitch")(
                NativeLibrary.GetExport(Lower, "StockLowerSetPitch")) == 1
                && Export<LowerFixtureBind>(Wrapper, "StockFixtureBindLowerTime")(
                    NativeLibrary.GetExport(Lower, "StockLowerSetTime")) == 1,
                "The exact owned wrapper did not bind both real lower entries.");
            Api.ConfigureUnityBacking(Wrapper); Api.ObserveSeekTimeReads();
            Api.SetRootedGraphValue("progress.now", 2.8); Api.SetRootedGraphValue("info.nowTime", 5.6f);
            Targets = PlaybackSourceGroupTargets.ResolveOwnedLowerTimeFixture(Wrapper, Lower);
            Check(Targets.SourceRoleCount == 22 && Targets.HasLowerTime && Targets.HasEnginePitch
                && Targets.HasUnityBacking && Targets.HasPlayerConstructor && Targets.HasProgressResource,
                "The time writer fixture omitted one of its exact twenty-two source roles.");
            State.Scene("Level"); State.SetAvailability(scenes: true, runs: true, pause: true);
            var started = State.Controller(Api.GameplayInformation.ToInt64(), true, false,
                "seek-time-writer-owned", Api.GameplayController.ToInt64())!;
            State.ObservePause(true);
            Binding = new(started.RunId, State.SceneGeneration, 1, 1, Api.GameplayController.ToInt64(),
                Api.GameplayInformation.ToInt64(), Api.GameplayProgress.ToInt64(), Api.GameplayPlayer.ToInt64(),
                Api.GameplaySource.ToInt64(), Api.GameplayClip.ToInt64(), Targets.GetTime.MethodInfo.ToInt64());
            var origin = new NativePlaybackRateOrigin(Binding.RunId, Binding.ControllerGeneration,
                Binding.Controller, Binding.Information, Binding.Progress, Api.GameplayPause.ToInt64());
            var run = State.CapturePlaybackRun(Binding.Information, Binding.Controller);
            NativePlaybackStockRootedSource? source = null;
            Broker = new(Path.GetFullPath(arguments[4]), Main, new Logger());
            Group = Broker.BindPlaybackSourceGroup(Targets, Path.GetFullPath(arguments[2]), Hash(arguments[2]),
                Path.GetFullPath(arguments[3]), Hash(arguments[3]),
                () => throw new InvalidOperationException("This owned time writer test issues no Clock."),
                _ => { }, _ => { }, observation =>
                {
                    NativeEvents.Add(new { stage = "before", role = observation.Role.ToString(), observation.Sequence,
                        observation.ParentSequence, observation.ArgBits, observation.WriterSequence,
                        observation.SourceSequence, observation.SchedulingSequence,
                        incomingMethod = observation.IncomingMethod.ToInt64(),
                        caller = observation.Caller.ToInt64(), lowerTime = observation.IsExactLowerTimeChild });
                    if (source is null) observation.Dispose(); else source.ObserveBefore(observation);
                }, completion =>
                {
                    NativeEvents.Add(new { stage = "returned", role = completion.Observation.Role.ToString(),
                        completion.Observation.Sequence, completion.Returned, completion.Abnormal,
                        completion.PhysicalExited, completion.Unknown });
                    source?.ObserveReturned(completion);
                });
            Check(Broker.ReadActualPlaybackDobbyCallCounts() == (23, 23),
                "The exact time writer group lacks twenty-three actual physical Prepare/Commit calls.");
            Source = source = new(Api.GameplayApi, Binding, origin, run, Group);
            Check(Api.ActiveRoots == 7, "The real source must retain exactly its original seven graph roots.");
            _readTime = Export<LowerFixtureRead>(Lower, "StockLowerTimeFixtureRead");
            Marshal.WriteInt32(Api.GameplaySource, 56, 1);
            var pause = Targets.SourceTarget(NativePlaybackSourceRole.Pause);
            Check(Export<InvokeStock>(Wrapper, "StockFixtureInvoke")(1, pause.Address, Api.GameplayPlayer,
                0, pause.MethodInfo, out var code) == 1 && code == 0,
                "The actual native Pause original did not complete normally.");
            var receipt = Source.Current ?? throw new InvalidOperationException("No real Pause receipt: " + Source.LastFailure);
            Check(receipt.PhysicalExited && receipt.Sample.AudioPositionSeconds == 3 && receipt.Sample.Paused,
                "The source lacks the actual exited Pause and independently read media baseline.");
        }

        internal ulong TimeCalls => _readTime(0);
        internal ulong TimeStores => _readTime(1);
        internal int PitchCalls => Marshal.ReadInt32(Api.GameplaySource, 28);
        internal float Media => (float)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(Api.GameplaySource, 32));
        internal NativePlaybackStockRootedSource.ReadbackReceipt Current => Source.Current!;
        internal KeyValuePair<uint, IntPtr>[] RootHandles()
        {
            var actual = Private<NativePlaybackActualSource>(Source, "_actual");
            return Private<List<NativeRuntime.RootHandle>>(actual, "_roots")
                .Select(root => new KeyValuePair<uint, IntPtr>(Private<uint>(root, "_id"), root.Target)).ToArray();
        }

        internal void Finish()
        {
            Api.DuringSeekTimeRead = null; Api.SeekTimeReadException = null;
            Source.Dispose(); Source.PumpCompleted(); Group.PumpCompleted();
            Check(Source.RootsReleased && Api.ActiveRoots == 0,
                "The completed time writer left original source roots after actual retirement.");
            Check(Api.SeekTimeInjectionFailure is null && Api.AllGameplayReceiversRooted,
                "An API callback failed independently or consumed an unrooted receiver.");
        }

        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            Api.DuringSeekTimeRead = null; Api.SeekTimeReadException = null;
            Source.Dispose(); Source.PumpCompleted(); Group.PumpCompleted();
            if (!Source.RootsReleased || Api.ActiveRoots != 0) { RetainedSeekTimeContexts.Add(this); return; }
            Api.ReleaseUnityBackingStorage(); Api.Dispose(); Main.Dispose();
        }
    }

    private static T SeekTimeClone<T>(T value) where T : class =>
        (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;

    private static bool SeekTimeRefused(Action action, ErrorCode? expected = null)
    {
        try { action(); }
        catch (GameplayStateException error) { return expected is null || error.Code == expected; }
        catch (Exception error) when (expected is null && error is UnsupportedSignatureException or ObjectDisposedException or InvalidOperationException)
        { return true; }
        return false;
    }

    private static int RunSeekTimeWriter(string[] arguments, string mode)
    {
        SeekTimeContext? native = null;
        try
        {
            Check(arguments.Length == 5 && SeekTimeModes.Contains(mode), "Unknown current Seek time writer case.");
            native = new(arguments);
            var receipt = native.Current;
            var baseline = receipt.Sample;
            var initial = native.Group.ReadSourceHealth();
            var pitchCalls = native.PitchCalls;
            NativePlaybackStockRootedSource.TimeWrite? writer = null;
            var injection = false;
            if (mode == "seek-time-prepare-reentry")
            {
                NativePlaybackStockRootedSource.TimeWrite? foreignTime = null;
                NativePlaybackStockRootedSource.PitchWrite? foreignPitch = null;
                var timeRefused = false; var pitchRefused = false; var noNestedReads = false;
                native.Api.DuringSeekTimeRead = name =>
                {
                    if (name != "get_pitch" || injection) return;
                    injection = true;
                    var reads = native.Api.SeekTimeApiReads;
                    timeRefused = SeekTimeRefused(() => foreignTime = native.Source.PreparePausedTimeWrite(), ErrorCode.Conflict);
                    pitchRefused = SeekTimeRefused(() => foreignPitch = native.Source.PreparePausedPitchWrite(receipt), ErrorCode.Conflict);
                    noNestedReads = native.Api.SeekTimeApiReads == reads;
                };
                writer = native.Source.PreparePausedTimeWrite();
                native.Api.DuringSeekTimeRead = null;
                Check(injection && timeRefused && pitchRefused && foreignTime is null && foreignPitch is null && noNestedReads,
                    "A baseline getter admitted a second time/pitch writer or performed nested API reads during preparation.");
                Check(native.TimeCalls == 0 && native.PitchCalls == pitchCalls && native.Api.ActiveRoots == 7,
                    "Writer preparation performed a native store or acquired replacement roots.");
            }
            else writer = native.Source.PreparePausedTimeWrite();

            Check(SeekTimeRefused(() => SeekTimeClone(writer).Write(6))
                && Task.Run(() => SeekTimeRefused(() => writer.Write(6), ErrorCode.WrongThread)).GetAwaiter().GetResult()
                && native.TimeCalls == 0, "A copied or worker time writer consumed the owner transaction.");

            if (mode is "seek-time-write-reentry" or "seek-time-restore-reentry")
            {
                if (mode == "seek-time-restore-reentry") writer.Write(6);
                var innerRefused = false; var releaseRefused = false; var recoveryRefused = false;
                var noNestedReads = false;
                native.Api.DuringSeekTimeRead = name =>
                {
                    if (name != "get_clip" || injection) return;
                    injection = true;
                    var reads = native.Api.SeekTimeApiReads;
                    innerRefused = SeekTimeRefused(() => writer.Write(7), ErrorCode.MutationReentrancy);
                    recoveryRefused = SeekTimeRefused(writer.RestoreOriginal, ErrorCode.MutationReentrancy);
                    releaseRefused = SeekTimeRefused(writer.Dispose, ErrorCode.MutationReentrancy);
                    noNestedReads = reads == native.Api.SeekTimeApiReads;
                };
                if (mode == "seek-time-write-reentry") writer.Write(6); else writer.RestoreOriginal();
                native.Api.DuringSeekTimeRead = null;
                Check(injection && innerRefused && recoveryRefused && releaseRefused && noNestedReads,
                    "An entry graph getter reentered, restored or released the running actual time writer.");
                if (mode == "seek-time-write-reentry") writer.RestoreOriginal();
            }
            else if (mode.StartsWith("seek-time-readback-", StringComparison.Ordinal))
            {
                var failures = 0;
                native.Api.SeekTimeReadException = name => name == "get_time" && native.TimeCalls > 0
                    && (mode == "seek-time-readback-always" || failures++ == 0);
                Check(SeekTimeRefused(() => writer.Write(6)), "The controlled actual SetTime getter failure was silently accepted.");
                Check(native.TimeCalls == 1 && native.TimeStores == 1 && native.Media == 6,
                    "The failed API read did not retain exactly one actual wrapper/lower SetTime original.");
                var beforePublicRead = native.Api.SeekTimeApiReads;
                Check(SeekTimeRefused(receipt.VerifyCurrent, ErrorCode.NotReady)
                    && native.Source.LastFailure is null && native.Api.SeekTimeApiReads == beforePublicRead,
                    "The pending time writer's old API receipt was consumed or faulted its private restoration owner.");
                if (mode == "seek-time-readback-progress")
                {
                    native.Api.SetRootedGraphValue("progress.now", baseline.ProgressSeconds + 1);
                    Check(SeekTimeRefused(writer.RestoreOriginal) && native.TimeCalls == 1 && native.Media == 6,
                        "Time recovery overwrote media after another paused progress baseline changed.");
                }
                else if (mode == "seek-time-readback-always")
                {
                    Check(SeekTimeRefused(writer.RestoreOriginal) && native.TimeCalls == 2 && native.TimeStores == 2
                        && native.Media == baseline.AudioPositionSeconds,
                        "Permanent API failure invented restoration or discarded the actual inverse original.");
                }
                else
                {
                    writer.RestoreOriginal();
                    Check(native.TimeCalls == 2 && native.TimeStores == 2 && writer.Read().AudioPositionSeconds == baseline.AudioPositionSeconds,
                        "The same retained native time continuation failed to restore and independently read its original media.");
                }
            }
            else if (mode == "seek-time-before-write-progress")
            {
                native.Api.DuringSeekTimeRead = name =>
                {
                    if (name != "get_clip" || injection) return;
                    injection = true;
                    native.Api.SetRootedGraphValue("progress.now", baseline.ProgressSeconds + 1);
                };
                Check(SeekTimeRefused(() => writer.Write(6), ErrorCode.Conflict) && injection
                    && native.TimeCalls == 0 && native.Media == baseline.AudioPositionSeconds,
                    "A progress change in the entry getter reached the native media store.");
                native.Api.DuringSeekTimeRead = null;
            }
            else if (mode is "seek-time-before-write-media" or "seek-time-before-next-write-media")
            {
                if (mode == "seek-time-before-next-write-media") writer.Write(6);
                var ownedMedia = native.Media;
                var previousCalls = native.TimeCalls;
                var previousStores = native.TimeStores;
                const float externalMedia = 4.5f;
                Marshal.WriteInt64(native.Api.GameplaySource, 32, BitConverter.DoubleToInt64Bits(externalMedia));
                Check(SeekTimeRefused(() => writer.Write(7), ErrorCode.Conflict)
                    && native.TimeCalls == previousCalls && native.TimeStores == previousStores && native.Media == externalMedia,
                    "A time write overwrote the paused position changed outside its retained source observation.");

                // The external owner puts its position back before the next request.
                Marshal.WriteInt64(native.Api.GameplaySource, 32, BitConverter.DoubleToInt64Bits(ownedMedia));
                writer.Write(7);
                Check(writer.Read().AudioPositionSeconds == 7 && native.TimeStores == previousStores + 1,
                    "The same writer could not advance from its independently observed position.");
                writer.RestoreOriginal();
            }
            else if (mode == "seek-time-external-equal")
            {
                writer.Write(6);
                native.Api.GameplayApi.Call(native.Api.GameplaySource, "set_time", NativeRuntime.Arg.Value(6f));
                Check(SeekTimeRefused(writer.RestoreOriginal) && native.TimeCalls == 2 && native.Media == 6,
                    "An external equal media write allowed restoration through the obsolete private continuation.");
            }
            else if (mode == "seek-time-release-inflight")
            {
                var roots = native.RootHandles(); var kept = false; var releaseRefused = false;
                object? rootsAtClose = null;
                native.Api.DuringSeekTimeRead = name =>
                {
                    if (name != "get_time" || injection || native.TimeCalls == 0) return;
                    injection = true; native.Source.Dispose();
                    var actualRoots = native.RootHandles();
                    rootsAtClose = new { before = roots.Select(row => new { id = row.Key, target = row.Value.ToInt64() }),
                        after = actualRoots.Select(row => new { id = row.Key, target = row.Value.ToInt64() }),
                        native.Source.RootsReleased, native.Api.ActiveRoots };
                    kept = actualRoots.SequenceEqual(roots) && native.Api.ActiveRoots >= 7 && !native.Source.RootsReleased;
                    releaseRefused = SeekTimeRefused(writer.Dispose, ErrorCode.MutationReentrancy);
                };
                var writeRefused = SeekTimeRefused(() => writer.Write(6));
                Console.WriteLine(JsonSerializer.Serialize(new { observation = "actual-post-time-getter-source-close",
                    writeRefused, injection, kept, releaseRefused, native.Api.ActiveRoots, rootsAtClose,
                    injectionFailure = native.Api.SeekTimeInjectionFailure?.ToString() }));
                Check(writeRefused && injection && kept && releaseRefused
                    && native.Api.SeekTimeInjectionFailure is null && native.TimeCalls == 1 && native.Api.ActiveRoots == 7,
                    "A reentrant source close freed the seven roots or its running SetTime operation before the getter returned.");
                native.Api.DuringSeekTimeRead = null;
            }
            else
            {
                writer.Write(6);
                var observed = writer.Read();
                Check(observed.AudioPositionSeconds == 6 && observed.Pitch == baseline.Pitch
                    && observed.ProgressSeconds == baseline.ProgressSeconds && observed.ChartPosition == baseline.ChartPosition,
                    "Actual time write changed the retained pitch/progress/chart baseline or failed independent media readback.");
                writer.RestoreOriginal();
            }

            Check(native.Api.SeekTimeInjectionFailure is null && native.PitchCalls == pitchCalls,
                "The time transaction changed pitch or hid a fixture injection failure.");
            var healthyRecovery = mode is not ("seek-time-readback-progress" or "seek-time-readback-always"
                or "seek-time-external-equal" or "seek-time-release-inflight" or "seek-time-before-write-progress");
            if (healthyRecovery)
                Check(writer.Read().AudioPositionSeconds == baseline.AudioPositionSeconds && native.Source.LastFailure is null,
                    "A successful time inverse was not independently confirmed on the original source.");
            writer.Dispose();
            if (!healthyRecovery)
                Check(SeekTimeRefused(writer.RestoreOriginal) && native.TimeCalls <= 2,
                    "An unresolved or disposed time attempt permitted another inverse store.");
            var final = native.Group.ReadSourceHealth();
            Check(final.Known && !final.Unknown && final.InFlight == 0 && final.SourceSequence == initial.SourceSequence
                && final.WriterSequence == initial.WriterSequence && final.SchedulingSequence == initial.SchedulingSequence + native.TimeCalls,
                "Time completion lost its actual scheduling fence or changed unrelated source/pitch authority.");
            native.Finish();
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-seek-time-writer-regression", mode, passed = true,
                checks = _checks, sourceRoleCount = 22, actualPrepare = 23, actualCommit = 23,
                timeOriginalCalls = native.TimeCalls, timeOriginalStores = native.TimeStores,
                pitchOriginalCalls = native.PitchCalls - pitchCalls, nativeEvents = native.NativeEvents,
                sourceRootsReleased = native.Source.RootsReleased, activeRoots = native.Api.ActiveRoots,
                sameActualTimeWriter = true, metadataSynthetic = true, formalFreshSourceScopeCovered = false,
                formalSeekProviderIssued = false, clockIssued = false, fullSeekCompleted = false,
                productionAdmission = false, gameExecuted = false, installed = false }));
            native.Dispose();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-seek-time-writer-regression", mode, passed = false,
                checks = _checks, failure = error.ToString(), nativeEvents = native?.NativeEvents,
                timeOriginalCalls = native?.TimeCalls, timeOriginalStores = native?.TimeStores,
                activeRoots = native?.Api.ActiveRoots, sourceFailure = native?.Source.LastFailure }));
            native?.Dispose();
            return 1;
        }
    }
}
