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
    private static readonly string[] SeekSourceModes =
    {
        "seek-source-normal", "seek-source-readback-once", "seek-source-withdrawn", "seek-source-route-change",
        "seek-source-getter-route", "seek-source-external-equal", "seek-source-release-inflight",
        "seek-source-prepare-release"
    };
    private static readonly List<SeekSourceContext> RetainedSeekSourceContexts = new();

    // The owned two-image group executes the same private source implementation.
    // Reflection selects that implementation only: no formal provider, Fresh
    // ticket, host registration or production metadata is manufactured here.
    private sealed class SeekSourceContext : IDisposable
    {
        internal readonly FakeNative Api = new();
        internal readonly MainThreadDispatcher Main = new();
        internal readonly GameEventState State = new();
        internal readonly IntPtr Wrapper, Lower;
        internal readonly PlaybackSourceGroupTargets Targets;
        internal readonly ExactHookBroker Broker;
        internal readonly PlaybackSourceGroupLease Group;
        internal readonly NativePlaybackBinding Binding;
        internal readonly NativePlaybackRateOrigin Origin;
        internal readonly NativePlaybackStockClockRouter Router;
        internal readonly NativePlaybackStockRootedSource Rooted;
        internal readonly NativePlaybackStockClockRouter.Session Session;
        internal readonly NativePlaybackClock Clock;
        internal readonly NativePlaybackStockRateProvider Provider;
        internal readonly List<object> NativeEvents = new();
        private readonly LowerFixtureRead _timeRead;
        private readonly GetTime _getTime;
        private bool _disposed;

        internal SeekSourceContext(string[] arguments, IntPtr mappedClockImage = default)
        {
            var wrapperPath = Path.GetFullPath(arguments[1]);
            var lowerPath = Path.Combine(Path.GetDirectoryName(wrapperPath)!, "RizToolbox.PlaybackLowerPitchFixture.dll");
            Check(Hash(wrapperPath) == OwnedPlaybackFixtureProfile.WrapperSha256
                && Hash(lowerPath) == OwnedPlaybackFixtureProfile.LowerSha256,
                "Seek source checks require the exact current two-image native fixture.");
            Wrapper = NativeLibrary.Load(wrapperPath); Lower = NativeLibrary.Load(lowerPath);
            Check(Export<LowerFixtureBind>(Wrapper, "StockFixtureBindLowerPitch")(
                    NativeLibrary.GetExport(Lower, "StockLowerSetPitch")) == 1
                && Export<LowerFixtureBind>(Wrapper, "StockFixtureBindLowerTime")(
                    NativeLibrary.GetExport(Lower, "StockLowerSetTime")) == 1,
                "The actual owned wrapper did not bind both independent lower originals.");
            Api.ConfigureUnityBacking(Wrapper); Api.ObserveSeekTimeReads();
            Api.SetRootedGraphValue("progress.now", 2.8); Api.SetRootedGraphValue("info.nowTime", 5.6f);
            if (mappedClockImage != IntPtr.Zero)
            {
                Api.SetRootedGraphValue("info.nowRealTime", 2.8f); Api.SetRootedGraphValue("info.nowSyncedTime", 2.8f);
                Api.ConfigureSeekProgress(mappedClockImage);
            }
            Targets = PlaybackSourceGroupTargets.ResolveOwnedLowerTimeFixture(Wrapper, Lower);
            Check(Targets.SourceRoleCount == 22 && Targets.HasLowerTime && Targets.HasEnginePitch,
                "The actual Seek source group omitted a time or pitch channel.");
            State.Scene("Level"); State.SetAvailability(scenes: true, runs: true, pause: true);
            var started = State.Controller(Api.GameplayInformation.ToInt64(), true, false,
                "seek-source-operation-owned", Api.GameplayController.ToInt64())!;
            State.ObservePause(true);
            Binding = new(started.RunId, State.SceneGeneration, 1, 1, Api.GameplayController.ToInt64(),
                Api.GameplayInformation.ToInt64(), Api.GameplayProgress.ToInt64(), Api.GameplayPlayer.ToInt64(),
                Api.GameplaySource.ToInt64(), Api.GameplayClip.ToInt64(), Targets.GetTime.MethodInfo.ToInt64());
            Origin = new(Binding.RunId, Binding.ControllerGeneration, Binding.Controller, Binding.Information,
                Binding.Progress, Api.GameplayPause.ToInt64());
            Router = new(Api.GameplayApi, Binding, Origin, State.CapturePlaybackRun(Binding.Information, Binding.Controller));
            Broker = new(Path.GetFullPath(arguments[4]), Main, new Logger());
            Group = Broker.BindPlaybackSourceGroup(Targets, Path.GetFullPath(arguments[2]), Hash(arguments[2]),
                Path.GetFullPath(arguments[3]), Hash(arguments[3]), () => Router.ReadClock(),
                Router.OnGetTimeFault, Router.OnSourceFault, observation =>
                {
                    NativeEvents.Add(new { stage = "before", role = observation.Role.ToString(), observation.Sequence,
                        observation.ParentSequence, observation.WriterSequence, observation.SourceSequence,
                        observation.SchedulingSequence, observation.ArgBits, lowerTime = observation.IsExactLowerTimeChild });
                    Router.ObserveBefore(observation);
                }, completion =>
                {
                    NativeEvents.Add(new { stage = "returned", role = completion.Observation.Role.ToString(),
                        completion.Observation.Sequence, completion.Returned, completion.Abnormal, completion.PhysicalExited });
                    Router.ObserveReturned(completion);
                });
            Router.Attach(Group);
            Rooted = Private<NativePlaybackStockRootedSource>(Router, "_source");
            Check(Broker.ReadActualPlaybackDobbyCallCounts() == (23, 23) && Api.ActiveRoots == 7,
                "The actual Seek source group lacks its twenty-three committed members or original seven roots.");
            Marshal.WriteInt32(Api.GameplaySource, 56, 1);
            var pause = Targets.SourceTarget(NativePlaybackSourceRole.Pause);
            Check(Export<InvokeStock>(Wrapper, "StockFixtureInvoke")(1, pause.Address, Api.GameplayPlayer,
                    0, pause.MethodInfo, out var code) == 1 && code == 0,
                "The actual stock Pause original failed.");
            var receipt = Rooted.Current ?? throw new InvalidOperationException("Pause lacks readback: " + Rooted.LastFailure);
            Check(receipt.PhysicalExited && receipt.Sample.AudioPositionSeconds == 3 && receipt.Sample.Paused,
                "The private source lacks a real exited Pause and independent media baseline.");
            Session = Router.PreparePausedClock();
            var armed = false;
            for (var attempt = 0; attempt < 4 && !armed; ++attempt)
            {
                Group.PumpCompleted(); Router.PumpCompleted(); armed = Session.TryArm();
            }
            Check(armed, "The actual owned Clock did not arm its unique registered native publication.");
            Clock = Session.Clock;
            Provider = NativePlaybackStockRateProvider.AdoptControlledFixture(Session);
            _timeRead = Export<LowerFixtureRead>(Lower, "StockLowerTimeFixtureRead");
            _getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(Targets.GetTime.Address);
            Check(Provider.Probe().Supported && Time() == 3,
                "The owned provider and actual detoured GetTime do not share their initial paused Clock.");
        }

        internal ulong TimeCalls => _timeRead(0);
        internal ulong TimeStores => _timeRead(1);
        internal float Media => (float)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(Api.GameplaySource, 32));
        internal float Time() => _getTime(Api.GameplayPlayer, Targets.GetTime.MethodInfo);
        internal INativePlaybackSeekSource CaptureImplementation()
        {
            Check(SeekTimeRefused(() => Provider.CaptureSeek(Origin), ErrorCode.OperationUnsupported),
                "The historical fixture provider issued a formal Seek source.");
            var implementation = typeof(NativePlaybackStockClockRouter.Session).GetNestedType("SeekSource", BindingFlags.NonPublic)!;
            return (INativePlaybackSeekSource)Activator.CreateInstance(implementation,
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { Session }, null)!;
        }
        internal void Finish()
        {
            Api.DuringSeekTimeRead = null; Api.SeekTimeReadException = null;
            Provider.Dispose(); Router.PumpCompleted(); Group.PumpCompleted();
            Check(Router.RootsReleased && Api.ActiveRoots == 0,
                "The completed private Seek source left source or independent preparation roots behind.");
            Check(Api.SeekTimeInjectionFailure is null && Api.AllGameplayReceiversRooted,
                "The private Seek source hid a fixture callback failure or used an unrooted receiver.");
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            Api.DuringSeekTimeRead = null; Api.SeekTimeReadException = null;
            Router.Dispose(); Router.PumpCompleted(); Group.PumpCompleted();
            if (!Router.RootsReleased || Api.ActiveRoots != 0) { RetainedSeekSourceContexts.Add(this); return; }
            Api.ReleaseUnityBackingStorage(); Api.Dispose(); Main.Dispose();
        }
    }

    private static int RunSeekSource(string[] arguments, string mode)
    {
        SeekSourceContext? native = null;
        INativePlaybackSeekSource? source = null;
        INativePlaybackTimeWrite? writer = null;
        NativePlaybackClock.NativePlaybackClockPositionChange? position = null;
        try
        {
            Check(arguments.Length == 5 && SeekSourceModes.Contains(mode), "Unknown private Seek source operation case.");
            native = new(arguments); source = native.CaptureImplementation();
            var baseline = source.Read(); var initialClock = source.Clock.Capture();
            Check(ReferenceEquals(source.Clock, native.Clock) && ReferenceEquals(source.Binding, native.Binding),
                "The private Seek source copied the actual binding or unique Clock registration.");
            position = source.Clock.PreparePausedPosition(source.Binding, 6);
            if (mode == "seek-source-prepare-release")
            {
                var injected = false;
                native.Api.DuringSeekTimeRead = name =>
                {
                    if (name != "get_time" || injected) return;
                    injected = true; source.Dispose();
                };
                var refused = SeekTimeRefused(() => writer = source.PrepareTimeWrite());
                native.Api.DuringSeekTimeRead = null;
                Check(refused && injected && writer is null && native.TimeCalls == 0
                    && Private<object?>(native.Rooted, "_timeWriter") is null && native.Api.ActiveRoots == 7,
                    "A source released during preparation left an unreturned native time writer or retained operation behind.");
            }
            else writer = source.PrepareTimeWrite();

            if (mode == "seek-source-prepare-release") { /* No writer was issued. */ }
            else if (mode == "seek-source-readback-once")
            {
                var failures = 0;
                native.Api.SeekTimeReadException = name => name == "get_time" && native.TimeCalls > 0 && failures++ == 0;
                Check(SeekTimeRefused(() => writer.Write(6)) && failures > 0
                    && native.TimeCalls == 1 && native.TimeStores == 1 && native.Media == 6,
                    "The actual completed time write did not retain its failed API readback.");
                var reads = native.Api.SeekTimeApiReads;
                var pending = native.Provider.Probe();
                Check(pending.Supported && !native.Session.IsWithdrawn && native.Api.SeekTimeApiReads == reads,
                    "Provider policy verification consumed the pending old time receipt and withdrew its only inverse owner.");
                writer.RestoreOriginal();
                Check(native.TimeCalls == 2 && native.TimeStores == 2 && source.Read().AudioPositionSeconds == 3
                    && native.Provider.Probe().Supported && native.Time() == 3,
                    "The same source-bound time continuation did not restore actual media while keeping the original Clock.");
                position.VerifyBaseline();
            }
            else if (mode is "seek-source-withdrawn" or "seek-source-route-change")
            {
                if (mode == "seek-source-withdrawn") native.Clock.Withdraw("The owned test withdrew the actual current Clock.");
                else typeof(NativePlaybackStockClockRouter).GetField("_sourceRoute", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(native.Router, new object());
                Check(SeekTimeRefused(() => writer.Write(6)) && native.TimeCalls == 0 && native.Media == 3,
                    "The returned raw time writer escaped its withdrawn Clock or changed source-route registration.");
            }
            else if (mode == "seek-source-getter-route")
            {
                var injected = false;
                native.Api.DuringSeekTimeRead = name =>
                {
                    if (name != "get_clip" || injected) return;
                    injected = true;
                    typeof(NativePlaybackStockClockRouter).GetField("_sourceRoute", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(native.Router, new object());
                };
                Check(SeekTimeRefused(() => writer.Write(6)) && injected && native.TimeCalls == 0 && native.Media == 3,
                    "The entry graph getter replaced the route after the wrapper guard but before the actual native media store.");
                native.Api.DuringSeekTimeRead = null;
            }
            else if (mode == "seek-source-external-equal")
            {
                writer.Write(6);
                native.Api.GameplayApi.Call(native.Api.GameplaySource, "set_time", NativeRuntime.Arg.Value(6f));
                Check(SeekTimeRefused(writer.RestoreOriginal) && native.TimeCalls == 2 && native.Media == 6
                    && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
                    "A prepared private writer flag mistook an external equal-value store for its actual native reservation.");
            }
            else if (mode == "seek-source-release-inflight")
            {
                var injected = false; var releaseRefused = false; var rootsKept = false;
                var roots = native.Api.ActiveRoots;
                native.Api.DuringSeekTimeRead = name =>
                {
                    if (name != "get_time" || injected || native.TimeCalls == 0) return;
                    injected = true;
                    releaseRefused = SeekTimeRefused(source.Dispose, ErrorCode.MutationReentrancy);
                    rootsKept = native.Api.ActiveRoots >= roots && !native.Router.RootsReleased;
                };
                writer.Write(6); native.Api.DuringSeekTimeRead = null;
                Check(injected && releaseRefused && rootsKept && native.Api.ActiveRoots == roots
                    && source.Read().AudioPositionSeconds == 6 && native.Provider.Probe().Supported,
                    "A busy source release dropped its preparation roots or disposed its only writer before the native read returned.");
                writer.RestoreOriginal();
            }
            else
            {
                writer.Write(6);
                var targetAudio = source.Read();
                Check(targetAudio.AudioPositionSeconds == 6 && targetAudio.ProgressSeconds == baseline.ProgressSeconds
                    && targetAudio.ChartPosition == baseline.ChartPosition && native.Time() == 3,
                    "The media stage changed progress or published Clock before its own participant committed.");
                // These real API inputs model the next participant's progress
                // stage. The pinned game conversion/profile and note/Hold/UI
                // participants are deliberately outside this owned case.
                native.Api.SetRootedGraphValue("progress.now", 5.8);
                native.Api.SetRootedGraphValue("info.nowTime", 11.6f);
                var target = source.Read();
                Check(target.ProgressSeconds == 5.8 && target.ChartPosition == 11.6f && target.AudioPositionSeconds == 6,
                    "The captured time source incorrectly froze the later participant's progress stage.");
                position.Commit();
                Check(position.Confirm() && native.Time() == 6 && source.Read().AudioPositionSeconds == 6
                    && native.Provider.Probe().Supported && native.TimeCalls == 1,
                    "The actual Clock did not publish and read back the independently completed media stage.");
                position.Restore();
                Check(position.ConfirmRestored() && native.Time() == 3,
                    "The actual inverse Clock did not restore before its media participant.");
                native.Api.SetRootedGraphValue("progress.now", baseline.ProgressSeconds);
                native.Api.SetRootedGraphValue("info.nowTime", baseline.ChartPosition);
                writer.RestoreOriginal();
                Check(source.Read().AudioPositionSeconds == 3 && native.Media == 3 && native.Time() == 3
                    && native.TimeCalls == 2 && native.TimeStores == 2 && native.Provider.Probe().Supported,
                    "The same private source could not read its time successor after Clock and progress restoration.");
                Check(native.Clock.Capture().Rate == initialClock.Rate && native.Clock.Capture().DspAnchorSeconds == initialClock.DspAnchorSeconds,
                    "Seek changed the selected rate or rewound the paused DSP anchor.");
            }
            Check(native.Api.SeekTimeInjectionFailure is null, "A fixture injection failure was mistaken for product rejection.");
            position.Dispose(); position = null;
            writer?.Dispose(); writer = null;
            source.Dispose(); source = null;
            var health = native.Group.ReadSourceHealth();
            Check(health.Known && !health.Unknown && health.InFlight == 0,
                "The private source operation left the actual native group unknown or in flight.");
            native.Finish();
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-seek-source-operation", mode, passed = true,
                checks = _checks, sourceRoleCount = 22, actualPrepare = 23, actualCommit = 23,
                timeOriginalCalls = native.TimeCalls, timeOriginalStores = native.TimeStores,
                nativeEvents = native.NativeEvents, sourceRootsReleased = native.Router.RootsReleased, activeRoots = native.Api.ActiveRoots,
                privateSourceImplementationExecuted = true, actualClockExecuted = true, metadataSynthetic = true,
                formalFreshSourceScopeCovered = false, formalSeekProviderIssued = false, fullSeekCompleted = false,
                progressParticipantExecuted = false, productionAdmission = false, gameExecuted = false, installed = false }));
            native.Dispose(); return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-seek-source-operation", mode, passed = false,
                checks = _checks, failure = error.ToString(), timeOriginalCalls = native?.TimeCalls,
                nativeEvents = native?.NativeEvents, sourceFailure = native?.Rooted.LastFailure, activeRoots = native?.Api.ActiveRoots }));
            foreach (var owner in new IDisposable?[] { position, writer, source, native })
                try { owner?.Dispose(); } catch (Exception cleanup) { Console.Error.WriteLine(cleanup); }
            return 1;
        }
    }
}
