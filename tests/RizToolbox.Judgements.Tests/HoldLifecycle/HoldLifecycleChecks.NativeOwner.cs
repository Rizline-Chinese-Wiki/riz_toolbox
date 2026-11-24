using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// These cases enter the existing prepared broker bridges and the actual
// Backend/HoldSourceCapture. Native fields are supplied by official-export
// fixtures; no caller constructs a native-owner receipt or engine terminal.
internal static partial class HoldLifecycleChecks
{
    internal static readonly (string Name, Action Run)[] NativeOwnerCases =
    {
        ("Hold native owner binds an actual source capture before synchronous first Move", NativeOwnerSynchronous),
        ("Hold native owner keeps the same pair and provider across pause revisions", NativeOwnerPause),
        ("Hold native owner refuses a changed nonzero pointer without replacing the original pair", NativeOwnerChanged),
        ("Hold native owner never turns an initial zero pointer into a valid pair", NativeOwnerInitiallyZero),
        ("Hold native owner retains its original pair while zero awaits independent death", NativeOwnerStableZero),
        ("Hold native owner never reactivates an owner after an observed zero", NativeOwnerZeroRevival),
        ("Hold native owner refuses a pointer changing between the two official reads", NativeOwnerUnstableRead),
        ("Hold native owner rejects wrong exact cached pointer metadata", NativeOwnerMetadata),
        ("Hold native owner stops pointer reads after callback disposal", NativeOwnerDisposal),
        ("Hold native owner refuses callback source capture reentry before native", NativeOwnerReentry),
        ("Hold native owner keeps foreign run pairs separate at reused managed addresses", NativeOwnerNewGeneration),
        ("Hold native owner retains an unknown original judge root without retry", NativeOwnerUnknownCleanup),
        ("Hold native owner Stop requests keep the captured pair without proving terminal", NativeOwnerStops),
        ("Hold native owner initial zero bars later same epoch binding but permits a new run", NativeOwnerInitialZeroBarrier),
        ("Hold native owner never takes a foreign managed class zero as the original owner death", NativeOwnerForeignClassZero)
    };

    private static HoldCoroutineLifecycle.CancellationProvider NativeOwnerProvider(Fixture f)
    {
        var row = f.Snapshot().Rows.Single();
        return f.Backend.HoldLifecycle.GetCancellationProvider(row.Identity.Run, row.Identity.Owner);
    }

    private static void NativeOwnerSynchronous()
    {
        using var f = new Fixture(startDuringPublication: true, synchronousMove: true);
        var snapshot = f.Snapshot(); var row = snapshot.Rows.Single();
        var provider = NativeOwnerProvider(f);
        var pair = row.NativeOwnerPair;
        Check(pair.ManagedReceiver == f.Native.HoldJudge.ToInt64()
            && pair.NativeOwner == f.Native.HoldNativeOwner.ToInt64()
            && pair.ManagedClass == f.Native.HoldManagedOwnerClass
            && provider.NativeOwnerPair == pair && !row.AwaitingIndependentDeath
            && !provider.AwaitingIndependentDeath && f.Native.HoldNativeOwnerReadCount >= 4
            && snapshot.InFlight == 0 && row.StartObserved && row.StartReturned && row.MoveCalls == 1
            && !snapshot.Unknown && !snapshot.FullCoverageKnown,
            "The synchronous actual Start/Move lost the original managed/native pairing or claimed full coverage.");
        var bindings = provider.NativeOwnerBindings;
        Check(bindings.Count >= 1 && bindings.All(binding => binding.Pair == row.NativeOwnerPair
            && binding.Identity.Run.SameGeneration(row.Identity.Run)
            && binding.Identity.Owner == row.Identity.Owner
            && binding.Source.Identity == binding.Identity
            && binding.Source.NativeOwnerPair == binding.Pair),
            "The provider substituted a caller receipt or another capture's native association.");
        if (bindings is IList<HoldNativeOwnerBinding> mutable)
            Expect<NotSupportedException>(mutable.Clear);
    }

