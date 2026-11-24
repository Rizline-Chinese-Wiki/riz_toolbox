using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

internal static class NativePlaybackOccurrenceProviderChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("owned native provider requires its private completed Pause rather than current booleans", PrivatePause),
        ("owned native accessor reads actual graph DSP media pitch and nullable origin", ActualAccess),
        ("concrete owned native provider is consumed through formal backend and service", ServiceConsumption),
        ("owned native SetSpeed preserves the separate media and progress values from the recorded Pause", PausedRateWithHeldProgress),
        ("owned native Replay captures actual schedule argument and restores selected pitch", Replay),
        ("owned native Resume retains media and independently witnesses actual schedule argument", Resume),
        ("owned native Pause reanchors actual audio time after playing", PausePlaying),
        ("each owned native Pause occurrence invalidates older captured source despite equal booleans", EqualPause),
        ("native writer Before invalidates an equal-value service-external owned setter", EqualWriter),
        ("native writer source fences reject actual owned pitch ABA", WriterAba),
        ("owned native partial pitch setter failure retains attempt for exact restoration", PartialSetter),
        ("owned native schedule pitch-reapply failure withdraws synchronization", ReapplyFailure),
        ("owned native schedule requires actual stock pitch reset readback", ResetReadback),
        ("owned native PlayScheduled failure cannot publish a completed clock schedule", ScheduleFailure),
        ("owned native skipped Resume cannot fabricate a schedule occurrence", SkippedSchedule),
        ("stock origin is only a cross-check of the native PlayScheduled argument", OriginCrossCheck),
        ("owned native schedule graph replacement withdraws without successor writes", GraphChange),
        ("owned native source setter invalidates private source tickets even for same clip", EqualSource),
        ("owned native capture rejects another exact origin before writes", WrongOrigin),
        ("owned native producer refuses cross-thread source access", WrongThread),
        ("owned provider source worker Dispose preserves seven roots for exact owner-thread retry", SourceWorkerDispose),
        ("actual playback accessor worker Dispose preserves seven roots for exact owner-thread retry", AccessorWorkerDispose),
        ("owned native prepared clock race withdraws after actual schedule side effects", ScheduleDuringPreparation),
        ("owned native stop during setter retains roots until native return", DisposeDuringSetter),
        ("owned native accessor failure releases every independent constructor root", ConstructorFailure),
        ("controlled coverage explicitly cannot observe naked runtime equal-value or ABA writes", NakedWriterBoundary),
        ("owned native external setter requires actual pitch readback", ExternalReadback),
        ("owned native DSP callback regression withdraws before PlayScheduled", DspCallbackRegression),
        ("owned native source DSP regression withdraws the current clock", SourceDspRegression),
        ("default actual backend speed and seek stay closed without the controlled provider", DefaultBoundary)
    };

    private static string Library => Environment.GetEnvironmentVariable("RIZ_PLAYBACK_OWNED_FIXTURE")
        ?? throw new InvalidOperationException("RIZ_PLAYBACK_OWNED_FIXTURE must name the independently built offline native owned-call fixture.");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static T Need<T>(Result<T> result) { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing value."); return result.Value!; }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Time(Fixture f, double dsp, float expected)
        => Check(f.Producer.Clock.TryGetTime(f.Binding, dsp, out var actual) && Math.Abs(actual - expected) < .00001f,
            "Unexpected owned clock media seconds at " + dsp + ": " + actual);
    private static GameplayControlRequest Request(Fixture f, double rate = 2)
        => new(GameplayControlAction.SetSpeed, f.Binding.ControllerGeneration, f.Binding.RunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
    private static void SetRate(Fixture f, float rate = 2)
    {
        using var lease = Need(f.Controls.Acquire(true)); var result = lease.Execute(Request(f, rate));
        Check(result.Success && f.Native.GameplayPitch == rate && f.Producer.Clock.Capture().Rate == rate,
            "The concrete provider's actual rate transaction did not finish: " + result.Error);
    }

    private static void PrivatePause()
    {
        using var f = new Fixture(pause: false);
        Check(f.Provider.Probe().Supported && !f.Provider.ProductionAdmission && !f.Producer.ProductionAdmission,
            "Controlled fixed coverage was conflated with production admission.");
        Throws<GameplayStateException>(() => f.Provider.Capture(f.Origin));
        Check(f.Native.GameplayPitchWrites == 0 && f.Producer.LastOccurrence is null, "A current paused Boolean fabricated a private occurrence.");
        f.Producer.Pause(); using var access = f.Provider.Capture(f.Origin); access.VerifyAuthority();
        Check(f.Producer.LastOccurrence is { Kind: "Pause", Returned: true, ActualScheduleReturned: false, Occurrence: 2 },
            "The actual native owned Pause completion was not consumed.");
    }
    private static void ActualAccess()
    {
        using var f = new Fixture(); var roots = f.Native.ActiveRoots;
        using (var access = f.Provider.Capture(f.Origin))
        {
            var sample = access.Read();
            Check(sample.Binding == f.Binding && sample.DspSeconds == 100 && sample.AudioPositionSeconds == 4
                && sample.Pitch == 1 && sample.ProgressSeconds == 3.8 && sample.AppliedOffsetSeconds == .2
                && !sample.HasStockOrigin && sample.StockOriginSeconds == 0 && f.Native.ActiveRoots == roots + 7,
                "The actual independently rooted source sample differs.");
            f.Native.SetOccurrenceOrigin(99);
            var actual = access.Read(); Check(actual.HasStockOrigin && actual.StockOriginSeconds == 99, "Accessor invented an absent stock origin.");
            f.Native.SetOccurrenceOrigin(null);
        }
        Check(f.Native.ActiveRoots == roots && f.Native.AllGameplayReceiversRooted, "An actual source receiver was unrooted or leaked.");
    }
    private static void ServiceConsumption()
    {
        using var f = new Fixture(); SetRate(f);
        Check(f.Journal.Single().Outcome == RunModificationOutcome.Applied && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f })
            && f.Provider.Probe().Evidence.Contains("Controlled native owned-call coverage only", StringComparison.Ordinal),
            "The formal backend did not consume the concrete native writer source/clock participants.");
    }
    private static void PausedRateWithHeldProgress()
    {
        // Operands from the r20 stock SetSpeed refusal after a completed Pause.
        const float media = 82.07517f, rate = 1.25f;
        const double progress = 81.99992388859391, offset = -0.041999999433755875;
        using var f = new Fixture(pause: false);
        f.Native.GameplayPosition = media;
        f.Native.SetGameplayField("progress.now", progress);
        f.Native.SetGameplayField("progress.offset", offset);
        f.Native.SetGameplayField("info.nowRealTime", (float)progress);
        f.Native.SetGameplayField("info.nowSyncedTime", (float)progress);
        f.Native.OccurrenceAudioPlaying = true;
        f.Native.SetOccurrenceOrigin(18);
        f.Producer.Pause();

        using var source = f.Provider.Capture(f.Origin);
        var baseline = source.Read();
        var clock = f.Producer.Clock.Capture();
        Check(f.Native.OccurrencePauseCalls == 1 && !baseline.HasStockOrigin && baseline.AudioPositionSeconds == media
            && baseline.ProgressSeconds == progress && baseline.AppliedOffsetSeconds == offset
            && baseline.AudioPositionSeconds - (baseline.ProgressSeconds + baseline.AppliedOffsetSeconds) == 0.11724853515625,
            "The completed owned Pause did not reproduce the recorded baseline.");

        using var lease = Need(f.Controls.Acquire(true));
        var receipt = Need(lease.Execute(Request(f, rate)));
        Check(receipt.Modification.TransactionState == GameplayControlTransactionState.Confirmed
            && receipt.Modification.ConfirmedValue == rate && receipt.Modification.ConfirmedUnit == GameplayControlValueUnit.PlaybackRateMultiplier
            && f.Journal.Single().Outcome == RunModificationOutcome.Applied && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { rate }),
            "SetSpeed did not confirm the actual owned pitch write.");
        Check(source.Read() == (baseline with { Pitch = rate })
            && f.Producer.Clock.Capture() == (clock with { Revision = clock.Revision + 1, Rate = rate }),
            "SetSpeed changed the paused occurrence, media anchor, progress or offset.");
        Time(f, clock.DspAnchorSeconds + 1, media);
    }
    private static void Replay()
    {
        using var f = new Fixture(); SetRate(f); f.Producer.ReplayScheduled(.375f);
        var expected = 100 + (double).375f; var clock = f.Producer.Clock.Capture();
        Check(f.Native.OccurrenceScheduleArguments.Single() == expected && clock.DspAnchorSeconds == expected
            && clock.MediaAnchorSeconds == 0 && clock.Rate == 2 && clock.Phase == NativePlaybackPhase.Scheduled
            && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f, 1f, 2f })
            && f.Producer.LastOccurrence is { Kind: "Replay", Returned: true, ActualScheduleReturned: true, ReadbackPitch: 2 },
            "Replay did not use its native actual argument/stock reset/restored pitch.");
        Time(f, expected - .125, -.125f); Time(f, expected + 1, 2);
        Throws<GameplayStateException>(() => f.Provider.Capture(f.Origin));
        Console.WriteLine("OWNED-SCHEDULE Replay " + System.Text.Json.JsonSerializer.Serialize(f.Producer.LastOccurrence));
    }
    private static void Resume()
    {
        using var f = new Fixture(); SetRate(f); f.Producer.ResumeScheduled(.25f);
        var clock = f.Producer.Clock.Capture();
        Check(f.Native.OccurrenceScheduleArguments.Single() == 100.25 && clock.DspAnchorSeconds == 100.25
            && clock.MediaAnchorSeconds == 4 && f.Native.GameplayPosition == 4
            && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f, 1f, 2f }), "Resume lost the saved actual source media or selected rate.");
        Time(f, 100.125, 3.875f); Time(f, 101.25, 6);
        Console.WriteLine("OWNED-SCHEDULE Resume " + System.Text.Json.JsonSerializer.Serialize(f.Producer.LastOccurrence));
    }
    private static void PausePlaying()
    {
        using var f = new Fixture(); SetRate(f);
        f.Native.OccurrenceAudioPlaying = true; f.Native.GameplayPosition = 6; f.Native.GameplayDsp = 101; f.Native.SetOccurrenceOrigin(98);
        f.Producer.Pause(); using var access = f.Provider.Capture(f.Origin); var sample = access.Read();
        Check(f.Native.OccurrencePauseCalls == 1 && !sample.AudioPlaying && !sample.HasStockOrigin && sample.AudioPositionSeconds == 6
            && f.Producer.Clock.Capture().MediaAnchorSeconds == 6 && f.Producer.Clock.Capture().Rate == 2, "Pause did not reanchor actual post-Pause media.");
        Time(f, 110, 6);
    }
    private static void EqualPause()
    {
        using var f = new Fixture(); using var old = f.Provider.Capture(f.Origin); using var writer = old.PreparePitchWrite();
        var prior = old.Read(); f.Producer.Pause();
        Throws<GameplayStateException>(old.VerifyAuthority); Throws<GameplayStateException>(() => writer.Write(2));
        using var current = f.Provider.Capture(f.Origin); Check(current.Read().SchedulingRevision == prior.SchedulingRevision + 1
            && f.Native.GameplayPitchWrites == 0, "Equal paused booleans hid a different actual occurrence or old ticket wrote.");
    }
    private static void EqualWriter()
    {
        using var f = new Fixture(); using var source = f.Provider.Capture(f.Origin); using var writer = source.PreparePitchWrite();
        f.Producer.WritePitchOutsideService(1); Throws<GameplayStateException>(writer.VerifyCurrent);
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 1f }) && f.Native.GameplayPitch == 1,
            "The native owned same-value setter did not execute or invalidate the private writer sequence.");
    }
    private static void WriterAba()
    {
        using var f = new Fixture(); using var source = f.Provider.Capture(f.Origin); using var writer = source.PreparePitchWrite();
        f.Producer.WritePitchOutsideService(7); f.Producer.WritePitchOutsideService(1);
        Throws<GameplayStateException>(writer.VerifyCurrent);
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 7f, 1f }) && f.Native.GameplayPitch == 1
            && !f.Provider.Probe().Supported && f.Producer.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
            "Actual owned ABA was admitted from its equal scalar endpoint.");
    }
    private static void PartialSetter()
    {
        using var f = new Fixture(); f.Native.OccurrenceFailPitchNumber = 1; using var lease = Need(f.Controls.Acquire(true));
        var failed = lease.Execute(Request(f));
        Check(!failed.Success && failed.Error!.Message.Contains("pitch changed before failure", StringComparison.Ordinal)
            && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f, 1f }) && f.Native.GameplayPitch == 1
            && f.Producer.Clock.Capture().Rate == 1 && f.Journal.Single().Outcome == RunModificationOutcome.Restored,
            "The native Before attempt/actual setter failure lost its independent restoration or original error.");
    }
    private static void ReapplyFailure()
    {
        using var f = new Fixture(); SetRate(f); f.Native.OccurrenceFailPitchNumber = 3;
        Throws<IOException>(() => f.Producer.ReplayScheduled(.25f));
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f, 1f, 2f }) && f.Native.OccurrenceScheduleArguments.Count == 1
            && !f.Provider.Probe().Supported && f.Producer.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
            "An entered but failed selected-pitch reapplication published a synchronized schedule.");
    }
    private static void ResetReadback()
    {
        using var f = new Fixture(); SetRate(f); f.Native.OccurrenceIgnorePitch = true;
        Throws<GameplayStateException>(() => f.Producer.ResumeScheduled(.25f));
        Check(f.Native.OccurrenceScheduleArguments.Count == 1 && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f, 1f })
            && !f.Provider.Probe().Supported, "Actual stock pitch reset was inferred from a normal setter return.");
    }
    private static void ScheduleFailure()
    {
        using var f = new Fixture(); SetRate(f); f.Native.OccurrenceFailScheduleNumber = 1;
        Throws<IOException>(() => f.Producer.ResumeScheduled(.25f));
        Check(f.Native.OccurrenceScheduleArguments.Single() == 100.25 && f.Native.GameplayPitch == 1
            && f.Producer.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn && !f.Provider.Probe().Supported,
            "The failed actual schedule callback was replaced by a fabricated Returned receipt.");
    }
    private static void SkippedSchedule()
    {
        using var f = new Fixture(); f.Native.OccurrenceAudioPlaying = true;
        Throws<GameplayStateException>(() => f.Producer.ResumeScheduled(.25f));
        Check(f.Native.OccurrenceScheduleArguments.Count == 0 && f.Native.GameplayPitchWrites == 0 && !f.Provider.Probe().Supported,
            "A stock no-op manufactured a private actual PlayScheduled argument.");
    }
    private static void OriginCrossCheck()
    {
        using var f = new Fixture(); f.Native.AfterOccurrenceSchedule = () => f.Native.SetOccurrenceOrigin(900);
        Throws<GameplayStateException>(() => f.Producer.ReplayScheduled(.25f));
        Check(f.Native.OccurrenceScheduleArguments.Single() == 100.25 && !f.Provider.Probe().Supported,
            "The mutated stock origin was used instead of independently witnessed actual schedule argument.");
    }
    private static void GraphChange()
    {
        using var f = new Fixture(); f.Native.AfterOccurrenceSchedule = () => f.Native.SetGameplayField("progress.player", f.Native.GameplaySource);
        Throws<GameplayStateException>(() => f.Producer.ResumeScheduled(.25f));
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 1f }) && !f.Provider.Probe().Supported,
            "Schedule source replacement was overwritten by selected-pitch restoration.");
    }
    private static void EqualSource()
    {
        using var f = new Fixture(); using var source = f.Provider.Capture(f.Origin); using var ticket = source.PreparePitchWrite();
        f.Producer.SetClipOutsideService(f.Native.GameplayClip); Throws<GameplayStateException>(ticket.VerifyCurrent);
        Check(f.Native.OccurrenceCalls.Last() == "clip" && !f.Provider.Probe().Supported && f.Native.GameplayPitchWrites == 0,
            "A same-clip native setter reused the original private source fence.");
    }
    private static void WrongOrigin()
    {
        using var f = new Fixture(); var roots = f.Native.ActiveRoots;
        Throws<GameplayStateException>(() => f.Provider.Capture(f.Origin with { RunId = "another-run" }));
        Check(f.Native.ActiveRoots == roots && f.Native.GameplayPitchWrites == 0, "A foreign origin retained or wrote this source.");
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var error = Task.Run(() => Throws<GameplayStateException>(() => f.Provider.Capture(f.Origin))).GetAwaiter().GetResult();
        Check(error.Code == ErrorCode.WrongThread && f.Native.GameplayPitchWrites == 0, "Cross-thread private source access was admitted.");
    }
    private static void SourceWorkerDispose()
    {
        using var f = new Fixture(); var baselineRoots = f.Native.ActiveRoots;
        var source = f.Provider.Capture(f.Origin); var retainedRoots = f.Native.ActiveRoots;
        var freeAttempts = f.Native.FreeHandleAttempts; var freedRoots = f.Native.FreedRoots;
        Check(retainedRoots == baselineRoots + 7, "The provider source did not independently retain seven exact roots.");
        var error = Task.Run(() => Throws<GameplayStateException>(source.Dispose)).GetAwaiter().GetResult();
        Check(error.Code == ErrorCode.WrongThread && f.Native.ActiveRoots == retainedRoots
            && f.Native.FreeHandleAttempts == freeAttempts && f.Native.FreedRoots == freedRoots,
            "Worker provider-source Dispose mutated state or attempted a native root release before thread rejection.");
        source.VerifyAuthority(); // Proves the rejected disposal did not poison the source's disposed state.
        freeAttempts = f.Native.FreeHandleAttempts; freedRoots = f.Native.FreedRoots;
        source.Dispose();
        Check(f.Native.ActiveRoots == baselineRoots && f.Native.FreeHandleAttempts == freeAttempts + 7
            && f.Native.FreedRoots == freedRoots + 7, "Owner-thread provider-source retry did not release exactly seven retained roots.");
        source.Dispose();
        Check(f.Native.ActiveRoots == baselineRoots && f.Native.FreeHandleAttempts == freeAttempts + 7
            && f.Native.FreedRoots == freedRoots + 7, "A repeated provider-source Dispose retried an already released root.");
    }
    private static void AccessorWorkerDispose()
    {
        using var f = new Fixture(); var baselineRoots = f.Native.ActiveRoots;
        var accessor = new NativePlaybackActualSource(f.Native.GameplayApi, f.Binding, f.Origin.Pause);
        var retainedRoots = f.Native.ActiveRoots; var freeAttempts = f.Native.FreeHandleAttempts; var freedRoots = f.Native.FreedRoots;
        Check(retainedRoots == baselineRoots + 7, "The direct actual accessor did not independently retain seven exact roots.");
        var error = Task.Run(() => Throws<GameplayStateException>(accessor.Dispose)).GetAwaiter().GetResult();
        Check(error.Code == ErrorCode.WrongThread && f.Native.ActiveRoots == retainedRoots
            && f.Native.FreeHandleAttempts == freeAttempts && f.Native.FreedRoots == freedRoots,
            "Worker actual-accessor Dispose lost its root list or attempted release before thread rejection.");
        accessor.VerifyGraph(); // The original independent root list and open state must remain usable.
        freeAttempts = f.Native.FreeHandleAttempts; freedRoots = f.Native.FreedRoots;
        accessor.Dispose();
        Check(f.Native.ActiveRoots == baselineRoots && f.Native.FreeHandleAttempts == freeAttempts + 7
            && f.Native.FreedRoots == freedRoots + 7, "Owner-thread actual-accessor retry did not release exactly seven retained roots.");
        accessor.Dispose();
        Check(f.Native.ActiveRoots == baselineRoots && f.Native.FreeHandleAttempts == freeAttempts + 7
            && f.Native.FreedRoots == freedRoots + 7, "A repeated actual-accessor Dispose retried an already released root.");
    }
    private static void ScheduleDuringPreparation()
    {
        using var f = new Fixture(); using var prepared = NativePlaybackRateProviderAdoption.Prepare(f.Provider, f.Origin, Request(f), () => { });
        Throws<GameplayStateException>(() => f.Producer.ResumeScheduled(.25f));
        Throws<GameplayStateException>(prepared.Commit);
        Check(f.Native.OccurrenceScheduleArguments.Count == 1 && f.Producer.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
            "A native schedule crossed a prepared paused rate baseline without withdrawal.");
    }
    private static void DisposeDuringSetter()
    {
        using var f = new Fixture(); using var lease = Need(f.Controls.Acquire(true));
        f.Native.AfterGameplayPitchWrite = () => { f.Producer.Dispose(); Check(f.Native.ActiveRoots >= 14, "Native callback stop freed still-active source roots."); };
        var result = lease.Execute(Request(f));
        Check(!result.Success && f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f })
            && f.Journal.Single().Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown,
            "Stopping the native producer during setter returned synchronized success or overwrote unknown state.");
    }
    private static void ConstructorFailure()
    {
        using var native = new FakeNative(); native.ConfigurePlaybackOccurrence(); native.GameplayDsp = double.NaN;
        var binding = Binding(native); var origin = Origin(native);
        Throws<GameplayStateException>(() => NativePlaybackOwnedCallProducer.CreateControlledForTests(native.GameplayApi, binding, origin, Library));
        Check(native.ActiveRoots == 0, "A failing concrete native source constructor leaked roots.");
    }
    private static void NakedWriterBoundary()
    {
        using var f = new Fixture(); using var source = f.Provider.Capture(f.Origin); using var ticket = source.PreparePitchWrite();
        foreach (var pitch in new[] { 1f, 7f, 1f }) f.Native.GameplayApi.Call(f.Native.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(pitch));
        ticket.VerifyCurrent(); // This is the explicit missing global observer, not a completeness claim.
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 1f, 7f, 1f }) && !f.Provider.ProductionAdmission
            && f.Provider.Probe().Evidence.Contains("direct external writer coverage", StringComparison.Ordinal),
            "The controlled producer concealed its naked Runtime/Unity writer coverage gap.");
        Console.WriteLine("OWNED-BOUNDARY productionAdmission=false; naked-runtime-same-value-and-ABA-observed=false; real-stock-hooks=false");
    }
    private static void DefaultBoundary()
    {
        using var f = new Fixture(); using var actual = new GameplayNativeBackend(f.Native.GameplayApi, f.Main);
        foreach (var action in new[] { GameplayControlAction.SetSpeed, GameplayControlAction.Seek })
            Check(!actual.Probe().Operations.Single(row => row.Action == action).Supported, "The offline producer enabled default real control " + action);
    }
    private static void ExternalReadback()
    {
        using var f = new Fixture(); f.Native.OccurrenceIgnorePitch = true;
        Throws<GameplayStateException>(() => f.Producer.WritePitchOutsideService(2));
        Check(f.Native.GameplayPitchAttempts.SequenceEqual(new[] { 2f }) && f.Native.GameplayPitch == 1 && !f.Provider.Probe().Supported,
            "A normal owned setter return substituted for actual external pitch readback.");
    }
    private static void DspCallbackRegression()
    {
        using var f = new Fixture(); var reads = 0;
        f.Native.AfterOccurrenceDspRead = () => { if (++reads == 2) f.Native.GameplayDsp = 99; };
        Throws<GameplayStateException>(() => f.Producer.ResumeScheduled(.25f));
        Check(reads == 3 && f.Native.OccurrenceStopCalls == 1 && f.Native.OccurrenceScheduleArguments.Count == 0
            && f.Producer.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn, "An intermediate native DSP regression was used as a schedule anchor.");
    }
    private static void SourceDspRegression()
    {
        using var f = new Fixture(); using var source = f.Provider.Capture(f.Origin); f.Native.GameplayDsp = 99;
        Throws<GameplayStateException>(() => source.Read());
        Check(!f.Provider.Probe().Supported && f.Producer.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
            "An actual invalid source read left the old clock active.");
    }

    private static NativePlaybackBinding Binding(FakeNative native) => new("owned-run", 3, 1, 1,
        native.GameplayController.ToInt64(), native.GameplayInformation.ToInt64(), native.GameplayProgress.ToInt64(),
        native.GameplayPlayer.ToInt64(), native.GameplaySource.ToInt64(), native.GameplayClip.ToInt64(), native.GameplayGetTimeMethod.ToInt64());
    private static NativePlaybackRateOrigin Origin(FakeNative native) => new("owned-run", 1, native.GameplayController.ToInt64(),
        native.GameplayInformation.ToInt64(), native.GameplayProgress.ToInt64(), native.GameplayPause.ToInt64());
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly MainThreadDispatcher Main = new();
        internal readonly NativePlaybackBinding Binding; internal readonly NativePlaybackRateOrigin Origin;
        internal readonly NativePlaybackOwnedCallProducer Producer; internal readonly NativePlaybackOccurrenceRateProvider Provider;
        internal readonly GameplayService Service; internal readonly Context Context; internal readonly IGameplayControlService Controls;
        internal readonly List<RunModificationInput> Journal = new();
        internal Fixture(bool pause = true)
        {
            Native.ConfigurePlaybackOccurrence(); Binding = NativePlaybackOccurrenceProviderChecks.Binding(Native); Origin = NativePlaybackOccurrenceProviderChecks.Origin(Native);
            Producer = NativePlaybackOwnedCallProducer.CreateControlledForTests(Native.GameplayApi, Binding, Origin, Library);
            Provider = NativePlaybackOccurrenceRateProvider.AdoptControlledForTests(Producer); if (pause) Producer.Pause();
            Service = new(new GameplayNativeBackend(Native.GameplayApi, Main, Provider), Main,
                () => new("controlled-owned-session", 1, 3, "NormalMode", "owned-run", "chart", RunLifecyclePhase.Paused, true, true, true, true, Array.Empty<OperationError>()),
                Journal.Add, matchesCurrentGameplayRun: (info, controller, run, scene) => info == Binding.Information && controller == Binding.Controller && run == Binding.RunId && scene == Binding.SceneGeneration);
            Context = new("owned-native-provider", Main); Controls = Service.ControlsForPlugin(Context);
        }
        public void Dispose()
        {
            Context.Dispose(); Service.Dispose(); Producer.Dispose(); Main.Dispose();
            Check(Native.ActiveRoots == 0, "The concrete native producer/provider leaked source or preparation roots."); Native.Dispose();
        }
    }
}
