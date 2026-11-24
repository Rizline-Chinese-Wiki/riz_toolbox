using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

internal static class NativeLocalMenuLedgerRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Menu ledger owns one normal and back credit, releases across actual staged handoffs", Lifecycle),
        ("Menu ledger leaves preexisting foreign lock credits and initially inactive objects intact", ForeignBaseline),
        ("Menu SetActive ABA revokes restoration even when activeSelf is unchanged", ObjectAba),
        ("Menu lock unlock and relock ABA never releases a later caller credit", LockAba),
        ("Menu balanced foreign lock activity preserves its still-owned credit", BalancedForeign),
        ("Menu successor or destroyed objects are never overwritten or revived", Successors),
        ("Menu worker and suppressed nested owned calls retain unknown responsibility", UnobservedCalls),
        ("Menu failed native mutations never retry or release uncertain roots", MutationFailure),
        ("Menu changed singleton lock field or unhooked state write cannot appear coherent", ChangedState),
        ("Menu wrong-thread operations and duplicate stages do not spend another mutation", ThreadAndOnce),
        ("Menu capture and observer free failures retain bounded root responsibility", RootFailure),
        ("Untouched menu observers release independently without changing game state", Untouched),
        ("Menu launch commit waits for paired callbacks and rejects later unobserved ownership changes", CommitValidation),
        ("Menu return never takes a new normal credit before the actual getter coroutines end", ReturnWaitsForCoroutines)
    };
    private static void Lifecycle()
    {
        using var f = new Fixture(); f.Owner.BeginLaunchOnce(); var state = f.Owner.Read();
        Check(state.NormalCreditHeld && state.BackCreditHeld); Equal(1, f.Backend.State.Normal.Count); Equal(1, f.Backend.State.Back.Count);
        Check(!f.Backend.State.Camera.ActiveSelf && !f.Backend.State.Particle.ActiveSelf);
        f.Initial(); Check(!f.Owner.Read().NormalCreditHeld && f.Owner.Read().BackCreditHeld);
        f.Return(); Check(f.Owner.Read().Restored); Equal(0, f.Backend.State.Normal.Count); Equal(0, f.Backend.State.Back.Count);
        Check(f.Backend.State.Camera.ActiveSelf && f.Backend.State.Particle.ActiveSelf); Check(f.Owner.TryReleaseAfterRestored()); Equal(0, f.Backend.RootRecords);
        Equal(f.Owner.Read().Entered, f.Owner.Read().Exited);
    }
    private static void ForeignBaseline()
    {
        using var f = new Fixture(b => b.State = b.State with { Normal = b.State.Normal with { Count = 2 }, Back = b.State.Back with { Count = 3 }, Camera = b.State.Camera with { ActiveSelf = false } });
        f.Owner.BeginLaunchOnce(); f.Initial(); f.Return();
        Equal(2, f.Backend.State.Normal.Count); Equal(3, f.Backend.State.Back.Count); Check(!f.Backend.State.Camera.ActiveSelf);
    }
    private static void ObjectAba()
    {
        using var f = new Fixture(); f.Owner.BeginLaunchOnce();
        f.Backend.Raw(LocalMenuOperation.SetActive, 0, 0); // Same value still establishes a later writer.
        f.Initial(); f.Return(); Check(f.Owner.Read().CameraRestoreRevoked); Check(!f.Backend.State.Camera.ActiveSelf); Check(f.Backend.State.Particle.ActiveSelf);
    }
    private static void LockAba()
    {
        using var f = new Fixture(); f.Owner.BeginLaunchOnce();
        f.Backend.Raw(LocalMenuOperation.Unlock, 3); f.Backend.Raw(LocalMenuOperation.Lock, 3);
        Check(!f.Owner.Read().BackCreditHeld); f.Initial(); f.Return(); Equal(1, f.Backend.State.Back.Count);
    }
    private static void BalancedForeign()
    {
        using var f = new Fixture(); f.Owner.BeginLaunchOnce(); f.Backend.Raw(LocalMenuOperation.Lock, 3); f.Backend.Raw(LocalMenuOperation.Unlock, 3);
        Check(f.Owner.Read().BackCreditHeld); f.Initial(); f.Return(); Equal(0, f.Backend.State.Back.Count);
    }
    private static void Successors()
    {
        using var f = new Fixture(); f.Owner.BeginLaunchOnce(); f.Initial();
        f.Backend.State = f.Backend.State with { Camera = f.Backend.State.Camera with { Current = false }, Particle = f.Backend.State.Particle with { Alive = false } };
        f.Return(); Check(!f.Backend.State.Camera.ActiveSelf && !f.Backend.State.Particle.ActiveSelf); Check(!f.Owner.Read().Unknown);
    }
    private static void UnobservedCalls()
    {
        using (var f = new Fixture())
        {
            f.Owner.BeginLaunchOnce(); Task.Run(() => f.Backend.Raw(LocalMenuOperation.Lock, 3)).GetAwaiter().GetResult();
            Check(f.Owner.Read().Unknown); var mutations = f.Backend.Invocations;
            Throws<InvalidOperationException>(() => f.Initial()); Equal(mutations, f.Backend.Invocations);
        }
        using (var f = new Fixture())
        {
            f.Backend.During = (operation, slot) => { if (operation == LocalMenuOperation.Lock && slot == 2) f.Backend.Raw(LocalMenuOperation.Lock, 3); };
            Throws<NativeLocalMenuException>(() => f.Owner.BeginLaunchOnce()); Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Releases);
        }
    }
    private static void MutationFailure()
    {
        using var f = new Fixture(); f.Backend.ThrowMutation = true;
        Throws<NativeLocalMenuException>(() => f.Owner.BeginLaunchOnce()); Equal(1, f.Backend.Invocations); Check(f.Owner.Read().Unknown);
        Throws<InvalidOperationException>(() => f.Owner.BeginLaunchOnce()); Equal(1, f.Backend.Invocations); Equal(7, f.Backend.RootRecords);
    }
    private static void ChangedState()
    {
        using (var f = new Fixture())
        {
            f.Backend.State = f.Backend.State with { Normal = f.Backend.State.Normal with { Count = 1 } };
            Throws<NativeLocalMenuException>(() => f.Owner.BeginLaunchOnce()); Equal(0, f.Backend.Invocations);
        }
        using (var f = new Fixture())
        {
            f.Owner.BeginLaunchOnce(); f.Backend.State = f.Backend.State with { Back = f.Backend.State.Back with { Current = false } };
            Throws<NativeLocalMenuException>(() => f.Initial()); Equal(0, f.Backend.Releases);
        }
    }
    private static void ThreadAndOnce()
    {
        using var f = new Fixture(); Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.BeginLaunchOnce())).GetAwaiter().GetResult(); Equal(0, f.Backend.Invocations);
        f.Owner.BeginLaunchOnce(); var count = f.Backend.Invocations;
        Throws<InvalidOperationException>(() => f.Owner.BeginLaunchOnce()); Equal(count, f.Backend.Invocations);
        f.Initial(); Throws<InvalidOperationException>(() => f.Initial()); f.Return();
        Throws<InvalidOperationException>(() => f.Owner.BeginReturnOnce());
    }
    private static void RootFailure()
    {
        using (var main = new MainThreadDispatcher())
        {
            var backend = new Backend(main) { ThrowCapture = true };
            try { NativeLocalMenuLedger.CaptureBackend(main, backend); throw new Exception("Expected partial capture"); }
            catch (NativeLocalMenuException error) { Check(error.Responsibility.Read().Unknown); Equal(3, backend.RootRecords); }
        }
        using var f = new Fixture(); f.Owner.BeginLaunchOnce(); f.Initial(); f.Return(); f.Backend.ThrowFree = true;
        Throws<NativeLocalMenuException>(() => f.Owner.TryReleaseAfterRestored()); Equal(1, f.Backend.Releases); Equal(1, f.Backend.RootRecords);
        Throws<InvalidOperationException>(() => f.Owner.TryReleaseAfterRestored()); Equal(1, f.Backend.Releases);
    }
    private static void Untouched()
    { using var f = new Fixture(); Check(f.Owner.TryReleaseBeforeAttempt()); Equal(0, f.Backend.Invocations); Equal(0, f.Backend.RootRecords); }
    private static void CommitValidation()
    {
        using var f = new Fixture(); f.Owner.BeginLaunchOnce(); f.Initial(); Check(f.Owner.TryVerifyCurrent());
        var waiting = false;
        f.Backend.During = (_, _) => waiting = !f.Owner.TryVerifyCurrent();
        f.Backend.Raw(LocalMenuOperation.SetActive, 0, 0); f.Backend.During = null;
        Check(waiting && !f.Owner.Read().Unknown && f.Owner.TryVerifyCurrent());
        f.Backend.State = f.Backend.State with { Back = f.Backend.State.Back with { Current = false } };
        Throws<NativeLocalMenuException>(() => f.Owner.TryVerifyCurrent()); Check(f.Owner.Read().Unknown);
    }
    private static void ReturnWaitsForCoroutines()
    {
        using var coroutines = new NativeLocalInitializationCoroutineRegression.Fixture();
        using var f = new Fixture(initialCoroutines: coroutines.Owner); f.Owner.BeginLaunchOnce(); f.Initial();
        var mutations = f.Backend.Invocations;
        Throws<InvalidOperationException>(() => f.Owner.BeginReturnOnce()); Equal(mutations, f.Backend.Invocations); Check(!f.Owner.Read().ReturnAttempted);
        coroutines.Finish(); coroutines.Backend.InFlight = 1;
        Throws<InvalidOperationException>(() => f.Owner.BeginReturnOnce()); Equal(mutations, f.Backend.Invocations);
        coroutines.Backend.InFlight = 0; f.Return(); Check(f.Owner.Read().Restored); Equal(0, f.Backend.State.Normal.Count);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly Backend Backend; internal readonly NativeLocalMenuLedger Owner;
        private readonly NativeLocalRunSceneTransitionReceipt _initial;
        private readonly NativeLocalRunReturnTransitionReceipt _returned;
        internal Fixture(Action<Backend>? configure = null, NativeLocalInitializationCoroutineTracker? initialCoroutines = null)
        {
            Backend = new(Main); configure?.Invoke(Backend); Owner = NativeLocalMenuLedger.CaptureBackend(Main, Backend, initialCoroutines);
            var identity = new LocalRunSessionIdentity("menu.fixture", new("menu.fixture", "song"), 1, "chart", 1);
            _initial = new(identity, 101, 501, 601, new(10, "LevelList", true, true), new(20, "Level", true, true));
            _returned = new(identity, 101, 501, 701, _initial.Destination, new(30, "LevelList", true, true), _initial.Origin);
        }
        internal void Initial() => Owner.CompleteInitialControlled(_initial);
        internal void Return() { Owner.BeginReturnOnce(); Owner.RestoreControlled(_returned); }
        public void Dispose()
        {
            try {
                var state = Owner.Read(); if (state.Closed || state.Unknown) return;
                if (!state.LaunchAttempted) { Owner.TryReleaseBeforeAttempt(); return; }
                if (!state.LaunchSceneCompleted) Initial();
                if (!Owner.Read().ReturnAttempted) Owner.BeginReturnOnce();
                if (!Owner.Read().Restored) Owner.RestoreControlled(_returned);
                Owner.TryReleaseAfterRestored();
            } finally { Main.Dispose(); }
        }
    }
    private sealed class Backend : INativeLocalMenuBackend
    {
        private static long NextAddress = 0x7E0000000;
        private readonly ExactHookBroker _broker; private readonly Engine _engine = new(); private readonly VerifiedHookTarget[] _targets;
        private readonly ExactHookBroker.Binding?[] _bindings = new ExactHookBroker.Binding?[4];
        private readonly ExactHookBroker.BoundaryProbeBinding?[] _probes = new ExactHookBroker.BoundaryProbeBinding?[4];
        internal NativeMenuObservation State = new(1, true, new(11, 21, true, true, true), new(12, 22, true, true, true), new(31, true, 0), new(32, true, 0));
        internal int Invocations, Releases; internal bool ThrowCapture, ThrowMutation, ThrowFree; internal Action<LocalMenuOperation, int>? During;
        public bool CleanupUnknown { get; private set; }
        public int RootRecords { get; private set; }
        internal Backend(MainThreadDispatcher main)
        {
            _broker = new(_engine, main, new Logger()); var start = Interlocked.Add(ref NextAddress, 0x1000);
            _targets = Enumerable.Range(0, 4).Select(i => new VerifiedHookTarget("menu.fixture." + i, new(start + i * 16), new(0x123), new(0x321),
                i == 0 ? ExactHookAbi.InstanceBooleanVoid : ExactHookAbi.InstanceVoid)).ToArray();
            _engine.Originals[_targets[0].Address] = new ExactHookBroker.InstanceBooleanVoid((self, value, _) => Body(LocalMenuOperation.SetActive, Slot(self), value));
            for (var i = 1; i < 4; ++i) { var op = (LocalMenuOperation)i; _engine.Originals[_targets[i].Address] = new ExactHookBroker.InstanceVoid((self, _) => Body(op, Slot(self), 0)); }
        }
        public NativeMenuObservation Capture() { RootRecords = ThrowCapture ? 3 : 7; if (ThrowCapture) throw new IOException("capture"); return State; }
        public NativeMenuObservation Read() => State;
        public void Bind(NativeLocalMenuLedger owner)
        {
            for (var i = 0; i < 4; ++i)
            {
                var op = (LocalMenuOperation)i; var target = _targets[i];
                _bindings[i] = i == 0
                    ? _broker.BindRetainedBooleanArgumentObservation(target, self => owner.Matches(op, self.ToInt64()), (self, value) => owner.CaptureInvocation(op, self.ToInt64(), value), (frame, _) => owner.CompleteInvocation(frame))
                    : _broker.BindEarlyRetainedObservation(target, self => owner.Matches(op, self.ToInt64()), self => owner.CaptureInvocation(op, self.ToInt64(), 0), owner.CompleteInvocation);
                _probes[i] = _broker.BindBoundaryProbe(target, (self, _, method) => owner.ProbeEnter(op, self.ToInt64(), method == target.MethodInfo || method == IntPtr.Zero), (self, _, _, returned) => owner.ProbeExit(op, self.ToInt64(), returned));
            }
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { var activity = _probes.Where(p => p is not null).Select(p => p!.ReadActivity()).ToArray(); return new(activity.Sum(p => p.InFlight), activity.Length == 4 && activity.All(p => p.Known) && !CleanupUnknown); }
        private long Identity(int slot) => slot switch { 0 => State.Camera.GameObject, 1 => State.Particle.GameObject, 2 => State.Normal.Identity, _ => State.Back.Identity };
        private int Slot(IntPtr receiver) => Enumerable.Range(0, 4).Single(slot => Identity(slot) == receiver.ToInt64());
        public void Invoke(LocalMenuOperation operation, int slot, byte value) { Invocations++; Raw(operation, slot, value); }
        internal void Raw(LocalMenuOperation op, int slot, byte value = 0)
        {
            var target = _targets[(int)op]; var receiver = new IntPtr(Identity(slot));
            if (op == LocalMenuOperation.SetActive) ((ExactHookBroker.InstanceBooleanVoid)_engine.Replacements[target.Address])(receiver, value, target.MethodInfo);
            else ((ExactHookBroker.InstanceVoid)_engine.Replacements[target.Address])(receiver, target.MethodInfo);
        }
        private void Body(LocalMenuOperation op, int slot, byte value)
        {
            if (ThrowMutation) throw new IOException("mutation"); During?.Invoke(op, slot);
            if (slot < 2) State = slot == 0 ? State with { Camera = State.Camera with { ActiveSelf = value != 0 } } : State with { Particle = State.Particle with { ActiveSelf = value != 0 } };
            else {
                var item = slot == 2 ? State.Normal : State.Back; var count = op == LocalMenuOperation.Lock ? item.Count + 1 : op == LocalMenuOperation.Unlock ? Math.Max(0, item.Count - 1) : 0;
                State = slot == 2 ? State with { Normal = item with { Count = count } } : State with { Back = item with { Count = count } };
            }
        }
        public void ReleaseObservers()
        {
            Releases++; for (var i = 3; i >= 0; --i) { _bindings[i]?.Dispose(); _probes[i]?.Dispose(); }
            RootRecords = ThrowFree ? 1 : 0; if (ThrowFree) { CleanupUnknown = true; throw new IOException("free"); }
        }
    }
    private sealed class Engine : INativeDetourEngine
    { internal readonly Dictionary<IntPtr, Delegate> Originals = new(), Replacements = new(); public Delegate Install(VerifiedHookTarget target, Delegate replacement) { Replacements.Add(target.Address, replacement); return Originals[target.Address]; } }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
