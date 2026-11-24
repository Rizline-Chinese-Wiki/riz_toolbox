using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Sdk;

return TailWindowChecks.Run(args);

static class ManagedChecks
{
    private static int _checks;
    internal static int Checks => _checks;
    private static readonly List<string> Passed = new();
    internal static void Check(bool condition, string message)
    {
        ++_checks;
        if (!condition) throw new InvalidOperationException(message);
    }
    internal static T Need<T>(Result<T> result)
    {
        Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing successful value.");
        return result.Value!;
    }
    private static void Reject(Action action, ErrorCode? code = null)
    {
        try { action(); }
        catch (Exception error)
        {
            Check(code is null || error is GameplayStateException state && state.Code == code, "Unexpected read failure " + error);
            return;
        }
        Check(false, "A stale or unavailable playback read was accepted.");
    }
    internal static NativePlaybackBinding Binding(long source = 1)
        => new("managed-run", 1, 1, source, 101, 102, 103, 105, 106, 107, 108);
    private static (NativePlaybackClock Clock, Publication Publication) Clock(double media = 5, double rate = 2)
    {
        var publication = new Publication();
        var clock = new NativePlaybackClock(Binding(), 10, media, rate, 100, publication);
        publication.State = clock.Capture();
        return (clock, publication);
    }
    private static void Independent(GameplayClockSnapshot snapshot)
    {
        Check(snapshot.GameTime.Available && snapshot.GameRealTime.Available && snapshot.GameSyncedTime.Available,
            "Optional native availability invalidated the three game clocks.");
        Check(snapshot.AudioDspTime.Available && snapshot.HostMonotonicTime.Available, "Independent DSP/host clocks were lost.");
        Check(snapshot.ObservedRunId == "managed-run", "An optional native failure changed the observed run.");
    }
    private static void Unavailable(GameplayClockSnapshot snapshot)
        => Check(!snapshot.Playback.Available && snapshot.Playback.Sample is null && snapshot.Playback.Error is not null,
            "An unavailable playback reading exposed a stale sample or omitted its error.");

