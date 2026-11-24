using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Controlled bridges exercise permanent managed ownership and logical fan-out.
// They neither create a publication credential nor execute a production target.
internal static class NativeEngineRuntimeCoordinatorTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Engine runtime coordinator shares one physical four-kind owner across logical consumers", SharedPhysicalOwner),
        ("Engine runtime coordinator detaches future membership while completing captured cross-kind duties", CapturedDuties),
        ("Engine runtime coordinator retains partial Attach and refuses another physical adoption", PartialAttach),
        ("Engine runtime coordinator makes native activity and sink faults permanently Unknown", StickyUnknown),
        ("Engine production generated identity facts never authorize startup or native observation", ProductionUnavailable),
        ("Engine production compiled profile copies five hashes and four exact fixed game RVAs", ProfileIdentity)
    };
    private static void Check(bool condition, string message = "Engine runtime coordinator regression failed.")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } catch (NotSupportedException) { return; } throw new Exception("Expected permanent engine owner refusal."); }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Scope : IDisposable
    { internal int Completed, Disposed; public void Dispose() => ++Disposed; }
    private sealed class Bridge : INativeEngineNodeContextBridge
    {
        private Func<NativeEngineNodeFrame, nuint>? _enter;
        private Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion>? _exit;
        private ulong _sequence;
        private long _inFlight;
        internal int Attaches;
        internal bool FailAttach, Known = true;
        public NativeEngineNodeChannelIdentity Identity { get; }
        internal Bridge(NativeEngineNodeKind kind)
            => Identity = new(kind, 28, new(0x1000 + (int)kind * 0x100), new(0x2000 + (int)kind * 0x100), new(0x3000 + (int)kind * 0x100), 1);
        public void Attach(Func<NativeEngineNodeFrame, nuint> enter, Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion> exit)
        {
            ++Attaches; Check(Attaches == 1); _enter = enter; _exit = exit;
            if (FailAttach) throw new InvalidOperationException("Controlled partial physical Attach.");
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(_inFlight, Known);
        internal (NativeEngineNodeFrame Frame, nuint Token) Begin()
        {
            var frame = new NativeEngineNodeFrame { Version = 1, Size = 112, Kind = Identity.Kind,
                Arg0 = new(0x4000), Caller = new(0x5000), Sequence = ++_sequence };
            if (Identity.Kind == NativeEngineNodeKind.Start) { frame.Arg1 = new(0x6000); frame.Arg2 = new(0x7000); frame.Arg3 = new(0x8000); }
            else { frame.Node = frame.Arg0; frame.Owner = new(0x9000); frame.EnumeratorType = 2; frame.EnumeratorCache = new(0xa000);
                frame.EnumeratorHandle = 7; frame.ReferenceCount = 3; frame.SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid; }
            ++_inFlight; return (frame, _enter!(frame));
        }
        internal void End((NativeEngineNodeFrame Frame, nuint Token) call)
        {
            var completion = new NativeEngineNodeCompletion { Version = 1, Size = 32,
                Flags = NativeEngineNodeExitFlags.Returned | (Identity.Kind == NativeEngineNodeKind.Detach ? 0 : NativeEngineNodeExitFlags.ReturnByteValid),
                RawReturn = Identity.Kind == NativeEngineNodeKind.Execute ? 0xabcdef01u : Identity.Kind == NativeEngineNodeKind.Detach ? 0u : 0xfeu };
            if (Identity.Kind == NativeEngineNodeKind.Start)
            { completion.StartOutputNode = new(0x4000); completion.OutputFlags = NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read; }
            _exit!(call.Frame, call.Token, completion); --_inFlight;
        }
    }
    private static Bridge[] Bridges() => Enumerable.Range(0, 4).Select(index => new Bridge((NativeEngineNodeKind)index)).ToArray();
    private static NativeEngineRuntimeCoordinator Share(object key, Bridge[] bridges)
        => NativeEngineRuntimeCoordinator.ShareControlled(key, bridges, new Logger());
    private static NativeEngineRuntimeCoordinator.Subscription Bind(NativeEngineRuntimeCoordinator owner, List<Scope> calls,
        Action<NativeEngineNodeCompletion>? result = null)
        => owner.Bind(_ => { var scope = new Scope(); calls.Add(scope); return scope; },
            (scope, _, completion) => { ++scope.Completed; result?.Invoke(completion); }, (_, _) => { });

    private static void SharedPhysicalOwner()
    {
        var bridges = Bridges(); var key = new object(); var owner = Share(key, bridges);
        Check(ReferenceEquals(owner, Share(key, bridges)) && bridges.All(bridge => bridge.Attaches == 1));
        var first = new List<Scope>(); var second = new List<Scope>();
        using var a = Bind(owner, first); using var b = Bind(owner, second);
        foreach (var bridge in bridges) { var call = bridge.Begin(); bridge.End(call); }
        Check(first.Count == 4 && second.Count == 4 && first.Concat(second).All(scope => scope.Completed == 1 && scope.Disposed == 1));
        Check(owner.ReadActivity() is { Known: true, InFlight: 0 } && !owner.Unknown && bridges.All(bridge => bridge.Attaches == 1));
    }
    private static void CapturedDuties()
    {
        var bridges = Bridges(); var owner = Share(new object(), bridges); var first = new List<Scope>(); var second = new List<Scope>();
        uint execute = 0; var a = Bind(owner, first, completion => execute = completion.RawReturn);
        var outer = bridges[0].Begin();
        using var b = Bind(owner, second);
        var nested = bridges[1].Begin(); a.Dispose(); a.Dispose();
        bridges[1].End(nested); bridges[0].End(outer);
        Check(first.Count == 2 && second.Count == 1 && first.Concat(second).All(scope => scope.Completed == 1 && scope.Disposed == 1));
        // The outer completion overwrites execute with AL. A fresh Execute still
        // reaches its remaining sink with the complete uint32 register value.
        uint raw = 0; using var c = owner.Bind(_ => new Scope(), (Scope _, NativeEngineNodeFrame _, NativeEngineNodeCompletion completion) => raw = completion.RawReturn, (_, _) => { });
        var later = bridges[1].Begin(); bridges[1].End(later);
        Check(raw == 0xabcdef01 && first.Count == 2 && second.Count == 2 && owner.ReadActivity().Known && execute == 0xfe);
    }
    private static void PartialAttach()
    {
        var bridges = Bridges(); bridges[2].FailAttach = true; var key = new object();
        Reject(() => Share(key, bridges)); Reject(() => Share(key, bridges));
        Check(bridges[0].Attaches == 1 && bridges[1].Attaches == 1 && bridges[2].Attaches == 1 && bridges[3].Attaches == 0);
        // Previously attached callbacks remain callable and safely transparent.
        var call = bridges[0].Begin(); bridges[0].End(call);
    }
    private static void StickyUnknown()
    {
        var bridges = Bridges(); var key = new object(); var owner = Share(key, bridges);
        var contexts = new List<Scope>(); using var a = Bind(owner, contexts);
        var before = bridges[0].Begin(); bridges[3].Known = false;
        Check(!owner.ReadActivity().Known && owner.Unknown); bridges[3].Known = true;
        bridges[0].End(before); Check(contexts.Single() is { Completed: 1, Disposed: 1 });
        Check(!owner.ReadActivity().Known); Reject(() => Share(key, bridges)); Reject(() => Bind(owner, contexts));

        var other = Bridges(); var failed = Share(new object(), other); var completed = new List<Scope>();
        using var bad = failed.Bind<Scope>(_ => throw new InvalidOperationException("Controlled consumer fault."), (_, _, _) => { }, (_, _) => { });
        using var peer = Bind(failed, completed);
        var actual = other[1].Begin(); other[1].End(actual);
        Check(failed.Unknown && completed.Single() is { Completed: 1, Disposed: 1 } && !failed.ReadActivity().Known);
    }
    private static void ProductionUnavailable()
    {
        Check(!NativeEngineFirstInstall.ProductionStartupAdmissionKnown);
        for (var index = 0; index < 2; ++index)
        {
            Check(!NativeEngineRuntimeCoordinator.TryAcquire(new Logger(), out var owner, out var status, out var reason));
            Check(owner is null && status == NativeEngineRuntimeStatus.CapabilityUnavailable && !string.IsNullOrWhiteSpace(reason));
            Reject(() => NativeEngineFirstInstall.RequireReady());
        }
        Check(NativeEnginePublication.OwnedReadyAdmission == 31 && NativeEnginePublication.ProductionFactsAdmission == 0x7f00
            && NativeEnginePublication.ProductionReadyAdmission == 0xff00);
        var value = new byte[4096];
        Check(!NativeEnginePublication.HasReadyShape(NativeEnginePublication.Decode(value), 2));
    }
    private static void ProfileIdentity()
    {
        var hash = new string('a', 64); var rvas = Enumerable.Range(0, 4).Select(index => NativeEngineNodeProfile.Rva((NativeEngineNodeKind)index)).ToArray();
        var profile = new NativeEngineProductionProfileData(hash, hash, hash, hash, hash, rvas); rvas[0] = 1;
        Check(profile.Hash(4) == hash && profile.FunctionRva(NativeEngineNodeKind.Start) == 0x792240);
        try { _ = new NativeEngineProductionProfileData(hash, hash, hash, hash, hash, rvas); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Runtime caller-provided arbitrary source RVAs must be refused.");
    }
}
