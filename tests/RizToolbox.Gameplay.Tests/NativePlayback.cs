using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

internal static class NativePlaybackChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("native media clock freezes actual paused media seconds", Paused),
        ("native media clock uses independent DSP rather than scaling stock raw", IndependentAnchor),
        ("native scheduled media clock retains unscaled pre-roll and scales after start", Scheduled),
        ("native pause reanchors source time and clip end clamps media", PauseAndEnd),
        ("native clock exact graph and generation mismatch cannot consume or disturb a clock", Binding),
        ("native DSP regression and nonfinite values withdraw the clock", DspFaults),
        ("native clock rejects unrepresentable rates and arithmetic overflow", InvalidClockInputs),
        ("native clock preparation is read-only and commits once", ClockPreparation),
        ("native private clock change restores its complete owned baseline once", ClockRestoration),
        ("native clock Dispose releases preparation without undoing rate", ClockDisposal),
        ("native schedule racing preparation withdraws without overwriting foreign state", ScheduleRace),
        ("stock resume pitch reset needs actual selected pitch readback", PitchReset),
        ("instance Single bridge replaces exact normal completion raw bits", ReturnBridge),
        ("instance Single bridge preserves exception and mismatched raw bits", ReturnGuards),
        ("null MethodInfo is confined to the fixed Progress.Update callsite", NullMethodInfoProfile),
        ("instance Single bridge preserves nonfinite stock return and withdraws", NonfiniteOriginal),
        ("native rate participants commit actual pitch then anchored clock", RateTransaction),
        ("native rate participants preserve fractional Single canonicalization", FractionalRate),
        ("native rate participants reject wrong request before native capture", WrongRequest),
        ("native rate preparation refuses pending schedules and invalid baselines", InvalidPreparation),
        ("native rate preparation requires the exact paused clock media, clip and pitch readback", PausedClockMismatch),
        ("native rate preparation rejects source change between captures", CaptureChange),
        ("native rate transaction rechecks baseline before first write", ChangedBaseline),
        ("native partial setter failure restores actual audio and clock baselines", SetterFailure),
        ("native restoration preserves successor pitch", SuccessorPitch),
        ("native restoration refuses same-value foreign writes and pitch ABA", PitchAba),
        ("native restoration refuses changed schedule authority", ChangedAuthority),
        ("native rollback reports withdrawn clock honestly", WithdrawnClock),
        ("native transaction Dispose keeps rate and releases captured access once", RateDisposal),
        ("native paused rate transaction feeds scheduled anchor computation", RateToScheduledClock),
        ("native clock publishes and reads back exact revision before managed state changes", PublicationOrder),
        ("native clock policy publication failure withdraws both clocks", PublicationFailure),
        ("native clock policy readback mismatch withdraws both clocks", PublicationReadback),
        ("native policy revocation cannot retain a confirmed managed clock", PublicationRevoked),
        ("native policy restoration failure remains unknown", PublicationRestoreFailure),
        ("native rate publication failure restores audio while preserving unknown clock", RatePublicationFailure)
    };

    private static NativePlaybackBinding Source => new("native-run", 3, 7, 2, 11, 12, 13, 14, 15, 16, 17);
    private static NativePlaybackClock Clock(double rate = 1, double media = 4, double length = 120)
        => new(Source, 100, media, rate, length);
    private static GameplayControlRequest Request(double rate = 2)
        => new(GameplayControlAction.SetSpeed, Source.ControllerGeneration, Source.RunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
    private static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Time(NativePlaybackClock clock, double dsp, float expected)
        => Check(clock.TryGetTime(Source, dsp, out var actual) && actual == expected, "Unexpected media seconds at DSP " + dsp + ": " + actual);

    private static void Paused()
    {
        var clock = Clock(); var state = clock.Capture(); Time(clock, 100, 4); Time(clock, 500, 4);
        Check(ReferenceEquals(state, clock.Capture()), "Reading changed paused baseline revision.");
    }
    private static void IndependentAnchor()
    {
        var clock = Clock(2); clock.ObservePlaying(Source, 100, 4, 2); var bridge = new NativePlaybackGetTimeBridge(clock, Source);
        Check(bridge.TryReplace(Source, 14, 17, true, Bits(5), 101, out var unscaled)
            && bridge.TryReplace(Source, 14, 17, true, Bits(6), 101, out var scaled)
            && unscaled == Bits(6) && scaled == Bits(6), "Already rate-scaled stock fallback was multiplied again.");
        Time(clock, 102, 8);
    }
    private static void Scheduled()
    {
        var clock = Clock(2); clock.ObserveScheduled(Source, 100, 103, 4, 2);
        Time(clock, 100, 1); Time(clock, 102, 3); Time(clock, 103, 4); Time(clock, 104, 6);
        var replay = Clock(2); replay.ObserveScheduled(Source, 100, 103, 0, 2);
        Time(replay, 100, -3); Time(replay, 103, 0); Time(replay, 104, 2);
    }
    private static void PauseAndEnd()
    {
        var clock = Clock(2, 4, 10); clock.ObservePlaying(Source, 100, 4, 2); Time(clock, 102, 8);
        clock.ObservePause(Source, 102, 7.95, 2); Time(clock, 104, 7.95f);
        clock.ObservePlaying(Source, 104, 7.95, 2); Time(clock, 110, 10);
    }
    private static void Binding()
    {
        var clock = Clock(); var before = clock.Capture();
        var alternatives = new[] { Source with { RunId = "successor" }, Source with { SceneGeneration = 4 },
            Source with { ControllerGeneration = 8 }, Source with { SourceGeneration = 3 }, Source with { Controller = 21 },
            Source with { Information = 22 }, Source with { Progress = 23 }, Source with { Player = 24 },
            Source with { AudioSource = 25 }, Source with { Clip = 26 }, Source with { GetTimeMethod = 27 } };
        foreach (var source in alternatives) Check(!clock.TryGetTime(source, 1, out _), "Foreign graph consumed the clock.");
        Check(ReferenceEquals(before, clock.Capture()), "Foreign callback disturbed an authorized clock."); Time(clock, 100, 4);
    }
    private static void DspFaults()
    {
        foreach (var dsp in new[] { 99d, -1d, double.NaN, double.PositiveInfinity })
        {
            var clock = Clock(); Check(!clock.TryGetTime(Source, dsp, out _), "Invalid DSP escaped.");
            Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn && !clock.TryGetTime(Source, 101, out _), "Failed clock revived.");
        }
    }
    private static void InvalidClockInputs()
    {
        foreach (var rate in new[] { 0d, -1d, double.Epsilon, double.NaN, double.PositiveInfinity, double.MaxValue }) Throws<GameplayStateException>(() => Clock(rate));
        Throws<GameplayStateException>(() => new NativePlaybackClock(Source with { SourceGeneration = 0 }, 100, 4, 1, 120));
        Throws<GameplayStateException>(() => Clock(media: 121)); Throws<GameplayStateException>(() => Clock(length: 0));
        var clock = Clock(float.MaxValue); clock.ObservePlaying(Source, 100, 4, float.MaxValue);
        Check(!clock.TryGetTime(Source, double.MaxValue, out _), "Overflow was hidden by clip clamping.");
    }
    private static void ClockPreparation()
    {
        var clock = Clock(); var before = clock.Capture(); using var change = clock.PreparePausedRate(Source, 2);
        Check(ReferenceEquals(before, clock.Capture()), "Preparation wrote media state."); Throws<GameplayStateException>(() => clock.PreparePausedRate(Source, 3));
        change.VerifyBaseline(); change.Commit(); Check(change.Confirm(), "Clock did not confirm its rate."); Time(clock, 105, 4);
        Check(clock.Capture().Rate == 2 && clock.Capture().MediaAnchorSeconds == 4, "Rate moved paused media."); Throws<InvalidOperationException>(change.Commit);
    }
    private static void ClockRestoration()
    {
        var clock = Clock(); var baseline = clock.Capture(); using var change = clock.PreparePausedRate(Source, 2); change.Commit();
        Check(!change.ConfirmRestored(), "Commit was called restoration."); change.Restore();
        Check(change.ConfirmRestored() && (clock.Capture() with { Revision = baseline.Revision }) == baseline, "Restoration lost baseline fields."); Throws<InvalidOperationException>(change.Restore);
    }
    private static void ClockDisposal()
    {
        var clock = Clock(); var change = clock.PreparePausedRate(Source, 2); change.Commit(); change.Dispose(); change.Dispose();
        Check(clock.Capture().Rate == 2, "Dispose undid committed rate."); Throws<ObjectDisposedException>(() => change.Confirm());
        using var next = clock.PreparePausedRate(Source, 3); next.VerifyBaseline();
    }
    private static void ScheduleRace()
    {
        var clock = Clock(); using var change = clock.PreparePausedRate(Source, 2);
        Throws<GameplayStateException>(() => clock.ObserveScheduled(Source, 100, 103, 4, 1));
        Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn, "Racing native schedule left prepared baseline valid.");
        Throws<GameplayStateException>(change.VerifyBaseline); Throws<GameplayStateException>(change.Restore);
    }
    private static void PitchReset()
    {
        var clock = Clock(2); Throws<GameplayStateException>(() => clock.ObserveScheduled(Source, 100, 103, 4, 1));
        Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn, "Stock pitch=1 was treated as selected rate.");
    }
    private static void ReturnBridge()
    {
        var clock = Clock(2); clock.ObservePlaying(Source, 100, 4, 2); var bridge = new NativePlaybackGetTimeBridge(clock, Source);
        Check(bridge.TryReplace(Source, 14, 17, true, 0x80000000u, 101, out var result) && result == Bits(6), "Single() return did not produce media seconds.");
    }
    private static void ReturnGuards()
    {
        var clock = Clock(); var bridge = new NativePlaybackGetTimeBridge(clock, Source); const uint raw = 0x80000000u;
        Check(!bridge.TryReplace(Source, 14, 17, false, raw, double.NaN, out var exception) && exception == raw, "Exceptional original was replaced.");
        Check(!bridge.TryReplace(Source, 99, 17, true, raw, double.NaN, out var receiver) && receiver == raw, "Foreign receiver was replaced.");
        Check(!bridge.TryReplace(Source, 14, 99, true, raw, double.NaN, out var method) && method == raw, "Foreign method was replaced.");
        Check(!bridge.TryReplace(Source with { SourceGeneration = 3 }, 14, 17, true, raw, double.NaN, out var generation)
            && generation == raw && clock.Capture().Phase == NativePlaybackPhase.Paused, "Rejected callback mutated clock.");
    }
    private static void NonfiniteOriginal()
    {
        foreach (var bits in new[] { 0x7fc01234u, 0x7f800000u, 0xff800000u })
        {
            var clock = Clock(); var bridge = new NativePlaybackGetTimeBridge(clock, Source);
            Check(!bridge.TryReplace(Source, 14, 17, true, bits, 100, out var result) && result == bits
                && clock.Capture().Phase == NativePlaybackPhase.Withdrawn, "Nonfinite original was replaced or normalized.");
        }
    }
    private static void NullMethodInfoProfile()
    {
        const long module = 0x180000000;
        Check(NativePlaybackGetTimeCallProfile.MatchesPinnedProgressReturn(module, module + 0x40121d)
            && !NativePlaybackGetTimeCallProfile.MatchesPinnedProgressReturn(module, module + 0x40121c)
            && !NativePlaybackGetTimeCallProfile.MatchesPinnedProgressReturn(module, module + 0x401218)
            && !NativePlaybackGetTimeCallProfile.MatchesPinnedProgressReturn(0, 0x40121d)
            && !NativePlaybackGetTimeCallProfile.MatchesPinnedProgressReturn(long.MaxValue, 0x40121d), "Null-MI widened beyond exact fixed return.");
        var clock = Clock(); var bridge = new NativePlaybackGetTimeBridge(clock, Source);
        Check(!bridge.TryReplace(Source, 14, 0, module + 0x40121d, true, Bits(4), 100, out var raw) && raw == Bits(4),
            "Matching integer address without actual native profile authorized null MI.");
    }

    private static void RateTransaction()
    {
        var access = new Access(); var clock = access.Clock(); var before = clock.Capture();
        using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
        Check(access.Writes.Count == 0 && ReferenceEquals(before, clock.Capture()) && access.Reads >= 2, "Prepare wrote audio/clock or skipped source recheck.");
        prepared.Commit(); var confirmed = prepared.Confirm();
        Check(confirmed.Observed == true && confirmed.Value == 2 && confirmed.Unit == GameplayControlValueUnit.PlaybackRateMultiplier
            && access.Writes.SequenceEqual(new[] { 2f }) && clock.Capture().Rate == 2, "Pitch and clock did not jointly confirm actual rate.");
    }
    private static void FractionalRate()
    {
        const double requested = 1.234567890123;
        var access = new Access(); var clock = access.Clock();
        using var prepared = NativePlaybackRateParticipants.Prepare(Request(requested), clock, () => access, () => { });
        prepared.Commit(); var confirmation = prepared.Confirm();
        Check(confirmation.Observed == true && confirmation.Value == (double)(float)requested
            && Math.Abs(confirmation.Value!.Value - requested) <= prepared.ValueTolerance, "Single canonicalization exceeded declared tolerance.");
    }
    private static void WrongRequest()
    {
        var count = 0; var clock = Clock();
        foreach (var request in new[] { Request() with { Action = GameplayControlAction.Seek }, Request() with { Unit = GameplayControlValueUnit.GameSeconds },
            Request(double.NaN), Request(0), Request(double.MaxValue), Request(double.Epsilon) })
            Throws<GameplayStateException>(() => NativePlaybackRateParticipants.Prepare(request, clock, () => { count++; return new Access(); }, () => { }));
        Check(count == 0, "Invalid rate captured native access.");
    }
    private static void InvalidPreparation()
    {
        var mutations = new Func<NativePlaybackRateSample, NativePlaybackRateSample>[]
        {
            s => s with { Preparing = true }, s => s with { Paused = false }, s => s with { AudioPlaying = true },
            s => s with { ProgressPlaying = true }, s => s with { TimeReady = false }, s => s with { LevelOver = true },
            s => s with { HasStockOrigin = true },
            s => s with { Crashed = true }, s => s with { SchedulingRevision = 0 }, s => s with { AuthorityRevision = 0 },
            s => s with { AppliedOffsetSeconds = double.PositiveInfinity }, s => s with { AudioPositionSeconds = 121 },
            s => s with { Pitch = float.NaN }, s => s with { ProgressSeconds = double.NaN }
        };
        foreach (var change in mutations)
        {
            var access = new Access(); var clock = access.Clock(); access.State = change(access.State);
            Throws<GameplayStateException>(() => NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { }));
            Check(access.Writes.Count == 0 && access.Disposals == 1 && clock.Capture().Rate == 1, "Failed preparation leaked access or changed state.");
        }
    }
    private static void PausedClockMismatch()
    {
        var mutations = new Func<NativePlaybackRateSample, NativePlaybackRateSample>[]
        {
            sample => sample with { AudioPositionSeconds = 4.0625f },
            sample => sample with { ClipLengthSeconds = 119 },
            sample => sample with { Pitch = 1.25f }
        };
        foreach (var mutate in mutations)
        {
            var access = new Access();
            var clock = access.Clock();
            var baseline = clock.Capture();
            access.State = mutate(access.State);
            Throws<GameplayStateException>(() => NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { }));
            Check(access.Writes.Count == 0 && access.Disposals == 1 && ReferenceEquals(clock.Capture(), baseline),
                "A conflicting paused media/clip/pitch readback reached a write or leaked its access.");
        }
    }
    private static void CaptureChange()
    {
        var access = new Access(); var clock = access.Clock();
        access.BeforeRead = () => { if (access.Reads == 2) access.State = access.State with { Binding = Source with { SourceGeneration = 9 } }; };
        Throws<GameplayStateException>(() => NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { }));
        Check(access.Writes.Count == 0 && access.Disposals == 1, "Mixed capture was accepted or leaked roots.");
        using var next = clock.PreparePausedRate(Source, 2); next.VerifyBaseline();
    }
    private static void ChangedBaseline()
    {
        var mutations = new Func<NativePlaybackRateSample, NativePlaybackRateSample>[]
        {
            sample => sample with { SchedulingRevision = 2 },
            sample => sample with { AudioPositionSeconds = 4.0625f },
            sample => sample with { ProgressSeconds = 3.85 },
            sample => sample with { ChartPosition = 7.7f },
            sample => sample with { AppliedOffsetSeconds = .25 }
        };
        foreach (var mutate in mutations)
        {
            var access = new Access();
            var clock = access.Clock();
            var baseline = clock.Capture();
            using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
            access.State = mutate(access.State);
            Throws<GameplayStateException>(prepared.Commit);
            Check(access.Writes.Count == 0 && ReferenceEquals(clock.Capture(), baseline),
                "A changed paused position, progress, offset or occurrence reached the first audio write.");
        }
    }
    private static void SetterFailure()
    {
        var access = new Access { FailWriteNumber = 1 }; var clock = access.Clock();
        using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
        Throws<IOException>(prepared.Commit); prepared.Restore(); var restored = prepared.ConfirmRestored();
        Check(restored.OriginalStateRestored && access.Writes.SequenceEqual(new[] { 2f, 1f }) && access.State.Pitch == 1
            && clock.Capture().Rate == 1, "A setter that changed then threw lost its complete restored baseline: " + restored.Evidence);
    }
    private static void SuccessorPitch()
    {
        var access = new Access(); var clock = access.Clock(); using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
        prepared.Commit(); access.State = access.State with { Pitch = 1.5f }; prepared.Restore();
        Check(access.State.Pitch == 1.5f && access.Writes.SequenceEqual(new[] { 2f }), "Restoration overwrote successor pitch.");
        Check(!prepared.ConfirmRestored().OriginalStateRestored, "Successor pitch was called restored.");
    }
    private static void ChangedAuthority()
    {
        var access = new Access(); var clock = access.Clock(); using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
        prepared.Commit(); access.State = access.State with { AuthorityRevision = 2 }; prepared.Restore();
        Check(access.Writes.SequenceEqual(new[] { 2f }) && !prepared.ConfirmRestored().OriginalStateRestored, "Old capture wrote a successor authority.");
    }
    private static void PitchAba()
    {
        foreach (var aba in new[] { false, true })
        {
            var access = new Access(); var clock = access.Clock(); using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
            prepared.Commit(); if (aba) access.ForeignPitch(7); access.ForeignPitch(2); prepared.Restore();
            Check(access.State.Pitch == 2 && access.Writes.SequenceEqual(new[] { 2f }) && !prepared.ConfirmRestored().OriginalStateRestored,
                "Foreign same-value or ABA pitch reused old value as ownership proof.");
        }
    }
    private static void WithdrawnClock()
    {
        var access = new Access(); var clock = access.Clock(); using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
        prepared.Commit(); clock.Withdraw("controlled schedule boundary became unknown"); prepared.Restore();
        Check(access.State.Pitch == 1 && clock.Capture().Phase == NativePlaybackPhase.Withdrawn
            && !prepared.ConfirmRestored().OriginalStateRestored, "Unknown clock was overwritten or called restored.");
    }
    private static void RateDisposal()
    {
        var access = new Access(); var clock = access.Clock(); var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
        prepared.Commit(); prepared.Dispose(); prepared.Dispose();
        Check(access.Disposals == 1 && access.State.Pitch == 2 && clock.Capture().Rate == 2, "Dispose retried cleanup or undid committed rate.");
        using var next = clock.PreparePausedRate(Source, 3); next.VerifyBaseline();
    }
    private static void RateToScheduledClock()
    {
        var access = new Access(); var clock = access.Clock();
        using (var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { }))
        { prepared.Commit(); Check(prepared.Confirm().Observed == true, "Rate was not jointly confirmed."); }
        clock.ObserveScheduled(Source, 100, 103, access.State.AudioPositionSeconds, access.State.Pitch);
        Time(clock, 102, 3); Time(clock, 103, 4); Time(clock, 104, 6);
    }
    private static NativePlaybackClock PublishedClock(Publication publisher)
        => new(Source, 100, 4, 1, 120, publisher);
    private static void PublicationOrder()
    {
        var publisher = new Publication(); var clock = PublishedClock(publisher); var baseline = clock.Capture();
        publisher.DuringPublish = () => Check(ReferenceEquals(clock.Capture(), baseline), "Managed rate published before native CAS/readback completed.");
        using var change = clock.PreparePausedRate(Source, 2); change.Commit();
        Check(change.Confirm() && publisher.State == clock.Capture() && publisher.State!.Revision == 2, "Native and managed clock revisions diverged.");
        publisher.DuringPublish = null; change.Restore();
        Check(change.ConfirmRestored() && publisher.State == clock.Capture() && publisher.State!.Revision == 3 && publisher.State.Rate == 1,
            "Conditional restoration did not publish the complete baseline at a fresh revision.");
    }
    private static void PublicationFailure()
    {
        var publisher = new Publication { FailAfterPublish = true }; var clock = PublishedClock(publisher);
        using var change = clock.PreparePausedRate(Source, 2); Throws<IOException>(change.Commit);
        Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn && publisher.Withdrawals == 1 && publisher.State is null,
            "A policy write then failure left a usable rate clock.");
        Throws<GameplayStateException>(change.Restore); Check(!change.ConfirmRestored(), "Unknown native publication was called restored.");
    }
    private static void PublicationReadback()
    {
        var publisher = new Publication { FailPublishedReadback = true }; var clock = PublishedClock(publisher);
        using var change = clock.PreparePausedRate(Source, 2); Throws<GameplayStateException>(change.Commit);
        Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn && publisher.Withdrawals == 1, "Incorrect policy readback was accepted.");
    }
    private static void PublicationRevoked()
    {
        var publisher = new Publication(); var clock = PublishedClock(publisher); using var change = clock.PreparePausedRate(Source, 2); change.Commit();
        publisher.State = null; Check(!change.Confirm() && clock.Capture().Phase == NativePlaybackPhase.Withdrawn
            && !clock.TryGetTime(Source, 101, out _), "Revoked native policy left managed GetTime active.");
    }
    private static void PublicationRestoreFailure()
    {
        var publisher = new Publication(); var clock = PublishedClock(publisher); using var change = clock.PreparePausedRate(Source, 2); change.Commit();
        publisher.FailAfterPublish = true; Throws<IOException>(change.Restore);
        Check(!change.ConfirmRestored() && clock.Capture().Phase == NativePlaybackPhase.Withdrawn && publisher.State is null, "Failed policy restoration fabricated synchronization.");
    }
    private static void RatePublicationFailure()
    {
        var publisher = new Publication { FailAfterPublish = true }; var access = new Access(); var clock = PublishedClock(publisher);
        using var prepared = NativePlaybackRateParticipants.Prepare(Request(), clock, () => access, () => { });
        Throws<IOException>(prepared.Commit); prepared.Restore();
        Check(access.State.Pitch == 1 && access.Writes.SequenceEqual(new[] { 2f, 1f })
            && !prepared.ConfirmRestored().OriginalStateRestored && clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
            "Failed clock publication prevented independent audio restoration or fabricated restored control.");
    }

    private sealed class Publication : INativePlaybackClockPublication
    {
        internal NativePlaybackClockState? State = new(Source, 1, NativePlaybackPhase.Paused, 100, 4, 1, 120);
        internal int Writes, Withdrawals;
        internal bool FailAfterPublish, FailPublishedReadback;
        internal Action? DuringPublish;
        public void Publish(NativePlaybackClockState expected, NativePlaybackClockState next)
        {
            Check(State == expected, "Native policy CAS expected state differs."); Writes++; DuringPublish?.Invoke(); State = next;
            if (FailAfterPublish) throw new IOException("Native policy changed before publication failed.");
        }
        public bool IsCurrent(NativePlaybackClockState state) => State == state && !(Writes != 0 && FailPublishedReadback);
        public void Withdraw(string reason) { Withdrawals++; State = null; }
    }

    private sealed class Access : INativePlaybackRateAccess
    {
        internal NativePlaybackRateSample State = new(Source, 1, 1, true, false, false, false, true, false, false,
            100, 4, 120, 1, 3.8, 7.6f, .2, false, 0);
        internal readonly List<float> Writes = new();
        internal int Reads, Disposals, FailWriteNumber, PitchWriteDisposals;
        private long _writerRevision = 1;
        internal Action? BeforeRead;
        public void VerifyAuthority() { if (Disposals != 0) throw new ObjectDisposedException(nameof(Access)); }
        public NativePlaybackRateSample Read() { Reads++; BeforeRead?.Invoke(); return State; }
        public INativePlaybackPitchWrite PreparePitchWrite() => new PitchWrite(this);
        internal void ForeignPitch(float pitch) { _writerRevision++; State = State with { Pitch = pitch }; }
        private void WritePitch(float pitch)
        {
            Writes.Add(pitch); State = State with { Pitch = pitch };
            if (Writes.Count == FailWriteNumber) throw new IOException("Pitch changed before controlled setter failure.");
        }
        public void Dispose() => Disposals++;
        internal NativePlaybackClock Clock() => new(Source, State.DspSeconds, State.AudioPositionSeconds, State.Pitch, State.ClipLengthSeconds);

        private sealed class PitchWrite : INativePlaybackPitchWrite
        {
            private readonly Access _owner;
            private long _ownedRevision;
            private bool _disposed;
            internal PitchWrite(Access owner) => (_owner, _ownedRevision) = (owner, owner._writerRevision);
            public void VerifyCurrent()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PitchWrite));
                _owner.VerifyAuthority();
                if (_ownedRevision != _owner._writerRevision) throw new GameplayStateException(ErrorCode.Conflict, "Foreign pitch writer interrupted private ownership.");
            }
            public void Write(float pitch)
            {
                VerifyCurrent(); _ownedRevision = ++_owner._writerRevision; _owner.WritePitch(pitch);
            }
            public void Dispose() { if (_disposed) return; _disposed = true; _owner.PitchWriteDisposals++; }
        }
    }
}
