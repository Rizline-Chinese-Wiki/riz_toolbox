using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

internal static class NativePlaybackSeekClockChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("paused Seek publishes media position and resumes at the selected rate", SeekAndResume),
        ("Seek restoration returns to the complete baseline without rewinding DSP", Restore),
        ("rate and Seek reserve the same clock baseline in both orders", SharedReservation),
        ("Seek rejects invalid positions and a schedule racing its preparation", Guards),
        ("failed native Seek publication withdraws instead of reporting restored", FailedPublication),
        ("disposing a Seek preserves its committed position and releases ownership", Dispose)
    };
    private static NativePlaybackBinding Source => new("seek-run", 3, 7, 2, 11, 12, 13, 14, 15, 16, 17);
    private static (NativePlaybackClock Clock, Publication Native) Create()
    {
        var native = new Publication();
        var clock = new NativePlaybackClock(Source, 100, 40, 1.5, 120, native);
        native.Current = clock.Capture();
        return (clock, native);
    }
    private static void SeekAndResume()
    {
        var (clock, native) = Create(); var baseline = clock.Capture();
        using (var change = clock.PreparePausedPosition(Source, 10.123456789))
        {
            Check(ReferenceEquals(clock.Capture(), baseline) && native.Writes == 0, "Preparation published state.");
            change.Commit(); Check(change.Confirm(), "Position was not read back.");
            Check(clock.Capture().Rate == 1.5 && native.Current!.MediaAnchorSeconds == (float)10.123456789,
                "Seek changed rate or bypassed the native Single position.");
            Throws<InvalidOperationException>(change.Commit);
        }
        clock.ObserveScheduled(Source, 102, 105, (float)10.123456789, 1.5);
        Check(clock.TryGetTime(Source, 107, out var media) && media == (float)((float)10.123456789 + 3d),
            "Resume did not advance from the sought media position at the selected rate.");
    }
    private static void Restore()
    {
        var (clock, native) = Create(); var baseline = clock.Capture();
        using var change = clock.PreparePausedPosition(Source, 8);
        change.Commit(); Check(clock.TryGetTime(Source, 110, out var value) && value == 8, "New position was not readable.");
        change.Restore(); Check(change.ConfirmRestored() && !change.Confirm(), "Restoration confirmation is wrong.");
        Check((clock.Capture() with { Revision = baseline.Revision }) == baseline && native.Writes == 2,
            "Restoration lost part of the original paused state.");
        Throws<InvalidOperationException>(change.Restore);
        Check(!clock.TryGetTime(Source, 109, out _), "Backward Seek/rollback rewound the independent DSP watermark.");
    }
    private static void SharedReservation()
    {
        var (clock, _) = Create();
        using (var seek = clock.PreparePausedPosition(Source, 8))
        {
            Throws<GameplayStateException>(() => clock.PreparePausedRate(Source, 2));
            Throws<GameplayStateException>(() => clock.PreparePausedPosition(Source, 8));
        }
        using (var rate = clock.PreparePausedRate(Source, 2))
            Throws<GameplayStateException>(() => clock.PreparePausedPosition(Source, 8));
        using var next = clock.PreparePausedPosition(Source, 9);
        next.Commit(); Check(next.Confirm(), "Disposed reservation prevented a later Seek.");
    }
    private static void Guards()
    {
        var (clock, _) = Create();
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1d, 120.0000001 })
            Throws<GameplayStateException>(() => clock.PreparePausedPosition(Source, value));
        Throws<GameplayStateException>(() => clock.PreparePausedPosition(Source with { RunId = "successor" }, 5));
        using var seek = clock.PreparePausedPosition(Source, 0);
        Throws<GameplayStateException>(() => clock.ObserveScheduled(Source, 101, 105, 40, 1.5));
        Throws<GameplayStateException>(seek.Commit);
        Throws<GameplayStateException>(seek.Restore);
        Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn, "A racing native schedule kept the prepared Seek alive.");
        var (playing, _) = Create(); playing.ObservePlaying(Source, 101, 40, 1.5);
        Throws<GameplayStateException>(() => playing.PreparePausedPosition(Source, 10));
    }
    private static void FailedPublication()
    {
        var (clock, native) = Create();
        using var seek = clock.PreparePausedPosition(Source, 20);
        native.ThrowAfterWrite = true;
        Throws<IOException>(seek.Commit);
        Check(native.Withdrawn && !seek.Confirm(), "Failed publication remained active.");
        Throws<GameplayStateException>(seek.Restore);
        Check(!seek.ConfirmRestored(), "A withdrawn native clock reported restoration.");
    }
    private static void Dispose()
    {
        var (clock, _) = Create(); var seek = clock.PreparePausedPosition(Source, 120);
        seek.Commit(); seek.Dispose(); seek.Dispose();
        Check(clock.Capture().MediaAnchorSeconds == 120, "Dispose rolled back a committed Seek.");
        Throws<ObjectDisposedException>(seek.Commit);
        using var rate = clock.PreparePausedRate(Source, 2); rate.Commit();
        Check(rate.Confirm() && clock.Capture().MediaAnchorSeconds == 120, "Seek leaked reservation or rate change lost position.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class Publication : INativePlaybackClockPublication
    {
        internal NativePlaybackClockState? Current;
        internal bool ThrowAfterWrite, Withdrawn;
        internal int Writes;
        public void Publish(NativePlaybackClockState expected, NativePlaybackClockState next)
        {
            Check(!Withdrawn && ReferenceEquals(Current, expected), "Native publication lost the expected revision.");
            Current = next; Writes++;
            if (ThrowAfterWrite) throw new IOException("native write completed; readback failed");
        }
        public bool IsCurrent(NativePlaybackClockState state) => !Withdrawn && ReferenceEquals(Current, state);
        public void Withdraw(string reason) => Withdrawn = true;
    }
}
