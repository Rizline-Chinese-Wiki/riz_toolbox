using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class ObservedClockChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("observed clock warms from three actual non-unit-rate samples", Warmup),
        ("observed clock converts explicit progress DSP and host domains in both directions", Conversions),
        ("observed clock only converts inside the latest three actual sample endpoints", LatestWindow),
        ("observed conversion freshly rejects changed subgraph provenance offset delay and progress without adding samples", FreshValidation),
        ("exact run binding rejects stale lifecycle identities then admits the caught-up same run", RunBinding),
        ("exact run binding rechecks read preparation and returned command association", RunBindingBoundaries),
        ("exact run binding rejects lost or throwing validation at the clock publication boundary", RunBindingPublication),
        ("observed clock refuses extrapolation and malformed domains before capture", RequestBounds),
        ("observed clock clamps request age to segment policy and never revives expiry", Expiry),
        ("observed clock starts new segments across pause and resume", PauseResume),
        ("observed clock run and either controller identity invalidate old requests", Identity),
        ("observed clock missing and invalid-unit samples preserve independent readings", MissingSamples),
        ("observed clock gaps and nonpositive progress restart warmup", Discontinuities),
        ("observed clock jump restarts without inventing a gameplay control", Jump),
        ("observed clock small measured rate changes retain actual residual uncertainty", Residual),
        ("observed clock policy changes require new samples and cannot reinterpret a segment", Policy),
        ("observed conversion getter reentry and run changes cannot return an old segment", Reentry),
        ("observed conversion cancellation and wrong thread precede capture", Guards),
        ("observed clock host regression and nonfinite captures invalidate the segment", HostFailure)
    };
    private static void Check(bool v, string reason) { if (!v) throw new InvalidOperationException(reason); }
    private static T Need<T>(Result<T> r) { Check(r.Success && r.Value is not null, r.Error?.Message ?? "Missing result."); return r.Value!; }
    private static void Error<T>(Result<T> r, ErrorCode code) => Check(!r.Success && r.Error?.KnownCode == code, "Expected " + code + ": " + r.Error);
    private static void Near(double? actual, double expected) => Check(actual is double n && Math.Abs(n - expected) < 1e-8, $"Expected {expected:R}; got {actual:R}.");
    private static GameplayClockSnapshot Sample(ClockFixture f, double host, double game, double dsp, GameplayClockObservationPolicy? policy = null)
    { f.Host = host; f.Backend.Game = game; f.Backend.Dsp = dsp; return Need(f.Clock.Read(observationPolicy: policy)); }
    private static GameplayClockSnapshot Ready(ClockFixture f, GameplayClockObservationPolicy? policy = null)
    { Sample(f, 10, 4, 200, policy); Sample(f, 10.1, 4.2, 200.05, policy); return Sample(f, 10.2, 4.4, 200.1, policy); }
    private static GameplayObservedClockConversionRequest Request(GameplayClockSnapshot s, double value = 4.1,
        GameplayClockDomain from = GameplayClockDomain.GameProgressSeconds, GameplayClockDomain to = GameplayClockDomain.AudioDspSeconds)
        => new(s.ControllerGeneration, s.ObservedRunId!, s.ObservedInterval!.SegmentId, value, from, to);

    private static void Warmup()
    {
        using var f = new ClockFixture();
        var a = Sample(f, 10, 4, 200); var b = Sample(f, 10.1, 4.2, 200.05);
        Check(a.ObservedInterval?.State == GameplayClockObservationState.WarmingUp && b.ObservedInterval?.Samples.Count == 2
            && b.Mapping!.GameSecondsPerDspSecond is null, "A single capture or pair invented a usable calibration.");
        var c = Sample(f, 10.2, 4.4, 200.1); var model = c.ObservedInterval!;
        Check(model.State == GameplayClockObservationState.ObservedInterval && model.Samples.Count == 3
            && ReferenceEquals(c.Mapping!.ObservedInterval, model), "The actual interval was not attached to the clock and mapping.");
        Near(model.GameSecondsPerDspSecond, 4); Near(model.GameSecondsPerHostSecond, 2);
        Near(model.GameDspOffsetSeconds, -796); Near(model.GameHostOffsetSeconds, -16.002);
        Check(model.CaptureSkewSeconds > .0059 && model.EstimatedGameUncertaintySeconds > 0
            && c.Mapping!.LatencySeconds is null && !c.Mapping.RuntimeVerified && !c.SecondsMappingVerified, "Observed sample fit was promoted to latency or production calibration.");
        Sample(f, 10.3, 4.6, 200.15);
        Check(a.ObservedInterval!.Samples.Count == 1 && model.Samples.Count == 3 && model.Samples[0].Revision == a.Revision,
            "A later read mutated a previously returned observed window.");
    }
    private static void Conversions()
    {
        using var f = new ClockFixture(); var s = Ready(f); var reads = f.Backend.DspReads; var mappings = f.Backend.MappingReads;
        foreach (var x in new[]
        {
            (GameplayClockDomain.GameProgressSeconds, 4.1, GameplayClockDomain.AudioDspSeconds, 200.025),
            (GameplayClockDomain.AudioDspSeconds, 200.075, GameplayClockDomain.GameProgressSeconds, 4.3),
            (GameplayClockDomain.GameProgressSeconds, 4.1, GameplayClockDomain.HostMonotonicSeconds, 10.051),
            (GameplayClockDomain.HostMonotonicSeconds, 10.151, GameplayClockDomain.GameProgressSeconds, 4.3),
            (GameplayClockDomain.AudioDspSeconds, 200.05, GameplayClockDomain.HostMonotonicSeconds, 10.101),
            (GameplayClockDomain.HostMonotonicSeconds, 10.101, GameplayClockDomain.AudioDspSeconds, 200.05)
        })
        {
            var result = Need(f.Clock.ConvertObserved(Request(s, x.Item2, x.Item1, x.Item3)));
            Near(result.ConvertedValue, x.Item4); Check(result.EstimatedUncertaintySeconds > 0 && !result.RuntimeVerified, "Conversion lost uncertainty/scope.");
        }
        Near(Need(f.Clock.ConvertObserved(Request(s, 4))).ConvertedValue, 200);
        Near(Need(f.Clock.ConvertObserved(Request(s, 4.4))).ConvertedValue, 200.1);
        Check(f.Backend.DspReads == reads && f.Backend.MappingReads == mappings + 8 && f.Modifications.Count == 0
            && s.ObservedInterval!.Samples[^1].Revision == s.Revision,
            "Observed conversion did not validate current mapping, produced DSP/history samples, or dispatched a control.");
        var next = Sample(f, 10.3, 4.6, 200.15);
        Check(next.Revision == s.Revision + 1 && next.ObservedInterval!.Samples[0].Revision == s.ObservedInterval!.Samples[1].Revision,
            "Conversion secretly advanced read revisions or the observed history.");
    }
    private static void RequestBounds()
    {
        using var f = new ClockFixture(); var s = Ready(f); var request = Request(s); var captures = f.Backend.Captures;
        foreach (var malformed in new[] { request with { Value = double.NaN }, request with { SourceDomain = GameplayClockDomain.ChartCoordinates },
            request with { TargetDomain = GameplayClockDomain.Unknown }, request with { MaxAgeSeconds = double.PositiveInfinity }, request with { MaxAgeSeconds = 0 },
            request with { ControllerGeneration = 0 }, request with { SegmentId = 0 }, request with { ObservedRunId = "" } })
            Error(f.Clock.ConvertObserved(malformed), ErrorCode.InvalidArgument);
        Check(captures == f.Backend.Captures, "Malformed observed conversion touched the backend.");
        foreach (var r in new[] { request with { Value = 3.999 }, request with { Value = 4.401 },
            Request(s, 199.999, GameplayClockDomain.AudioDspSeconds), Request(s, 200.101, GameplayClockDomain.AudioDspSeconds),
            Request(s, 10, GameplayClockDomain.HostMonotonicSeconds), Request(s, 10.202, GameplayClockDomain.HostMonotonicSeconds) })
            Error(f.Clock.ConvertObserved(r), ErrorCode.InvalidArgument);
    }
    private static void FreshValidation()
    {
        foreach (var (change, expected) in new (Action<ClockFixture>, ErrorCode)[]
        {
            (f => f.Backend.Offset += .1, ErrorCode.TargetExpired), (f => f.Backend.Delay += .1, ErrorCode.TargetExpired),
            (f => f.Backend.ProgressSource = "replacement source", ErrorCode.TargetExpired),
            (f => f.Backend.Fingerprint = "replacement build", ErrorCode.TargetExpired),
            (f => f.Backend.ProgressIdentity++, ErrorCode.TargetExpired), (f => f.Backend.ChartIdentity++, ErrorCode.TargetExpired),
            (f => f.Backend.PauseIdentity++, ErrorCode.TargetExpired),
            (f => f.Backend.Game = 4.1, ErrorCode.TargetExpired), (f => f.Backend.Game += 10, ErrorCode.TargetExpired),
            (f => f.Backend.ProgressUnit = GameplayClockUnit.ChartCoordinates, ErrorCode.ObservationUnavailable),
            (f => f.Backend.Game = double.NaN, ErrorCode.ObservationUnavailable),
            (f => f.Backend.MissingProgress = true, ErrorCode.ObservationUnavailable),
            (f => f.Backend.MappingMissing = true, ErrorCode.ObservationUnavailable)
        })
        {
            using var f = new ClockFixture(); var before = Ready(f); var dspReads = f.Backend.DspReads; change(f);
            Error(f.Clock.ConvertObserved(Request(before)), expected);
            Check(f.Backend.DspReads == dspReads && before.ObservedInterval!.Samples.Count == 3 && f.Modifications.Count == 0,
                "Fresh source validation produced a DSP/history sample, mutated the published interval, or dispatched a control.");
            var restarted = Sample(f, 10.3, 4.6, 200.15);
            Check(restarted.ObservedInterval!.SegmentId != before.ObservedInterval!.SegmentId
                && restarted.ObservedInterval.Samples.Count <= 1, "A validation failure allowed the previous samples to be revived.");
        }
    }
    private static void RunBinding()
    {
        long information = 1, controller = 1, scene = 1; var run = "controlled-run";
        using var f = new Fixture(values: true, matchesCurrentGameplayRun: (i, c, r, s) => i == information && c == controller && r == run && s == scene);
        var before = Need(f.Clocks.Read()); var oldLease = Need(f.Controls.Acquire(true));
        f.Backend.Identity = 2; f.Backend.ControllerIdentity = 2;
        var stale = Need(f.Clocks.Read());
        Check(stale.ObservedRunId is null && stale.GameTime.Available && stale.AudioDspTime.Available
            && stale.ObservedInterval!.State == GameplayClockObservationState.Unavailable,
            "A newly captured graph inherited the sampler's stale run or erased independent readings.");
        Error(oldLease.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
        Error(f.Controls.Acquire(true), ErrorCode.TargetExpired);
        Error(f.Clocks.Convert(new(before.ControllerGeneration, before.ObservedRunId!, 4,
            GameplayClockUnit.Seconds, GameplayClockUnit.ChartCoordinates)), ErrorCode.TargetExpired);
        Check(f.Backend.Dispatches.Count == 0, "An unassociated graph authorized a control.");
        information = 2; controller = 2; scene = 2; run = "caught-up-run";
        f.Lifecycle = f.Lifecycle! with { RunId = run, SceneGeneration = scene, Revision = 2 };
        var current = Need(f.Clocks.Read()); var lease = Need(f.Controls.Acquire(true));
        var receipt = Need(lease.Execute(GameplayControlAction.Pause));
        Check(current.ControllerGeneration > stale.ControllerGeneration && current.ObservedRunId == run
            && receipt.Modification.ObservedRunId == run && f.Backend.Dispatches.Count == 1,
            "A caught-up exact same-run association did not reopen normally.");
    }
    private static void RunBindingBoundaries()
    {
        foreach (var boundary in new[] { "read", "prepare", "dispatch" })
        {
            var matches = true;
            using var f = new Fixture(values: true, matchesCurrentGameplayRun: (_, _, _, _) => matches);
            var before = Need(f.Clocks.Read()); var lease = Need(f.Controls.Acquire(true));
            if (boundary == "read")
            {
                f.Backend.OnReadMapping = () => matches = false;
                var changed = Need(f.Clocks.Read());
                Check(changed.ObservedRunId is null && changed.Mapping is null && changed.AudioDspTime.Available,
                    "A read published an association that ended inside mapping capture.");
            }
            else if (boundary == "prepare")
            {
                f.Backend.OnPrepare = () => matches = false; f.Backend.Playing = false; f.Backend.Paused = true;
                Error(lease.Execute(new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId,
                    1.5, GameplayControlValueUnit.PlaybackRateMultiplier)), ErrorCode.TargetExpired);
                Check(f.Backend.ValueCommits == 0, "An association changed during preparation but still committed.");
            }
            else
            {
                f.Backend.OnDispatch = () => matches = false;
                Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
                Check(f.Backend.Dispatches.Count == 1 && Need(f.Clocks.Read()).ObservedRunId is null,
                    "A command returning after association loss issued a bound receipt or retried dispatch.");
            }
        }
    }
    private static void RunBindingPublication()
    {
        foreach (var throws in new[] { false, true })
        {
            var valid = true; var hostReads = 0;
            using var f = new Fixture(values: true, monotonicSeconds: () =>
                { if (++hostReads == 2) valid = false; return hostReads * .001; },
                matchesCurrentGameplayRun: (_, _, _, _) => valid ? true
                    : throws ? throw new InvalidOperationException("sampler unavailable") : false);
            var sample = Need(f.Clocks.Read());
            Check(sample.ObservedRunId is null && !sample.GameTime.Available && sample.Mapping is null
                && sample.AudioDspTime.Available && sample.HostMonotonicTime.Available,
                "A run association lost at publication escaped as a bound sample, exception, or lost independent source.");
        }
    }
    private static void LatestWindow()
    {
        using var f = new ClockFixture(); var first = Ready(f); var old = first.ObservedInterval!;
        var current = Sample(f, 10.3, 4.6, 200.15); var interval = current.ObservedInterval!;
        Check(interval.SegmentId == old.SegmentId && interval.Samples.Count == 3
            && interval.Samples[0].Revision == old.Samples[1].Revision && interval.Samples[^1].Revision == current.Revision,
            "The interpolation window did not advance with the actual read revisions.");
        Error(f.Clock.ConvertObserved(Request(first, 4.1)), ErrorCode.InvalidArgument);
        var conversion = Need(f.Clock.ConvertObserved(Request(first, 4.5)));
        Near(conversion.ConvertedValue, 200.125);
        Check(conversion.Interval.Samples[^1].Revision == current.Revision && old.Samples[0].GameProgressSeconds == 4,
            "Conversion retained stale endpoints or a later sample changed an earlier snapshot.");
    }
    private static void Expiry()
    {
        using var f = new ClockFixture(); var s = Ready(f); var request = Request(s);
        f.Host += .1; Error(f.Clock.ConvertObserved(request with { MaxAgeSeconds = .05 }), ErrorCode.ObservationUnavailable);
        Need(f.Clock.ConvertObserved(request with { MaxAgeSeconds = 100 }));
        f.Host = s.CaptureFinishedMonotonicSeconds + .50001;
        Error(f.Clock.ConvertObserved(request with { MaxAgeSeconds = 100 }), ErrorCode.ObservationUnavailable);
        Error(f.Clock.ConvertObserved(request with { MaxAgeSeconds = 1000 }), ErrorCode.ObservationUnavailable);
        var fresh = Sample(f, 11, 6, 200.5, new(MaxSampleGapSeconds: 2, MaxAgeSeconds: 5));
        Check(fresh.ObservedInterval!.State == GameplayClockObservationState.WarmingUp && fresh.ObservedInterval.Samples.Count == 1
            && fresh.ObservedInterval.SegmentId != s.ObservedInterval!.SegmentId, "A larger new policy revived old samples.");
    }
    private static void PauseResume()
    {
        using var f = new ClockFixture(); var s = Ready(f); f.Backend.Playing = false; f.Backend.Paused = true;
        Error(f.Clock.ConvertObserved(Request(s)), ErrorCode.TargetExpired);
        Check(Sample(f, 10.3, 4.4, 200.15).ObservedInterval!.State == GameplayClockObservationState.Unavailable, "Paused game time became invertible.");
        f.Backend.Playing = true; f.Backend.Paused = false;
        var resumed = Sample(f, 10.4, 4.6, 200.2);
        Check(resumed.ObservedInterval!.State == GameplayClockObservationState.WarmingUp && resumed.ObservedInterval.Samples.Count == 1,
            "Resume reused a pre-pause slope.");
    }
    private static void Identity()
    {
        foreach (var change in new Action<ClockFixture>[] { f => f.Run = "next-run", f => f.Backend.ControllerIdentity++, f => f.Backend.Identity++ })
        {
            using var f = new ClockFixture(); var s = Ready(f); change(f);
            Error(f.Clock.ConvertObserved(Request(s)), ErrorCode.TargetExpired);
            var next = Sample(f, 10.3, 4.6, 200.15);
            Check(next.ObservedInterval!.Samples.Count == 1 && next.ObservedInterval.SegmentId != s.ObservedInterval!.SegmentId, "A replaced target retained its old window.");
        }
    }
    private static void MissingSamples()
    {
        foreach (var change in new Action<ClockFixture>[] { f => f.Backend.MissingDsp = true, f => f.Backend.DspUnit = GameplayClockUnit.RawGameUnits,
            f => f.Backend.MissingProgress = true, f => f.Backend.ProgressUnit = GameplayClockUnit.ChartCoordinates, f => f.Backend.MappingMissing = true,
            f => f.Backend.Dsp = double.NaN, f => f.Backend.Dsp = double.PositiveInfinity,
            f => f.Backend.Game = double.NaN, f => f.Backend.Game = double.NegativeInfinity })
        {
            using var f = new ClockFixture(); var old = Ready(f); f.Host = 10.3; f.Backend.Game = 4.6; f.Backend.Dsp = 200.15;
            change(f); var sample = Need(f.Clock.Read());
            Check(sample.ObservedInterval!.State == GameplayClockObservationState.Unavailable && sample.Mapping?.GameSecondsPerHostSecond is null
                && sample.HostMonotonicTime.Available && (double.IsFinite(f.Backend.Game) == sample.GameTime.Available),
                "An invalid correlation erased independent readings or reused a slope.");
            Error(f.Clock.ConvertObserved(Request(old)), ErrorCode.ObservationUnavailable);
        }
    }
    private static void Discontinuities()
    {
        foreach (var next in new[] { (11d, 4.6, 200.15), (10.3, 3.9, 200.15), (10.3, 4.4, 200.15), (10.3, 4.6, 200.1), (10.3, 4.6, 199.9) })
        {
            using var f = new ClockFixture(); var previous = Ready(f); var sample = Sample(f, next.Item1, next.Item2, next.Item3);
            Check(sample.ObservedInterval!.State == GameplayClockObservationState.WarmingUp && sample.ObservedInterval.Samples.Count == 1
                && sample.ObservedInterval.SegmentId != previous.ObservedInterval!.SegmentId, "A gap, stalled or negative clock retained its old model.");
        }
    }
    private static void Jump()
    {
        using var f = new ClockFixture(); var old = Ready(f); var jump = Sample(f, 10.3, 14.6, 200.15);
        Check(jump.ObservedInterval!.State == GameplayClockObservationState.WarmingUp && jump.ObservedInterval.Samples.Count == 1, "A positive discontinuity became a huge playback-rate claim.");
        Sample(f, 10.4, 14.8, 200.2); var next = Sample(f, 10.5, 15, 200.25);
        Check(next.ObservedInterval!.State == GameplayClockObservationState.ObservedInterval && next.ObservedInterval.SegmentId != old.ObservedInterval!.SegmentId
            && f.Modifications.Count == 0, "A new observed interval invented a seek/speed control audit.");
        Near(next.Mapping!.GameSecondsPerHostSecond, 2);
    }
    private static void Residual()
    {
        using var f = new ClockFixture(); var before = Ready(f); var sample = Sample(f, 10.3, 4.601, 200.15); var interval = sample.ObservedInterval!;
        Check(interval.State == GameplayClockObservationState.ObservedInterval && interval.SegmentId == before.ObservedInterval!.SegmentId
            && interval.ObservedResidualGameSeconds > 0 && interval.EstimatedGameUncertaintySeconds >= interval.ObservedResidualGameSeconds
            && interval.GameSecondsPerHostSecond != before.ObservedInterval.GameSecondsPerHostSecond && f.Modifications.Count == 0,
            "A small actual rate change was erased or classified as an unknown control.");
    }
    private static void Policy()
    {
        using var f = new ClockFixture(); var before = Ready(f); var captures = f.Backend.Captures;
        Error(f.Clock.Read(observationPolicy: new(MaxAgeSeconds: double.NaN)), ErrorCode.InvalidArgument);
        Check(f.Backend.Captures == captures, "Invalid observation policy captured the backend.");
        var changed = Sample(f, 10.3, 4.6, 200.15, new(AbsoluteResidualToleranceSeconds: .1));
        Check(changed.ObservedInterval!.Samples.Count == 1 && changed.ObservedInterval.SegmentId != before.ObservedInterval!.SegmentId,
            "A policy change reinterpreted previously signed samples.");
        Error(f.Clock.ConvertObserved(Request(before)), ErrorCode.ObservationUnavailable);
    }
    private static void Reentry()
    {
        using var f = new ClockFixture(); var before = Ready(f); var dspReads = f.Backend.DspReads;
        f.Backend.OnPlayingRead = () =>
        {
            f.Backend.OnPlayingRead = null; var captures = f.Backend.Captures;
            Error(f.Clock.Read(), ErrorCode.Conflict); Error(f.Clock.ConvertObserved(Request(before)), ErrorCode.Conflict);
            Check(f.Backend.Captures == captures, "Getter reentry replaced active capture roots."); f.Run = "during-getter";
        };
        Error(f.Clock.ConvertObserved(Request(before)), ErrorCode.TargetExpired);
        Check(f.Backend.DspReads == dspReads, "State validation secretly sampled clocks.");
    }
    private static void Guards()
    {
        using var f = new ClockFixture(); var s = Ready(f); var captures = f.Backend.Captures; using var stop = new CancellationTokenSource(); stop.Cancel();
        Error(f.Clock.ConvertObserved(Request(s), stop.Token), ErrorCode.Cancelled);
        Result<GameplayObservedClockConversion>? result = null; var thread = new Thread(() => result = f.Clock.ConvertObserved(Request(s))); thread.Start(); thread.Join();
        Error(result!, ErrorCode.WrongThread); Check(f.Backend.Captures == captures, "A refused observed conversion touched the target.");
    }
    private static void HostFailure()
    {
        foreach (var host in new[] { 9d, double.NaN, double.PositiveInfinity })
        {
            using var f = new ClockFixture(); var before = Ready(f); var sample = Sample(f, host, 4.6, 200.15);
            Check(sample.ObservedInterval!.State == GameplayClockObservationState.Unavailable, "An invalid host interval retained a model.");
            Error(f.Clock.ConvertObserved(Request(before)), ErrorCode.ObservationUnavailable);
        }
    }
}

