using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

internal static class NativePlaybackProviderChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("actual native gameplay backend retains unsupported Speed and Seek without a provider", DefaultUnsupported),
        ("queued native rate executes one actual pitch setter through the formal service", ServiceAdoption),
        ("rate provider cannot mask missing native capture metadata", MetadataGate),
        ("native backend and cached formal service observe provider capability withdrawal", DynamicCapability),
        ("provider capability does not assert a particular run is ready", PerRunReadiness),
        ("native backend rejects provider run controller information and progress mismatch before writing", ExactBinding),
        ("native provider capture cannot replace rooted graph during preparation", CaptureReplacement),
        ("native provider rechecks every captured graph edge after setters and before restoration", ChangedGraph),
        ("native provider source and clock replacement during confirmation cannot reuse old authority", ChangedSource),
        ("actual native setter failure restores independent audio and clock through formal service", SetterRecovery),
        ("native preparation failure attempts every cleanup once and preserves original failure", PreparationCleanup),
        ("provider binding getter failure releases transferred source once", BindingFailure),
        ("adopted source and writer reject copied or worker cleanup before consuming actual owner responsibility", BoundOwnerCleanup),
        ("cancellation after native provider capture releases resources without any write", PreparationCancellation),
        ("module and plugin stop during actual pitch setter restore before releasing native capture", StopDuringSetter),
        ("formal PluginHost can consume provider via actual native gameplay backend", HostAdoption)
    };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static T Need<T>(Result<T> result) { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing result."); return result.Value!; }
    private static void Error<T>(Result<T> result, ErrorCode expected)
        => Check(!result.Success && result.Error?.KnownCode == expected, "Expected " + expected + "; got " + result.Error);
    private static GameplayControlRequest Request(IGameplayControlLease lease)
        => new(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier);
    private static GameplayControlAvailability Rate(GameplayProbe probe) => probe.Operations.Single(x => x.Action == GameplayControlAction.SetSpeed);
    private static GameplayControlAvailability Rate(GameplayControlSnapshot snapshot) => snapshot.Operations.Single(x => x.Action == GameplayControlAction.SetSpeed);

    private static void DefaultUnsupported()
    {
        using var f = new NativeFixture(provider: false);
        Check(!Rate(f.Backend.Probe()).Supported && !f.Backend.Probe().Operations.Single(x => x.Action == GameplayControlAction.Seek).Supported, "Default native controls became supported.");
        var lease = Need(f.Controls.Acquire(true)); Error(lease.Execute(Request(lease)), ErrorCode.OperationUnsupported);
        Check(f.Native.GameplayPitchWrites == 0 && f.Provider.Captures == 0, "Default native backend entered a provider or setter.");
    }
    private static void ServiceAdoption()
    {
        using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
        f.Native.AfterGameplayPitchWrite = () => Check(f.Provider.Clock!.Capture().Rate == 1, "Clock published before actual audio write/readback.");
        var probes = f.Provider.Probes;
        var command = Need(lease.Submit(Request(lease)));
        Check(command.Snapshot.State == GameplayControlCommandState.Queued && f.Provider.Probes == probes
            && f.Native.GameplayPitchWrites == 0, "Submission entered actual native playback.");
        f.Service.PumpControlCommand();
        Check(command.Snapshot.State == GameplayControlCommandState.Succeeded && command.Snapshot.Receipt is not null,
            command.Snapshot.Error?.Message ?? "The native rate command did not complete.");
        var receipt = command.Snapshot.Receipt!;
        Check(f.Provider.Probes - probes == 2, "Rate execution repeats provider probing inside its bound source or writer.");
        Check(receipt.ImmediatePostconditionObserved == true && receipt.Modification.TransactionState == GameplayControlTransactionState.Confirmed
            && receipt.Modification.ConfirmedValue == 2 && receipt.Modification.ConfirmedUnit == GameplayControlValueUnit.PlaybackRateMultiplier
            && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f }) && f.Provider.Clock!.Capture().Rate == 2
            && f.Provider.Last!.Disposals == 1 && f.Provider.Last.WriteDisposals == 1 && f.Provider.Last.Reads >= 8
            && f.Native.AllGameplayReceiversRooted && f.Native.AllArrayReadsRooted
            && f.Journal.Single().Outcome == RunModificationOutcome.Applied, "Formal native adoption did not confirm actual independent audio/clock participants.");
        Check(f.Provider.LastOrigin is { RunId: "provider-run", ControllerGeneration: 1 }
            && f.Provider.LastOrigin.Controller == f.Native.GameplayController.ToInt64()
            && f.Provider.LastOrigin.Information == f.Native.GameplayInformation.ToInt64()
            && f.Provider.LastOrigin.Progress == f.Native.GameplayProgress.ToInt64()
            && f.Provider.LastOrigin.Pause == f.Native.GameplayPause.ToInt64(), "The backend did not supply its actual rooted graph to the provider.");
        Check(!f.Backend.Probe().Operations.Single(x => x.Action == GameplayControlAction.Seek).Supported, "A rate provider implicitly enabled Seek.");
    }
    private static void MetadataGate()
    {
        foreach (var field in new[] { "timeReady", "_pausing", "levelInformation", "loadOver" })
        {
            using var f = new NativeFixture(missingField: field);
            var rate = Rate(f.Backend.Probe());
            Check(!rate.Supported && rate.Error?.KnownCode == ErrorCode.UnsupportedSignature && f.Provider.Captures == 0,
                "Provider support concealed missing native field " + field + ".");
        }
    }
    private static void DynamicCapability()
    {
        for (var mode = 0; mode < 3; mode++)
        {
            using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
            Check(Rate(Need(f.SampledControlState())).Supported, "Initial provider support is missing.");
            if (mode == 0) f.Provider.Supported = false;
            if (mode == 1) f.Provider.ProbeError = new IOException("controlled transport withdrawn");
            if (mode == 2) f.Provider.Evidence = " ";
            Check(!Rate(f.Backend.Probe()).Supported && !Rate(f.Service.Probe).Supported && !Rate(Need(f.SampledControlState())).Supported,
                "A cached backend/service probe retained withdrawn provider support.");
            Error(lease.Execute(Request(lease)), ErrorCode.CapabilityUnavailable);
            Check(f.Provider.Captures == 0 && f.Native.GameplayPitchWrites == 0, "Withdrawn provider was captured or wrote audio.");
        }
    }
    private static void PerRunReadiness()
    {
        using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
        f.Provider.CaptureError = new GameplayStateException(ErrorCode.NotReady, "Current schedule observer has no admitted paused occurrence.");
        Error(lease.Execute(Request(lease)), ErrorCode.NotReady);
        Check(Rate(f.Backend.Probe()).Supported && f.Provider.Last is null && f.Native.GameplayPitchWrites == 0
            && Need(f.SampledControlState()).Modifications.Count == 0, "Metadata capability was conflated with per-run admission or fabricated an audit mutation.");
    }
    private static void ExactBinding()
    {
        var mutations = new Func<NativePlaybackBinding, NativePlaybackBinding>[] {
            b => b with { RunId = "another-run" }, b => b with { ControllerGeneration = b.ControllerGeneration + 1 },
            b => b with { Controller = b.Controller + 1 }, b => b with { Information = b.Information + 1 },
            b => b with { Progress = b.Progress + 1 }, b => b with { SceneGeneration = b.SceneGeneration + 1 }
        };
        foreach (var mutate in mutations)
        {
            using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true)); f.Provider.BindingMutation = mutate;
            Error(lease.Execute(Request(lease)), ErrorCode.TargetExpired);
            Check(f.Provider.Last!.Disposals == 1 && f.Native.GameplayPitchWrites == 0 && f.Provider.Clock!.Capture().Rate == 1,
                "A provider binding mismatch wrote audio or leaked transferred source roots.");
        }
    }
    private static void CaptureReplacement()
    {
        using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
        f.Provider.OnCapture = () => f.Native.SetGameplayField("controller.pause", f.Native.GameplaySource);
        Error(lease.Execute(Request(lease)), ErrorCode.TargetExpired);
        Check(f.Provider.Last!.Disposals == 1 && f.Native.GameplayPitchWrites == 0, "Provider capture crossed a changed native graph or leaked roots.");
    }
    private static void ChangedGraph()
    {
        foreach (var edge in new[] { "controller.info", "info.progress", "progress.info", "controller.pause", "controller.active" })
        {
            using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
            f.Native.AfterGameplayPitchWrite = () => {
                if (edge == "controller.active") f.Native.GameplayActive = false;
                else f.Native.SetGameplayField(edge, f.Native.GameplaySource);
            };
            Error(lease.Execute(Request(lease)), ErrorCode.TargetExpired);
            Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f }) && f.Provider.Last!.Disposals == 1
                && f.Provider.Last.WriteDisposals == 1 && f.Journal.Single().Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown,
                "Changed graph " + edge + " was confirmed or overwritten during restoration.");
        }
    }
    private static void ChangedSource()
    {
        for (var mode = 0; mode < 3; mode++)
        {
            using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
            f.Provider.OnRead = source => {
                if (f.Provider.Clock!.Capture().Rate != 2) return;
                if (mode == 0) f.Provider.BindingMutation = b => b with { SourceGeneration = b.SourceGeneration + 1 };
                if (mode == 1) f.Provider.Clock = new(source.Binding, 100, 4, 2, 120);
                if (mode == 2) f.Provider.Supported = false;
            };
            var result = lease.Execute(Request(lease));
            Error(result, mode == 2 ? ErrorCode.CapabilityUnavailable : ErrorCode.TargetExpired);
            Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f }) && f.Provider.Last!.Disposals == 1
                && f.Journal.Single().Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown
                && !Rate(Need(f.SampledControlState())).Supported, "Changed provider/source/clock was confirmed or called fully restored.");
        }
    }
    private static void SetterRecovery()
    {
        using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true)); f.Provider.FailWriteNumber = 1;
        var failure = lease.Execute(Request(lease)); Error(failure, ErrorCode.InternalError);
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f, 1f }) && f.Provider.Clock!.Capture().Rate == 1
            && f.Journal.Single().Outcome == RunModificationOutcome.Restored && Rate(Need(f.SampledControlState())).Supported
            && f.Provider.Last!.Disposals == 1 && f.Provider.Last.WriteDisposals == 1
            && failure.Error!.Message.Contains("controlled pitch changed before setter failed"), "Actual native partial setter failure lost independent restoration or its original failure.");
    }
    private static void PreparationCleanup()
    {
        using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
        f.Provider.ThrowWriteDispose = true; f.Provider.ThrowSourceDispose = true;
        f.Provider.OnRead = source => { if (source.Reads == 2) f.Provider.ScheduleRevision++; };
        var failure = lease.Execute(Request(lease)); Error(failure, ErrorCode.Conflict);
        Check(f.Provider.Last!.Disposals == 1 && f.Provider.Last.WriteDisposals == 1 && f.Native.GameplayPitchWrites == 0
            && failure.Error!.Message.Contains("prepared audio/progress/offset/origin/schedule authority changed"),
            "A cleanup exception replaced preparation failure or prevented another independent cleanup.");
        using var next = f.Provider.Clock!.PreparePausedRate(f.Provider.Clock.Capture().Binding, 2); next.VerifyBaseline();
    }
    private static void BindingFailure()
    {
        using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true)); f.Provider.ThrowBindingGetter = true;
        Error(lease.Execute(Request(lease)), ErrorCode.InternalError);
        Check(f.Provider.Last!.Disposals == 1 && f.Native.GameplayPitchWrites == 0, "Binding getter failure leaked transferred native source.");
    }
    private static void BoundOwnerCleanup()
    {
        using var f = new NativeFixture();
        var n = f.Native;
        var origin = new NativePlaybackRateOrigin("provider-run", 1, n.GameplayController.ToInt64(),
            n.GameplayInformation.ToInt64(), n.GameplayProgress.ToInt64(), n.GameplayPause.ToInt64());
        var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, 1, origin.RunId, 2,
            GameplayControlValueUnit.PlaybackRateMultiplier);
        using var prepared = NativePlaybackRateProviderAdoption.Prepare(f.Provider, origin, request, () => { });
        var verify = (Action)prepared.GetType().GetField("_verifyOrigin",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(prepared)!;
        var bound = (INativePlaybackRateAccess)verify.Target!;
        var writer = bound.PreparePitchWrite();
        var clone = typeof(object).GetMethod("MemberwiseClone",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var copiedSource = (INativePlaybackRateAccess)clone.Invoke(bound, null)!;
        var copiedWriter = (INativePlaybackPitchWrite)clone.Invoke(writer, null)!;
        var roots = n.ActiveRoots; var frees = n.FreeHandleAttempts;
        static void Refuse(Action action, ErrorCode code)
        {
            try { action(); }
            catch (GameplayStateException error) when (error.Code == code) { return; }
            throw new InvalidOperationException("An adopted wrapper did not reject foreign authority before cleanup.");
        }
        Refuse(copiedSource.Dispose, ErrorCode.TargetExpired);
        Refuse(copiedWriter.Dispose, ErrorCode.TargetExpired);
        Task.Run(() =>
        {
            Refuse(bound.Dispose, ErrorCode.WrongThread);
            Refuse(writer.Dispose, ErrorCode.WrongThread);
            Refuse(() => writer.Write(3), ErrorCode.WrongThread);
        }).GetAwaiter().GetResult();
        Check(n.ActiveRoots == roots && n.FreeHandleAttempts == frees && n.GameplayPitchWrites == 0
            && f.Provider.Last!.Disposals == 0 && f.Provider.Last.WriteDisposals == 0,
            "Copied or worker cleanup consumed the actual owner's roots or private writer.");
        bound.VerifyAuthority(); writer.VerifyCurrent(); writer.Dispose();
        prepared.Commit();
        Check(prepared.Confirm().Observed == true && n.GameplayPitchAttempts.SequenceEqual(new[] { 2f })
            && f.Provider.Clock!.Capture().Rate == 2, "Rejected foreign cleanup poisoned the original prepared rate operation.");
        prepared.Dispose(); bound.Dispose(); writer.Dispose();
        Check(n.ActiveRoots == 0 && f.Provider.Last!.Disposals == 1 && f.Provider.Last.WriteDisposals == 2,
            "Owner cleanup did not release each independently prepared source and writer exactly once.");
    }
    private static void PreparationCancellation()
    {
        using var f = new NativeFixture(); using var cancellation = new CancellationTokenSource(); var lease = Need(f.Controls.Acquire(true));
        f.Provider.OnCapture = cancellation.Cancel; Error(lease.Execute(Request(lease), cancellation.Token), ErrorCode.Cancelled);
        Check(f.Provider.Last!.Disposals == 1 && f.Provider.Last.WriteDisposals == 1 && f.Native.GameplayPitchWrites == 0
            && f.Journal.Count == 0, "Cancelled native preparation wrote or failed to release every resource.");
    }
    private static void StopDuringSetter()
    {
        for (var mode = 0; mode < 2; mode++)
        {
            using var f = new NativeFixture(); var lease = Need(f.Controls.Acquire(true));
            f.Native.AfterGameplayPitchWrite = () => {
                if (f.Native.GameplayPitchWrites != 1) return;
                if (mode == 0) f.Service.Dispose(); else f.Context.Dispose();
                Check(f.Provider.Last!.Disposals == 0 && f.Native.HasRootFor(f.Native.GameplayController), "Stop freed source/capture roots inside setter.");
            };
            Error(lease.Execute(Request(lease)), ErrorCode.TargetExpired);
            Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f, 1f }) && f.Provider.Clock!.Capture().Rate == 1
                && f.Provider.Last!.Disposals == 1 && f.Journal.Single().Outcome == RunModificationOutcome.Restored,
                "Stop during native setter did not restore audio+clock before once-only cleanup.");
        }
    }
    private static void HostAdoption()
    {
        using var f = new NativeFixture(); var capabilities = new CapabilityRegistry(new("controlled", "controlled", "controlled", "controlled"));
        using var host = new PluginHost(capabilities, f.Main, new EventHub(new Logger()), new Logger());
        capabilities.RegisterScoped<IGameplayControlService>(GameplayCapabilityIds.Controls, c => f.Service.ControlsForPlugin(c));
        IGameplayControlLease? lease = null;
        host.Register(new Plugin(context => {
            lease = Need(context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!.Acquire(true));
            Need(lease.Execute(Request(lease)));
        }));
        Check(host.Enable("probe").Success, "Formal host did not adopt actual native backend provider."); host.Disable("probe");
        Error(lease!.Execute(Request(lease)), ErrorCode.ScopeExpired);
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f }) && f.Provider.Last!.Disposals == 1,
            "Formal host scope cleanup undid/repeated the confirmed native command.");
    }

    private sealed class NativeFixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly MainThreadDispatcher Main = new();
        internal readonly Provider Provider; internal readonly GameplayNativeBackend Backend;
        internal readonly GameplayService Service; internal readonly Context Context;
        internal readonly IGameplayControlService Controls; internal readonly List<RunModificationInput> Journal = new();
        internal GameLifecycleSnapshot Lifecycle = new("controlled-provider-session", 1, 3, "NormalMode", "provider-run", "chart", RunLifecyclePhase.Paused, true, true, true, true, Array.Empty<OperationError>());
        internal NativeFixture(bool provider = true, string? missingField = null)
        {
            Native.ConfigureGameplay(); Native.MissingGameplayField = missingField; Provider = new(this);
            Backend = new(Native.GameplayApi, Main, provider ? Provider : null);
            Service = new(Backend, Main, () => Lifecycle, Journal.Add, matchesCurrentGameplayRun: (info, controller, run, scene) =>
                info == Native.GameplayInformation.ToInt64() && controller == Native.GameplayController.ToInt64()
                && run == Lifecycle.RunId && scene == Lifecycle.SceneGeneration);
            Context = new("native-provider-plugin", Main); Controls = Service.ControlsForPlugin(Context);
        }
        private long _hostTick;
        internal Result<GameplayControlSnapshot> SampledControlState()
        {
            Service.ObserveControlsAfterMaintenance(++_hostTick, _hostTick, requested: true);
            return Controls.GetState();
        }
        public void Dispose()
        {
            Context.Dispose(); Service.Dispose(); Main.Dispose();
            Check(Native.ActiveRoots == 0, "Native provider/capture roots were not released."); Native.Dispose();
        }
    }

    private sealed class Provider : INativePlaybackRateProvider
    {
        internal readonly NativeFixture Fixture;
        internal bool Supported = true, ThrowWriteDispose, ThrowSourceDispose, ThrowBindingGetter;
        internal string Evidence = "Controlled complete return/schedule/writer observations; no game production authority.";
        internal Exception? ProbeError, CaptureError; internal int Probes, Captures, FailWriteNumber;
        internal long ScheduleRevision = 1; internal Source? Last;
        internal NativePlaybackClock? Clock; internal NativePlaybackRateOrigin? LastOrigin;
        internal Action? OnCapture; internal Action<Source>? OnRead;
        internal Func<NativePlaybackBinding, NativePlaybackBinding>? BindingMutation;
        internal Provider(NativeFixture fixture) => Fixture = fixture;
        public NativePlaybackRateProviderProbe Probe()
        {
            Probes++;
            if (ProbeError is not null) throw ProbeError;
            return new(Supported, Evidence, Supported ? null : new(ErrorCode.CapabilityUnavailable, "Controlled observer transport was withdrawn."));
        }
        public INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin)
        {
            Captures++; LastOrigin = origin; if (CaptureError is not null) throw CaptureError;
            var n = Fixture.Native;
            var binding = new NativePlaybackBinding(origin.RunId, Fixture.Lifecycle.SceneGeneration, origin.ControllerGeneration, 2,
                origin.Controller, origin.Information, origin.Progress, n.GameplayPlayer.ToInt64(), n.GameplaySource.ToInt64(), n.GameplayClip.ToInt64(), n.GameplayGetTimeMethod.ToInt64());
            Clock ??= new(binding, n.GameplayDsp, n.GameplayPosition, n.GameplayPitch, n.GameplayLength);
            Last = new(this, binding); OnCapture?.Invoke(); return Last;
        }
        internal sealed class Source : INativePlaybackRateSource
        {
            private readonly Provider _owner; private readonly NativePlaybackBinding _binding;
            private readonly NativeRuntime.RootHandle[] _roots;
            internal int Disposals, WriteDisposals, Reads;
            internal Source(Provider owner, NativePlaybackBinding binding)
            {
                _owner = owner; _binding = binding;
                _roots = new[] { binding.Controller, binding.Information, binding.Progress, binding.Player, binding.AudioSource, binding.Clip }
                    .Select(value => owner.Fixture.Native.GameplayApi.Root(new IntPtr(value))).ToArray();
            }
            public NativePlaybackBinding Binding => _owner.ThrowBindingGetter ? throw new IOException("Controlled binding getter unavailable.")
                : _owner.BindingMutation?.Invoke(_binding) ?? _binding;
            public NativePlaybackClock Clock => _owner.Clock!;
            public void VerifyAuthority()
            {
                if (Disposals != 0) throw new ObjectDisposedException(nameof(Source));
                if (!_owner.Supported || _owner.ProbeError is not null)
                    throw new GameplayStateException(ErrorCode.CapabilityUnavailable, "The captured source's transport was withdrawn.");
                var state = _owner.Fixture.Lifecycle;
                if (state.RunId != _binding.RunId || state.SceneGeneration != Binding.SceneGeneration)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "Controlled exact run/scene source changed.");
                Check(_roots.All(root => root.Target != IntPtr.Zero), "Provider source roots disappeared.");
            }
            public NativePlaybackRateSample Read()
            {
                VerifyAuthority(); Reads++; _owner.OnRead?.Invoke(this); var n = _owner.Fixture.Native; var api = n.GameplayApi;
                return new(Binding, 1, _owner.ScheduleRevision, api.Read<bool>(n.GameplayPause, "_pausing"),
                    api.Unbox<bool>(api.Call(n.GameplaySource, "get_isPlaying")), api.Read<bool>(n.GameplayProgress, "isPlaying"),
                    api.Read<bool>(n.GameplayProgress, "prepareToPlay"), api.Read<bool>(n.GameplayProgress, "timeReady"),
                    api.Read<bool>(n.GameplayInformation, "levelOver"), api.Read<bool>(n.GameplayInformation, "crashed"), n.GameplayDsp,
                    api.Unbox<float>(api.Call(n.GameplaySource, "get_time")), api.Unbox<float>(api.Call(n.GameplayClip, "get_length")),
                    api.Unbox<float>(api.Call(n.GameplaySource, "get_pitch")), api.Read<double>(n.GameplayProgress, "_nowTime"),
                    api.Read<float>(n.GameplayInformation, "nowTime"), api.Read<double>(n.GameplayProgress, "_offset"), false, 0);
            }
            public INativePlaybackPitchWrite PreparePitchWrite() => new PitchWrite(this);
            public void Dispose()
            {
                Disposals++;
                foreach (var root in _roots.Reverse()) root.Dispose();
                if (_owner.ThrowSourceDispose) throw new IOException("Controlled source cleanup failed.");
            }
            private sealed class PitchWrite : INativePlaybackPitchWrite
            {
                private readonly Source _source; private long _revision; private bool _disposed;
                internal PitchWrite(Source source) { _source = source; _revision = source._owner.Fixture.Native.GameplayPitchRevision; }
                public void VerifyCurrent()
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(PitchWrite)); _source.VerifyAuthority();
                    if (_revision != _source._owner.Fixture.Native.GameplayPitchRevision)
                        throw new GameplayStateException(ErrorCode.Conflict, "Controlled continuous pitch writer source changed.");
                }
                public void Write(float pitch)
                {
                    VerifyCurrent(); var n = _source._owner.Fixture.Native;
                    _revision = checked(n.GameplayPitchRevision + 1); // Attempt recorded before actual runtime_invoke.
                    n.GameplayApi.Call(n.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(pitch));
                    if (n.GameplayPitchWrites == _source._owner.FailWriteNumber) throw new IOException("controlled pitch changed before setter failed");
                }
                public void Dispose()
                {
                    if (_disposed) return; _disposed = true; _source.WriteDisposals++;
                    if (_source._owner.ThrowWriteDispose) throw new IOException("Controlled pitch receipt cleanup failed.");
                }
            }
        }
    }
}