    internal static int Run()
    {
        var scenarios = new (string Name, Action Body)[]
        {
            ("paused and playing Single media domains", () => {
                var (clock, publication) = Clock(); var state = clock.Capture();
                Check(clock.ReadPublished(state, 20).MediaSeconds == 5, "Paused media advanced with DSP.");
                Check(ReferenceEquals(clock.Capture(), state) && publication.Publishes == 0 && publication.Withdrawals == 0,
                    "A readonly sample wrote or withdrew policy.");
                clock.ObservePlaying(Binding(), 10, 5, 2); state = clock.Capture();
                Check(clock.ReadPublished(state, 12).MediaSeconds == 9, "Playing media failed its anchored rate.");
                Check(clock.ReadPublished(state, 100).MediaSeconds == 100, "Existing clip upper clamp changed.");
            }),
            ("scheduled delay stays unscaled before the start", () => {
                var (clock, _) = Clock(); clock.ObserveScheduled(Binding(), 10, 20, 5, 2); var state = clock.Capture();
                Check(clock.ReadPublished(state, 18).MediaSeconds == 3, "Prestart delay was rate scaled.");
                Check(clock.ReadPublished(state, 20).MediaSeconds == 5, "Schedule boundary changed media anchor.");
                Check(clock.ReadPublished(state, 22).MediaSeconds == 9, "Poststart media was not rate scaled.");
                var (negative, _) = Clock(0); negative.ObserveScheduled(Binding(), 10, 20, 0, 2);
                Check(negative.ReadPublished(negative.Capture(), 18).MediaSeconds == -2, "Legal negative prestart media was clamped away.");
            }),
            ("SDK arithmetic does not consume the callback DSP watermark", () => {
                var (clock, publication) = Clock(); var state = clock.Capture();
                _ = clock.ReadPublished(state, 90);
                Check(clock.TryGetTime(Binding(), 11, out var value) && value == 5, "Readonly sampling consumed callback DSP authority.");
                Check(ReferenceEquals(state, clock.Capture()) && publication.Publishes == 0 && publication.Withdrawals == 0,
                    "Readonly sampling changed callback policy.");
            }),
            ("nonfinite negative and regressed DSP have no policy side effect", () => {
                var (clock, publication) = Clock(); var state = clock.Capture();
                foreach (var dsp in new[] { double.NaN, double.PositiveInfinity, -1d, 9d })
                    Reject(() => clock.ReadPublished(state, dsp), ErrorCode.ObservationUnavailable);
                Check(ReferenceEquals(state, clock.Capture()) && publication.Publishes == 0 && publication.Withdrawals == 0,
                    "Rejected readonly DSP mutated policy.");
            }),
            ("publication absence loss throw and equal-copy expected state reject", () => {
                var bare = new NativePlaybackClock(Binding(), 10, 5, 2, 100);
                Reject(() => bare.ReadPublished(bare.Capture(), 10), ErrorCode.NotReady);
                var (clock, publication) = Clock(); var state = clock.Capture();
                Reject(() => clock.ReadPublished(state with { }, 10), ErrorCode.TargetExpired);
                publication.Active = false;
                Reject(() => clock.ReadPublished(state, 10), ErrorCode.TargetExpired);
                publication.Active = true; publication.Throw = true;
                Reject(() => clock.ReadPublished(state, 10));
                Check(ReferenceEquals(state, clock.Capture()) && publication.Publishes == 0 && publication.Withdrawals == 0,
                    "An optional publication read failure wrote policy.");
            }),
            ("publication revision changes on either read fence reject", () => {
                foreach (var fence in new[] { 1, 2 })
                {
                    var (clock, publication) = Clock(); var state = clock.Capture();
                    publication.TriggerRead = publication.Reads + fence;
                    publication.OnRead = () => clock.ObservePlaying(Binding(), 11, 6, 2);
                    Reject(() => clock.ReadPublished(state, 12), ErrorCode.TargetExpired);
                    Check(clock.Capture().Revision == state.Revision + 1 && publication.Publishes == 1 && publication.Withdrawals == 0,
                        "The injected transition was hidden or the read added a write.");
                }
            }),
            ("formal two-plugin views expose current optional samples", () => {
                using var fixture = new Fixture();
                var first = Need(fixture.FirstClock.Read()); var second = Need(fixture.SecondClock.Read());
                Independent(first); Independent(second);
                Check(first.Playback.Available && first.Playback.Sample?.MediaSeconds == 5
                    && second.Playback.Sample?.ClockRevision == 1 && first.ControllerGeneration == second.ControllerGeneration,
                    "Scoped views lost current typed playback information.");
                fixture.Backend.Revision = 2; fixture.Backend.MediaSeconds = 7;
                var next = Need(fixture.FirstClock.Read());
                Check(next.Playback.Sample is { MediaSeconds: 7, ClockRevision: 2 },
                    "A new request reused an earlier playback value.");
            }),
            ("unconfigured reader is independently unavailable", () => {
                using var fixture = new Fixture(); fixture.Backend.ReaderAvailable = false;
                var snapshot = Need(fixture.FirstClock.Read()); Independent(snapshot); Unavailable(snapshot);
            }),
            ("reader exception and descriptor null preserve independent clocks and lease", () => {
                using var fixture = new Fixture(); var first = Need(fixture.FirstClock.Read());
                using var lease = Need(fixture.Controls.Acquire(true)); fixture.Backend.ThrowRead = true;
                var failed = Need(fixture.FirstClock.Read()); Independent(failed); Unavailable(failed);
                Check(failed.ControllerGeneration == first.ControllerGeneration, "Reader exception changed controller generation.");
                fixture.Backend.ThrowRead = false; fixture.Backend.DescriptorAvailable = false;
                var absent = Need(fixture.FirstClock.Read()); Independent(absent); Unavailable(absent);
                Check(absent.ControllerGeneration == first.ControllerGeneration, "Unknown native availability replaced the physical graph.");
                Check(lease.Execute(GameplayControlAction.Pause).Success && fixture.Backend.Dispatches == 1,
                    "An optional native read failure expired the independent generic control lease.");
            }),
            ("old descriptor null same descriptor retains generation", () => {
                using var fixture = new Fixture(); var initial = Need(fixture.FirstClock.Read());
                fixture.Backend.DescriptorAvailable = false; Unavailable(Need(fixture.FirstClock.Read()));
                fixture.Backend.DescriptorAvailable = true; var restored = Need(fixture.FirstClock.Read());
                Independent(restored);
                Check(restored.ControllerGeneration == initial.ControllerGeneration && restored.Playback.Available,
                    "A temporary descriptor gap invented a source successor.");
            }),
            ("initial null to new descriptor expires the old control generation", () => {
                using var fixture = new Fixture(); fixture.Backend.DescriptorAvailable = false;
                var initial = Need(fixture.FirstClock.Read()); using var lease = Need(fixture.Controls.Acquire(true));
                fixture.Backend.DescriptorAvailable = true; var adopted = Need(fixture.FirstClock.Read());
                Check(adopted.ControllerGeneration > initial.ControllerGeneration && adopted.Playback.Available,
                    "First concrete native adoption retained an earlier unknown-source generation.");
                Check(!lease.Execute(GameplayControlAction.Pause).Success && fixture.Backend.Dispatches == 0,
                    "An old source-unknown control lease crossed concrete adoption.");
            }),
            ("same addresses new native source session expires the old lease", () => {
                using var fixture = new Fixture(); var first = Need(fixture.FirstClock.Read());
                using var lease = Need(fixture.Controls.Acquire(true));
                fixture.Backend.Binding = fixture.Backend.Binding with { SourceGeneration = 2 };
                var successor = Need(fixture.FirstClock.Read());
                Check(successor.ControllerGeneration > first.ControllerGeneration && successor.Playback.Available,
                    "A same-address source successor reused the prior service generation.");
                Check(!lease.Execute(GameplayControlAction.Pause).Success && fixture.Backend.Dispatches == 0,
                    "An old lease crossed a same-address new session.");
            }),
            ("last monotonic getter revision change rejects only native sample", () => {
                using var fixture = new Fixture(); fixture.OnEndCapture = () => fixture.Backend.Revision++;
                var snapshot = Need(fixture.FirstClock.Read()); Independent(snapshot); Unavailable(snapshot);
                Check(fixture.Backend.VerifyCalls == 1, "Final exact-revision verification was skipped.");
            }),
            ("final native verification controller run or roots change invalidates mixed game samples", () => {
                foreach (var change in new[] { "controller", "run", "roots" })
                {
                    using var fixture = new Fixture();
                    fixture.Backend.OnVerify = () => {
                        if (change == "controller") fixture.Backend.Information++;
                        if (change == "run") fixture.Backend.RunId = "successor-run";
                        if (change == "roots") fixture.Backend.RootsAlive = false;
                    };
                    var snapshot = Need(fixture.FirstClock.Read()); Unavailable(snapshot);
                    Check(!snapshot.GameTime.Available && !snapshot.GameRealTime.Available && !snapshot.GameSyncedTime.Available
                        && snapshot.ObservedRunId is null && snapshot.AudioDspTime.Available,
                        "A graph change in final native verification published mixed prior game clocks.");
                }
            }),
            ("last SameCapture revision change is caught by local state validation", () => {
                using var fixture = new Fixture();
                fixture.Backend.OnVerify = () => fixture.Backend.OnGraph = () => fixture.Backend.Revision++;
                var snapshot = Need(fixture.FirstClock.Read()); Independent(snapshot); Unavailable(snapshot);
                Check(fixture.Backend.VerifyCalls == 1 && fixture.Backend.LocalChecks == 1,
                    "The final graph-stage revision change missed the local-only check.");
            }),
            ("final native-only loss preserves the independently verified graph", () => {
                using var fixture = new Fixture(); var first = Need(fixture.FirstClock.Read());
                using var lease = Need(fixture.Controls.Acquire(true));
                fixture.Backend.OnVerify = () => { fixture.Backend.DescriptorAvailable = false; fixture.Backend.ReaderAvailable = false; };
                var snapshot = Need(fixture.FirstClock.Read()); Independent(snapshot); Unavailable(snapshot);
                Check(snapshot.ControllerGeneration == first.ControllerGeneration && lease.Execute(GameplayControlAction.Pause).Success,
                    "A native-only final validation failure invalidated the generic game lease.");
            }),
            ("nonfinite payload has no stale value and capture cleanup drains", () => {
                var fixture = new Fixture(); fixture.Backend.MediaSeconds = double.NaN;
                var snapshot = Need(fixture.FirstClock.Read()); Independent(snapshot); Unavailable(snapshot);
                Check(fixture.Backend.ActiveCaptures == 1, "Current managed capture was not retained independently.");
                fixture.Dispose();
                Check(fixture.Backend.ActiveCaptures == 0 && fixture.Backend.Disposals == 1, "Managed capture cleanup did not drain.");
            })
        };
        try
        {
            foreach (var scenario in scenarios) { scenario.Body(); Passed.Add(scenario.Name); }
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, scenarioCount = Passed.Count, checks = _checks,
                scenarios = Passed, scope = "pure-managed-clock-and-gameplay-service", ownedNativeValidation = false,
                standardProductBuild = false, reflectionUsed = false, gameExecuted = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { passed = false, scenarioCount = Passed.Count, checks = _checks, scenarios = Passed }));
            return 1;
        }
    }
}

