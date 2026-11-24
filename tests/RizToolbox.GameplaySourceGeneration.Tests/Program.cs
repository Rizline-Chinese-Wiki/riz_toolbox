using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

return SourceGenerationChecks.Run();

internal static class SourceGenerationChecks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static T Need<T>(Result<T> result)
    { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing value."); return result.Value!; }
    private static void Expired<T>(Result<T> result)
        => Check(!result.Success && result.Error?.KnownCode == ErrorCode.TargetExpired, "Expected TargetExpired; got " + result.Error);
    private static GameplayControlRequest Speed(IGameplayControlLease lease, double rate)
        => new(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);

    internal static int Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("same-run player replacement without an intervening capture expires the old lease and controls the new source", () => ReplacedGraph(true, false, false)),
            ("same-run AudioSource replacement without an intervening capture expires the old lease and controls the new source", () => ReplacedGraph(false, true, false)),
            ("same-run clip replacement without an intervening capture expires the old lease and controls the new source", () => ReplacedGraph(false, false, true)),
            ("same-address native source epoch replacement expires ownership and admits a new service lease", EpochReplacement),
            ("unchanged source preserves generation and ownership across captures and confirmed rates", StableSource),
            ("the same service and cached SDK views continue across host runs with independent native generations", CrossRun),
            ("source descriptor withdrawal expires the old lease and readiness permits a fresh lease", DescriptorReadiness),
            ("an initially absent descriptor reports NotReady without provider calls and readiness expires its pending lease", InitiallyNotReady),
            ("descriptor withdrawal during provider preparation stays TargetExpired before any mutation", WithdrawalDuringPreparation),
            ("source replacement during provider capture writes nothing and permits a fresh lease", DuringPreparation),
            ("a descriptor cannot map a request from an unbound service context", UnboundRequest),
            ("an SDK request cannot substitute the native controller generation for its service lease generation", ForgedNativeGeneration),
            ("a descriptor with a different run or rooted graph cannot authorize a service request", MismatchedDescriptor),
            ("an optional descriptor failure preserves independent reads, refuses writes and releases every root on disposal", OptionalDescriptorFailure)
        };
        foreach (var test in tests)
        {
            test.Run(); Console.WriteLine("PASS " + test.Name);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "gameplay-source-generation-checks", passed = true,
            tests = tests.Length, checks = _checks, actualNativeBackend = true, syntheticMetadata = true,
            nativeSourceAdmissionProven = false, gameExecuted = false }));
        return 0;
    }

    private static void ReplacedGraph(bool player, bool source, bool clip)
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        var information = f.Native.GameplayInformation; var controller = f.Native.GameplayController;
        var firstIdentity = f.Backend.Capture();
        var oldIdentity = firstIdentity.SourceIdentity;
        f.Native.ReplacePlaybackGraph(player, source, clip); f.Provider.ReplaceSource(advanceEpoch: false);
        Check(firstIdentity.SourceIdentity == oldIdentity && !firstIdentity.CurrentGraphMatches,
            "An old native capture followed the replacement source.");
        firstIdentity.Dispose();
        // Execute is the first Service capture after the complete replacement.
        Expired(lease.Execute(Speed(lease, 1.25)));
        Check(f.Native.GameplayPitchWrites == 0 && f.Provider.Captures == 0 && f.Journal.Count == 0,
            "The stale lease reached the new source or recorded a mutation.");
        var fresh = Need(f.Controls.Acquire(true));
        Check(fresh.ControllerGeneration == lease.ControllerGeneration + 1 && fresh.ObservedRunId == lease.ObservedRunId
            && f.Native.GameplayInformation == information && f.Native.GameplayController == controller,
            "The source replacement did not create exactly one private service generation within the same host graph/run.");
        ConfirmSpeed(f, fresh, 1.25);
        lease.Dispose(); ConfirmSpeed(f, fresh, 1.5);
        Check(Need(f.SampledControlState()).CurrentOwner == f.Context.PluginId,
            "Disposing the superseded lease ended the new owner.");
    }

    private static void EpochReplacement()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        var old = f.Provider.Descriptor;
        f.Provider.ReplaceSource();
        Check(f.Provider.Descriptor.Binding.Player == old.Binding.Player
            && f.Provider.Descriptor.Binding.AudioSource == old.Binding.AudioSource
            && f.Provider.Descriptor.Binding.SourceGeneration == old.Binding.SourceGeneration + 1,
            "Epoch fixture accidentally changed addresses.");
        Expired(lease.Execute(Speed(lease, 1.25)));
        Check(f.Native.GameplayPitchWrites == 0 && f.Provider.Captures == 0, "The old epoch borrowed a new source.");
        var fresh = Need(f.Controls.Acquire(true));
        Check(fresh.ControllerGeneration == lease.ControllerGeneration + 1, "The native source epoch did not advance the service generation.");
        ConfirmSpeed(f, fresh, 1.25);
    }

    private static void StableSource()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        ConfirmSpeed(f, lease, 1.25); f.SampleControls();
        var clocks = Need(f.Clocks.Read());
        Check(clocks.GameTime.Available && clocks.ControllerGeneration == lease.ControllerGeneration,
            "Unavailable optional conversion invalidated independent clock readings or their source generation.");
        Check(Need(f.SampledControlState()).ControllerGeneration == lease.ControllerGeneration,
            "Stable native source changed the service generation.");
        ConfirmSpeed(f, lease, 1.5);
        Check(f.Provider.LastOrigin!.ControllerGeneration == 23 && lease.ControllerGeneration == 1,
            "The test did not distinguish native and service controller generations.");
    }

    private static void CrossRun()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true)); ConfirmSpeed(f, lease, 1.25);
        f.Lifecycle = f.Lifecycle with { RunId = "next-run", Revision = f.Lifecycle.Revision + 1,
            SceneGeneration = f.Lifecycle.SceneGeneration + 1 };
        f.Provider.ReplaceSource(nativeControllerGeneration: 61);
        var writes = f.Native.GameplayPitchWrites;
        Expired(lease.Execute(Speed(lease, 1.5)));
        Check(f.Native.GameplayPitchWrites == writes, "A prior run wrote into its successor.");
        var fresh = Need(f.Controls.Acquire(true));
        Check(fresh.ObservedRunId == "next-run" && fresh.ControllerGeneration == lease.ControllerGeneration + 1,
            "Cached service/SDK views did not remain available across host runs.");
        ConfirmSpeed(f, fresh, 1.5);
        var clocks = Need(f.Clocks.Read());
        Check(clocks.AudioDspTime.Available && clocks.HostMonotonicTime.Available,
            "The cached clock view lost its independent readings across host runs.");
    }

    private static void DescriptorReadiness()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        f.Provider.Ready = false;
        Expired(lease.Execute(Speed(lease, 1.25)));
        var pending = Need(f.Controls.Acquire(true));
        var rejected = pending.Execute(Speed(pending, 1.25));
        Check(!rejected.Success && rejected.Error?.KnownCode == ErrorCode.NotReady && f.Native.GameplayPitchWrites == 0,
            "Expected NotReady without writes for an absent descriptor; actual=" + rejected.Error);
        f.Provider.ReplaceSource(); f.Provider.Ready = true;
        var fresh = Need(f.Controls.Acquire(true)); ConfirmSpeed(f, fresh, 1.25);
    }

    private static void InitiallyNotReady()
    {
        using var f = new Fixture(); f.Provider.Ready = false;
        var pending = Need(f.Controls.Acquire(true));
        var clocks = Need(f.Clocks.Read());
        Check(clocks.AudioDspTime.Available && clocks.HostMonotonicTime.Available,
            "An absent write descriptor removed independent DSP/host clock reads.");
        for (var attempt = 0; attempt < 2; ++attempt)
        {
            var result = pending.Execute(Speed(pending, 1.25));
            Check(!result.Success && result.Error?.KnownCode == ErrorCode.NotReady
                && result.Error.CapabilityId == GameplayCapabilityIds.Controls
                && result.Error.TargetId == "gameplay.controller/" + pending.ControllerGeneration + "/SetSpeed"
                && f.Provider.Captures == 0 && f.Native.GameplayPitchWrites == 0 && f.Journal.Count == 0,
                "An initially pending source expired, entered the provider, mutated, audited a commit or lost its structured NotReady context. Error="
                    + result.Error + " captures=" + f.Provider.Captures + " writes=" + f.Native.GameplayPitchWrites
                    + " journal=" + f.Journal.Count + " leaseGeneration=" + pending.ControllerGeneration);
            // Existing preparation-failure semantics end this exclusive lease.
            // A retry acquires a fresh lease; NotReady is not permission to
            // reuse the failed scope or later borrow descriptor authority.
            Expired(pending.Execute(Speed(pending, 1.25)));
            pending = Need(f.Controls.Acquire(true));
        }
        f.Provider.Ready = true;
        Expired(pending.Execute(Speed(pending, 1.5)));
        Check(f.Provider.Captures == 0 && f.Native.GameplayPitchWrites == 0 && f.Journal.Count == 0,
            "A newly appearing descriptor authorized the earlier descriptor-less lease.");
        var fresh = Need(f.Controls.Acquire(true));
        Check(fresh.ControllerGeneration == pending.ControllerGeneration + 1,
            "Readiness did not advance the source-bound service generation.");
        ConfirmSpeed(f, fresh, 1.5);
    }

    private static void WithdrawalDuringPreparation()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        f.Provider.OnCapture = () => { f.Provider.OnCapture = null; f.Provider.Ready = false; };
        Expired(lease.Execute(Speed(lease, 1.25)));
        Check(f.Provider.Captures == 1 && f.Native.GameplayPitchWrites == 0 && f.Journal.Count == 0,
            "A previously authoritative descriptor withdrawal became NotReady or reached a write/commit audit.");
        f.Provider.Ready = true; var fresh = Need(f.Controls.Acquire(true)); ConfirmSpeed(f, fresh, 1.25);
    }

    private static void DuringPreparation()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        f.Provider.OnCapture = () => { f.Provider.OnCapture = null; f.Provider.ReplaceSource(); };
        Expired(lease.Execute(Speed(lease, 1.25)));
        Check(f.Native.GameplayPitchWrites == 0 && f.Journal.Count == 0, "A source changed during preparation reached commit.");
        var fresh = Need(f.Controls.Acquire(true)); ConfirmSpeed(f, fresh, 1.25);
    }

    private static void OptionalDescriptorFailure()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        var retainedRoots = f.Native.ActiveRoots;
        Check(retainedRoots > 0, "The fixture did not retain an independent source graph.");
        f.Provider.DescriptorFailure = new IOException("descriptor capture failed after source roots");
        // Descriptor capture is optional for observations. Repeated captures
        // replace the retained graph without retaining its predecessor's roots.
        for (var attempt = 0; attempt < 2; ++attempt)
        {
            var state = Need(f.SampledControlState());
            var clocks = Need(f.Clocks.Read());
            Check(state.ControllerGeneration == lease.ControllerGeneration
                && clocks.AudioDspTime.Available && clocks.HostMonotonicTime.Available
                && f.Native.ActiveRoots == retainedRoots,
                "An optional descriptor failure expired independent reads or leaked a replaced capture's roots.");
        }
        Expired(lease.Execute(Speed(lease, 1.25)));
        var pending = Need(f.Controls.Acquire(true));
        var rejected = pending.Execute(Speed(pending, 1.25));
        Check(!rejected.Success && rejected.Error?.KnownCode == ErrorCode.NotReady
            && f.Provider.Captures == 0 && f.Native.GameplayPitchWrites == 0 && f.Journal.Count == 0
            && f.Native.ActiveRoots == retainedRoots,
            "An optional descriptor failure entered the write provider, mutated, audited a commit or leaked roots.");
        f.Provider.DescriptorFailure = null; var fresh = Need(f.Controls.Acquire(true)); ConfirmSpeed(f, fresh, 1.25);
        f.Service.Dispose();
        Check(f.Native.ActiveRoots == 0, "Disposing the service retained the independent graph after descriptor recovery.");
    }

    private static void UnboundRequest()
    {
        using var f = new Fixture();
        using var capture = f.Backend.Capture();
        GameplayStateException? failure = null;
        try { using var prepared = capture.Prepare(new(GameplayControlAction.SetSpeed, 23, "same-run", 1.25, GameplayControlValueUnit.PlaybackRateMultiplier)); }
        catch (GameplayStateException error) { failure = error; }
        Check(failure?.Code == ErrorCode.TargetExpired && f.Native.GameplayPitchWrites == 0 && f.Provider.Captures == 0,
            "The private native descriptor mapped an unbound request into provider authority.");
    }

    private static void ForgedNativeGeneration()
    {
        using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
        var request = Speed(lease, 1.25) with { ControllerGeneration = f.Provider.Descriptor.Binding.ControllerGeneration };
        Check(request.ControllerGeneration != lease.ControllerGeneration, "The fixture did not use independent generations.");
        Expired(lease.Execute(request));
        Check(f.Native.GameplayPitchWrites == 0 && f.Provider.Captures == 0 && f.Journal.Count == 0,
            "A caller-supplied native generation reached provider capture or mutation.");
        ConfirmSpeed(f, lease, 1.25);
    }

    private static void MismatchedDescriptor()
    {
        foreach (var wrongRun in new[] { true, false })
        {
            using var f = new Fixture(); var original = f.Provider.Descriptor;
            f.Provider.SkipDescriptorGraphVerification = true;
            f.Provider.Descriptor = original with { Binding = wrongRun
                ? original.Binding with { RunId = "another-run" }
                : original.Binding with { Player = original.Binding.Player + 8 } };
            // The test provider deliberately returns its malformed descriptor unchanged.
            // Only the rooted backend capture may map the service request to native authority.
            var lease = Need(f.Controls.Acquire(true));
            Expired(lease.Execute(Speed(lease, 1.25)));
            Check(f.Native.GameplayPitchWrites == 0 && f.Provider.Captures == 0 && f.Journal.Count == 0,
                "A mismatched native descriptor reached provider capture or mutation.");
            f.Provider.Descriptor = original; f.Provider.SkipDescriptorGraphVerification = false;
            var fresh = Need(f.Controls.Acquire(true)); ConfirmSpeed(f, fresh, 1.25);
        }
    }

    private static void ConfirmSpeed(Fixture fixture, IGameplayControlLease lease, double rate)
    {
        var request = Speed(lease, rate); var receipt = Need(lease.Execute(request));
        Check(receipt.Modification.Request == request && receipt.Modification.ControllerGeneration == lease.ControllerGeneration
            && receipt.Modification.ConfirmedValue == rate && receipt.Modification.TransactionState == GameplayControlTransactionState.Confirmed
            && fixture.Native.GameplayPitch == (float)rate && fixture.Provider.Clock.Capture().Rate == (float)rate
            && fixture.Provider.LastOrigin!.ControllerGeneration == fixture.Provider.Descriptor.Binding.ControllerGeneration,
            "Native generation translation lost the original SDK request/receipt or actual audio/clock confirmation.");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly MainThreadDispatcher Main = new();
        internal readonly Provider Provider; internal readonly GameplayNativeBackend Backend; internal readonly GameplayService Service;
        internal readonly Context Context; internal readonly IGameplayControlService Controls; internal readonly IGameplayClockService Clocks;
        internal readonly List<RunModificationInput> Journal = new();
        internal GameLifecycleSnapshot Lifecycle = new("source-generation-session", 1, 7, "NormalMode", "same-run", "chart",
            RunLifecyclePhase.Paused, true, true, true, true, Array.Empty<OperationError>());
        internal Fixture()
        {
            Native.ConfigurePlaybackOccurrence(); Provider = new(this); Backend = new(Native.GameplayApi, Main, Provider);
            Service = new(Backend, Main, () => Lifecycle, Journal.Add, matchesCurrentGameplayRun: (information, controller, run, scene) =>
                information == Native.GameplayInformation.ToInt64() && controller == Native.GameplayController.ToInt64()
                && run == Lifecycle.RunId && scene == Lifecycle.SceneGeneration);
            Context = new(Main); Controls = Service.ControlsForPlugin(Context); Clocks = Service.ClockForPlugin(Context);
        }
        private long _hostTick;
        internal void SampleControls() => Service.ObserveControlsAfterMaintenance(++_hostTick, _hostTick, requested: true);
        internal Result<GameplayControlSnapshot> SampledControlState()
        {
            SampleControls();
            return Controls.GetState();
        }
        public void Dispose()
        {
            Context.Dispose(); Service.Dispose(); Main.Dispose();
            Check(Native.ActiveRoots == 0 && Native.AllGameplayReceiversRooted,
                "Native backend source/capture roots leaked or an actual receiver was unrooted.");
            Native.Dispose();
        }
    }

    // Deliberately controlled metadata/native-export fixture provider. It supplies
    // no stock/native handoff proof; production Router admission is verified separately.
    private sealed class Provider : INativePlaybackRateProvider, INativePlaybackRateSourceDescriptorProvider
    {
        private readonly Fixture _fixture;
        internal NativePlaybackRateSourceDescriptor Descriptor = null!;
        internal NativePlaybackClock Clock = null!;
        internal NativePlaybackRateOrigin? LastOrigin;
        internal bool Ready = true;
        internal int Captures;
        internal Action? OnCapture;
        internal Exception? DescriptorFailure;
        internal bool SkipDescriptorGraphVerification;
        internal Provider(Fixture fixture) { _fixture = fixture; ReplaceSource(nativeControllerGeneration: 23); }
        internal void ReplaceSource(bool advanceEpoch = true, long? nativeControllerGeneration = null)
        {
            var n = _fixture.Native;
            var binding = new NativePlaybackBinding(_fixture.Lifecycle.RunId!, _fixture.Lifecycle.SceneGeneration,
                nativeControllerGeneration ?? Descriptor.Binding.ControllerGeneration,
                Descriptor is null ? 101 : Descriptor.Binding.SourceGeneration + (advanceEpoch ? 1 : 0),
                n.GameplayController.ToInt64(), n.GameplayInformation.ToInt64(), n.GameplayProgress.ToInt64(),
                n.GameplayPlayer.ToInt64(), n.GameplaySource.ToInt64(), n.GameplayClip.ToInt64(), n.GameplayGetTimeMethod.ToInt64());
            Descriptor = new(binding, n.GameplayPause.ToInt64());
            Clock = new(binding, n.GameplayDsp, n.GameplayPosition, n.GameplayPitch, n.GameplayLength);
        }
        public NativePlaybackRateProviderProbe Probe() => new(true, "Controlled source-generation fixture; no production admission.");
        public NativePlaybackRateSourceDescriptor? CaptureSourceDescriptor()
        {
            if (DescriptorFailure is not null) throw DescriptorFailure;
            if (!Ready) return null;
            if (SkipDescriptorGraphVerification) return Descriptor;
            using var actual = new NativePlaybackActualSource(_fixture.Native.GameplayApi, Descriptor.Binding, Descriptor.Pause);
            actual.VerifyGraph(); return Descriptor;
        }
        public INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin)
        {
            ++Captures; LastOrigin = origin;
            var binding = Descriptor.Binding;
            Check(origin == new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration,
                binding.Controller, binding.Information, binding.Progress, Descriptor.Pause),
                "The service generation was passed to the native provider as native authority.");
            var source = new Source(this); OnCapture?.Invoke(); return source;
        }
        private sealed class Source : INativePlaybackRateSource
        {
            private readonly Provider _owner; private readonly NativePlaybackRateSourceDescriptor _descriptor;
            private readonly NativePlaybackActualSource _actual; private bool _disposed;
            public NativePlaybackBinding Binding => _descriptor.Binding;
            public NativePlaybackClock Clock { get; }
            internal Source(Provider owner)
            {
                _owner = owner; _descriptor = owner.Descriptor; Clock = owner.Clock;
                _actual = new(owner._fixture.Native.GameplayApi, Binding, _descriptor.Pause);
            }
            public void VerifyAuthority()
            {
                if (_disposed || !_owner.Ready || _owner.Descriptor != _descriptor || !ReferenceEquals(_owner.Clock, Clock))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The controlled source was replaced.");
                _actual.VerifyGraph();
            }
            public NativePlaybackRateSample Read() { VerifyAuthority(); return _actual.Read(1, 1); }
            public INativePlaybackPitchWrite PreparePitchWrite() { VerifyAuthority(); return new PitchWrite(this); }
            public void Dispose() { if (_disposed) return; _actual.Dispose(); _disposed = true; }
            private sealed class PitchWrite : INativePlaybackPitchWrite
            {
                private readonly Source _source; private long _revision; private bool _disposed;
                internal PitchWrite(Source source) { _source = source; _revision = source._owner._fixture.Native.GameplayPitchRevision; }
                public void VerifyCurrent()
                {
                    _source.VerifyAuthority();
                    if (_disposed || _revision != _source._owner._fixture.Native.GameplayPitchRevision)
                        throw new GameplayStateException(ErrorCode.TargetExpired, "The controlled fixture writer changed.");
                }
                public void Write(float pitch)
                {
                    VerifyCurrent(); _revision = checked(_revision + 1); _source._actual.InvokePitch(pitch);
                }
                public void Dispose() => _disposed = true;
            }
        }
    }

    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Context : IPluginContext, IDisposable
    {
        private readonly List<IDisposable> _resources = new();
        internal Context(IMainThreadDispatcher main) => MainThread = main;
        public string PluginId => "source-generation-plugin";
        public ILogger Log { get; } = new Logger(); public IMainThreadDispatcher MainThread { get; }
        public IEventBus Events => throw new NotSupportedException();
        public GameBuildDescriptor GameBuild => new("fixture", "fixture", "fixture", "fixture");
        public CapabilityDescriptor GetCapability(string id) => throw new NotSupportedException();
        public ServiceLookup<T> GetService<T>(string id) where T : class => throw new NotSupportedException();
        public T Own<T>(T resource) where T : IDisposable { _resources.Add(resource); return resource; }
        public void Dispose() { foreach (var resource in _resources.AsEnumerable().Reverse()) resource.Dispose(); _resources.Clear(); }
    }
}
