using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime.Hooks;
using static Assert;
using TerminalFixture = NativeLocalTerminalCoroutineRegression.Fixture;

internal static class NativeLocalTerminalRecycleRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Terminal completed roots recycle across more than sixteen factories, raw address reuse and role changes", Recycling),
        ("Terminal withdrawal waits for unrelated native calls without freeing or republishing the old enum", WithdrawalWait),
        ("Terminal withdrawn worker or main reentry revokes cleanup before spending independent roots", WithdrawalReentry),
        ("Terminal root free failure still attempts known independent roots once and never reuses the slot", FreeFailure),
        ("Terminal calls arriving after free keep the spent slot unavailable without repeating its root operations", PostFreeActivity),
        ("Terminal final drain worker faults retain the spent record and cannot report successful recycling", FinalDrainFault),
        ("Terminal observed-end query detects worker faults raised during its activity read", EndQueryFault)
    };
    private static long FinishFade(TerminalFixture f, long? address = null, bool start = true)
    {
        var value = f.Backend.Factory(TerminalCoroutineRole.Fade, address);
        if (start) f.Backend.EnumStart(TerminalCoroutineRole.Fade, value);
        f.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 1); f.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0); return value;
    }
    private static void Recycling()
    {
        using var f = new TerminalFixture(); long address = 0;
        for (var index = 0; index < 64; ++index)
        {
            address = FinishFade(f, index % 2 == 0 || address == 0 ? null : address);
            Equal(1, f.Owner.Read().LiveRecords); Equal(2, f.Backend.Roots); Check(f.Owner.TryConfirmObservedEnded());
            Equal(1, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.Roots); Equal(0, f.Owner.Read().LiveRecords);
            Equal(index+1, f.Owner.Read().RetiredRecords); Equal(index+1, f.Owner.Read().NaturallyEnded);
            Check(f.Owner.TryConfirmObservedEnded());
        }
        Equal(64, f.Owner.Read().Created); Equal(128, f.Backend.Frees.Count); Check(f.Backend.Frees.All(slot => slot is 0 or 16));
        f.CaptureCutout(); Check(!f.Owner.TryConfirmObservedEnded()); var cutout = f.Backend.LastEnum;
        var beforeFree = f.Backend.FreeCalls;
        f.Backend.Move(TerminalCoroutineRole.Cutout, cutout, -1, 0, () => Equal(0, f.Owner.RecycleNaturallyEnded()));
        Equal(beforeFree, f.Backend.FreeCalls); Check(f.Owner.Read().CutoutNaturallyEnded);
        Equal(1, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.Roots); Check(f.Owner.TryConfirmObservedEnded());
        Equal(65, f.Owner.Read().Created); Equal(65, f.Owner.Read().RetiredRecords); Check(f.Owner.Read().ResultReturned);
        var fresh = f.Backend.Factory(TerminalCoroutineRole.Fade, address); Check(!f.Owner.TryConfirmObservedEnded());
        f.Backend.Move(TerminalCoroutineRole.Fade, fresh, 1, 1); f.Backend.Move(TerminalCoroutineRole.Fade, fresh, -1, 0);
        Equal(1, f.Owner.RecycleNaturallyEnded()); Check(f.Owner.Read().CutoutNaturallyEnded && !f.Owner.Read().Unknown);
    }
    private static void WithdrawalWait()
    {
        using var f = new TerminalFixture(); var value = FinishFade(f); f.Backend.ActivityReads = 0;
        f.Backend.DuringActivity = index => { if (index == 2) f.Backend.InFlight = 1; };
        Equal(0, f.Owner.RecycleNaturallyEnded()); Equal(2, f.Backend.Roots); Equal(0, f.Backend.FreeCalls);
        Check(!f.Owner.TryConfirmObservedEnded() && !f.Owner.Read().Unknown);
        f.Backend.DuringActivity = null; f.Backend.InFlight = 0;
        Equal(1, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.Roots); Check(f.Owner.TryConfirmObservedEnded());
        var next = FinishFade(f, value, start: false); Equal(value, next); Equal(1, f.Backend.Roots);
        Equal(1, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.Roots); Equal(2, f.Owner.Read().RetiredRecords);
    }
    private static void WithdrawalReentry()
    {
        foreach (var worker in new[] { false, true })
        {
            using var f = new TerminalFixture(); var value = FinishFade(f); f.Backend.ActivityReads = 0;
            f.Backend.DuringActivity = index => {
                if (index != 2) return;
                var frame = f.Backend.Frame(NativeCoroutineKind.MoveNext, value);
                if (worker) Task.Run(() => f.Owner.ObserveFault(frame, "Retiring enum worker")).GetAwaiter().GetResult();
                else Check(f.Owner.CaptureMove(TerminalCoroutineRole.Fade, frame) is null);
            };
            Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
            Equal(0, f.Backend.FreeCalls); Equal(2, f.Backend.Roots); Check(f.Owner.Read().Unknown); Equal(1, f.Owner.Read().LiveRecords);
        }
    }
    private static void FreeFailure()
    {
        using var f = new TerminalFixture(); FinishFade(f); f.Backend.FailRootFree.Add(16);
        Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
        Check(f.Backend.Frees.SequenceEqual(new[] { 16, 0 })); Equal(1, f.Backend.Roots); Equal(1, f.Backend.FreeCalls);
        Check(f.Owner.Read().Unknown && f.Owner.Read().LiveRecords == 1 && f.Owner.Read().RetiredRecords == 0);
        Throws<InvalidOperationException>(() => f.Owner.RecycleNaturallyEnded()); Equal(1, f.Backend.FreeCalls);
        f.Backend.Factory(TerminalCoroutineRole.Fade); Equal(1, f.Backend.Retains); // The second attempt forwards original but acquires no new root.
    }
    private static void PostFreeActivity()
    {
        using var f = new TerminalFixture(); FinishFade(f); f.Backend.DuringFree = index => { if (index == 0) f.Backend.InFlight = 1; };
        Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
        Equal(0, f.Backend.Roots); Equal(1, f.Backend.FreeCalls); Equal(1, f.Owner.Read().LiveRecords); Equal(0, f.Owner.Read().RetiredRecords);
        f.Backend.InFlight = 0; f.Backend.DuringFree = null;
        Throws<InvalidOperationException>(() => f.Owner.RecycleNaturallyEnded()); Equal(1, f.Backend.FreeCalls);
    }
    private static void FinalDrainFault()
    {
        using var f = new TerminalFixture(); var value = FinishFade(f); f.Backend.ActivityReads = 0;
        f.Backend.DuringActivity = index => {
            if (index == 3) Task.Run(() => f.Owner.ObserveFault(f.Backend.Frame(NativeCoroutineKind.MoveNext, value), "Worker during final drain")).GetAwaiter().GetResult();
        };
        Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
        Equal(0, f.Backend.Roots); Equal(1, f.Owner.Read().LiveRecords); Equal(0, f.Owner.Read().RetiredRecords); Check(f.Owner.Read().Unknown);
        f.Backend.DuringActivity = null;
        Throws<InvalidOperationException>(() => f.Owner.RecycleNaturallyEnded()); Equal(1, f.Backend.FreeCalls);
    }
    private static void EndQueryFault()
    {
        using var f = new TerminalFixture(); var value = FinishFade(f); f.Backend.ActivityReads = 0;
        f.Backend.DuringActivity = index => {
            if (index == 1) Task.Run(() => f.Owner.ObserveFault(f.Backend.Frame(NativeCoroutineKind.MoveNext, value), "Worker during ended query")).GetAwaiter().GetResult();
        };
        Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.TryConfirmObservedEnded());
        Check(f.Owner.Read().Unknown); Equal(2, f.Backend.Roots); Equal(0, f.Backend.FreeCalls);
    }
}