sealed class Publication : INativePlaybackClockPublication
{
    internal NativePlaybackClockState? State { get; set; }
    internal bool Active { get; set; } = true;
    internal bool Throw { get; set; }
    internal int Publishes { get; private set; }
    internal int Withdrawals { get; private set; }
    internal int Reads { get; private set; }
    internal int TriggerRead { get; set; }
    internal Action? OnRead { get; set; }
    public bool IsCurrent(NativePlaybackClockState state)
    {
        ++Reads;
        if (Reads == TriggerRead) { var action = OnRead; OnRead = null; action?.Invoke(); }
        if (Throw) throw new IOException("managed publication read failure");
        return Active && ReferenceEquals(State, state);
    }
    public void Publish(NativePlaybackClockState expected, NativePlaybackClockState next)
    {
        ManagedChecks.Check(ReferenceEquals(State, expected), "Test transition replaced an unexpected publication.");
        State = next; ++Publishes;
    }
    public void Withdraw(string reason) { ++Withdrawals; Active = false; }
}

sealed class Fixture : IDisposable
{
    internal readonly MainThreadDispatcher Main = new();
    internal readonly ManagedBackend Backend = new();
    internal readonly GameplayService Service;
    internal readonly PluginHost Host;
    internal readonly IGameplayClockService FirstClock, SecondClock;
    internal readonly IGameplayControlService Controls;
    internal Action? OnEndCapture { get; set; }
    private int _hostReads;
    private bool _disposed;
    internal Fixture()
    {
        Service = new(Backend, Main, () => new("managed-session", 1, 1, "NormalMode", Backend.RunId, "chart",
            RunLifecyclePhase.Playing, false, true, true, true, Array.Empty<OperationError>()), monotonicSeconds: () => {
                ++_hostReads;
                if ((_hostReads & 1) == 0) { var action = OnEndCapture; OnEndCapture = null; action?.Invoke(); }
                return _hostReads * .01;
            });
        var log = new Logger();
        var capabilities = new CapabilityRegistry(new("managed", "managed", "offline", "managed"));
        capabilities.RegisterScoped<IGameplayClockService>(GameplayCapabilityIds.Clock, Service.ClockForPlugin);
        capabilities.RegisterScoped<IGameplayControlService>(GameplayCapabilityIds.Controls, Service.ControlsForPlugin);
        Host = new(capabilities, Main, new EventHub(log), log, Path.Combine(AppContext.BaseDirectory, "plugin-data"));
        var first = new ProbePlugin("clock-one"); var second = new ProbePlugin("clock-two");
        ManagedChecks.Check(Host.Register(first).Success && Host.Register(second).Success
            && Host.Enable(first.Manifest.Id).Success && Host.Enable(second.Manifest.Id).Success, "Formal managed plugin scopes did not enable.");
        FirstClock = first.Context!.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
        SecondClock = second.Context!.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
        Controls = first.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        Host.Dispose(); Service.Dispose(); Main.Dispose();
    }
}

sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class ProbePlugin : IRizPlugin
{
    public PluginManifest Manifest { get; }
    internal IPluginContext? Context { get; private set; }
    internal ProbePlugin(string id) => Manifest = new(id, new(1, 0), new(1, 2),
        RequiredCapabilities: new[] { GameplayCapabilityIds.Clock, GameplayCapabilityIds.Controls });
    public void Enable(IPluginContext context) => Context = context;
    public void Disable() { }
}

// A controlled managed backend exercises service lifetime logic only. These data
// records never enter a native broker and issue no source or callback ticket.
sealed class ManagedBackend : IGameplayBackend
{
    internal NativePlaybackBinding Binding { get; set; } = ManagedChecks.Binding();
    internal long Information { get; set; } = 102;
    internal string RunId { get; set; } = "managed-run";
    internal long Revision { get; set; } = 1;
    internal double MediaSeconds { get; set; } = 5;
    internal bool DescriptorAvailable { get; set; } = true;
    internal bool ReaderAvailable { get; set; } = true;
    internal bool RootsAlive { get; set; } = true;
    internal bool ThrowRead { get; set; }
    internal bool Paused { get; set; }
    internal Action? OnVerify { get; set; }
    internal Action? OnGraph { get; set; }
    internal Action? OnDispatch { get; set; }
    internal int ActiveCaptures { get; set; }
    internal int Disposals { get; private set; }
    internal int Dispatches { get; set; }
    internal int VerifyCalls { get; set; }
    internal int LocalChecks { get; set; }
    internal NativePlaybackRateSourceDescriptor? Descriptor => DescriptorAvailable ? new(Binding, 104) : null;
    public GameplayProbe Probe() => new(Enum.GetValues<GameplayControlAction>().Select(action => new GameplayControlAvailability(
        action, action is GameplayControlAction.Pause or GameplayControlAction.Resume, "Managed backend operation.")).ToArray(),
        new Dictionary<string, string?>());
    public GameplayCapture Capture() => new CaptureValue(this);
    public GameplayClockReading ReadDsp() => new(true, 123, GameplayClockUnit.Seconds, "managed independent DSP");
    public void Dispose() => ++Disposals;

