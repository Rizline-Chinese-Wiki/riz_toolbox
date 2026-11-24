using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using static Assert;

internal static class NativeTweenTargetDeathTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Tween target death captures separate Component GameObject Transform live roots and confirms two later frames", ConfirmConcreteObjects),
        ("Tween target death cannot use its Return frame a repeated frame or only one dead frame", LaterFrames),
        ("Tween target death initially dead target remains unproven and never borrows a TweenInfo death ticket", InitiallyDead),
        ("Tween target death requires all concrete objects and rejects changed hierarchy while retaining every root", AllObjectsAndHierarchy),
        ("Tween target death receipt rereads native objects and refuses resurrection before another pump", ReceiptFreshness),
        ("Tween target death rejects same pointer foreign handles copied diagnostics and a foreign issuer", PrivateIdentity),
        ("Tween target death reentrant sampling cannot acquire a second proof interval", Reentry)
    };
    private static void ConfirmConcreteObjects()
    {
        using var f = new TweenCompletionFixture(target: true); var baseline = f.Owner.ReadTweenTargetDeaths();
        Check(baseline.Count == 3 && baseline.Select(row => row.Role).Distinct().Count() == 3
            && baseline.All(row => row.LiveFrame == 10 && row.ReturnFrame == -1 && !row.Production));
        f.ConfirmDeaths(); var receipt = f.Owner.ReadTweenTargetDeathReceipt(f.Backend.Tween);
        Check(receipt is not null); receipt!.VerifyCurrent(f.Owner, f.Backend.Tween);
        Check(f.Owner.ReadTweenTargetDeaths().All(row => row.ReturnFrame == 10 && row.FirstDeathFrame == 11
            && row.ConfirmedFrame == 12 && row.SourceEpoch == 41 && !row.Unknown) && f.Backend.Frees == 0);
    }
    private static void LaterFrames()
    {
        using var f = new TweenCompletionFixture(target: true); f.ReturnAndSeal();
        f.Backend.DeadTween(10); f.Backend.DeadTargets(10); f.Owner.PumpControlledTweenDeathsForTests();
        Check(f.Owner.ReadTweenTargetDeathReceipt(f.Backend.Tween) is null);
        f.Backend.DeadTween(11); f.Backend.DeadTargets(11); f.Owner.PumpControlledTweenDeathsForTests(); f.Owner.PumpControlledTweenDeathsForTests();
        Check(f.Owner.ReadTweenTargetDeathReceipt(f.Backend.Tween) is null && f.Owner.ReadTweenTargetDeaths().All(row => row.ConfirmedFrame < 0));
        f.Backend.DeadTween(12); f.Backend.DeadTargets(12); f.Owner.PumpControlledTweenDeathsForTests();
        Check(f.Owner.ReadTweenTargetDeathReceipt(f.Backend.Tween) is not null && f.Backend.Frees == 0);
    }
    private static void InitiallyDead()
    {
        using var f = new TweenCompletionFixture(target: true, initiallyDeadTarget: true); f.ConfirmDeaths();
        Check(f.Owner.ReadTweenDeathReceipt(f.Backend.Tween) is not null && f.Owner.ReadTweenTargetDeathReceipt(f.Backend.Tween) is null
            && f.Owner.ReadTweenTargetDeaths().All(row => row.LiveFrame < 0) && !f.Owner.Unknown && f.Backend.Frees == 0);
    }
    private static void AllObjectsAndHierarchy()
    {
        using (var f = new TweenCompletionFixture(target: true))
        {
            f.ReturnAndSeal(); f.Backend.DeadTween(11); f.Backend.DeadTargets(11); f.Owner.PumpControlledTweenDeathsForTests();
            f.Backend.DeadTween(12);
            f.Backend.Targets[501] = f.Backend.Targets[501] with { Frame = 12 };
            f.Backend.Targets[502] = f.Backend.Targets[502] with { Frame = 12 };
            f.Owner.PumpControlledTweenDeathsForTests();
            Check(f.Owner.ReadTweenTargetDeathReceipt(f.Backend.Tween) is null && f.Owner.ReadTweenTargetDeaths().Single(row => row.Role == "Transform").ConfirmedFrame < 0);
        }
        using (var f = new TweenCompletionFixture(target: true))
        {
            f.ReturnAndSeal(); f.Backend.ChangedBinding = true;
            Throws<InvalidOperationException>(() => f.Owner.PumpControlledTweenDeathsForTests());
            Check(f.Owner.Unknown && f.Backend.Frees == 0);
        }
    }
    private static void ReceiptFreshness()
    {
        using var f = new TweenCompletionFixture(target: true); f.ConfirmDeaths();
        var receipt = f.Owner.ReadTweenTargetDeathReceipt(f.Backend.Tween)!;
        var prior = f.Backend.Targets[503]; f.Backend.Targets[503] = prior with { Frame = 13, Alive = true, CachedNative = 1503 };
        Throws<InvalidOperationException>(() => receipt.VerifyCurrent(f.Owner, f.Backend.Tween));
        Check(f.Backend.Frees == 0);
    }
    private static void PrivateIdentity()
    {
        using var f = new TweenCompletionFixture(target: true); f.ConfirmDeaths();
        using var root = new NativeRuntime.RootHandle(999, _ => { }, _ => new(101), f.Main.VerifyAccess);
        Throws<InvalidOperationException>(() => f.Owner.ReadTweenTargetDeathReceipt(root));
        var copy = f.Owner.ReadTweenTargetDeaths().Single(row => row.Role == "Component") with { ConfirmedFrame = 1000 };
        Check(copy.ConfirmedFrame == 1000 && f.Owner.ReadTweenTargetDeaths().Single(row => row.Role == "Component").ConfirmedFrame == 12);
        Throws<InvalidOperationException>(() => new NativeLocalTweenWorkOwner.TargetDeathReceipt(new object(), f.Owner, copy, null!, 41));
        Check(f.Backend.Frees == 0);
    }
    private static void Reentry()
    {
        using var f = new TweenCompletionFixture(target: true); f.ReturnAndSeal();
        f.Backend.DuringTargetRead = () => f.Owner.PumpControlledTweenDeathsForTests();
        Throws<InvalidOperationException>(() => f.Owner.PumpControlledTweenDeathsForTests());
        Check(f.Owner.Unknown && f.Backend.Frees == 0);
    }
}
