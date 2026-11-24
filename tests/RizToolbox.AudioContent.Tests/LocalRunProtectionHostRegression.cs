using System.Reflection;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Real broker dispatch over controlled detours and real registry ownership.
/// No actual game metadata, scene transition, save, native consumer or Dobby patch is claimed.</summary>
internal static class LocalRunProtectionHostRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("LOCAL protection installs all five gates including LevelOver but refuses admission before a complete host handler", Admission),
        ("LOCAL host routes exact controller and result roles to their own session without claiming an unbound first Start", Routing),
        ("LOCAL native activity includes the complete claimed and ordinary entry until its actual exit", Activity),
        ("Provider and module withdrawal preserve all LOCAL gates until exact verified session retirement", Withdrawal),
        ("A LOCAL handler cannot detach its current protection before returning even after session retirement", HandlerStop),
        ("LOCAL host worker classification uses only immutable tokens and never reads native root targets", Worker),
        ("Wrong-thread LOCAL host installation admission handler replacement and stop preserve ownership", WrongThread),
        ("LOCAL retry admission permits only the currently handled exact retained revision and chart", RetryAdmission),
        ("Process-retained LOCAL transaction pumping survives module withdrawal and coalesces recursive ticks", ProcessPump),
        ("LOCAL host pump faults isolate peers stop new admission and retain existing exact protection", PumpFailure),
        ("LOCAL protection module declares only internal status and rejects unsupported native builds before installation", ModuleContract),
        ("LOCAL process host quota includes abandoned owners and rejects before invoking another transport", Capacity),
        ("Verified session retirement permits gate release despite unknown resource frees without stale pointer claims", UnknownSessionFree),
        // Last cases intentionally retain bounded unknown process responsibility; never reset it.
        ("Partial LOCAL gate installation attempts known independent rollback once and retains every unknown reservation", InstallFailures),
        ("Unknown LOCAL gate detach still releases independent bindings once and never retries its ambiguous slot", DetachFailure)
    };
    private static LocalRunSessionDescriptor Descriptor(long revision = 1) => new(new("host.test", "song"), revision, "chart-a");
    private static long _identity = 0x33000000;
    private static long Identity() => Interlocked.Increment(ref _identity);
    private static void Admission()
    {
        using var f = new HostFixture(installHandler: false); var lease = new Lease();
        var before = f.Owner.Read(); Check(before.AllGatesInstalled && !before.AcceptingNewRuns && !before.StopRequested);
        Equal(5, before.RetainedBindings); Equal(0, before.UnretiredSessions);
        Check(System.Text.Json.JsonSerializer.Serialize(before).Length <= 1800);
        Throws<InvalidOperationException>(() => f.Owner.RegisterPrepared(Descriptor(), Identity(), lease)); Equal(0, lease.Frees);
        Throws<InvalidOperationException>(() => f.Owner.InstallHandler(new UnverifiedHandler()));
        Check(!f.Owner.Read().AcceptingNewRuns);
        f.Owner.InstallHandler(f.Handler); Check(f.Owner.Read().AcceptingNewRuns);
        Throws<InvalidOperationException>(() => f.Owner.InstallHandler(new Handler()));
        Throws<NotSupportedException>(() => ((IList<LocalRunBoundaryCounters>)before.Boundaries)[0] = before.Boundaries[0]);
        Check(!before.AcceptingNewRuns); // copied status does not mutate in place
        var run = f.Prepare(lease); Equal(1, f.Owner.Read().UnretiredSessions);
        f.End(run); Equal(1, lease.Frees);
    }
    private static void Routing()
    {
        using var f = new HostFixture(); var first = f.Committed(); var second = f.Committed();
        var firstSnapshot = f.Owner.Sessions.Read(first); var secondSnapshot = f.Owner.Sessions.Read(second);
        var result = Identity(); f.Owner.Sessions.BindResultScene(first, result, 1, new Lease());
        var delivered = new List<(LocalBoundaryKind, LocalRunSessionRegistry.SessionHandle, long)>();
        f.Handler.OnHandle = (kind, session, receiver) => { delivered.Add((kind, session, receiver)); throw new IOException("Owned transaction failure."); };
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, firstSnapshot.ControllerIdentity);
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, secondSnapshot.ControllerIdentity);
        f.Transport.Invoke(LocalBoundaryKind.ControllerLevelOver, firstSnapshot.ControllerIdentity);
        foreach (var kind in new[] { LocalBoundaryKind.ResultStart, LocalBoundaryKind.ResultRetry, LocalBoundaryKind.ResultBack }) f.Transport.Invoke(kind, result);
        f.Transport.Invoke(LocalBoundaryKind.ResultStart, secondSnapshot.ControllerIdentity);
        f.Transport.Invoke(LocalBoundaryKind.ResultStart, Identity()); // still unbound: no guessed first-Start protection
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, result);
        f.Transport.Invoke(LocalBoundaryKind.ControllerLevelOver, result);
        Equal(6, delivered.Count); Check(ReferenceEquals(delivered[0].Item2, first) && ReferenceEquals(delivered[1].Item2, second));
        Check(ReferenceEquals(delivered[2].Item2, first) && delivered[2].Item3 == firstSnapshot.ControllerIdentity);
        Check(delivered.Skip(3).All(row => ReferenceEquals(row.Item2, first) && row.Item3 == result));
        Equal(2L, f.Transport.Read(LocalBoundaryKind.ResultStart).OriginalCalls);
        Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).OriginalCalls);
        Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerLevelOver).OriginalCalls);
        Equal(6L, f.Owner.Read().Boundaries.Sum(row => row.Skipped));
        Equal(6L, f.Owner.Read().Boundaries.Sum(row => row.Faults));
    }
    private static void Activity()
    {
        using var f = new HostFixture(); var run = f.Committed();
        var receiver = f.Owner.Sessions.Read(run).ControllerIdentity;
        Check(f.Owner.ReadNativeBoundaryActivity() is { Known: true, InFlight: 0 });
        f.Handler.OnHandle = (_, _, _) => {
            Check(f.Owner.ReadNativeBoundaryActivity() is { Known: true, InFlight: 1 });
            f.Transport.Slots[LocalBoundaryKind.ResultBack].OnOriginal = () =>
                Check(f.Owner.ReadNativeBoundaryActivity() is { Known: true, InFlight: 2 });
            f.Transport.Invoke(LocalBoundaryKind.ResultBack, Identity());
            Check(f.Owner.ReadNativeBoundaryActivity() is { Known: true, InFlight: 1 });
            throw new IOException("Claimed failure still leaves the native entry.");
        };
        f.Transport.Invoke(LocalBoundaryKind.ControllerLevelOver, receiver);
        Check(f.Owner.ReadNativeBoundaryActivity() is { Known: true, InFlight: 0 });
        f.Transport.Slots[LocalBoundaryKind.ControllerRetry].OnOriginal = () => {
            Check(f.Owner.ReadNativeBoundaryActivity() is { Known: true, InFlight: 1 });
            throw new IOException("Ordinary original exits through the boundary finally.");
        };
        Throws<IOException>(() => f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, Identity()));
        Check(f.Owner.ReadNativeBoundaryActivity() is { Known: true, InFlight: 0 });
        f.Transport.Slots[LocalBoundaryKind.ResultRetry].ActivityKnown = false;
        Check(!f.Owner.ReadNativeBoundaryActivity().Known);
    }
    private static void Withdrawal()
    {
        using var f = new HostFixture(); var run = f.Committed(); var receiver = f.Owner.Sessions.Read(run).ControllerIdentity;
        f.Owner.Sessions.MarkProviderWithdrawn(run);
        Check(!f.Owner.RequestStop()); Check(f.Owner.Read().AllGatesInstalled); Equal(5, f.Owner.Read().RetainedBindings);
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver); Equal(1, f.Handler.Deliveries);
        var unadopted = new Lease(); Throws<InvalidOperationException>(() => f.Owner.RegisterPrepared(Descriptor(), Identity(), unadopted)); Equal(0, unadopted.Frees);
        Check(ReferenceEquals(run, f.Owner.Sessions.FindProtectedSession(LocalBoundaryKind.ControllerRetry, receiver)));
        f.End(run); Check(f.Owner.TryCompleteStop()); Equal(0, f.Owner.Read().RetainedBindings);
        Check(f.Owner.Sessions.FindProtectedSession(LocalBoundaryKind.ControllerRetry, receiver) is null);
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver); Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).OriginalCalls);
    }
    private static void HandlerStop()
    {
        using var f = new HostFixture(); var run = f.Committed(); var receiver = f.Owner.Sessions.Read(run).ControllerIdentity;
        f.Handler.OnHandle = (_, session, _) => {
            Check(!f.Owner.RequestStop()); f.End(session); Check(!f.Owner.TryCompleteStop());
            Equal(5, f.Owner.Read().RetainedBindings); f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver);
        };
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver);
        Equal(0L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).OriginalCalls);
        Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).SuppressedReentries);
        LocalRunProtectionHost.TickProcessOwners(); Check(f.Owner.Read().Stopped);
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver); Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).OriginalCalls);
    }
    private static void Worker()
    {
        using var f = new HostFixture(); var reads = 0; var frees = 0; var receiver = Identity(); var run = f.Prepare();
        var root = new NativeRuntime.RootHandle(1, _ => frees++, _ => { reads++; f.Main.VerifyAccess(); return new(receiver); }, f.Main.VerifyAccess);
        var commit = f.Owner.Sessions.BeginCommit(run); f.Owner.Sessions.BindPendingController(commit, receiver, root); f.Owner.Sessions.Commit(commit);
        var result = Identity(); f.Owner.Sessions.BindResultScene(run, result, 3, new Lease());
        Task.Run(() => {
            for (var i = 0; i < 1000; i++) Check(ReferenceEquals(run, f.Owner.Sessions.FindProtectedSession(LocalBoundaryKind.ResultStart, result)));
            f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver);
            f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, Identity());
        }).GetAwaiter().GetResult();
        Equal(0, reads); Equal(0, frees); Equal(0, f.Handler.Deliveries);
        Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).WrongThreadClaims);
        Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).OriginalCalls); f.End(run); Equal(1, frees); Equal(0, reads);
    }
    private static void WrongThread()
    {
        using var f = new HostFixture(); var lease = new Lease(); var unused = new Transport(f.Main, f.Log); var baseline = LocalRunProtectionHost.RetainedProcessHostCount;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => LocalRunProtectionHost.Create(f.Main, f.Log, unused));
            Throws<InvalidOperationException>(() => f.Owner.RegisterPrepared(Descriptor(), Identity(), lease));
            Throws<InvalidOperationException>(() => f.Owner.InstallHandler(f.Handler));
            Throws<InvalidOperationException>(() => f.Owner.RequestStop());
            Throws<InvalidOperationException>(() => f.Owner.TryCompleteStop());
            Throws<InvalidOperationException>(() => f.Owner.Read());
        }).GetAwaiter().GetResult();
        Equal(0, unused.BindCalls); Equal(baseline, LocalRunProtectionHost.RetainedProcessHostCount); Equal(0, lease.Frees);
        Check(f.Owner.Read().AcceptingNewRuns); var run = f.Prepare(lease); f.End(run); Equal(1, lease.Frees);
    }
    private static void RetryAdmission()
    {
        using var f = new HostFixture(); var run = f.Committed(); var receiver = f.Owner.Sessions.Read(run).ControllerIdentity;
        var unadopted = new Lease();
        f.Handler.OnHandle = (_, _, _) => {
            Throws<InvalidOperationException>(() => f.Owner.RegisterPrepared(Descriptor(2), Identity(), unadopted));
            f.Prepare();
        };
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver); Equal(0, unadopted.Frees); Equal(2, f.Owner.Read().UnretiredSessions);
        var result = Identity(); f.Owner.Sessions.BindResultScene(run, result, 7, new Lease());
        f.Handler.OnHandle = (_, _, _) => Throws<InvalidOperationException>(() => f.Owner.RegisterPrepared(Descriptor(), Identity(), unadopted));
        f.Transport.Invoke(LocalBoundaryKind.ResultRetry, result); // ResultCapturing/Ready is still required
        var claim = f.Owner.Sessions.TryClaimResult(run).Claim!;
        f.Owner.Sessions.CompleteResult(claim, Identity(), f.Owner.Sessions.Read(run).MetadataIdentity,
            new RunFinished(null, "local", 1, 1, 1, 1, 0, 0, false, false), new Lease());
        f.Handler.OnHandle = (_, _, _) => f.Prepare(); f.Transport.Invoke(LocalBoundaryKind.ResultRetry, result);
        Equal(3, f.Owner.Read().UnretiredSessions);
        f.Handler.OnHandle = (_, _, _) => Throws<InvalidOperationException>(() => f.Owner.RegisterPrepared(Descriptor(), Identity(), unadopted));
        f.Transport.Invoke(LocalBoundaryKind.ResultBack, result); f.Transport.Invoke(LocalBoundaryKind.ResultStart, result);
        Equal(0L, f.Owner.Read().Boundaries.Sum(row => row.Faults));
    }
    private static void ProcessPump()
    {
        using var f = new HostFixture(); var run = f.Committed(); Check(!f.Owner.RequestStop());
        f.Handler.OnTick = LocalRunProtectionHost.TickProcessOwners;
        LocalRunProtectionHost.TickProcessOwners(); Equal(1, f.Handler.Ticks); Check(!f.Owner.Read().Stopped);
        f.Handler.Pending = true; f.End(run); Check(!f.Owner.TryCompleteStop());
        Check(f.Owner.Read().HandlerWorkPending && f.Owner.Read().UnretiredSessions == 0); Equal(5, f.Owner.Read().RetainedBindings);
        f.Handler.OnTick = () => { f.Handler.Pending = false; LocalRunProtectionHost.TickProcessOwners(); };
        LocalRunProtectionHost.TickProcessOwners(); Equal(2, f.Handler.Ticks); Check(f.Owner.Read().Stopped);
        LocalRunProtectionHost.TickProcessOwners(); Equal(2, f.Handler.Ticks);
    }
    private static void PumpFailure()
    {
        using var bad = new HostFixture(); using var good = new HostFixture(); var run = bad.Committed(); var receiver = bad.Owner.Sessions.Read(run).ControllerIdentity;
        bad.Handler.OnTick = () => throw new IOException("Transaction pump failed."); bad.Log.Throw = true;
        LocalRunProtectionHost.TickProcessOwners(); Equal(1, bad.Handler.Ticks); Equal(1, good.Handler.Ticks);
        Check(bad.Owner.Read().StopRequested && !bad.Owner.Read().AcceptingNewRuns && bad.Owner.Read().AllGatesInstalled);
        bad.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver); Equal(0, bad.Handler.Deliveries);
        Equal(0L, bad.Transport.Read(LocalBoundaryKind.ControllerRetry).OriginalCalls);
        LocalRunProtectionHost.TickProcessOwners(); Equal(1, bad.Handler.Ticks); Equal(2, good.Handler.Ticks);
        bad.End(run); LocalRunProtectionHost.TickProcessOwners();
        Check(!bad.Owner.Read().Stopped && bad.Owner.Read().HandlerWorkUnknown && bad.Owner.Read().CleanupUnknown);
        Equal(5, bad.Owner.Read().RetainedBindings); Equal(1, bad.Handler.Ticks);
        using var predicate = new HostFixture(); var predicateReads = predicate.Handler.WorkReads;
        predicate.Handler.PendingFailure = true;
        Check(!predicate.Owner.RequestStop()); Equal(predicateReads + 1, predicate.Handler.WorkReads);
        Check(predicate.Owner.Read().HandlerWorkUnknown && predicate.Owner.Read().CleanupUnknown);
        Check(!predicate.Owner.TryCompleteStop()); LocalRunProtectionHost.TickProcessOwners();
        Equal(predicateReads + 1, predicate.Handler.WorkReads); Equal(0, predicate.Handler.Ticks);
    }
    private static void ModuleContract()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative(); var log = new TestLogger();
        var profile = new AdapterBuildProfile("test", "unsupported", "test", "2022.3.62f2", "test", "test", new Dictionary<string, TypeBinding>());
        var before = LocalRunProtectionHost.RetainedProcessHostCount;
        Throws<UnsupportedSignatureException>(() => NativeLocalRunBoundaryTransport.Resolve(native.Api, main, log, profile, new(1), "unused"));
        var module = new LocalRunProtectionModule(native.Api, main, log, profile, "unused");
        Equal(5, module.Manifest.RequiredSymbols.Count); Equal(1, module.Manifest.Capabilities.Count);
        Equal(LocalRunProtectionModule.Capability, module.Manifest.Capabilities[0]); Check(module.Probe().Permanent);
        Equal(before, LocalRunProtectionHost.RetainedProcessHostCount); Equal(0, native.Invocations); module.Release();
    }
    private static void Capacity()
    {
        using var main = new MainThreadDispatcher(); var log = new TestLogger(); var baseline = LocalRunProtectionHost.RetainedProcessHostCount;
        var abandoned = Abandon(main, log); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Check(abandoned.IsAlive);
        var owners = new List<LocalRunProtectionHost>();
        try
        {
            for (var i = baseline + 1; i < 32; i++) owners.Add(LocalRunProtectionHost.Create(main, log, new Transport(main, log)));
            var refused = new Transport(main, log);
            Throws<InvalidOperationException>(() => LocalRunProtectionHost.Create(main, log, refused)); Equal(0, refused.BindCalls);
        }
        finally { foreach (var owner in owners) Check(owner.RequestStop()); Check(((LocalRunProtectionHost)abandoned.Target!).RequestStop()); }
        Equal(baseline, LocalRunProtectionHost.RetainedProcessHostCount);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference Abandon(MainThreadDispatcher main, TestLogger log)
        => new(LocalRunProtectionHost.Create(main, log, new Transport(main, log)));
    private static void UnknownSessionFree()
    {
        using var f = new HostFixture(); var failed = new Lease(() => throw new IOException("Unknown bundle free."));
        var run = f.Prepare(failed); var pending = f.Owner.Sessions.BeginCommit(run); var receiver = Identity();
        f.Owner.Sessions.BindPendingController(pending, receiver, new Lease()); f.Owner.Sessions.Commit(pending);
        f.Owner.Sessions.BeginReturn(run); var token = f.Owner.Sessions.VerifyConsumersEnded(run, new Verifier())!;
        Check(!f.Owner.Sessions.Close(run, token)); Check(f.Owner.Sessions.Read(run).ProtectionRetired);
        Equal(0, f.Owner.Sessions.UnretiredSessionCount); Check(f.Owner.RequestStop());
        f.Transport.Invoke(LocalBoundaryKind.ControllerRetry, receiver); Equal(1L, f.Transport.Read(LocalBoundaryKind.ControllerRetry).OriginalCalls);
        Equal(1, failed.Frees); Check(!f.Owner.Sessions.Close(run, token)); Equal(1, failed.Frees);
    }
    private static void InstallFailures()
    {
        using var main = new MainThreadDispatcher(); var log = new TestLogger(); var baseline = LocalRunProtectionHost.RetainedProcessHostCount;
        foreach (var fail in Enum.GetValues<LocalBoundaryKind>()) foreach (var after in new[] { false, true })
        {
            var transport = new Transport(main, log) { InstallFailure = fail, FailAfterBind = after };
            LocalRunProtectionCreationException? caught = null;
            try { LocalRunProtectionHost.Create(main, log, transport); } catch (LocalRunProtectionCreationException error) { caught = error; }
            Check(caught is not null); var owner = caught!.Responsibility;
            Check(owner.Read().CleanupUnknown && !owner.Read().AllGatesInstalled && !owner.Read().AcceptingNewRuns);
            var attempts = transport.Slots.Values.Sum(slot => slot.DetachAttempts);
            Check(!owner.RequestStop() && !owner.TryCompleteStop()); Equal(attempts, transport.Slots.Values.Sum(slot => slot.DetachAttempts));
            foreach (var slot in transport.Slots.Values) Check(slot.DetachAttempts == (slot.Boundary == fail ? 0 : 1));
            var untouched = new Lease(); Throws<InvalidOperationException>(() => owner.RegisterPrepared(Descriptor(), Identity(), untouched)); Equal(0, untouched.Frees);
        }
        Equal(baseline + 10, LocalRunProtectionHost.RetainedProcessHostCount);
    }
    private static void DetachFailure()
    {
        using var f = new HostFixture(); var baseline = LocalRunProtectionHost.RetainedProcessHostCount;
        f.Transport.Slots[LocalBoundaryKind.ResultRetry].ThrowOnDetach = true;
        Check(!f.Owner.RequestStop()); Check(f.Owner.Read().CleanupUnknown && !f.Owner.Read().AllGatesInstalled);
        Equal(1, f.Owner.Read().RetainedBindings); Check(f.Transport.Slots.Values.All(slot => slot.DetachAttempts == 1));
        Check(!f.Owner.TryCompleteStop()); Check(!f.Owner.RequestStop()); Equal(baseline, LocalRunProtectionHost.RetainedProcessHostCount);
        Check(f.Transport.Slots.Values.All(slot => slot.DetachAttempts == 1));
    }
    private sealed class HostFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly TestLogger Log = new(); internal readonly Handler Handler = new();
        internal readonly Transport Transport; internal readonly LocalRunProtectionHost Owner;
        private readonly List<LocalRunSessionRegistry.SessionHandle> _runs = new();
        internal HostFixture(bool installHandler = true)
        { Transport = new(Main, Log); Owner = LocalRunProtectionHost.Create(Main, Log, Transport); if (installHandler) Owner.InstallHandler(Handler); }
        internal LocalRunSessionRegistry.SessionHandle Prepare(Lease? lease = null)
        { var run = Owner.RegisterPrepared(Descriptor(), Identity(), lease ?? new Lease()); _runs.Add(run); return run; }
        internal LocalRunSessionRegistry.SessionHandle Committed()
        { var run = Prepare(); var claim = Owner.Sessions.BeginCommit(run); Owner.Sessions.BindPendingController(claim, Identity(), new Lease()); Owner.Sessions.Commit(claim); return run; }
        internal void End(LocalRunSessionRegistry.SessionHandle run)
        {
            var state = Owner.Sessions.Read(run); if (state.CleanupAttempted || state.Phase == LocalRunSessionPhase.Closed) return;
            Owner.Sessions.BeginReturn(run); var proof = Owner.Sessions.VerifyConsumersEnded(run, new Verifier())!; Check(Owner.Sessions.Close(run, proof));
        }
        public void Dispose() { try { foreach (var run in _runs) End(run); Owner.RequestStop(); } finally { Main.Dispose(); } }
    }
    private sealed class Handler : ILocalRunBoundaryHandler
    {
        public void ValidateHost(LocalRunSessionRegistry registry) { }
        internal int Deliveries, Ticks, WorkReads; internal bool Pending, PendingFailure;
        internal Action<LocalBoundaryKind, LocalRunSessionRegistry.SessionHandle, long>? OnHandle; internal Action? OnTick;
        public bool HasPendingWork { get { WorkReads++; if (PendingFailure) throw new IOException("Unknown pending native work."); return Pending; } }
        public void Handle(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver) { Deliveries++; OnHandle?.Invoke(boundary, session, receiver); }
        public void Tick() { Ticks++; OnTick?.Invoke(); }
    }
    private sealed class UnverifiedHandler : ILocalRunBoundaryHandler
    {
        public bool HasPendingWork => false;
        public void Handle(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver) { }
        public void Tick() { }
    }
    private sealed class Lease : IDisposable
    { private readonly Action? _free; internal int Frees; internal Lease(Action? free = null) => _free = free; public void Dispose() { Frees++; _free?.Invoke(); } }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled backend has no native consumers, scenes or callbacks; exact test responsibility is ended."; return true; } }
    private sealed class TestLogger : ILogger
    { internal bool Throw; public void Log(LogLevel level, string message, Exception? exception = null) { if (Throw) throw new IOException("Log fault."); } }

    private sealed class Transport : ILocalRunBoundaryTransport, INativeDetourEngine
    {
        private static long _nextAddress = 0x730000000;
        private readonly ExactHookBroker _broker;
        private readonly Dictionary<IntPtr, Slot> _byAddress = new();
        internal readonly Dictionary<LocalBoundaryKind, Slot> Slots = new();
        internal LocalBoundaryKind? InstallFailure; internal bool FailAfterBind; internal int BindCalls;
        internal Transport(MainThreadDispatcher main, TestLogger log) => _broker = new(this, main, log);
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
        {
            var slot = _byAddress[target.Address]; slot.Entry = (ExactHookBroker.InstanceVoid)replacement;
            return new ExactHookBroker.InstanceVoid((_, _) => { slot.Originals++; slot.OnOriginal?.Invoke(); });
        }
        public ILocalRunBoundaryBinding Bind(LocalBoundaryKind boundary, Func<long, bool> classify, Action<long> handle)
        {
            BindCalls++; if (InstallFailure == boundary && !FailAfterBind) throw new IOException("Before binding.");
            var target = new VerifiedHookTarget("host.fixture." + boundary, new(Interlocked.Add(ref _nextAddress, 0x100)), new(0x222), new(0x4321), ExactHookAbi.InstanceVoid);
            var slot = new Slot(boundary); Slots.Add(boundary, slot); _byAddress.Add(target.Address, slot);
            if (boundary == LocalBoundaryKind.ResultStart)
            {
                var token = (ExactHookBroker.LocalResultStartTarget)typeof(ExactHookBroker.LocalResultStartTarget)
                    .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VerifiedHookTarget) }, null)!.Invoke(new object[] { target });
                var binding = _broker.BindLocalResultStartPolicy(token, receiver => classify(receiver.ToInt64())
                    ? ExactHookBroker.LocalResultStartDecision.ClaimLocal : ExactHookBroker.LocalResultStartDecision.ContinueOriginal, receiver => handle(receiver.ToInt64()));
                slot.Free = binding.Dispose; slot.Activity = () => { var state = binding.ReadActivity(); return new(state.InFlight, state.Known); }; slot.Describe = () => {
                    var hook = binding.DescribeHook(); var p = binding.DescribePolicy();
                    return new(boundary, hook.NativeCalls, hook.OriginalCalls, p.Classified, p.Continued, p.Claimed, p.Skipped, p.Faults, p.WrongThreadClaims, p.SuppressedPolicyReentries);
                };
            }
            else
            {
                var command = boundary switch { LocalBoundaryKind.ControllerRetry => ExactHookBroker.LocalRunCommand.ControllerRetry,
                    LocalBoundaryKind.ResultRetry => ExactHookBroker.LocalRunCommand.ResultRetry,
                    LocalBoundaryKind.ControllerLevelOver => ExactHookBroker.LocalRunCommand.ControllerLevelOver,
                    _ => ExactHookBroker.LocalRunCommand.ResultBack };
                var token = (ExactHookBroker.LocalRunCommandTarget)typeof(ExactHookBroker.LocalRunCommandTarget)
                    .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VerifiedHookTarget), typeof(ExactHookBroker.LocalRunCommand) }, null)!
                    .Invoke(new object[] { target, command });
                var binding = _broker.BindLocalRunCommandPolicy(token, receiver => classify(receiver.ToInt64())
                    ? ExactHookBroker.LocalRunCommandDecision.ClaimLocal : ExactHookBroker.LocalRunCommandDecision.ContinueOriginal, receiver => handle(receiver.ToInt64()));
                slot.Free = binding.Dispose; slot.Activity = () => { var state = binding.ReadActivity(); return new(state.InFlight, state.Known); }; slot.Describe = () => {
                    var hook = binding.DescribeHook(); var p = binding.DescribePolicy();
                    return new(boundary, hook.NativeCalls, hook.OriginalCalls, p.Classified, p.Continued, p.Claimed, p.Skipped, p.Faults, p.WrongThreadClaims, p.SuppressedPolicyReentries);
                };
            }
            if (InstallFailure == boundary) throw new IOException("After actual policy binding but before returning its handle.");
            return slot;
        }
        internal void Invoke(LocalBoundaryKind boundary, long receiver) => Slots[boundary].Entry!(new(receiver), new(0x222));
        internal LocalRunBoundaryCounters Read(LocalBoundaryKind boundary) => Slots[boundary].Read();
    }
    private sealed class Slot : ILocalRunBoundaryBinding
    {
        internal readonly LocalBoundaryKind Boundary; internal ExactHookBroker.InstanceVoid? Entry; internal Action? Free;
        internal Func<LocalRunBoundaryCounters>? Describe; internal int DetachAttempts, Originals; internal bool ThrowOnDetach;
        internal Func<LocalRunBoundaryActivity>? Activity; internal bool ActivityKnown = true; internal Action? OnOriginal;
        internal Slot(LocalBoundaryKind boundary) => Boundary = boundary;
        public LocalRunBoundaryCounters Read() => Describe!();
        public LocalRunBoundaryActivity ReadActivity() => ActivityKnown ? Activity!() : new(0, false);
        public void Dispose() { DetachAttempts++; if (ThrowOnDetach) throw new IOException("Unknown detach."); Free!(); }
    }
}