    private sealed class CaptureValue : GameplayCapture
    {
        private readonly ManagedBackend _owner;
        private readonly long _information;
        private readonly GameplaySourceIdentity _identity;
        private bool _disposed;
        private long _readRevision;
        private NativePlaybackBinding? _readBinding;
        internal CaptureValue(ManagedBackend owner)
        {
            _owner = owner; _information = owner.Information;
            _identity = new(103, 104, 105, 106, 107, owner.Descriptor); ++owner.ActiveCaptures;
        }
        internal override long Identity => _information;
        internal override long ControllerIdentity => 101;
        internal override GameplaySourceIdentity? SourceIdentity => _identity;
        internal override NativePlaybackRateSourceDescriptor? CurrentNativeSourceDescriptor => _owner.Descriptor;
        internal override bool CurrentGraphMatches
        {
            get
            {
                var action = _owner.OnGraph; _owner.OnGraph = null; action?.Invoke();
                return !_disposed && _owner.RootsAlive && _information == _owner.Information;
            }
        }
        internal override bool LoadOver => true;
        internal override bool TimeReady => true;
        internal override bool Playing => !_owner.Paused;
        internal override bool Paused => _owner.Paused;
        internal override bool LevelOver => false;
        internal override bool Crashed => false;
        internal override bool CanDoOperation => true;
        internal override OperationError? PauseReadinessError => null;
        internal override GameplayClockReading ReadClock(string field) => new(true, 12, GameplayClockUnit.Seconds, field);
        internal override GameplayPlaybackClockReading ReadPlaybackClock()
        {
            if (!_owner.ReaderAvailable) return base.ReadPlaybackClock();
            if (_owner.ThrowRead) throw new IOException("managed reader exception");
            if (_identity.NativeDescriptor is null) throw new GameplayStateException(ErrorCode.NotReady, "No descriptor in this actual capture.");
            _readRevision = _owner.Revision; _readBinding = _owner.Binding;
            return new(true, new(_readRevision, _owner.Paused ? GameplayPlaybackPhase.Paused : GameplayPlaybackPhase.Playing,
                20, _owner.MediaSeconds, 2), "Managed service sample only.");
        }
        internal override void VerifyPlaybackClock()
        {
            ++_owner.VerifyCalls;
            var action = _owner.OnVerify; _owner.OnVerify = null; action?.Invoke();
            if (!_owner.ReaderAvailable || !_owner.DescriptorAvailable || !_owner.RootsAlive || _information != _owner.Information
                || _readRevision != _owner.Revision || _readBinding != _owner.Binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Managed source or read state ended.");
        }
        internal override void VerifyPlaybackClockState()
        {
            ++_owner.LocalChecks;
            if (!_owner.ReaderAvailable || !_owner.RootsAlive || _readRevision != _owner.Revision || _readBinding != _owner.Binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Managed final local read state ended.");
        }
        internal override void Dispatch(GameplayControlAction action)
        {
            ++_owner.Dispatches;
            if (action == GameplayControlAction.Pause) _owner.Paused = true;
            if (action == GameplayControlAction.Resume) _owner.Paused = false;
            var callback = _owner.OnDispatch; _owner.OnDispatch = null; callback?.Invoke();
        }
        internal override bool? Postcondition(GameplayControlAction action) => action switch
        { GameplayControlAction.Pause => _owner.Paused, GameplayControlAction.Resume => !_owner.Paused, _ => null };
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true; --_owner.ActiveCaptures;
        }
    }
}
