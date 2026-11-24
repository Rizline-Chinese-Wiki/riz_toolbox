using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    // A physical Unknown must keep the fake objects backing native roots alive
    // for the rest of this one-process fixture run.
    private static readonly List<ConstructorContext> ConstructorRetainedContexts = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong ConstructorRead(uint field);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ConstructorForeign(uint index, IntPtr initial);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ConstructorBoundary(IntPtr self, IntPtr reference, IntPtr method, uint at);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ConstructorConfigure(uint flags, ConstructorBoundary? boundary);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LowerFixtureBind(IntPtr target);

    private sealed class ConstructorContext : IDisposable
    {
        internal readonly FakeNative Api = new();
        internal readonly GameEventState State = new();
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Logger Log = new();
        internal readonly IntPtr Module;
        internal readonly IntPtr LowerModule;
        internal readonly PlaybackSourceGroupTargets Targets;
        internal readonly NativePlaybackBinding Binding;
        internal readonly NativePlaybackRateOrigin Origin;
        internal readonly GameEventState.PlaybackRunAssociation Run;
        internal readonly NativePlaybackStockClockRouter Router;
        internal readonly ExactHookBroker Broker;
        internal readonly PlaybackSourceGroupLease Group;
        internal readonly InvokeStock Invoke;
        internal readonly ConstructorRead Read;
        internal readonly ConstructorConfigure Configure;
        internal readonly GetTime Time;
        internal readonly List<NativePlaybackSourceObservation> Observations = new();
        internal NativePlaybackStockClockRouter.Session Session = null!;
        internal NativePlaybackClock Clock = null!;
        internal Action<NativePlaybackSourceObservation>? AfterConstructorBefore;
        internal Action<NativePlaybackSourceObservation>? AfterDirectAudioBefore;
        internal Action<NativePlaybackSourceObservation>? AfterLegacyAudioBefore;
        internal int PhysicalMemberCount => Targets.SourceRoleCount + 1;
        internal int SourceField(int historicalOwnedOffset) => Targets.HasUnityBacking ? historicalOwnedOffset + 8 : historicalOwnedOffset;
        internal string? CallbackFailure;
        internal bool UniqueFinalClock = true;
        internal int ClockReads, ConstructorBefores;

        internal ConstructorContext(string[] a, bool hostOnly = false, Action<object, Action>? faultDelivery = null)
        {
            Module = NativeLibrary.Load(Path.GetFullPath(a[1]));
            var lowerPitch = Hash(a[1]) == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256;
            if (lowerPitch)
            {
                LowerModule = NativeLibrary.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a[1]))!, "RizToolbox.PlaybackLowerPitchFixture.dll"));
                Check(Export<LowerFixtureBind>(Module, "StockFixtureBindLowerPitch")(
                    NativeLibrary.GetExport(LowerModule, "StockLowerSetPitch")) == 1,
                    "The owned wrapper did not bind its actual independent lower entry once.");
            }
            var unityBacking = lowerPitch || Hash(a[1]) == PlaybackSourceGroupTargets.UnityBackingFixtureSha256;
            var resource = unityBacking || Hash(a[1]) == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256;
            if (unityBacking) Api.ConfigureUnityBacking(Module);
            else
            {
                Api.ConfigureInitializer(Module); Api.ConfigureSharedConstructor(Module);
                if (resource || Hash(a[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256) Api.ConfigureDirectAudio(Module);
                if (resource) Api.ConfigureProgressResource(Module);
            }
            Api.SetRootedGraphValue("progress.now", 2.8); Api.SetRootedGraphValue("info.nowTime", 5.6f);
            Targets = lowerPitch ? PlaybackSourceGroupTargets.ResolveOwnedLowerPitchFixture(Module, LowerModule)
                : unityBacking ? PlaybackSourceGroupTargets.ResolveOwnedUnityBackingFixture(Module)
                : resource ? PlaybackSourceGroupTargets.ResolveOwnedProgressResourceFixture(Module)
                : Hash(a[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256
                ? PlaybackSourceGroupTargets.ResolveOwnedDirectAudioFixture(Module)
                : PlaybackSourceGroupTargets.ResolveOwnedConstructorFixture(Module);
            Check(Targets.HasPlayerConstructor && Targets.SourceRoleCount == (resource ? 19 : Targets.HasDirectAudioControls ? 18 : 12) + (lowerPitch ? 1 : 0), "Constructor fixture must expose its exact fixed source roles.");
            State.Scene("Level"); State.SetAvailability(scenes: true, runs: true, pause: true);
            var started = State.Controller(Api.GameplayInformation.ToInt64(), true, false, "constructor-sdk", Api.GameplayController.ToInt64())!;
            State.ObservePause(true);
            Binding = new(started.RunId, State.SceneGeneration, 1, 1, Api.GameplayController.ToInt64(), Api.GameplayInformation.ToInt64(),
                Api.GameplayProgress.ToInt64(), Api.GameplayPlayer.ToInt64(), Api.GameplaySource.ToInt64(), Api.GameplayClip.ToInt64(), Targets.GetTime.MethodInfo.ToInt64());
            Origin = new(Binding.RunId, Binding.ControllerGeneration, Binding.Controller, Binding.Information, Binding.Progress, Api.GameplayPause.ToInt64());
            Run = State.CapturePlaybackRun(Binding.Information, Binding.Controller);
            if (hostOnly)
            {
                Marshal.WriteIntPtr(Api.GameplayProgress, 0x38, IntPtr.Zero);
                Router = new(Api.GameplayApi, NativePlaybackInitializationHost.Capture(Api.GameplayApi, Run), Run);
            }
            else Router = new(Api.GameplayApi, Binding, Origin, Run);
            Broker = new(Path.GetFullPath(a[4]), Main, Log);
            Group = Broker.BindPlaybackSourceGroup(Targets, Path.GetFullPath(a[2]), Hash(a[2]), Path.GetFullPath(a[3]), Hash(a[3]),
                () => { ++ClockReads; UniqueFinalClock &= Clock is not null && ReferenceEquals(Clock, Session.Clock); return Router.ReadClock(); },
                fault =>
                {
                    if (faultDelivery is null) Router.OnGetTimeFault(fault);
                    else faultDelivery(fault, () => Router.OnGetTimeFault(fault));
                }, fault =>
                {
                    if (faultDelivery is null) Router.OnSourceFault(fault);
                    else faultDelivery(fault, () => Router.OnSourceFault(fault));
                },
                observation =>
                {
                    Observations.Add(observation);
                    // Logical withdrawal must occur before our assertions. An
                    // assertion exception must never become a transport fault.
                    Router.ObserveBefore(observation);
                    if (observation.Role == NativePlaybackSourceRole.PlayerConstructor)
                    {
                        ++ConstructorBefores;
                        try { AfterConstructorBefore?.Invoke(observation); }
                        catch (Exception error) { CallbackFailure ??= error.ToString(); }
                    }
                    else if (observation.Role >= NativePlaybackSourceRole.AudioPlayHelper)
                    {
                        try { AfterDirectAudioBefore?.Invoke(observation); }
                        catch (Exception error) { CallbackFailure ??= error.ToString(); }
                    }
                    else if (LegacyAudioRoles.Contains(observation.Role))
                    {
                        try { AfterLegacyAudioBefore?.Invoke(observation); }
                        catch (Exception error) { CallbackFailure ??= error.ToString(); }
                    }
                }, Router.ObserveReturned);
            Router.Attach(Group);
            Check(Broker.ReadActualPlaybackDobbyCallCounts() == (PhysicalMemberCount, PhysicalMemberCount), "Constructor group lacks thirteen actual Dobby Prepare/Commit calls.");
            Invoke = Export<InvokeStock>(Module, "StockFixtureInvoke");
            Read = Export<ConstructorRead>(Module, "StockFixtureConstructorRead");
            Configure = Export<ConstructorConfigure>(Module, "StockFixtureConfigureConstructor");
            Configure(0, null);
            Time = Marshal.GetDelegateForFunctionPointer<GetTime>(Targets.GetTime.Address);
            if (hostOnly)
            {
                Check(Private<object?>(Router, "_source") is null && Api.ActiveRoots == 4,
                    "Host-only constructor context must retain only its four actual host roots.");
                return;
            }
            var source = Private<NativePlaybackStockRootedSource>(Router, "_source");
            var actual = Private<NativePlaybackActualSource>(source, "_actual");
            var roots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
            Check(ReferenceEquals(Private<NativeRuntime>(source, "_api"), Api.GameplayApi) && roots.Count == 7
                && roots.Select(root => root.Target.ToInt64()).SequenceEqual(new[] { Binding.Controller, Binding.Information, Binding.Progress,
                    Origin.Pause, Binding.Player, Binding.AudioSource, Binding.Clip }) && Api.ActiveRoots == 7,
                "Actual constructor route must own its seven NativeRuntime GC roots.");
        }
        internal void Stock(NativePlaybackSourceRole role, float argument = 0)
        {
            var target = Targets.SourceTarget(role);
            Check(Invoke(role == NativePlaybackSourceRole.Pause ? 1u : 2u, target.Address, Api.GameplayPlayer,
                unchecked((uint)BitConverter.SingleToInt32Bits(argument)), target.MethodInfo, out var code) == 1 && code == 0,
                "Actual constructor-route stock body failed: " + role + "; " + Router.LastFailure);
        }
        internal void Arm()
        {
            Marshal.WriteInt32(Api.GameplaySource, SourceField(48), 1); Stock(NativePlaybackSourceRole.Pause);
            Session = Router.PreparePausedClock(); Check(Session.TryArm(), "Actual constructor-route final Clock did not arm."); Clock = Session.Clock;
            Check(Group.GetTimeLease.ReadCurrent().Active && Time(Api.GameplayPlayer, Targets.GetTime.MethodInfo) == (float)Clock.Capture().MediaAnchorSeconds,
                "Actual native GetTime did not consume the unique armed Clock.");
        }
        internal void SynchronizePausedFixtureProgress()
        {
            // The POD source has no Progress.Update producer. Sample the real
            // paused AudioSource API, then materialize the fixture's progress
            // seconds and its seeded 2-chart-units-per-second raw nowTime.
            // Playback rate changes elapsed media time, not this unit mapping.
            var actualAudioTime = Api.GameplayApi.Unbox<float>(Api.GameplayApi.Call(Api.GameplaySource, "get_time"));
            var offset = Api.GameplayApi.Read<double>(Api.GameplayProgress, "_offset");
            Check(Clock.Capture().Phase == NativePlaybackPhase.Paused
                && !Api.GameplayApi.Unbox<bool>(Api.GameplayApi.Call(Api.GameplaySource, "get_isPlaying"))
                && float.IsFinite(actualAudioTime) && double.IsFinite(offset),
                "Fixture progress may only follow a finite actual paused audio sample.");
            var progressSeconds = actualAudioTime - offset;
            Api.SetRootedGraphValue("progress.now", progressSeconds);
            Api.SetRootedGraphValue("info.nowTime", (float)(progressSeconds * 2));
            Check(Api.GameplayApi.Read<double>(Api.GameplayProgress, "_nowTime") + offset == actualAudioTime
                && Api.GameplayApi.Read<float>(Api.GameplayInformation, "nowTime") == (float)(progressSeconds * 2),
                "Paused fixture progress/raw chart time did not match actual audio seconds and the applied offset.");
        }
        internal int Ctor(IntPtr self, IntPtr reference, IntPtr method, out uint code)
            => Invoke(4, Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor).Address, self, unchecked((ulong)reference.ToInt64()), method, out code);
        internal void RecordBoundary(Action action)
        { try { action(); } catch (Exception error) { CallbackFailure ??= error.ToString(); } }
        public void Dispose()
        {
            Configure(0, null);
            try { Router.Dispose(); Router.PumpCompleted(); }
            finally
            {
                if (Router.RootsReleased && Api.ActiveRoots == 0)
                {
                    if (Targets.HasUnityBacking) Api.ReleaseUnityBackingStorage();
                    Api.Dispose(); Main.Dispose();
                }
                else ConstructorRetainedContexts.Add(this);
            }
        }
    }

    private sealed class ConstructorSdk : IDisposable
    {
        internal readonly ConstructorContext Native;
        internal readonly NativePlaybackStockClockRouter.RateProviderHost Provider;
        internal readonly RunModificationJournal Journal;
        internal readonly GameplayModule Module;
        internal readonly GameplayService Service;
        internal readonly PluginHost Host;
        internal readonly IGameplayControlService Controls, Other;
        internal readonly IGameplayClockService ClockView, OtherClock;
        internal readonly IGameplayControlLease Lease;
        private long _hostTick;
        internal ConstructorSdk(ConstructorContext native)
        {
            Native = native; native.Arm(); Provider = native.Router.AdoptControlledRateHost();
            Journal = new(native.Main, native.Log, native.State);
            Module = new(native.Api.GameplayApi, native.Main, () => native.State.LifecycleSnapshot(), Journal.Record, native.State.MatchesCurrentGameplayRun, Provider);
            Check(Module.Probe().Ready && Provider.Probe().Supported, "Actual constructor provider did not reach the formal Module.");
            Service = Private<GameplayService>(Module, "_service");
            var capabilities = new CapabilityRegistry(new("constructor-sdk", "fixture", "synthetic", "offline"));
            var registration = new ModuleRegistration(capabilities, Module.Manifest); Module.Register(registration); registration.Complete();
            capabilities.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, Journal.ForPlugin);
            Host = new(capabilities, native.Main, new EventHub(native.Log), native.Log);
            var first = new RouterPlugin("constructor-one"); var second = new RouterPlugin("constructor-two");
            Check(Host.Register(first).Success && Host.Register(second).Success && Host.Enable(first.Manifest.Id).Success && Host.Enable(second.Manifest.Id).Success,
                "Actual constructor Module failed to expose independent plugin scopes.");
            Controls = first.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            Other = second.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            ClockView = first.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
            OtherClock = second.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
            Lease = Need(Controls.Acquire(true));
            Check(!Other.Acquire(true).Success && ClockView.Read().Success && OtherClock.Read().Success && native.Api.ActiveRoots == 14,
                "Dual SDK views did not share exclusive control and independent formal capture roots.");
        }
        internal GameplayControlRequest Request(double value) => new(GameplayControlAction.SetSpeed, Lease.ControllerGeneration,
            Lease.ObservedRunId, value, GameplayControlValueUnit.PlaybackRateMultiplier);
        internal void SampleControls() => Module.ObserveControls(++_hostTick, _hostTick, requested: true);
        internal void Speed(double value)
        {
            var calls = Marshal.ReadInt32(Native.Api.GameplaySource, Native.SourceField(20)); var health = Native.Group.ReadSourceHealth();
            var result = Need(Lease.Execute(Request(value)));
            Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed && result.Modification.ConfirmedValue == value
                && Native.Clock.Capture().Rate == value && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(Native.Api.GameplaySource, Native.SourceField(16))) == (float)value,
                "Actual SDK SetSpeed did not confirm both native pitch and the unique final Clock.");
            Check(Marshal.ReadInt32(Native.Api.GameplaySource, Native.SourceField(20)) == calls + 1
                && Marshal.ReadIntPtr(Native.Api.GameplaySource, Native.SourceField(72)) == Native.Targets.PitchInstance.MethodInfo
                && unchecked((ulong)Marshal.ReadInt64(Native.Api.GameplaySource, Native.SourceField(104))) == unchecked((uint)BitConverter.SingleToInt32Bits((float)value))
                && Native.Group.ReadSourceHealth().WriterSequence == health.WriterSequence + 1,
                "Confirmed SDK SetSpeed did not execute exactly one native pitch body with the exact MI/argument/writer fence.");
        }
        public void Dispose()
        { Lease.Dispose(); Module.Release(); Host.Dispose(); Journal.Dispose(); Provider.PumpCompleted(); Provider.Dispose(); }
    }

    private static int RunConstructor(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode is "ctor-sdk-normal" or "ctor-sdk-same" or "ctor-sdk-null" or "ctor-sdk-distinct" or "ctor-sdk-aba"
                or "ctor-sdk-null-mi" or "ctor-sdk-wrong-mi" or "ctor-sdk-throw-before" or "ctor-sdk-throw-after" or "ctor-sdk-runtime" or "ctor-sdk-foreign",
                "Unknown actual constructor SDK mode.");
            Check(Hash(a[1]) == PlaybackSourceGroupTargets.ConstructorFixtureSha256, "Only the separately fixed owned constructor fixture may execute.");
            using var native = new ConstructorContext(a);
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                if (mode == "ctor-sdk-normal") ConstructorNormal(native, sdk);
                else if (mode == "ctor-sdk-foreign") ConstructorForeignCalls(native, sdk);
                else ConstructorCurrent(native, sdk, mode);
            }
            native.Router.PumpCompleted();
            Check(native.CallbackFailure is null, "An actual constructor callback assertion failed: " + native.CallbackFailure);
            Check(native.UniqueFinalClock && native.ClockReads > 0 && native.Broker.ReadActualPlaybackDobbyCallCounts() == (13, 13),
                "Actual constructor SDK used a temporary Clock or replaced physical hooks.");
            var abnormal = mode is "ctor-sdk-throw-before" or "ctor-sdk-throw-after";
            if (abnormal)
                Check(!native.Router.RootsReleased && native.Api.ActiveRoots == 7
                    && native.Group.ReadActivity() is { Known: false, InFlight: 0 },
                    "SDK disposal lost the seven actual source roots after constructor transport Unknown.");
            else
                Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(observation => observation.IsReleased),
                    "Physically exited constructor SDK drain retained actual NativeRuntime roots or observations.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, uniqueFinalClock = native.UniqueFinalClock,
                nativeReads = native.ClockReads, constructorBefores = native.ConstructorBefores, runtimeConstructorInvocations = native.Api.SharedConstructorInvocations,
                sameModuleServiceSdk = true, independentPluginScopes = 2, retainedRoots = native.Api.ActiveRoots, actualPrepare = 13, actualCommit = 13,
                nativeSourceVersion = 4, gameExecuted = false, productionAdmission = false })); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1; }
    }

    private static void ConstructorNormal(ConstructorContext native, ConstructorSdk sdk)
    {
        var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress, native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress);
        var clock = native.Clock;
        native.Stock(NativePlaybackSourceRole.Replay, .125f); var replay = clock.Capture(); native.Api.SetRootedDsp(replay.DspAnchorSeconds + .5);
        Check(replay.Rate == 1.5 && Math.Abs(native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo) - .5f * (float)replay.Rate) < .00001f,
            "Actual Replay did not preserve SDK-selected pitch and consume the same final Clock.");
        native.Stock(NativePlaybackSourceRole.Pause); native.Stock(NativePlaybackSourceRole.Resume, .25f); var resume = clock.Capture();
        native.Api.SetRootedDsp(resume.DspAnchorSeconds + .375);
        Check(resume.Rate == 1.5 && Math.Abs(native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo)
                - (float)(resume.MediaAnchorSeconds + .375 * resume.Rate)) < .00001f,
            "Actual Resume did not preserve actual SetTime/pitch/schedule/final Clock continuity.");
        // This POD fixture has no engine mixer advancing AudioSource.time when
        // DSP advances. Materialize the media value already returned by the
        // unique Clock before the next real Pause/readback and SDK write.
        var resumedMedia = resume.MediaAnchorSeconds + .375 * resume.Rate;
        Marshal.WriteInt64(native.Api.GameplaySource, 24, BitConverter.DoubleToInt64Bits(resumedMedia));
        native.Stock(NativePlaybackSourceRole.Pause); native.SynchronizePausedFixtureProgress(); sdk.Speed(2.5);
        Check(sdk.Module.Tick().Ready && ReferenceEquals(sdk.Service, Private<GameplayService>(sdk.Module, "_service")) && ReferenceEquals(clock, native.Session.Clock)
            && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success && !sdk.Other.Acquire(true).Success
            && physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress, native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress),
            "Normal constructor route lost its original Module/Service/SDK/physical Clock identity.");
    }

    private static void ConstructorCurrent(ConstructorContext native, ConstructorSdk sdk, string mode)
    {
        var oldSource = native.Api.GameplaySource; var oldClock = native.Clock;
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
        using var captured = sdk.Provider.Capture(native.Origin);
        using var writer = captured.PreparePitchWrite();
        using var prepared = NativePlaybackRateProviderAdoption.Prepare(sdk.Provider, native.Origin,
            new(GameplayControlAction.SetSpeed, native.Binding.ControllerGeneration, native.Binding.RunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier),
            () => native.Run.VerifyCurrent(native.Binding));
        var distinct = native.Api.CreateSharedConstructorSource();
        var value = mode == "ctor-sdk-null" ? IntPtr.Zero : mode is "ctor-sdk-distinct" or "ctor-sdk-aba" or "ctor-sdk-throw-before" or "ctor-sdk-throw-after" or "ctor-sdk-runtime" ? distinct : oldSource;
        var method = mode == "ctor-sdk-null-mi" ? IntPtr.Zero : mode == "ctor-sdk-wrong-mi" ? native.Targets.Pause.MethodInfo
            : native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor).MethodInfo;
        var health = native.Group.ReadSourceHealth(); var calls = native.Read(0); var returns = native.Read(1); var pitchCalls = Marshal.ReadInt32(oldSource, 20);
        var initialIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
        Check(initialIdentity is { NativeDescriptor: not null }, "Initial SDK source did not carry its exact native descriptor.");
        var enteredChecks = 0; var boundaries = new List<uint>();
        void Revoked()
        {
            Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && (!native.Group.IsActive || !native.Group.GetTimeLease.ReadCurrent().Active)
                && !sdk.Provider.Probe().Supported, "Current constructor retained old Clock/publication/provider before the real store.");
            Refuse(() => { _ = rooted.Current; }, "Current constructor retained rooted source authority.");
            Refuse(captured.VerifyAuthority, "Current constructor retained independently captured source authority.");
            Refuse(writer.VerifyCurrent, "Current constructor retained its old continuous writer.");
            Check(!sdk.Lease.Execute(sdk.Request(3)).Success, "Current constructor retained the original formal SDK control lease.");
            var firstClock = Need(sdk.ClockView.Read()); var secondClock = Need(sdk.OtherClock.Read());
            Check(firstClock.ControllerGeneration > sdk.Lease.ControllerGeneration && secondClock.ControllerGeneration > sdk.Lease.ControllerGeneration,
                "An original SDK view still published the retired native source generation.");
            Check(Marshal.ReadInt32(oldSource, 20) == pitchCalls, "Revoked SDK/source probe wrote to the retired native source.");
        }
        native.AfterConstructorBefore = observation =>
        {
            Check(observation.Self == native.Api.GameplayPlayer && observation.ReferenceArgument == value && observation.IncomingMethod == method
                && Marshal.ReadIntPtr(native.Api.GameplayPlayer, 16) == oldSource && native.Read(0) == calls
                && native.Group.ReadSourceHealth().SourceSequence == health.SourceSequence + 1,
                "Actual Before did not precede original entry/store or changed raw constructor arguments/fence.");
            Revoked();
            var freshIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
            Check(freshIdentity != initialIdentity && freshIdentity is { NativeDescriptor: null },
                "Before-store SDK resampling retained the old native descriptor instead of the new raw-clock source identity.");
            Refuse(prepared.Commit, "Prepared old rate transaction committed after constructor Before."); prepared.Restore();
            Check(Marshal.ReadInt32(oldSource, 20) == pitchCalls, "Prepared stale commit/restore changed the old native pitch."); ++enteredChecks;
        };
        ConstructorBoundary boundary = (self, reference, incoming, at) => native.RecordBoundary(() =>
        {
            boundaries.Add(at);
            Check(self == native.Api.GameplayPlayer && reference == value && incoming == method
                && Marshal.ReadIntPtr(self, 16) == (at == 1 ? oldSource : value), "Actual constructor boundary changed raw args or store ordering.");
            Revoked();
        });
        var flags = mode == "ctor-sdk-throw-before" ? 1u : mode == "ctor-sdk-throw-after" ? 2u : 0u;
        native.Configure(flags, boundary);
        uint code = 0; int status;
        if (mode == "ctor-sdk-runtime")
        {
            Check(!native.Api.HasRootFor(value), "Runtime constructor reference was already rooted before its temporary argument capture.");
            native.Api.BeginSharedConstructorRootAudit();
            Check(native.Api.GameplayApi.Call(native.Api.GameplayPlayer, ".ctor", NativeRuntime.Arg.Ref(value)) == IntPtr.Zero,
                "Actual NativeRuntime constructor did not return void."); status = 1;
            Check(native.Api.SharedConstructorInvocations == 1 && native.Api.AllGameplayReceiversRooted
                && native.Api.SharedConstructorTemporaryReceiverRoots == 2 && native.Api.SharedConstructorTemporaryReferenceRoots == 1
                && native.Api.SharedConstructorTemporaryRootsReleased && !native.Api.HasRootFor(value),
                "Constructor bypassed the rebuilt runtime export, lost its two temporary receiver roots/one new reference root, or retained them after return.");
        }
        else status = native.Ctor(native.Api.GameplayPlayer, value, method, out code);
        native.Configure(0, null); GC.KeepAlive(boundary);
        Check(native.CallbackFailure is null && enteredChecks == 1, "Actual constructor Before/boundary revocation assertion failed: " + native.CallbackFailure);
        Check(status == (flags == 0 ? 1 : 2) && code == (flags == 1 ? 0xe2400041u : flags == 2 ? 0xe2400042u : 0u)
            && boundaries.SequenceEqual(flags == 1 ? new uint[] { 1 } : new uint[] { 1, 2 })
            && native.Read(0) == calls + 1 && native.Read(1) == returns + (flags == 1 ? 0ul : 1ul)
            && native.Read(2) == unchecked((ulong)native.Api.GameplayPlayer.ToInt64()) && native.Read(3) == unchecked((ulong)value.ToInt64())
            && native.Read(4) == unchecked((ulong)method.ToInt64()) && native.Read(5) == unchecked((ulong)oldSource.ToInt64())
            && Marshal.ReadIntPtr(native.Api.GameplayPlayer, 16) == (flags == 1 ? oldSource : value),
            "Current constructor did not execute exactly one original/store or preserve its precise native SEH.");
        if (flags != 0)
        {
            var actual = native.Observations.Single(observation => observation.Role == NativePlaybackSourceRole.PlayerConstructor);
            var physical = actual.TryReadCompletion(out var completion);
            var activity = native.Group.ReadActivity();
            Check(physical && completion is { PhysicalExited: true, Abnormal: true, Returned: false, Unknown: true }
                && completion.Sequence == actual.Sequence && !activity.Known && activity.InFlight == 0
                && !native.Group.IsActive && !native.Router.RootsReleased && native.Api.ActiveRoots >= 7,
                "Constructor SEH lost its actual abnormal physical exit, sticky transport Unknown, or retained seven-root responsibility.");
        }
        Revoked();
        if (mode == "ctor-sdk-aba")
        {
            native.AfterConstructorBefore = null; var fence = native.Group.ReadSourceHealth().SourceSequence;
            var secondCalls = native.Read(0); var secondReturns = native.Read(1);
            Check(native.Ctor(native.Api.GameplayPlayer, oldSource, method, out code) == 1 && code == 0
                && native.Group.ReadSourceHealth().SourceSequence == fence + 1 && Marshal.ReadIntPtr(native.Api.GameplayPlayer, 16) == oldSource
                && native.Read(0) == secondCalls + 1 && native.Read(1) == secondReturns + 1 && native.ConstructorBefores == 2,
                "A to B to A constructor failed to record the second native attempt."); Revoked();
        }
        Check(flags != 0 || native.Group.ReadSourceHealth() is { Known: true, InFlight: 0, Unknown: false }
            && native.Group.PollOperationNativeFaults() is null && Private<string?>(native.Router, "_physicalFailure") is null,
            "Normal current constructor invented a physical fault instead of logical withdrawal.");
        native.AfterConstructorBefore = null;
    }

    private static void ConstructorForeignCalls(ConstructorContext native, ConstructorSdk sdk)
    {
        var health = native.Group.ReadSourceHealth(); var initialization = native.Group.ReadInitialization(); var observations = native.Observations.Count;
        var before = native.Read(0); var after = native.Read(1); var clock = native.Clock.Capture(); var reader = native.ClockReads;
        var ctor = native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor);
        var foreign = Export<ConstructorForeign>(native.Module, "StockFixtureCreateForeignSharedReceiver");
        var other = Export<CreateChild>(native.Module, "StockFixtureCreatePlayer")(native.Api.GameplaySource);
        Marshal.WriteIntPtr(other, Marshal.ReadIntPtr(native.Api.GameplayPlayer));
        var count = 0;
        void Call(IntPtr self, IntPtr value, IntPtr method)
        {
            Check(native.Ctor(self, value, method, out var code) == 1 && code == 0 && Marshal.ReadIntPtr(self, 16) == value
                && native.Read(2) == unchecked((ulong)self.ToInt64()) && native.Read(3) == unchecked((ulong)value.ToInt64())
                && native.Read(4) == unchecked((ulong)method.ToInt64()), "Foreign shared constructor did not transparently forward raw arguments and the real store."); ++count;
        }
        Call(other, native.Api.GameplaySource, ctor.MethodInfo);
        var receiver = foreign(3, IntPtr.Zero);
        for (var index = 0; index < 320; ++index)
            Call(receiver, new IntPtr(0x7700 + index), index % 3 == 0 ? ctor.MethodInfo : index % 3 == 1 ? IntPtr.Zero : native.Targets.Pause.MethodInfo);
        var worker = Task.Run(() => native.Ctor(receiver, new IntPtr(0x9911), ctor.MethodInfo, out var code) == 1 && code == 0);
        Check(worker.Wait(10000) && worker.Result && Marshal.ReadIntPtr(receiver, 16) == new IntPtr(0x9911), "Foreign worker constructor did not return transparently."); ++count;
        Check(native.Read(0) == before + (ulong)count && native.Read(1) == after + (ulong)count && native.Observations.Count == observations
            && native.ConstructorBefores == 0 && native.Group.ReadSourceHealth() == health && native.Group.ReadInitialization().Equals(initialization)
            && native.Clock.Capture() == clock && native.ClockReads == reader && !native.Router.PhysicalFaultPending
            && native.Group.GetTimeLease.ReadCurrent().Active && sdk.Provider.Probe().Supported && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success,
            "Foreign same-class/address, worker or >256 flood changed playback fences, journals, Clock, SDK or thread health.");
        ConstructorNormal(native, sdk);
    }
}
