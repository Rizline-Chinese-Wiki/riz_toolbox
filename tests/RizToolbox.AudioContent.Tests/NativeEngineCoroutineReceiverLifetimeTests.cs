using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

// Uses the lifecycle and retirement predicates used by the real launch path.
// Copied sources, owner pairs and frames are controlled observations, not Unity
// execution, production IsFor authority or cancellation-completed evidence.
internal static class NativeEngineCoroutineReceiverLifetimeTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Receiver lifetime keeps two live roles and immutable original native owners independently", LiveAndCopies),
        ("Controller stable zero remains Pending while the actual death pump can reach its first later frame", ControllerLaterFrame),
        ("Controller and EventTrigger each need their own zero and independent death source", IndependentRoles),
        ("Aggregate cutout closure death waits for EventTrigger cached zero and never restores Ready", AggregateSource),
        ("Receiver death sources reject foreign sessions identities roles and unconfirmed same-frame clone death", SourceRejection),
        ("Receiver lifetime rejects changed root identity class and copied native owner", ReceiverDrift),
        ("Receiver zero resurrection and withdrawn source death stay permanently Unknown", Resurrection),
        ("Each receiver blocks new owned entries after zero or independent death without blocking the other live role", LateEntries),
        ("Previously captured engine contexts complete and dispose after receiver zero with all ownership still Pending", FrozenContexts),
        ("Receiver observation pump preserves source order and does not run destruction after a source failure", PumpOrder),
        ("Receiver lifetime refuses workers before mutation and validates the fixed two-role registration", ThreadsAndRegistration),
        ("Observer retirement requires exact final native generation and does not claim engine cancellation", ExactRetirement),
        ("Actual inline cleanup without a final Release receipt remains Pending and retains tracker identity", InlineCleanup),
        ("Observer retirement rejects missing duplicate or mismatched node generation owner Enumerator and Start sequence", RetirementJoin),
        ("Observer retirement retains outstanding contexts untracked calls and published tracker records without engine identities", RetirementHolds)
    };
    private static void Check(bool value)
    { if (!value) throw new InvalidOperationException("Receiver lifetime check failed."); }
    private static void Throws<T>(Action operation) where T : Exception
    { try { operation(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionIdentity Session = new("receiver.lifetime", new("test", "song"), 1, "chart", 1);
        internal readonly NativeUnityCoroutineOwner[] Pairs = { new(101, 0xA000, new(0xC000)), new(201, 0xB000, new(0xD000)) };
        internal readonly ActiveDestructionIdentity Destruction = new(101, 102, 103, 104, 105);
        internal readonly NativeCutoutTrigger[] Triggers = { new(301, 401, BitConverter.SingleToInt32Bits(1f)) };
        internal readonly NativeCutoutEventObservation Source;
        internal readonly NativeEngineCoroutineReceiverLifetime Owner;
        internal Fixture()
        {
            Source = new(101, 201, 202, 203, 204, 1, true, Triggers);
            Owner = new(Main, Session, Pairs, Destruction, Source);
        }
        internal void Observe(int index, long? cached = null)
            => Owner.ObserveReceiver((NativeEngineCoroutineReceiverRole)index,
                new(Pairs[index].ManagedReceiver, cached ?? Pairs[index].NativeOwner, Pairs[index].ManagedClass));
        internal NativeLocalActiveDestructionSnapshot Controller(bool dead = false)
            => new(Session, dead ? NativeLocalActiveDestructionPhase.CloneDestroyed : NativeLocalActiveDestructionPhase.AwaitingLaterFrame,
                Destruction, 10, dead ? 11 : -1, -1, -1, false, dead, false, false, false, 5, null);
        internal NativeLocalCutoutEventSourceSnapshot Events(bool alive = true)
            => new(Session, Source with { Alive = alive }, false, false, true, false, 0, 0, 0, 7, null);
        public void Dispose() => Main.Dispose();
    }

    private static void LiveAndCopies()
    {
        using var f = new Fixture(); var before = f.Owner.Read();
        Check(before.MayCommit && before.Pending && !before.Unknown && before.Receivers.Count == 2);
        f.Pairs[0] = new(999, 888, new(777));
        Check(f.Owner.Read().Receivers[0].Retained == before.Receivers[0].Retained);
        f.Triggers[0] = new(999, 401, 0);
        Throws<InvalidOperationException>(() => f.Owner.ObserveEventTriggerDeath(true, f.Events(false)));
        Check(f.Owner.Unknown && before.MayCommit && !before.Unknown);
    }
    private static void ControllerLaterFrame()
    {
        using var f = new Fixture(); f.Observe(0, 0);
        var sourceTicks = 0; var controllerTicks = 0;
        Check(!NativeEngineCoroutineRuntimeCoordinator.PumpDeathSources(() => ++sourceTicks, () => { ++controllerTicks; return false; }));
        f.Owner.ObserveControllerDeath(true, false, f.Controller());
        var pending = f.Owner.Read(); Check(!pending.MayCommit && pending.AwaitingIndependentDeath && !pending.Unknown);
        Check(pending.Receivers[0].Phase == NativeEngineCoroutineReceiverPhase.AwaitingIndependentDeath
            && pending.Receivers[0].Retained.NativeOwner == 0xA000 && pending.Receivers[0].CachedNativeOwner == 0);
        f.Observe(0, 0); f.Owner.ObserveControllerDeath(true, false, f.Controller());
        Check(NativeEngineCoroutineRuntimeCoordinator.PumpDeathSources(() => ++sourceTicks, () => { ++controllerTicks; return true; }));
        f.Owner.ObserveControllerDeath(true, true, f.Controller(true));
        Check(sourceTicks == 2 && controllerTicks == 2 && !f.Owner.Read().Unknown
            && f.Owner.Read().Receivers[0].Phase == NativeEngineCoroutineReceiverPhase.ConfirmedDead && !f.Owner.Read().MayCommit);
    }
    private static void IndependentRoles()
    {
        using var f = new Fixture(); f.Observe(0, 0); f.Observe(1, 0);
        f.Owner.ObserveControllerDeath(true, true, f.Controller(true));
        var value = f.Owner.Read(); Check(value.Receivers[0].Phase == NativeEngineCoroutineReceiverPhase.ConfirmedDead
            && value.Receivers[1].Phase == NativeEngineCoroutineReceiverPhase.AwaitingIndependentDeath);
        f.Owner.ObserveEventTriggerDeath(true, f.Events()); Check(f.Owner.Read().AwaitingIndependentDeath);
        f.Owner.ObserveEventTriggerDeath(true, f.Events(false)); value = f.Owner.Read();
        Check(value.Receivers.All(row => row.Phase == NativeEngineCoroutineReceiverPhase.ConfirmedDead)
            && !value.AwaitingIndependentDeath && !value.MayCommit && value.Pending && !value.Unknown);
        Check(value.Receivers[0].Retained.ManagedReceiver == 101 && value.Receivers[1].Retained.ManagedReceiver == 201);
    }
    private static void AggregateSource()
    {
        using var f = new Fixture(); f.Owner.ObserveEventTriggerDeath(true, f.Events(false)); f.Observe(1);
        Check(f.Owner.Read().Receivers[1].Phase == NativeEngineCoroutineReceiverPhase.AwaitingIndependentDeath
            && !f.Owner.Read().MayCommit && !f.Owner.Read().Unknown);
        f.Observe(1, 0); f.Owner.ObserveEventTriggerDeath(true, f.Events(false));
        Check(f.Owner.Read().Receivers[1].Phase == NativeEngineCoroutineReceiverPhase.ConfirmedDead && !f.Owner.Read().MayCommit);
        Check(f.Owner.Read().Receivers[0].Phase == NativeEngineCoroutineReceiverPhase.Live);
    }
    private static void SourceRejection()
    {
        foreach (var mode in new[] { "session", "identity", "same-frame", "not-ticked", "source" })
        {
            using var f = new Fixture(); f.Observe(0, 0); var value = f.Controller(true);
            if (mode == "session") value = value with { Session = f.Session with { RunGeneration = 2 } };
            if (mode == "identity") value = value with { Identity = f.Destruction with { CloneGameObject = 999 } };
            if (mode == "same-frame") value = value with { CloneConfirmedFrame = value.CutoutObservedFrame };
            if (mode == "not-ticked")
            {
                f.Owner.ObserveControllerDeath(true, false, value);
                Check(f.Owner.Read().AwaitingIndependentDeath && !f.Owner.Unknown); continue;
            }
            Throws<InvalidOperationException>(() => f.Owner.ObserveControllerDeath(mode != "source", true, value));
            Check(f.Owner.Unknown && f.Owner.Read().Pending);
        }
        foreach (var mode in new[] { "session", "controller", "receiver", "list", "version", "trigger", "source" })
        {
            using var f = new Fixture(); f.Observe(1, 0); var value = f.Events(false);
            if (mode == "session") value = value with { Session = f.Session with { SessionId = "foreign" } };
            var source = value.Source!;
            source = mode switch {
                "controller" => source with { Controller = 999 }, "receiver" => source with { EventTrigger = 999 },
                "list" => source with { List = 999 }, "version" => source with { Version = 2 },
                "trigger" => source with { Triggers = new[] { new NativeCutoutTrigger(302, 401, 0) } }, _ => source };
            value = value with { Source = source };
            Throws<InvalidOperationException>(() => f.Owner.ObserveEventTriggerDeath(mode != "source", value)); Check(f.Owner.Unknown);
        }
    }
    private static void ReceiverDrift()
    {
        foreach (var role in new[] { 0, 1 }) foreach (var mode in new[] { "root", "class", "owner" })
        {
            using var f = new Fixture(); var before = f.Owner.Read().Receivers[role].Retained;
            var value = new NativeUnityCoroutineOwnerObservation(before.ManagedReceiver, before.NativeOwner, before.ManagedClass);
            value = mode switch { "root" => value with { ManagedReceiver = 999 }, "class" => value with { ManagedClass = new(999) },
                _ => value with { CachedNativeOwner = 999 } };
            Throws<InvalidOperationException>(() => f.Owner.ObserveReceiver((NativeEngineCoroutineReceiverRole)role, value));
            Check(f.Owner.Unknown && f.Owner.Read().Receivers[role].Retained == before);
        }
    }
    private static void Resurrection()
    {
        foreach (var role in new[] { 0, 1 })
        {
            using var f = new Fixture(); f.Observe(role, 0);
            Throws<InvalidOperationException>(() => f.Observe(role)); Check(f.Owner.Unknown && f.Owner.Read().Pending);
            Throws<InvalidOperationException>(() => f.Observe(role, 0));
        }
        using var source = new Fixture(); source.Owner.ObserveEventTriggerDeath(true, source.Events(false));
        Throws<InvalidOperationException>(() => source.Owner.ObserveEventTriggerDeath(true, source.Events())); Check(source.Owner.Unknown);
    }
    private static void LateEntries()
    {
        foreach (var role in new[] { 0, 1 }) foreach (var proofFirst in new[] { false, true })
        {
            using var f = new Fixture();
            if (!proofFirst) f.Observe(role, 0);
            else if (role == 0) f.Owner.ObserveControllerDeath(true, true, f.Controller(true));
            else f.Owner.ObserveEventTriggerDeath(true, f.Events(false));
            f.Owner.RequireNewOwnedEntry(f.Pairs[1 - role].NativeOwner); Check(!f.Owner.Unknown);
            Throws<InvalidOperationException>(() => f.Owner.RequireNewOwnedEntry(f.Pairs[role].NativeOwner));
            Check(f.Owner.Unknown && f.Owner.Read().Pending);
        }
    }
    private static NativeEngineNodeCompletion Done(NativeEngineNodeKind kind, long output = 0) => new() {
        Version = 1, Size = 32, Flags = NativeEngineNodeExitFlags.Returned | NativeEngineNodeExitFlags.ReturnByteValid,
        RawReturn = 1, StartOutputNode = new(output),
        OutputFlags = kind == NativeEngineNodeKind.Start ? NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read : 0 };
    private sealed class EngineFixture : IDisposable
    {
        internal readonly Fixture Receivers = new();
        internal readonly object Tracker = new();
        internal NativeCoroutineCancellationIdentity Row;
        internal readonly NativeEngineCoroutineIdentityLedger Ledger;
        internal readonly NativeEngineNodeFrame Start, Execute;
        internal readonly NativeEngineCoroutineIdentityLedger.Call StartCall, ExecuteCall;
        internal EngineFixture()
        {
            Row = new(Tracker, 101, 0, 1, 1001, 0, false, false, true, false, false, true);
            Ledger = new(Receivers.Main, new(Receivers.Main), new[] {
                new NativeCoroutineCancellationIdentityProvider(Tracker, 101, () => new[] { Row }, value => Row.SameRecord(value)) },
                new[] { new NativeEngineCoroutineOwnerPair(101, 0xA000) });
            Start = new() { Version = 1, Size = 112, Kind = NativeEngineNodeKind.Start, Arg0 = new(0xA000), Arg1 = new(1001),
                Arg3 = new(0x9000), Caller = new(0x8000), Sequence = 1 };
            Execute = new() { Version = 1, Size = 112, Kind = NativeEngineNodeKind.Execute, Arg0 = new(301), Node = new(301),
                Owner = new(0xA000), EnumeratorCache = new(1001), EnumeratorHandle = 401, EnumeratorType = 2, ReferenceCount = 1,
                Caller = new(0x8000), Sequence = 2, SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid };
            StartCall = Ledger.Capture(Start); ExecuteCall = Ledger.Capture(Execute); Check(!Ledger.Unknown);
        }
        internal void Complete(long startOutput)
        {
            Ledger.Complete(ExecuteCall, Execute, Done(Execute.Kind)); ExecuteCall.Dispose();
            Ledger.Complete(StartCall, Start, Done(Start.Kind, startOutput)); StartCall.Dispose();
            Row = Row with { StartReturned = true, NaturalTerminal = true }; Ledger.RefreshTrackedIdentities();
        }
        public void Dispose() => Receivers.Dispose();
    }
    private static void FrozenContexts()
    {
        using var f = new EngineFixture(); f.Receivers.Observe(0, 0); f.Complete(301);
        var value = f.Ledger.Read();
        Check(!value.Unknown && value.Outstanding == 0 && value.Pending && !value.EngineCancelled
            && value.Nodes.Nodes.Single().ActiveExecutions == 0 && value.Identities.Single().StartDisposed);
        Check(f.Receivers.Owner.Read().AwaitingIndependentDeath && !f.Receivers.Owner.Read().Unknown
            && f.Receivers.Owner.Read().Receivers[0].Retained.NativeOwner == 0xA000);
    }
    private static void PumpOrder()
    {
        var order = new List<int>();
        Check(NativeEngineCoroutineRuntimeCoordinator.PumpDeathSources(() => order.Add(1), () => { order.Add(2); return true; }));
        Check(order.SequenceEqual(new[] { 1, 2 })); order.Clear();
        Throws<IOException>(() => NativeEngineCoroutineRuntimeCoordinator.PumpDeathSources(() => { order.Add(1); throw new IOException(); },
            () => { order.Add(2); return true; })); Check(order.SequenceEqual(new[] { 1 }));
    }
    private static void ThreadsAndRegistration()
    {
        using var f = new Fixture();
        Task.Run(() => Throws<InvalidOperationException>(() => f.Observe(0, 0))).GetAwaiter().GetResult();
        Check(f.Owner.Read().MayCommit && !f.Owner.Unknown);
        foreach (var pairs in new[] { new[] { f.Pairs[0] }, new[] { f.Pairs[0], f.Pairs[0] },
            new[] { f.Pairs[0], f.Pairs[1] with { NativeOwner = f.Pairs[0].NativeOwner } } })
            Throws<ArgumentException>(() => new NativeEngineCoroutineReceiverLifetime(f.Main, f.Session, pairs, f.Destruction, f.Source));
    }
    private static (NativeEngineCoroutineIdentityLedgerSnapshot Ledger, NativeCoroutineCancellationIdentity Row) Clean()
    {
        var row = new NativeCoroutineCancellationIdentity(new object(), 101, 0, 1, 1001, 2001, true, true, true, false, true, true);
        var identity = new NativeEngineCoroutineIdentitySnapshot(1, 1, 0xA000, row, 301, 1, true, true, true);
        var node = new NativeEngineCoroutineNodeSnapshot(1, 1, 301, 0xA000, 1001, 401, 0, 0, true, true, true,
            true, false, 1, 1, 1, 0);
        var nodes = new NativeEngineCoroutineLedgerSnapshot(4, 0, 0, false, true, false, null, new[] { node });
        return (new(4, 0, 0, false, true, false, null, new[] { identity }, nodes), row);
    }
    private static bool Retire(NativeEngineCoroutineIdentityLedgerSnapshot value, params NativeCoroutineCancellationIdentity[] rows)
        => NativeEngineCoroutineObserverRetirement.MayRetire(new(0, true), value, rows);
    private static void ExactRetirement()
    {
        var (value, row) = Clean(); Check(Retire(value, row));
        Check(value.Pending && !value.EngineCancelled && value.Nodes.Pending && !value.Nodes.EngineCancelled);
    }
    private static void InlineCleanup()
    {
        using var f = new EngineFixture(); f.Complete(0); var value = f.Ledger.Read();
        Check(!value.Unknown && value.Pending && value.Outstanding == 0
            && value.Nodes.Nodes.Single() is { InlineCleanupUnproven: true, FinalCleanupObserved: false });
        for (var index = 0; index < 3; ++index)
        {
            Check(!Retire(value, f.Row)); f.Ledger.RefreshTrackedIdentities();
            Check(!f.Ledger.Unknown && f.Ledger.Read().Identities.Single().Tracker.Published);
        }
    }
    private static void RetirementJoin()
    {
        foreach (var mode in new[] { "missing", "duplicate", "generation", "owner", "enumerator", "sequence", "node", "disposed", "no-node" })
        {
            var (value, row) = Clean(); var node = value.Nodes.Nodes.Single(); var identity = value.Identities.Single();
            node = mode switch { "generation" => node with { Generation = 2 }, "owner" => node with { NativeOwner = 0xB000 },
                "enumerator" => node with { Enumerator = 1002 }, "sequence" => node with { StartSequence = 2 },
                "node" => node with { Node = 302 }, _ => node };
            if (mode == "disposed") identity = identity with { StartDisposed = false };
            if (mode == "no-node") identity = identity with { Node = 0, EngineGeneration = 0 };
            value = value with { Identities = new[] { identity }, Nodes = value.Nodes with {
                Nodes = mode == "missing" ? Array.Empty<NativeEngineCoroutineNodeSnapshot>() : mode == "duplicate" ? new[] { node, node } : new[] { node } } };
            Check(!Retire(value, row) && !value.Unknown);
        }
    }
    private static void RetirementHolds()
    {
        var (value, row) = Clean();
        Check(!Retire(value with { Outstanding = 1 }, row) && !Retire(value with { Untracked = 1 }, row));
        Check(!Retire(value with { Nodes = value.Nodes with { Outstanding = 1 } }, row));
        Check(!Retire(value with { Identities = Array.Empty<NativeEngineCoroutineIdentitySnapshot>() }, row));
        Check(!Retire(value, row with { Owner = new object() }) && !Retire(value, row with { StartReturned = false }));
        Check(!NativeEngineCoroutineObserverRetirement.MayRetire(new(1, true), value, new[] { row }));
        Check(!NativeEngineCoroutineObserverRetirement.MayRetire(new(0, false), value, new[] { row }));
    }
}