    private static void NativeOwnerPause()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var before = f.Snapshot().Rows.Single(); var provider = NativeOwnerProvider(f);
        var originalBinding = provider.NativeOwnerBindings.First();
        f.State.ObservePause(true); f.Backend.TickHoldLifecycle();
        Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "Pause observation changed the original Move result.");
        var after = f.Snapshot().Rows.Single();
        Check(ReferenceEquals(provider, NativeOwnerProvider(f)) && provider.NativeOwnerPair == before.NativeOwnerPair
            && after.NativeOwnerPair == before.NativeOwnerPair && !after.AwaitingIndependentDeath
            && provider.Run.Revision > before.Identity.Run.Revision
            && provider.Run.SameGeneration(before.Identity.Run)
            && ReferenceEquals(provider.NativeOwnerBindings.First().Source, originalBinding.Source)
            && !f.Snapshot().Unknown && !f.Snapshot().FullCoverageKnown,
            "A same-generation pause replaced the native owner or its original retained source.");
    }

    private static void NativeOwnerChanged()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var before = f.Snapshot().Rows.Single(); var provider = NativeOwnerProvider(f);
        var changed = new IntPtr(f.Native.HoldNativeOwner.ToInt64() + 0x1000);
        f.Native.SetHoldNativeOwner(changed);
        f.Move.Original = _ =>
        {
            Check(f.SynchronousIterator() == f.Native.HoldIterator.ToInt64(), "Pair rejection lost actual synchronous iterator attribution.");
            return 0xff;
        };
        Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "A changed native owner suppressed the physical original.");
        var after = f.Snapshot();
        Check(after.Unknown && after.Rows.Single().NativeOwnerPair == before.NativeOwnerPair
            && provider.NativeOwnerPair == before.NativeOwnerPair && after.Rows.Single().MoveCalls == before.MoveCalls
            && after.RootResponsibilities > 0 && after.NaturallyCompleted == 0 && !after.FullCoverageKnown
            && f.Native.HoldNativeOwner == changed,
            "A nonzero owner change was adopted, overwritten or signed as a terminal.");
    }

    private static void NativeOwnerInitiallyZero()
    {
        using var f = new Fixture(beforeCoroutineBindings: x => x.Native.SetHoldNativeOwner(IntPtr.Zero));
        var result = f.Start.Invoke(f.StartFrame());
        var snapshot = f.Snapshot();
        Check(result == unchecked((nuint)f.Native.HoldHandle.ToInt64()) && f.Start.Originals == 1
            && snapshot.Unknown && snapshot.Records == 0 && snapshot.NaturallyCompleted == 0
            && !snapshot.FullCoverageKnown && f.Native.HoldNativeOwnerReadCount >= 2,
            "An initially absent native owner became a valid pair or suppressed its native original.");
    }

    private static void NativeOwnerStableZero()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var pair = f.Snapshot().Rows.Single().NativeOwnerPair; var provider = NativeOwnerProvider(f);
        f.Native.SetHoldNativeOwner(IntPtr.Zero);
        Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "Stable-zero observation changed the physical Move result.");
        var snapshot = f.Snapshot(); var row = snapshot.Rows.Single();
        Check(row.NativeOwnerPair == pair && row.AwaitingIndependentDeath && provider.NativeOwnerPair == pair
            && provider.AwaitingIndependentDeath && provider.HasResponsibility
            && snapshot.NaturallyCompleted == 0 && snapshot.RootResponsibilities > 0 && !snapshot.FullCoverageKnown,
            "A stable zero lost the original nonzero pair or fabricated independent destruction/idle evidence.");
    }

    private static void NativeOwnerZeroRevival()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var originalPointer = f.Native.HoldNativeOwner; var before = f.Snapshot().Rows.Single();
        f.Native.SetHoldNativeOwner(IntPtr.Zero); f.Move.Invoke(f.MoveFrame());
        Check(f.Snapshot().Rows.Single().AwaitingIndependentDeath, "The zero observation was not latched.");
        f.Native.SetHoldNativeOwner(originalPointer); f.Move.Invoke(f.MoveFrame());
        var snapshot = f.Snapshot();
        Check(snapshot.Unknown && snapshot.Rows.Single().AwaitingIndependentDeath
            && snapshot.Rows.Single().NativeOwnerPair == before.NativeOwnerPair && snapshot.NaturallyCompleted == 0
            && !snapshot.FullCoverageKnown,
            "A later same pointer reactivated a source whose zero still lacks independent destruction evidence.");
    }

    private static void NativeOwnerUnstableRead()
    {
        using var f = new Fixture(); var first = f.Native.HoldNativeOwner;
        f.Native.AfterHoldNativeOwnerRead = (ordinal, _) =>
        {
            if (ordinal == 1) f.Native.SetHoldNativeOwner(new(first.ToInt64() + 0x2000));
        };
        f.Start.Invoke(f.StartFrame()); f.Native.AfterHoldNativeOwnerRead = null;
        Check(f.Native.HoldNativeOwnerReadCount == 2 && f.Snapshot().Unknown && f.Snapshot().Records == 0
            && f.Start.Originals == 1 && f.Snapshot().NaturallyCompleted == 0,
            "A torn cached-pointer observation published a stable owner receipt.");
    }

    private static void NativeOwnerMetadata()
    {
        foreach (var fault in Enum.GetValues<FakeNative.HoldNativeOwnerMetadataFault>().Where(value => value != FakeNative.HoldNativeOwnerMetadataFault.None))
        {
            using var f = new Fixture(beforeCoroutineBindings: x => x.Native.SetHoldNativeOwnerMetadataFault(fault));
            f.Start.Invoke(f.StartFrame());
            Check(f.Snapshot().Unknown && f.Snapshot().Records == 0 && f.Start.Originals == 1
                && f.Native.HoldNativeOwnerReadCount == 0 && !f.Snapshot().FullCoverageKnown,
                "Wrong cached-pointer declaration/layout reached a field read: " + fault);
        }
    }

    private static void NativeOwnerDisposal()
    {
        using var f = new Fixture(); var disposed = false;
        f.Native.AfterHoldNativeOwnerRead = (_, _) =>
        {
            if (disposed) return; disposed = true; f.Backend.Dispose();
        };
        f.Start.Invoke(f.StartFrame()); f.Native.AfterHoldNativeOwnerRead = null;
        Check(disposed && f.Native.HoldNativeOwnerReadCount == 1 && f.Start.Originals == 1
            && f.Snapshot().Records == 0 && f.Snapshot().NaturallyCompleted == 0,
            "Callback disposal permitted another cached-pointer read or signed a complete pairing.");
    }

    private static void NativeOwnerReentry()
    {
        using var f = new Fixture(); var reentered = false;
        f.Native.AfterHoldNativeOwnerRead = (_, _) =>
        {
            if (reentered) return; reentered = true;
            var before = (f.Native.FieldReads, f.Native.Invocations, f.Native.NewRoots, f.Native.FreeHandleAttempts);
            Expect<InvalidOperationException>(() => f.Backend.CaptureHoldStart(f.StartFrame()));
            Expect<InvalidOperationException>(() => f.Backend.CaptureHoldMove(f.MoveFrame()));
            Check(before == (f.Native.FieldReads, f.Native.Invocations, f.Native.NewRoots, f.Native.FreeHandleAttempts),
                "Source-capture callback reentry reached native metadata/root ownership.");
        };
        f.Start.Invoke(f.StartFrame()); f.Native.AfterHoldNativeOwnerRead = null;
        Check(reentered && f.Snapshot().Records == 1 && !f.Snapshot().Unknown
            && f.Snapshot().Rows.Single().NativeOwnerPair.NativeOwner != 0 && f.Start.Originals == 1,
            "A refused metadata callback reentry revoked the original valid pairing.");
        f.Native.BeginLowerIl2CppAudit();
        try { Task.Run(() => Expect<InvalidOperationException>(() => f.Backend.CaptureHoldStart(f.StartFrame()))).GetAwaiter().GetResult(); }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0,
            "A worker source capture reached an official export.");
    }

    private static void NativeOwnerNewGeneration()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var old = f.Snapshot().Rows.Single(); var provider = NativeOwnerProvider(f);
        var binding = provider.NativeOwnerBindings.First();
        f.State.Scene("Play-new-native-owner");
        f.State.Controller(f.Native.GameplayInformation.ToInt64(), true, false,
            "synthetic-hold-native-owner-next", f.Native.GameplayController.ToInt64());
        f.Backend.TickHoldLifecycle();
        f.Native.SetHoldNativeOwner(new(f.Native.HoldNativeOwner.ToInt64() + 0x3000));
        f.Start.Invoke(f.StartFrame()); var next = f.Snapshot().Rows.Single(); var nextProvider = NativeOwnerProvider(f);
        Check(!ReferenceEquals(provider, nextProvider) && provider.Retired
            && !old.Identity.Run.SameGeneration(next.Identity.Run) && next.Identity.Owner == old.Identity.Owner
            && next.Identity.Enumerator == old.Identity.Enumerator && next.NativeOwnerPair != old.NativeOwnerPair
            && provider.NativeOwnerPair == old.NativeOwnerPair && binding.Pair == old.NativeOwnerPair
            && binding.Identity.Run.SameGeneration(old.Identity.Run)
            && !ReferenceEquals(nextProvider.NativeOwnerBindings.First().Source, binding.Source)
            && !f.Snapshot().FullCoverageKnown,
            "Reused managed addresses borrowed the retired epoch's native owner/source.");
    }

    private static void NativeOwnerUnknownCleanup()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var pair = f.Snapshot().Rows.Single().NativeOwnerPair; var provider = NativeOwnerProvider(f);
        var failed = f.Native.NoteViewHandlesFor(f.Native.HoldJudge).First();
        f.Native.FailFreeHandle = failed; f.RetainStorage = true; UnknownOwners.Add(f);
        f.State.Scene("Play-native-owner-retired"); f.Backend.TickHoldLifecycle();
        var snapshot = f.Snapshot();
        Check(snapshot.Unknown && snapshot.UnknownRootFrees == 1 && snapshot.RootResponsibilities == 1
            && provider.NativeOwnerPair == pair && provider.HasResponsibility
            && f.Native.NoteViewFreeLog.Count(row => row.InjectedFailure && row.Handle == failed && row.Target == f.Native.HoldJudge) == 1,
            "Unknown original owner-root cleanup lost its exact pair or publication responsibility.");
        var attempts = f.Native.FreeHandleAttempts; f.Backend.TickHoldLifecycle(); f.Backend.Dispose(); f.Backend.Dispose();
        Check(f.Native.FreeHandleAttempts == attempts && !f.Snapshot().FullCoverageKnown,
            "Tick/withdrawal retried an unknown original judge-root free.");
    }

    private static void NativeOwnerStops()
    {
        using var f = new StopFixture(); var item = f.Schedule();
        var before = f.Hold.Snapshot().Rows.Single(); var provider = NativeOwnerProvider(f.Hold);
        f.Invoke(CoroutineStopOperation.Enumerator, item.Iterator);
        f.Invoke(CoroutineStopOperation.All);
        var after = f.Hold.Snapshot();
        Check(after.Rows.Single().NativeOwnerPair == before.NativeOwnerPair && provider.NativeOwnerPair == before.NativeOwnerPair
            && after.Rows.Single().CancellationRequests >= 1 && after.NaturallyCompleted == 0
            && provider.HasResponsibility && !after.FullCoverageKnown && !provider.AwaitingIndependentDeath,
            "A Stop request changed the source pairing or fabricated engine terminal/idle evidence.");
    }

    private static void NativeOwnerInitialZeroBarrier()
    {
        using var f = new Fixture(beforeCoroutineBindings: x => x.Native.SetHoldNativeOwner(IntPtr.Zero));
        f.Start.Invoke(f.StartFrame());
        Check(f.Snapshot().Records == 0 && f.Snapshot().Unknown, "Initial zero published a native pair.");
        f.Native.SetHoldNativeOwner(new(0x71504010)); f.Start.Invoke(f.StartFrame());
        Check(f.Start.Originals == 2 && f.Snapshot().Records == 0 && f.Snapshot().NaturallyCompleted == 0
            && !f.Snapshot().FullCoverageKnown,
            "An observed initial zero was forgotten and the same epoch accepted a later nonzero owner.");
        f.State.Scene("Play-after-initial-owner-zero");
        f.State.Controller(f.Native.GameplayInformation.ToInt64(), true, false,
            "synthetic-hold-after-initial-zero", f.Native.GameplayController.ToInt64());
        f.Backend.TickHoldLifecycle(); f.Start.Invoke(f.StartFrame());
        var row = f.Snapshot().Rows.Single();
        Check(row.NativeOwnerPair.NativeOwner == f.Native.HoldNativeOwner.ToInt64()
            && !row.AwaitingIndependentDeath && f.Start.Originals == 3 && !f.Snapshot().FullCoverageKnown,
            "The previous run's zero tombstone was borrowed by the new run at reused managed addresses.");
    }

    private static void NativeOwnerForeignClassZero()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var before = f.Snapshot().Rows.Single(); var reads = f.Native.HoldNativeOwnerReadCount; var changed = false;
        f.Native.AfterHoldNativeOwnerRead = (ordinal, _) =>
        {
            if (ordinal != reads + 1 || changed) return;
            changed = true; f.Native.SetHoldNativeOwner(IntPtr.Zero); f.Native.SetHoldManagedOwnerClassToForeign();
        };
        Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "Managed class change suppressed the original Move result.");
        f.Native.AfterHoldNativeOwnerRead = null;
        var row = f.Snapshot().Rows.Single();
        Check(changed && f.Native.HoldNativeOwnerReadCount == reads + 1 && f.Snapshot().Unknown
            && row.NativeOwnerPair == before.NativeOwnerPair && !row.AwaitingIndependentDeath
            && row.MoveCalls == before.MoveCalls && !f.Snapshot().FullCoverageKnown,
            "A foreign-class zero was signed as the original owner's stable zero or pairing.");
        f.Native.RestoreHoldManagedOwnerClass();
    }
}
