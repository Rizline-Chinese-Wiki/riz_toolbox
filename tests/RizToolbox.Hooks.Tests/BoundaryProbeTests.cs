using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class BoundaryProbeTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Boundary probes require an existing exact physical entry and reject aliases without patching", Admission),
        ("Boundary probes precede capture and public callbacks, then exit after retained cleanup", Ordering),
        ("Boundary probes observe raw Boolean worker aliases and suppressed nested calls", EveryCall),
        ("Boundary probe snapshots pair exits after detach and contain faults without altering original", DetachAndFault)
    };
    private static VerifiedHookTarget Target(ExactHookAbi abi = ExactHookAbi.InstanceVoid)
        => new("boundary.probe.fixture", new(Interlocked.Add(ref Addresses.Next, 16)), new(0x123), new(0x321), abi);
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Boundary probe check failed"); }
    private sealed class Context : IDisposable { private readonly Action _dispose; internal Context(Action dispose) => _dispose = dispose; public void Dispose() => _dispose(); }
    private static void Admission()
    {
        using var f = new Fixture(); var target = Target();
        try { f.Broker.BindBoundaryProbe(target, (_, _, _) => { }, (_, _, _, _) => { }); throw new Exception("Missing entry accepted"); }
        catch (UnsupportedSignatureException) { }
        Check(f.Engine.Installs == 0);
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { });
        using var binding = f.Broker.Bind(target, _ => true, (_, _, _, _) => { });
        foreach (var alias in new[] { target with { MethodInfo = new(9) }, target with { DeclaringClass = new(8) }, target with { Abi = ExactHookAbi.InstanceEnum32Void } })
        {
            try { f.Broker.BindBoundaryProbe(alias, (_, _, _) => { }, (_, _, _, _) => { }); throw new Exception("Alias accepted"); }
            catch (UnsupportedSignatureException) { }
        }
        Check(f.Engine.Installs == 1);
    }
    private static void Ordering()
    {
        using var f = new Fixture(); var target = Target(); var order = new List<string>();
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => order.Add("original"));
        using var ordinary = f.Broker.Bind(target, _ => true, (phase, _, _, _) => order.Add(phase.ToString()));
        using var retained = f.Broker.BindEarlyRetainedObservation(target, _ => true,
            _ => { order.Add("capture"); return new Context(() => order.Add("dispose")); }, _ => order.Add("retained-after"));
        using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => order.Add("probe-enter"), (_, _, _, returned) => { Check(returned); order.Add("probe-exit"); });
        ((ExactHookBroker.InstanceVoid)f.Engine.Replacement!)(new(1), target.MethodInfo);
        Check(string.Join(",", order) == "probe-enter,capture,Before,original,retained-after,After,dispose,probe-exit");
        Check(probe.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void EveryCall()
    {
        using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceBooleanVoid);
        var entered = new List<byte>(); var exited = new List<byte>(); var captures = 0;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanVoid((self, value, method) => {
            if (value == 1) ((ExactHookBroker.InstanceBooleanVoid)f.Engine.Replacement!)(self, 0x80, method);
        });
        using var binding = f.Broker.BindRetainedBooleanArgumentObservation(target, _ => true,
            (_, _) => { captures++; return new Context(() => { }); }, (_, _) => { });
        using var probe = f.Broker.BindBoundaryProbe(target, (_, value, _) => entered.Add(value), (_, value, _, returned) => { Check(returned); exited.Add(value); });
        var invoke = (ExactHookBroker.InstanceBooleanVoid)f.Engine.Replacement!;
        invoke(new(1), 1, target.MethodInfo); // Nested capture is suppressed, scalar probe is paired.
        Task.Run(() => invoke(new(1), 0xff, target.MethodInfo)).GetAwaiter().GetResult();
        invoke(new(1), 0, new(0x999)); // Foreign metadata is still visible to the scalar lane.
        Check(entered.SequenceEqual(new byte[] { 1, 0x80, 0xff, 0 }) && exited.SequenceEqual(new byte[] { 0x80, 1, 0xff, 0 }) && captures == 1);
    }
    private static void DetachAndFault()
    {
        using var f = new Fixture(); var target = Target(); var exits = 0; var originals = 0; var abnormal = false;
        ExactHookBroker.BoundaryProbeBinding? probe = null;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { originals++; probe!.Dispose(); if (abnormal) throw new IOException("original"); });
        using var binding = f.Broker.Bind(target, _ => true, (_, _, _, _) => { });
        probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => throw new IOException("probe"), (_, _, _, returned) => { exits++; Check(returned); });
        var invoke = (ExactHookBroker.InstanceVoid)f.Engine.Replacement!; invoke(new(1), target.MethodInfo);
        Check(originals == 1 && exits == 1 && !probe.ReadActivity().Known);
        invoke(new(1), target.MethodInfo); Check(originals == 2 && exits == 1);
        using var abnormalProbe = f.Broker.BindBoundaryProbe(target, (_, _, _) => { }, (_, _, _, returned) => { Check(!returned); exits++; });
        abnormal = true; try { invoke(new(1), target.MethodInfo); throw new Exception("Original exception disappeared"); } catch (IOException) { }
        Check(originals == 3 && exits == 2);
    }
}