internal sealed class ClockFixture : IDisposable
{
    internal readonly MainThreadDispatcher Main = new();
    internal readonly Context Context;
    internal readonly ObservedBackend Backend;
    internal readonly GameplayService Service;
    internal readonly IGameplayClockService Clock;
    internal readonly List<RunModificationInput> Modifications = new();
    internal double Host;
    internal string Run = "observed-run";
    internal ClockFixture()
    {
        Context = new("clock-plugin", Main); Backend = new(this);
        Service = new(Backend, Main, () => new("clock-session", 1, 1, "NormalMode", Run, "chart", RunLifecyclePhase.Playing,
            false, true, true, true, Array.Empty<OperationError>()), Modifications.Add, () => Host);
        Clock = Service.ClockForPlugin(Context);
    }
    public void Dispose() { Context.Dispose(); Service.Dispose(); Main.Dispose(); }
}
internal sealed class ObservedBackend : IGameplayBackend
{
    private readonly ClockFixture _fixture;
    internal double Game, Dsp, CaptureSeconds = .002;
    internal long Identity = 1, ControllerIdentity = 1, ProgressIdentity = 2, ChartIdentity = 3, PauseIdentity = 4;
    internal double Offset = .2, Delay = .35;
    internal string ProgressSource = "controlled progress", Fingerprint = "controlled build";
    internal bool Playing = true, Paused, MissingDsp, MissingProgress, MappingMissing;
    internal GameplayClockUnit DspUnit = GameplayClockUnit.Seconds, ProgressUnit = GameplayClockUnit.Seconds;
    internal int Captures, DspReads, MappingReads;
    internal Action? OnPlayingRead;
    internal ObservedBackend(ClockFixture fixture) => _fixture = fixture;
    public GameplayProbe Probe() => new(Array.Empty<GameplayControlAvailability>(), new Dictionary<string, string?>());
    public GameplayCapture Capture() { Captures++; return new CaptureValue(this); }
    public GameplayClockReading ReadDsp()
    { DspReads++; _fixture.Host += CaptureSeconds; return new(!MissingDsp, MissingDsp ? null : Dsp, DspUnit, "controlled DSP"); }
    public void Dispose() { }
    private sealed class CaptureValue : GameplayCapture
    {
        private readonly ObservedBackend _owner;
        internal CaptureValue(ObservedBackend owner) => _owner = owner;
        internal override long Identity => _owner.Identity;
        internal override long ControllerIdentity => _owner.ControllerIdentity;
        internal override GameplayClockSourceIdentity? ClockSourceIdentity => new(_owner.ProgressIdentity, _owner.ChartIdentity, _owner.PauseIdentity);
        internal override bool LoadOver => true;
        internal override bool TimeReady => true;
        internal override bool Playing { get { _owner.OnPlayingRead?.Invoke(); return _owner.Playing; } }
        internal override bool Paused => _owner.Paused;
        internal override bool LevelOver => false;
        internal override bool Crashed => false;
        internal override bool CanDoOperation => true;
        internal override OperationError? PauseReadinessError => null;
        internal override GameplayClockReading ReadClock(string name) => new(true, _owner.Game, GameplayClockUnit.Seconds, name);
        internal override GameplayClockMappingSnapshot? ReadMapping(GameplayClockReading game)
        {
            _owner.MappingReads++;
            return _owner.MappingMissing ? null : new(new(true, _owner.Game, GameplayClockUnit.Seconds, "controlled converted chart"),
                new(!_owner.MissingProgress, _owner.MissingProgress ? null : _owner.Game, _owner.ProgressUnit, _owner.ProgressSource),
                new(true, _owner.Offset, GameplayClockUnit.Seconds, "controlled offset"), new(true, _owner.Delay, GameplayClockUnit.Seconds, "controlled chart delay"),
                _owner.Playing, _owner.Paused, "controlled actual samples") { BuildFingerprint = _owner.Fingerprint };
        }
        internal override void Dispatch(GameplayControlAction action) => throw new InvalidOperationException("Clock tests never dispatch controls.");
        internal override bool? Postcondition(GameplayControlAction action) => null;
        public override void Dispose() { }
    }
}
