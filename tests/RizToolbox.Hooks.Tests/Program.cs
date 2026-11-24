using System.Runtime.InteropServices;
using System.Reflection.Emit;
using RizToolbox.Core;
using RizToolbox.Core.Hooks;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

var tests = new (string Name, Action Run)[]
{
    ("one address gets one patch across broker instances and logical rebinds", OnePatch),
    ("judge delegate forwards instance enum float and MethodInfo unchanged exactly once", JudgeArguments),
    ("enum32 unmanaged entry preserves signed argument receiver MethodInfo and after-original order", Enum32Arguments),
    ("enum32 original failure propagates and nested entries forward exactly once", Enum32FaultAndReentry),
    ("enum32 wrong-thread unmatched and detached entries preserve native pass-through", Enum32Isolation),
    ("before and after callback exceptions cannot skip duplicate or swallow original", ManagedCallbackFaults),
    ("original exception is propagated and native reentry gate is released", OriginalFault),
    ("nested native entries call original once but suppress nested logical delivery", Reentry),
    ("off-main entries forward original without receiver metadata access", OffMain),
    ("shared address receiver filter drops unrelated receiver but preserves original", ReceiverFilter),
    ("unconfirmed published install retains process ownership and conflicting ABI is rejected", InstallRejections),
    ("delegate and trampoline survive GC after all logical bindings release", ProcessRoots),
    ("metadata layout validates against exported fields and exact code address", MetadataValidation),
    ("unreadable metadata and non-image executable address are rejected", MemoryRejections),
    ("dispatcher type version phase ordering and ownership are enforced", TypedDispatcher),
    ("formal PluginHost hook callback fault stops owner and preserves unrelated observer", HostFaultIntegration),
    ("reference result forwards once, validates both results and retains roots across dispatch", ReferenceResult),
    ("reference result preserves original exception, null and invalid replacement", ReferenceFaults),
    ("reference result reentry and wrong thread pass through without transforms", ReferenceIsolation),
    ("reference coordinator is exclusive, shares one patch and in-flight disable preserves original", ReferenceOwnership),
    ("pure reference observer captures entry provenance and cannot replace original return", ReferenceObservation),
    ("pure reference observer sees original before shared replacement coordinator and retains independent roots", ReferenceObservationShared),
    ("pure reference observation failures preserve original exception null result and exact forwarding", ReferenceObservationFaults),
    ("pure reference observer rejects wrong-thread reentry mismatch and in-flight withdrawal", ReferenceObservationIsolation),
    ("pure reference observer cleanup faults isolate peers and validate result roots", ReferenceObservationCleanup),
    ("retained observation keeps one typed context through original and after without changing ordinary phases", RetainedObservation),
    ("retained observation releases after an original exception and preserves the reentry gate", RetainedOriginalFault),
    ("retained observation disabled by the original skips after and still releases", RetainedDisabled),
    ("failed context capture forwards original once and preserves other retained observers", RetainedCaptureFault),
    ("after and cleanup exceptions cannot skip remaining retained cleanup or ordinary observers", RetainedAfterFault),
    ("retained wrong-thread reentry and unmatched entries never capture context", RetainedIsolation),
    ("retained observation rejects unsupported native ABIs before installation", RetainedAbi)
    ,("native context prepares configures then commits and shares one patch with ordinary observers", NativePreparedOrdering)
    ,("native context original failure and reentry preserve exact forwarding and release", NativeContextOriginalFailure)
    ,("native context capture after and release faults isolate other observers", NativeContextObserverFailure)
    ,("native context wrong thread unmatched and disabled bindings retain no stale delivery", NativeContextIsolation)
    ,("native context failed preparation configuration and uncertain commit reserve ownership", NativeContextInstallFailure)
    ,("native context refuses a second patch over a managed entry and rejects wrong ABI", NativeContextTransportConflict)
    ,("native context process roots survive complete logical detach and GC", NativeContextProcessRoots)
    ,("native context managed ABI matches every pinned register and argument slot", NativeContextLayout)
    ,("retained inner judge captures before ordinary observers and reads after original before public callbacks", RetainedJudgeOrder)
    ,("retained inner judge abnormal original and in-flight disable release every captured root", RetainedJudgeCleanup)
    ,("retained judge observers share native transport without losing their capture callbacks", RetainedJudgeNativeTransport)
}.Concat(LocalResultStartPolicyTests.Cases).Concat(LocalResultStartUnmanagedTests.Cases)
 .Concat(LocalRunCommandPolicyTests.Cases).Concat(LocalProgressIsolationTests.Cases).Concat(LocalInitializationIsolationTests.Cases)
 .Concat(BooleanArgumentObservationTests.Cases).Concat(JudgeActivityTests.Cases).Concat(BoundaryProbeTests.Cases)
 .Concat(CoroutineObservationTests.Cases).Concat(NativeVoidObservationTests.Cases).Concat(NativeVoidAdmissionLeaseTests.Cases)
 .Concat(NativeBootstrappedVoidBridgeTests.Cases).Concat(NativeFirstInstallAdoptionTests.Cases)
 .Concat(NativeListenerContextBridgeTests.Cases)
 .Concat(NativeOperationObservationTests.Cases).Concat(NativeOperationPreinitTests.Cases).ToArray();
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} exact-hook checks passed.");
return failed == 0 ? 0 : 1;

static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static T Need<T>(OperationResult<T> result) { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing value."); return result.Value!; }
static void Good(OperationResult result) => Check(result.Success, result.Error?.Message ?? "Operation failed.");
static void Throws(Action action, string expected)
{
    try { action(); throw new Exception("Expected " + expected); }
    catch (Exception ex) { Check(ex.ToString().Contains(expected), "Unexpected error: " + ex); }
}
static VerifiedHookTarget Target(ExactHookAbi abi = ExactHookAbi.InstanceVoid) => new("test." + Guid.NewGuid().ToString("N"),
    new IntPtr(Interlocked.Add(ref Addresses.Next, 16)), new IntPtr(0x1234), new IntPtr(0x4321), abi);

static void OnePatch()
{
    using var f = new Fixture(); var target = Target(); var calls = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
    var a = f.Broker.Bind(target, _ => true, (_, _, _, _) => { });
    var other = new ExactHookBroker(f.Engine, f.Main, f.Log);
    var b = other.Bind(target with { Id = "alias" }, _ => true, (_, _, _, _) => { });
    f.Engine.InvokeVoid(); a.Dispose(); b.Dispose();
    var c = f.Broker.Bind(target, _ => true, (_, _, _, _) => { }); f.Engine.InvokeVoid(); c.Dispose();
    Check(f.Engine.Installs == 1 && calls == 2 && c.Describe().OriginalCalls == 2, "Address was patched twice or rebind changed original count.");
}
static void RetainedObservation()
{
    using var f = new Fixture(); var target = Target(); var stages = new List<string>(); RetainedContext? context = null;
    var captures = 0; var afters = 0; var originals = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((receiver, method) =>
    {
        originals++; Check(receiver == new IntPtr(0x111) && method == new IntPtr(0x222), "Retained hook changed the original ABI arguments.");
        Check(context is { Disposed: false }, "Context was not retained across original invocation."); stages.Add("original");
    });
    using var ordinary = f.Broker.Bind(target, _ => true, (phase, _, _, _) => stages.Add(phase.ToString()));
    using var binding = f.Broker.BindRetainedObservation(target, _ => true, receiver =>
    {
        captures++; stages.Add("retain"); context = new(receiver, () => stages.Add("dispose")); return context;
    }, retained =>
    {
        afters++; Check(ReferenceEquals(retained, context) && !retained.Disposed && retained.Receiver == new IntPtr(0x111), "After lost the exact live typed context.");
        stages.Add("retained-after");
    });
    f.Engine.InvokeVoid();
    Check(captures == 1 && originals == 1 && afters == 1 && context!.DisposeCalls == 1
        && stages.SequenceEqual(new[] { "Before", "retain", "original", "retained-after", "After", "dispose" })
        && binding.Describe().OriginalCalls == 1 && f.Engine.Installs == 1,
        "Retained observation duplicated a call, changed ordinary phases, or released context at the wrong boundary.");
}
static void RetainedOriginalFault()
{
    using var f = new Fixture(); var target = Target(); var contexts = new List<RetainedContext>(); var calls = 0; var afters = 0; var fail = true;
    var originalFailure = new IOException("retained original failure");
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { calls++; if (fail) throw originalFailure; });
    using var binding = f.Broker.BindRetainedObservation(target, _ => true,
        receiver =>
        {
            var first = contexts.Count == 0;
            var context = new RetainedContext(receiver, () => { if (first) throw new IOException("cleanup must not replace original"); });
            contexts.Add(context); return context;
        }, _ => afters++);
    Exception? observed = null; try { f.Engine.InvokeVoid(); } catch (Exception ex) { observed = ex; }
    Check(ReferenceEquals(observed, originalFailure) && calls == 1 && afters == 0 && contexts.Single().DisposeCalls == 1,
        "Original exception was replaced, retried, observed as success, or leaked a retained context.");
    fail = false; f.Engine.InvokeVoid();
    Check(calls == 2 && afters == 1 && contexts.Count == 2 && contexts.All(x => x.DisposeCalls == 1)
        && binding.Describe().SuppressedReentries == 0, "Exception leaked the reentry gate or context into the next invocation.");
}
static void RetainedDisabled()
{
    using var f = new Fixture(); var target = Target(); ExactHookBroker.Binding? binding = null; RetainedContext? context = null;
    var calls = 0; var afters = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) =>
    {
        calls++;
        if (calls == 1) { Check(context is { Disposed: false }, "Context was unavailable before in-flight disable."); binding!.Dispose(); }
        else Check(context is { Disposed: true }, "Detached original retained a new observer context.");
    });
    binding = f.Broker.BindRetainedObservation(target, _ => true, receiver => context = new(receiver), _ => afters++);
    f.Engine.InvokeVoid(); binding.Dispose(); f.Engine.InvokeVoid();
    Check(calls == 2 && afters == 0 && context!.DisposeCalls == 1,
        "Disabling during original delivered a stale after callback or leaked/double-released context.");
}
static void RetainedCaptureFault()
{
    using var f = new Fixture(); var target = Target(); var calls = 0; var failedAfter = 0; var healthyAfter = 0; RetainedContext? healthy = null;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
    using var broken = f.Broker.BindRetainedObservation<RetainedContext>(target, _ => true,
        _ => throw new IOException("retain unavailable"), _ => failedAfter++);
    using var nullContext = f.Broker.BindRetainedObservation<RetainedContext>(target, _ => true, _ => null!, _ => failedAfter++);
    using var valid = f.Broker.BindRetainedObservation(target, _ => true, receiver => healthy = new(receiver), _ => healthyAfter++);
    f.Engine.InvokeVoid();
    Check(calls == 1 && failedAfter == 0 && healthyAfter == 1 && healthy!.DisposeCalls == 1
        && f.Log.Messages.Count(x => x.Contains("context capture failed")) == 2,
        "Capture failure skipped/retried original, delivered an invalid context, or blocked a healthy observer.");
}
static void RetainedAfterFault()
{
    using var f = new Fixture(); var target = Target(); var calls = 0; var ordinaryAfters = 0; var healthyAfters = 0; var contexts = new List<RetainedContext>();
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
    using var ordinary = f.Broker.Bind(target, _ => true, (phase, _, _, _) => { if (phase == HookPhase.After) ordinaryAfters++; });
    using var healthy = f.Broker.BindRetainedObservation(target, _ => true,
        receiver => { var context = new RetainedContext(receiver); contexts.Add(context); return context; }, _ => healthyAfters++);
    using var broken = f.Broker.BindRetainedObservation(target, _ => true,
        receiver => { var context = new RetainedContext(receiver, () => throw new IOException("retained cleanup failure")); contexts.Add(context); return context; },
        _ => throw new IOException("retained after failure"));
    f.Engine.InvokeVoid();
    Check(calls == 1 && ordinaryAfters == 1 && healthyAfters == 1 && contexts.Count == 2 && contexts.All(x => x.DisposeCalls == 1)
        && f.Log.Messages.Any(x => x.Contains("observation failed")) && f.Log.Messages.Any(x => x.Contains("context release failed")),
        "After/cleanup failure escaped, skipped original/ordinary after, or prevented another context from being released.");
}
static void RetainedIsolation()
{
    using var f = new Fixture(); var target = Target(); var calls = 0; var matches = 0; var captures = 0; var afters = 0; var releases = 0; var accept = true;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { if (++calls == 1) f.Engine.InvokeVoid(); });
    using var binding = f.Broker.BindRetainedObservation(target, _ => { matches++; return accept; },
        receiver => { captures++; return new RetainedContext(receiver, () => releases++); }, _ => afters++);
    f.Engine.InvokeVoid();
    Exception? workerFailure = null; var worker = new Thread(() => { try { f.Engine.InvokeVoid(); } catch (Exception ex) { workerFailure = ex; } });
    worker.Start(); worker.Join(); accept = false; f.Engine.InvokeVoid();
    Check(workerFailure is null && calls == 4 && matches == 2 && captures == 1 && afters == 1 && releases == 1
        && binding.Describe().SuppressedWrongThread == 1 && binding.Describe().SuppressedReentries == 1,
        "Wrong-thread, reentrant, or unmatched native entries accessed retention metadata or changed pass-through behavior.");
}
static void RetainedAbi()
{
    using var f = new Fixture();
    foreach (var abi in new[] { ExactHookAbi.InstanceEnum32SingleVoid, ExactHookAbi.InstanceReferenceReturn0 })
        Throws(() => f.Broker.BindRetainedObservation(Target(abi), _ => true, receiver => new RetainedContext(receiver), _ => { }), "InstanceVoid");
    Check(f.Engine.Installs == 0, "Unsupported retained ABI installed a native patch.");
}
static NativeJudgeFrame NativeFrame() => new()
{
    AbiVersion = 1, ByteSize = 0x1a0, ReturnAddress = 0x3fd730, EntryStackPointer = 0x1238, Rsp = 0x1238,
    Rcx = 0x111, Rdx = 0xabcdef0080000005, R9 = 0x222, Rbx = 0x333, R14 = 0x444,
    Xmm2 = new() { Low = 0x987654327fc12345, High = 0xfedcba9876543210 }
};
static void NativePreparedOrdering()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid);
    var order = new List<string>(); var bridge = new NativeBridge(); RetainedContext? captured = null;
    f.Engine.PrepareAction = () => { Check(!bridge.Configured, "Bridge configured without a real trampoline."); order.Add("prepare"); };
    bridge.ConfigureAction = () => order.Add("configure");
    bridge.Original = frame => { Check(frame.Judge == unchecked((int)0x80000005) && frame.R9 == 0x222
        && BitConverter.SingleToInt32Bits(frame.Delta) == 0x7fc12345 && captured is { Disposed: false }, "Original arguments or retention changed."); order.Add("original"); };
    f.Engine.CommitAction = () => { Check(bridge.Configured, "Patch published before immutable bridge configuration."); order.Add("commit"); bridge.Invoke(NativeFrame()); };
    using var native = f.Broker.BindNativeJudgeContext(target, () => bridge,
        frame => { Check(frame.Rbx == 0x333 && frame.R14 == 0x444, "Caller registers lost."); order.Add("capture"); return captured = new(frame.Receiver, () => order.Add("release")); },
        (context, frame) => { Check(ReferenceEquals(context, captured) && frame.IsValid, "Native exit did not retain the same context."); order.Add("after-context"); });
    Check(order.SequenceEqual(new[] { "prepare", "configure", "commit", "capture", "original", "after-context", "release" }), "Native publication or callback order differs.");
    var other = new ExactHookBroker(f.Engine, f.Main, f.Log);
    using var ordinary = other.Bind(target, _ => true, (phase, _, _, _) => order.Add(phase == HookPhase.Before ? "before" : "after"));
    using var extra = other.BindNativeJudgeContext<RetainedContext>(target, () => throw new Exception("second native bridge"), _ => null, (_, _) => { });
    order.Clear(); bridge.Invoke(NativeFrame());
    Check(order.SequenceEqual(new[] { "capture", "before", "original", "after-context", "after", "release" })
        && f.Engine.Prepares == 1 && f.Engine.Commits == 1 && f.Engine.Installs == 0
        && native.Describe().OriginalCalls == 2, "Logical observers installed another patch or changed native ordering.");
}
static void NativeContextOriginalFailure()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid); var bridge = new NativeBridge();
    var calls = 0; var captures = 0; var afters = 0; var releases = 0; var fail = true; var exception = new IOException("native original fixture failure");
    bridge.Original = frame => { Check(ExactHookBroker.NativeJudgeContextDepth == (calls == 1 ? 2 : 1), "Suppressed native reentry inherited an outer invocation boundary."); if (++calls == 1) bridge.Invoke(frame); if (fail) throw exception; };
    using var binding = f.Broker.BindNativeJudgeContext(target, () => bridge,
        frame => { captures++; return new RetainedContext(frame.Receiver, () => releases++); }, (_, _) => afters++);
    Exception? seen = null; try { bridge.Invoke(NativeFrame()); } catch (Exception error) { seen = error; }
    Check(ReferenceEquals(seen, exception) && calls == 2 && captures == 1 && releases == 1 && afters == 0,
        "Original exception/reentry was swallowed, retried, inherited context or leaked retention.");
    fail = false; bridge.Invoke(NativeFrame());
    Check(calls == 3 && captures == 2 && releases == 2 && afters == 1 && binding.Describe().SuppressedReentries == 1,
        "Native abnormal exit did not clear the reentry gate.");
    Check(ExactHookBroker.NativeJudgeContextDepth == 0, "Native exception/reentry leaked physical invocation depth.");
}
static void NativeContextObserverFailure()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid); var bridge = new NativeBridge();
    var calls = 0; var afters = 0; var ordinary = 0; var releases = 0;
    bridge.Original = _ => calls++;
    using var captureFault = f.Broker.BindNativeJudgeContext<RetainedContext>(target, () => bridge,
        _ => throw new IOException("capture failure"), (_, _) => throw new Exception("must not deliver"));
    using var afterFault = f.Broker.BindNativeJudgeContext(target, () => bridge,
        frame => new RetainedContext(frame.Receiver, () => { releases++; throw new IOException("release failure"); }),
        (_, _) => throw new IOException("after failure"));
    using var healthy = f.Broker.BindNativeJudgeContext(target, () => bridge,
        frame => new RetainedContext(frame.Receiver, () => releases++), (_, _) => afters++);
    using var logical = f.Broker.Bind(target, _ => true, (phase, _, _, _) => { if (phase == HookPhase.After) ordinary++; });
    bridge.Invoke(NativeFrame()); bridge.Invoke(NativeFrame());
    Check(calls == 2 && afters == 2 && ordinary == 2 && releases == 4 && f.Log.Messages.Count(x => x.Contains("failed")) == 6,
        "Observer failure crossed the original boundary or skipped healthy observers/cleanup.");
}
static void NativeContextIsolation()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid); var bridge = new NativeBridge();
    var calls = 0; var captures = 0; var releases = 0; var afters = 0; var filters = 0;
    ExactHookBroker.Binding? binding = null;
    bridge.Original = _ => { calls++; if (f.Main.IsMainThread) binding?.Dispose(); };
    binding = f.Broker.BindNativeJudgeContext(target, () => bridge,
        frame => { captures++; return new RetainedContext(frame.Receiver, () => releases++); }, (_, _) => afters++);
    using var unmatched = f.Broker.Bind(target, _ => { filters++; return false; }, (_, _, _, _) => throw new Exception("unmatched"));
    Exception? failure = null; var thread = new Thread(() => { try { bridge.Invoke(NativeFrame()); } catch (Exception error) { failure = error; } });
    thread.Start(); thread.Join(); Check(failure is null && captures == 0 && filters == 0, "Wrong thread accessed native metadata or a context.");
    bridge.Invoke(NativeFrame()); bridge.Invoke(NativeFrame());
    Check(calls == 3 && captures == 1 && releases == 1 && afters == 0 && filters == 2 && binding.Describe().SuppressedWrongThread == 1,
        "In-flight detach delivered a stale after, freed a live context or changed native forwarding.");
}
static void NativeContextInstallFailure()
{
    foreach (var stage in new[] { "prepare", "configure", "commit" })
    {
        using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid); var bridge = new NativeBridge(); var calls = 0;
        bridge.Original = _ => calls++;
        if (stage == "prepare") f.Engine.PrepareAction = () => throw new IOException("prepare uncertain");
        if (stage == "configure") bridge.ConfigureAction = () => throw new IOException("configure uncertain");
        if (stage == "commit") f.Engine.CommitAction = () => { bridge.Invoke(NativeFrame()); throw new IOException("commit uncertain"); };
        Throws(() => f.Broker.BindNativeJudgeContext<RetainedContext>(target, () => bridge, _ => null, (_, _) => { }), "uncertain");
        Throws(() => f.Broker.BindNativeJudgeContext<RetainedContext>(target, () => throw new Exception("retried"), _ => null, (_, _) => { }), "process-owned");
        Throws(() => f.Broker.Bind(target, _ => true, (_, _, _, _) => { }), "process-owned");
        Check(f.Engine.Prepares == 1 && f.Engine.Installs == 0 && f.Engine.Commits == (stage == "commit" ? 1 : 0), "Uncertain native installation retried or installed another transport.");
        if (stage == "commit") { bridge.Invoke(NativeFrame()); Check(calls == 2, "An uncertain published native entry lost its process roots/original."); }
    }
}
static void NativeContextTransportConflict()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid);
    f.Engine.Original = new ExactHookBroker.InstanceJudge((_, _, _, _) => { });
    using var managed = f.Broker.Bind(target, _ => true, (_, _, _, _) => { });
    Throws(() => f.Broker.BindNativeJudgeContext<RetainedContext>(target, () => throw new Exception("created second bridge"), _ => null, (_, _) => { }), "second patch");
    foreach (var abi in new[] { ExactHookAbi.InstanceVoid, ExactHookAbi.InstanceEnum32Void, ExactHookAbi.InstanceReferenceReturn0 })
        Throws(() => f.Broker.BindNativeJudgeContext<RetainedContext>(Target(abi), () => throw new Exception("wrong ABI bridge created"), _ => null, (_, _) => { }), "ABI");
    Check(f.Engine.Installs == 1 && f.Engine.Prepares == 0 && f.Engine.Commits == 0, "A rejected context hook mutated native ownership.");
}
static void NativeContextProcessRoots()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid); var calls = 0;
    var bridge = new NativeBridge { Original = _ => calls++ }; var weak = new WeakReference(bridge);
    var binding = f.Broker.BindNativeJudgeContext<RetainedContext>(target, () => bridge, _ => throw new Exception("detached capture"), (_, _) => { });
    binding.Dispose(); bridge = null!; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Check(weak.IsAlive, "The native library/callback owner was collected after logical detach.");
    ((NativeBridge)weak.Target!).Invoke(NativeFrame()); Check(calls == 1, "A detached context entry no longer forwards original.");
}
static void NativeContextLayout()
{
    Check(Marshal.SizeOf<NativeJudgeFrame>() == 0x1a0 && Marshal.SizeOf<NativeJudgeXmm>() == 16
        && Marshal.OffsetOf<NativeJudgeFrame>(nameof(NativeJudgeFrame.Rcx)).ToInt32() == 0x28
        && Marshal.OffsetOf<NativeJudgeFrame>(nameof(NativeJudgeFrame.Rdx)).ToInt32() == 0x30
        && Marshal.OffsetOf<NativeJudgeFrame>(nameof(NativeJudgeFrame.R9)).ToInt32() == 0x68
        && Marshal.OffsetOf<NativeJudgeFrame>(nameof(NativeJudgeFrame.R15)).ToInt32() == 0x98
        && Marshal.OffsetOf<NativeJudgeFrame>(nameof(NativeJudgeFrame.Xmm2)).ToInt32() == 0xc0
        && Marshal.OffsetOf<NativeJudgeFrame>(nameof(NativeJudgeFrame.Xmm15)).ToInt32() == 0x190, "Managed/native frame layout differs.");
    var frame = NativeFrame(); Check(frame.IsValid && frame.Receiver == new IntPtr(0x111), "A real frame was rejected.");
    frame.EntryStackPointer++; Check(!frame.IsValid, "Mismatched entry stack accepted.");
    frame = NativeFrame(); frame.ByteSize--; Check(!frame.IsValid, "Wrong snapshot size accepted.");
}
static void RetainedJudgeOrder()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid); var order = new List<string>();
    f.Engine.Original = new ExactHookBroker.InstanceJudge((receiver,judge,delta,method) =>
    { Check(receiver == new IntPtr(1) && judge == -1 && delta == -2 && method == new IntPtr(2),"Inner original arguments changed."); order.Add("original"); });
    using var ordinary = f.Broker.Bind(target,_ => true,(phase,_,_,_) => order.Add(phase == HookPhase.Before ? "before" : "after"));
    using var retained = f.Broker.BindRetainedJudgeObservation(target,_ => true,(receiver,judge,delta) =>
    { Check(judge == -1 && delta == -2,"Retained inner arguments changed."); order.Add("capture"); return new RetainedContext(receiver,() => order.Add("release")); },
        (context,invocation) => { Check(!context.Disposed && invocation == 1,"Inner retained lifetime/sequence changed."); order.Add("retained-after"); });
    f.Engine.InvokeJudge(new(1),-1,-2,new(2));
    Check(order.SequenceEqual(new[]{"capture","before","original","retained-after","after","release"}),"Inner observer order is not stable around original.");
}
static void RetainedJudgeCleanup()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid); var releases = 0; var afters = 0; var calls = 0;
    var fail = true; ExactHookBroker.Binding? retained = null; var failure = new IOException("inner original failure");
    f.Engine.Original = new ExactHookBroker.InstanceJudge((_,_,_,_) => { calls++; if(fail) throw failure; retained!.Dispose(); });
    retained = f.Broker.BindRetainedJudgeObservation(target,_ => true,(receiver,_,_) => new RetainedContext(receiver,() => releases++),(_,_) => afters++);
    Exception? caught = null; try { f.Engine.InvokeJudge(new(1),1,.1f,new(2)); } catch(Exception error) { caught = error; }
    Check(ReferenceEquals(caught,failure) && releases == 1 && afters == 0,"Inner original exception was transformed or leaked its root.");
    fail = false; f.Engine.InvokeJudge(new(1),1,.1f,new(2)); f.Engine.InvokeJudge(new(1),1,.1f,new(2));
    Check(calls == 3 && releases == 2 && afters == 0,"Detached inner observation delivered an after callback or retained another root.");
}
static void RetainedJudgeNativeTransport()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceInt32SingleVoid); var bridge = new NativeBridge();
    using var outer = f.Broker.BindNativeJudgeContext<RetainedContext>(target,() => bridge,_ => null,(_,_) => {});
    var captures = 0; var afters = 0; var releases = 0;
    using var retained = f.Broker.BindRetainedJudgeObservation(target,_ => true,(receiver,_,_) =>
    { captures++; return new RetainedContext(receiver,() => releases++); },(_,_) => afters++);
    bridge.Invoke(NativeFrame());
    Check(captures == 1 && afters == 1 && releases == 1 && f.Engine.Prepares == 1 && f.Engine.Installs == 0,
        "Native transport silently dropped retained judge observers or installed a second patch.");
}
static void ReferenceResult()
{
    using var f=new Fixture();var target=Target(ExactHookAbi.InstanceReferenceReturn0)with{ReturnClass=new(9)};var calls=0;var releases=0;var retained=new List<IntPtr>();
    f.Engine.Original=new ExactHookBroker.InstanceReferenceReturn0((self,method)=>{calls++;Check(self==new IntPtr(1)&&method==new IntPtr(2)&&releases==0,"Reference ABI/receiver root changed before original.");return new(3);});
    using var binding=f.Broker.BindReferenceResult(target,_=>true,_=>new TestRelease(()=>releases++),p=>{retained.Add(p);return new TestRelease(()=>releases++);},
        (_,original)=>{Check(original==new IntPtr(3)&&releases==0,"Original result was not rooted through transform.");return new NativeRuntime.RootHandle(1,_=>releases++,_=>new IntPtr(4));});
    var returned=((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1),new(2));
    Check(returned==new IntPtr(4)&&calls==1&&retained.SequenceEqual(new[]{new IntPtr(3),new IntPtr(4)})&&releases==4&&binding.Describe().OriginalCalls==1,"Rooted reference return mismatch.");
}
static void ReferenceFaults()
{
    using var f=new Fixture();var target=Target(ExactHookAbi.InstanceReferenceReturn0)with{ReturnClass=new(9)};var transforms=0;var roots=0;var failure=true;var nullResult=false;
    f.Engine.Original=new ExactHookBroker.InstanceReferenceReturn0((_,_)=>failure?throw new Exception("ref original failure"):nullResult?IntPtr.Zero:new IntPtr(3));
    using var binding=f.Broker.BindReferenceResult(target,_=>true,_=>new TestRelease(()=>roots++),p=>p==new IntPtr(4)?throw new UnsupportedSignatureException("wrong reference type"):new TestRelease(()=>roots++),
        (_,_)=>{transforms++;return new NativeRuntime.RootHandle(1,_=>roots++,_=>new IntPtr(4));});
    var invoke=()=>((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1),new(2));
    Throws(()=>invoke(),"ref original failure");Check(transforms==0&&roots==1,"Original exception was transformed or leaked receiver root.");failure=false;nullResult=true;Check(invoke()==IntPtr.Zero&&transforms==0,"Null original changed semantics.");
    nullResult=false;Check(invoke()==new IntPtr(3)&&transforms==1&&roots==5,"Invalid replacement was returned or its root leaked.");
}
static void ReferenceIsolation()
{
    using var f=new Fixture();var target=Target(ExactHookAbi.InstanceReferenceReturn0)with{ReturnClass=new(9)};var calls=0;var transforms=0;var metadata=0;
    f.Engine.Original=new ExactHookBroker.InstanceReferenceReturn0((_,_)=>{if(++calls==1)((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1),new(2));return new(3);});
    using var binding=f.Broker.BindReferenceResult(target,_=>{metadata++;return true;},_=>new TestRelease(()=>{}),_=>new TestRelease(()=>{}),(_,_)=>{transforms++;return new NativeRuntime.RootHandle(1,_=>{},_=>new IntPtr(4));});
    Check(((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1),new(2))==new IntPtr(4)&&calls==2&&transforms==1&&metadata==1,"Reference reentry did not pass through.");
    var worker=new Thread(()=>Check(((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1),new(2))==new IntPtr(3),"Off-main result changed."));worker.Start();worker.Join();
    Check(calls==3&&transforms==1&&metadata==1&&binding.Describe().SuppressedWrongThread==1,"Reference worker touched metadata or transform.");
}
static void ReferenceOwnership()
{
    using var f=new Fixture();var target=Target(ExactHookAbi.InstanceReferenceReturn0)with{ReturnClass=new(9)};var transforms=0;ExactHookBroker.Binding? active=null;
    f.Engine.Original=new ExactHookBroker.InstanceReferenceReturn0((_,_)=>{active?.Dispose();return new(3);});
    active=f.Broker.BindReferenceResult(target,_=>true,_=>new TestRelease(()=>{}),_=>new TestRelease(()=>{}),(_,_)=>{transforms++;return new NativeRuntime.RootHandle(1,_=>{},_=>new IntPtr(4));});
    Throws(()=>f.Broker.BindReferenceResult(target,_=>true,_=>new TestRelease(()=>{}),_=>new TestRelease(()=>{}),(_,_)=>null),"Conflict");
    Throws(()=>f.Broker.Bind(target,_=>true,(_,_,_,_)=>{}),"rooted result binding");
    Check(((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1),new(2))==new IntPtr(3)&&transforms==0&&f.Engine.Installs==1,"Disable during original returned withdrawn content or patched twice.");
    using var next=f.Broker.BindReferenceResult(target,_=>true,_=>new TestRelease(()=>{}),_=>new TestRelease(()=>{}),(_,_)=>null);Check(f.Engine.Installs==1,"Logical rebind patched a second time.");
}

static void ReferenceObservation()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceReferenceReturn0) with { ReturnClass = new(9) };
    var stages = new List<string>(); RetainedContext? context = null; RetainedContext? resultRoot = null; var calls = 0;
    f.Engine.Original = new ExactHookBroker.InstanceReferenceReturn0((receiver, method) =>
    {
        calls++; Check(receiver == new IntPtr(1) && method == new IntPtr(2) && context is { Disposed: false },
            "Entry provenance was not retained or original ABI changed."); stages.Add("original"); return new(3);
    });
    using var observer = f.Broker.BindRetainedReferenceObservation(target, _ => true,
        receiver => { stages.Add("capture"); return context = new(receiver, () => stages.Add("context-release")); },
        result => { Check(result == new IntPtr(3), "Non-original result was rooted."); stages.Add("result-root"); return resultRoot = new(result, () => stages.Add("result-release")); },
        (entry, result) =>
        {
            Check(ReferenceEquals(entry, context) && !entry.Disposed && resultRoot is { Disposed: false }
                && result == new IntPtr(3), "Original after observation lost entry context or result root."); stages.Add("observe");
        });
    var returned = ((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1), new(2));
    Check(returned == new IntPtr(3) && calls == 1 && context!.DisposeCalls == 1 && resultRoot!.DisposeCalls == 1
        && stages.SequenceEqual(new[] { "capture", "original", "result-root", "observe", "result-release", "context-release" }),
        "Pure observation changed the original return or lifetime boundaries.");
    Throws(() => f.Broker.BindRetainedReferenceObservation(Target(), _ => true, p => new RetainedContext(p),
        p => new RetainedContext(p), (_, _) => { }), "reference-return ABI");
    Check(f.Engine.Installs == 1, "Unsupported observer ABI patched a native entry.");
}
static void ReferenceObservationShared()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceReferenceReturn0) with { ReturnClass = new(9) };
    var run = "A"; var entryRun = ""; var observedRun = ""; var calls = 0; var observed = IntPtr.Zero; var releases = 0;
    f.Engine.Original = new ExactHookBroker.InstanceReferenceReturn0((_, _) => { calls++; run = "B"; return new(3); });
    using var replacement = f.Broker.BindReferenceResult(target, _ => true,
        _ => { Check(entryRun == "A", "Replacement capture ran before pure entry capture."); return new TestRelease(() => releases++); },
        _ => new TestRelease(() => releases++), (_, _) =>
        {
            Check(observed == new IntPtr(3) && observedRun == "A", "Coordinator ran before original provenance observation.");
            return new NativeRuntime.RootHandle(1, _ => releases++, _ => new IntPtr(4));
        });
    using var observer = f.Broker.BindRetainedReferenceObservation(target, _ => true,
        p => { entryRun = run; return new RetainedContext(p, () => releases++); },
        _ => new TestRelease(() => releases++), (_, result) => { observed = result; observedRun = entryRun; });
    var returned = ((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1), new(2));
    Check(calls == 1 && returned == new IntPtr(4) && observed == new IntPtr(3) && observedRun == "A" && run == "B"
        && releases == 6 && f.Engine.Installs == 1, "Pure observer borrowed exit run, observed replacement, leaked roots or created another patch.");
}
static void ReferenceObservationFaults()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceReferenceReturn0) with { ReturnClass = new(9) };
    var mode = 0; var calls = 0; var afters = 0; var releases = 0; var roots = 0;
    var originalFailure = new IOException("original reference failure");
    f.Engine.Original = new ExactHookBroker.InstanceReferenceReturn0((_, _) =>
    { calls++; if (mode == 0) throw originalFailure; return mode == 1 ? IntPtr.Zero : new IntPtr(3); });
    using var observer = f.Broker.BindRetainedReferenceObservation(target, _ => true,
        p => new RetainedContext(p, () => { releases++; if (mode == 0) throw new IOException("release failure"); }),
        _ => { roots++; return new TestRelease(() => releases++); }, (_, _) => { afters++; throw new IOException("after failure"); });
    var invoke = () => ((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1), new(2));
    Exception? observed = null; try { invoke(); } catch (Exception error) { observed = error; }
    Check(ReferenceEquals(observed, originalFailure) && calls == 1 && releases == 1 && afters == 0 && roots == 0,
        "Original exception was swallowed, replaced, retried, or leaked context.");
    mode = 1; Check(invoke() == IntPtr.Zero && calls == 2 && afters == 0 && roots == 0 && releases == 2, "Null result was observed or changed.");
    mode = 2; Check(invoke() == new IntPtr(3) && calls == 3 && afters == 1 && roots == 1 && releases == 4,
        "Managed after failure changed native return or left the reentry gate/roots alive.");
}
static void ReferenceObservationIsolation()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceReferenceReturn0) with { ReturnClass = new(9) };
    var calls = 0; var captures = 0; var afters = 0; var releases = 0; var matches = 0; var accept = true; var withdraw = false;
    ExactHookBroker.Binding? observer = null;
    var invoke = () => ((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1), new(2));
    f.Engine.Original = new ExactHookBroker.InstanceReferenceReturn0((_, _) =>
    { if (++calls == 1) Check(invoke() == new IntPtr(3), "Reentry return changed."); if (withdraw) observer!.Dispose(); return new(3); });
    observer = f.Broker.BindRetainedReferenceObservation(target, _ => { matches++; return accept; },
        p => { captures++; return new RetainedContext(p, () => releases++); },
        _ => new TestRelease(() => releases++), (_, _) => afters++);
    Check(invoke() == new IntPtr(3), "Outer return changed.");
    Exception? workerFailure = null;
    var worker = new Thread(() => { try { Check(invoke() == new IntPtr(3), "Worker return changed."); } catch (Exception ex) { workerFailure = ex; } });
    worker.Start(); worker.Join(); accept = false; invoke(); accept = true; withdraw = true; invoke();
    withdraw = false; invoke(); observer.Dispose();
    Check(workerFailure is null && calls == 6 && captures == 2 && afters == 1 && releases == 3 && matches == 3
        && observer.Describe().SuppressedReentries == 1 && observer.Describe().SuppressedWrongThread == 1,
        "Reentry/worker/unmatched/withdrawn observer captured a foreign origin or skipped original.");
}
static void ReferenceObservationCleanup()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceReferenceReturn0) with { ReturnClass = new(9) };
    var calls = 0; var afters = 0; var releases = 0;
    f.Engine.Original = new ExactHookBroker.InstanceReferenceReturn0((_, _) => { calls++; return new(3); });
    using var captureFault = f.Broker.BindRetainedReferenceObservation<RetainedContext>(target, _ => true,
        _ => throw new IOException("capture fault"), _ => throw new Exception("unreachable"), (_, _) => throw new Exception("unreachable"));
    using var nullRoot = f.Broker.BindRetainedReferenceObservation(target, _ => true,
        p => new RetainedContext(p, () => releases++), _ => (IDisposable)null!, (_, _) => throw new Exception("unrooted result delivered"));
    using var invalidRoot = f.Broker.BindRetainedReferenceObservation(target, _ => true,
        p => new RetainedContext(p, () => releases++), _ => throw new UnsupportedSignatureException("wrong result type"),
        (_, _) => throw new Exception("invalid result delivered"));
    using var broken = f.Broker.BindRetainedReferenceObservation(target, _ => true,
        p => new RetainedContext(p, () => { releases++; throw new IOException("context cleanup"); }),
        _ => new TestRelease(() => { releases++; throw new IOException("root cleanup"); }), (_, _) => throw new IOException("after fault"));
    using var healthy = f.Broker.BindRetainedReferenceObservation(target, _ => true,
        p => new RetainedContext(p, () => releases++), _ => new TestRelease(() => releases++), (_, _) => afters++);
    var result = ((ExactHookBroker.InstanceReferenceReturn0)f.Engine.Replacement!)(new(1), new(2));
    Check(result == new IntPtr(3) && calls == 1 && afters == 1 && releases == 6
        && f.Log.Messages.Any(x => x.Contains("capture failed")) && f.Log.Messages.Any(x => x.Contains("result-root release failed"))
        && f.Log.Messages.Any(x => x.Contains("context release failed")),
        "Failed observers changed original behavior or prevented healthy observation/independent cleanup.");
}

static void JudgeArguments()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid);
    var stages = new List<string>(); var instance = new IntPtr(0x111); var info = new IntPtr(0x222);
    f.Engine.Original = new ExactHookBroker.InstanceJudge((receiver, kind, delta, method) =>
    { Check(receiver == instance && method == info && kind == -7 && BitConverter.SingleToInt32Bits(delta) == BitConverter.SingleToInt32Bits(-.013f), "Judge ABI arguments changed."); stages.Add("original"); });
    using var binding = f.Broker.Bind(target, _ => true, (phase, invocation, kind, delta) =>
    { Check(invocation == 1 && kind == -7 && delta == -.013f, "Observation changed value."); stages.Add(phase.ToString()); });
    f.Engine.InvokeJudge(instance, -7, -.013f, info);
    Check(stages.SequenceEqual(new[] { "Before", "original", "After" }) && binding.Describe().NativeCalls == 1 && binding.Describe().OriginalCalls == 1, "Judge entry ordering/count failed.");
}

static void Enum32Arguments()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32Void); var stages = new List<string>();
    var calls = 0; var phases = new List<long>(); var receiver = new IntPtr(0x12345); var method = new IntPtr(0x76543);
    f.Engine.Original = new ExactHookBroker.InstanceEnum32((self, value, info) =>
    { calls++; Check(self == receiver && value == int.MinValue && info == method, "Enum32 native ABI arguments changed."); stages.Add("original"); });
    using var binding = f.Broker.Bind(target, _ => true, (phase, invocation, value, unused) =>
    { Check(value == int.MinValue && unused == 0, "Enum32 observer changed argument."); phases.Add(invocation); stages.Add(phase.ToString()); });
    // calli crosses the real unmanaged cdecl thunk; a managed delegate-to-delegate call would not test register placement.
    var pointer = Marshal.GetFunctionPointerForDelegate(f.Engine.Replacement!);
    var invoke = new DynamicMethod("enum32_cdecl_fixture", typeof(void), new[] { typeof(IntPtr), typeof(int), typeof(IntPtr) });
    var il = invoke.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
    il.Emit(OpCodes.Ldc_I8, pointer.ToInt64()); il.Emit(OpCodes.Conv_I);
    il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, typeof(void), new[] { typeof(IntPtr), typeof(int), typeof(IntPtr) }); il.Emit(OpCodes.Ret);
    ((Action<IntPtr, int, IntPtr>)invoke.CreateDelegate(typeof(Action<IntPtr, int, IntPtr>)))(receiver, int.MinValue, method);
    Check(calls == 1 && phases.SequenceEqual(new long[] { 1, 1 }) && stages.SequenceEqual(new[] { "Before", "original", "After" })
        && binding.Describe().NativeCalls == 1 && binding.Describe().OriginalCalls == 1, "Enum32 native entry duplicated/skipped original or reordered phases.");
    GC.KeepAlive(f.Engine.Replacement);
}

static void Enum32FaultAndReentry()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32Void); var calls = 0; var afters = 0; var observations = 0;
    var fail = true; var originalError = new IOException("enum32 original failure");
    f.Engine.Original = new ExactHookBroker.InstanceEnum32((_, value, _) =>
    {
        calls++;
        if (calls == 1) ((ExactHookBroker.InstanceEnum32)f.Engine.Replacement!)(new(0x111), 2, new(0x222));
        if (value == 1 && fail) throw originalError;
    });
    using var binding = f.Broker.Bind(target, _ => true, (phase, _, _, _) =>
    { observations++; if (phase == HookPhase.After) afters++; else throw new IOException("isolated observer failure"); });
    Exception? observed = null;
    try { ((ExactHookBroker.InstanceEnum32)f.Engine.Replacement!)(new(0x111), 1, new(0x222)); } catch (Exception ex) { observed = ex; }
    Check(ReferenceEquals(observed, originalError) && calls == 2 && afters == 0 && observations == 1,
        "Enum32 original failure was retried/swallowed or nested entry inherited observers.");
    fail = false; ((ExactHookBroker.InstanceEnum32)f.Engine.Replacement!)(new(0x111), 1, new(0x222));
    Check(calls == 3 && afters == 1 && observations == 3 && binding.Describe().SuppressedReentries == 1 && binding.Describe().OriginalCalls == 3,
        "Enum32 failure leaked the reentry gate or changed original count.");
}

static void Enum32Isolation()
{
    using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32Void); var calls = 0; var matches = 0; var phases = 0; var accept = true;
    f.Engine.Original = new ExactHookBroker.InstanceEnum32((_, value, _) => { Check(value == 16, "Update type was rewritten."); calls++; });
    var binding = f.Broker.Bind(target, _ => { matches++; return accept; }, (_, _, _, _) => phases++);
    Action invoke = () => ((ExactHookBroker.InstanceEnum32)f.Engine.Replacement!)(new(0x111), 16, new(0x222));
    Exception? workerError = null; var worker = new Thread(() => { try { invoke(); } catch (Exception ex) { workerError = ex; } }); worker.Start(); worker.Join();
    accept = false; invoke(); accept = true; invoke(); binding.Dispose(); invoke();
    Check(workerError is null && calls == 4 && matches == 2 && phases == 2 && binding.Describe().SuppressedWrongThread == 1,
        "Enum32 wrong-thread/unmatched/detached invocation accessed observers or changed pass-through.");
    Throws(() => f.Broker.Bind(target with { Abi = ExactHookAbi.InstanceVoid }, _ => true, (_, _, _, _) => { }), "different exact hook ABI");
    Check(f.Engine.Installs == 1, "Enum32 address was patched twice.");
}

static void ManagedCallbackFaults()
{
    using var f = new Fixture(); var target = Target(); var calls = 0; var phases = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
    using var binding = f.Broker.Bind(target, _ => true, (_, _, _, _) => { phases++; throw new Exception("observer fault"); });
    f.Engine.InvokeVoid();
    Check(calls == 1 && phases == 2 && binding.Describe().OriginalCalls == 1, "Observer error interfered with original.");
}

static void OriginalFault()
{
    using var f = new Fixture(); var target = Target(); var originals = 0; var phases = new List<HookPhase>(); var fail = true;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { originals++; if (fail) throw new Exception("original failure"); });
    using var binding = f.Broker.Bind(target, _ => true, (phase, _, _, _) => phases.Add(phase));
    Throws(f.Engine.InvokeVoid, "original failure"); fail = false; f.Engine.InvokeVoid();
    Check(originals == 2 && phases.SequenceEqual(new[] { HookPhase.Before, HookPhase.Before, HookPhase.After }) && binding.Describe().SuppressedReentries == 0,
        "Original exception swallowed, retried, emitted After, or leaked reentry guard.");
}

static void Reentry()
{
    using var f = new Fixture(); var target = Target(); var originals = 0; var phases = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { if (++originals == 1) f.Engine.InvokeVoid(); });
    using var binding = f.Broker.Bind(target, _ => true, (_, _, _, _) => phases++);
    f.Engine.InvokeVoid();
    Check(originals == 2 && phases == 2 && binding.Describe().SuppressedReentries == 1 && binding.Describe().NativeCalls == 2 && binding.Describe().OriginalCalls == 2,
        "Native reentry did not preserve original exactly once per entry.");
}

static void OffMain()
{
    using var f = new Fixture(); var target = Target(); var calls = 0; var metadataCalls = 0; var deliveries = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => Interlocked.Increment(ref calls));
    using var binding = f.Broker.Bind(target, _ => { metadataCalls++; return true; }, (_, _, _, _) => deliveries++);
    var worker = new Thread(f.Engine.InvokeVoid); worker.Start(); worker.Join();
    Check(calls == 1 && metadataCalls == 0 && deliveries == 0 && binding.Describe().SuppressedWrongThread == 1, "Off-main callback read Unity metadata or skipped original.");
}

static void ReceiverFilter()
{
    using var f = new Fixture(); var target = Target(); var calls = 0; var deliveries = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
    using var binding = f.Broker.Bind(target, _ => false, (_, _, _, _) => deliveries++);
    f.Engine.InvokeVoid(); Check(calls == 1 && deliveries == 0, "Shared entry leaked another receiver's observation.");
    using var broken = f.Broker.Bind(target, _ => throw new Exception("receiver failure"), (_, _, _, _) => deliveries++);
    f.Engine.InvokeVoid(); Check(calls == 2 && deliveries == 0, "Receiver-filter exception interfered with original.");
}

static void InstallRejections()
{
    foreach (var abi in new[] { ExactHookAbi.InstanceVoid, ExactHookAbi.InstanceEnum32Void, ExactHookAbi.InstanceEnum32SingleVoid,
        ExactHookAbi.InstanceInt32SingleVoid, ExactHookAbi.InstanceReferenceReturn0, ExactHookAbi.InstanceBooleanReturn0 })
    {
        using var f = new Fixture(); var target = Target(abi) with { ReturnClass = abi == ExactHookAbi.InstanceReferenceReturn0 ? new(9) : IntPtr.Zero };
        var callbacks = 0; f.Engine.Fail = true;
        Func<ExactHookBroker.Binding> bind = abi switch
        {
            ExactHookAbi.InstanceReferenceReturn0 => () => f.Broker.BindRetainedReferenceObservation(target, _ => true,
                _ => { callbacks++; return new TestRelease(() => { }); }, _ => new TestRelease(() => { }), (_, _) => callbacks++),
            ExactHookAbi.InstanceBooleanReturn0 => () => f.Broker.BindRetainedBooleanObservation(target, _ => true,
                _ => { callbacks++; return new TestRelease(() => { }); }, _ => callbacks++),
            _ => () => f.Broker.Bind(target, _ => true, (_, _, _, _) => callbacks++)
        };
        Throws(() => bind(), "install failed");
        var retained = new WeakReference(f.Engine.Replacement); f.Engine.Replacement = null;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(retained.IsAlive, "Unconfirmed native replacement lost its strong process owner.");
        var replacement = (Delegate)retained.Target!;
        var parameters = abi switch
        {
            ExactHookAbi.InstanceEnum32Void => new[] { typeof(IntPtr), typeof(int), typeof(IntPtr) },
            ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid => new[] { typeof(IntPtr), typeof(int), typeof(float), typeof(IntPtr) },
            _ => new[] { typeof(IntPtr), typeof(IntPtr) }
        };
        var returns = abi == ExactHookAbi.InstanceReferenceReturn0 ? typeof(IntPtr) : abi == ExactHookAbi.InstanceBooleanReturn0 ? typeof(byte) : typeof(void);
        var invoke = new DynamicMethod("unknown_install_cdecl_fixture", returns, parameters); var il = invoke.GetILGenerator();
        for (var index = 0; index < parameters.Length; index++) il.Emit(OpCodes.Ldarg, (short)index);
        il.Emit(OpCodes.Ldc_I8, Marshal.GetFunctionPointerForDelegate(replacement).ToInt64()); il.Emit(OpCodes.Conv_I);
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, returns, parameters); il.Emit(OpCodes.Ret);
        var native = invoke.CreateDelegate(replacement.GetType());
        switch (native)
        {
            case ExactHookBroker.InstanceVoid value: value(new(1), new(2)); break;
            case ExactHookBroker.InstanceEnum32 value: value(new(1), int.MinValue, new(2)); break;
            case ExactHookBroker.InstanceJudge value: value(new(1), int.MaxValue, float.NaN, new(2)); break;
            case ExactHookBroker.InstanceReferenceReturn0 value: Check(value(new(1), new(2)) == IntPtr.Zero, "Unconfirmed reference install fabricated a result."); break;
            case ExactHookBroker.InstanceBooleanReturn0 value: Check(value(new(1), new(2)) == 0 && ExactHookBroker.BooleanInvocationDepth == 0, "Unconfirmed Boolean install fabricated success or retained scope."); break;
            default: throw new InvalidOperationException("Missing unconfirmed ABI fixture.");
        }
        Check(callbacks == 0, "Unconfirmed native install published ordinary observer success.");
        f.Engine.Fail = false;
        var rejected = false; try { bind(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Unconfirmed native install was retried as a fresh successful patch.");
        var conflict = abi == ExactHookAbi.InstanceVoid ? ExactHookAbi.InstanceEnum32SingleVoid : ExactHookAbi.InstanceVoid;
        Throws(() => f.Broker.Bind(target with { Abi = conflict }, _ => true, (_, _, _, _) => { }), "different exact hook ABI");
        Check(f.Engine.Installs == 1, "Unconfirmed native install was attempted twice.");
        GC.KeepAlive(replacement);
    }
}

static void ProcessRoots()
{
    using var f = new Fixture(); var target = Target(); var calls = 0;
    f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
    var binding = f.Broker.Bind(target, _ => true, (_, _, _, _) => { }); binding.Dispose();
    var replacement = new WeakReference(f.Engine.Replacement); var original = new WeakReference(f.Engine.Original);
    f.Engine.Replacement = null; f.Engine.Original = null; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Check(replacement.IsAlive && original.IsAlive, "Native entry/trampoline delegate was collected after logical detach.");
    ((ExactHookBroker.InstanceVoid)replacement.Target!)(IntPtr.Zero, IntPtr.Zero);
    Check(calls == 1, "Logically detached native entry no longer forwarded original.");
}

static void MetadataValidation()
{
    var value = new HookTargetValidation.MethodInfo2022
    { Name = new(1), Class = new(2), ReturnType = new(3), Flags = 6, ParameterCount = 2, MethodPointer = new(0x1234) };
    HookTargetValidation.ValidateMetadata(value, new(1), new(2), new(3), 6, 2, new(0x1234));
    Check(Marshal.OffsetOf<HookTargetValidation.MethodInfo2022>(nameof(HookTargetValidation.MethodInfo2022.MethodPointer)) == IntPtr.Zero
        && Marshal.SizeOf<HookTargetValidation.MethodInfo2022>() == 88, "Pinned Win64 MethodInfo layout changed.");
    Throws(() => HookTargetValidation.ValidateMetadata(value with { MethodPointer = new(0x9999) }, new(1), new(2), new(3), 6, 2, new(0x1234)), "UnsupportedSignature");
    Throws(() => HookTargetValidation.ValidateMetadata(value, new(1), new(9), new(3), 6, 2, new(0x1234)), "UnsupportedSignature");
    Throws(() => HookTargetValidation.ValidateMetadata(value, new(1), new(2), new(3), 0x16, 2, new(0x1234)), "UnsupportedSignature");
    Throws(() => HookTargetValidation.ValidateMetadata(value, new(1), new(2), new(3), 6, 1, new(0x1234)), "UnsupportedSignature");
}

static void MemoryRejections()
{
    if (!OperatingSystem.IsWindows()) return;
    Throws(() => HookTargetValidation.VerifyReadable(IntPtr.Zero, 88), "UnsupportedSignature");
    var memory = Marshal.AllocHGlobal(128);
    try
    {
        HookTargetValidation.VerifyReadable(memory, 88);
        Throws(() => HookTargetValidation.VerifyExecutable(memory, new(0x1234)), "UnsupportedSignature");
    }
    finally { Marshal.FreeHGlobal(memory); }
}

static void TypedDispatcher()
{
    using var main = new MainThreadDispatcher(); var log = new Logger(); using var dispatcher = new HookDispatcher(main, log);
    Good(dispatcher.Declare(GameHookPoints.Judge, "judge", "test", "test evidence"));
    using var a = new Context("a", main); using var b = new Context("b", main); var order = new List<string>();
    var first = dispatcher.ForPlugin(a); var second = dispatcher.ForPlugin(b);
    Need(first.Subscribe(GameHookPoints.Judge, HookPhase.Before, _ => order.Add("normal-a")));
    Need(second.Subscribe(GameHookPoints.Judge, HookPhase.Before, _ => order.Add("high-b"), 10));
    Need(second.Subscribe(GameHookPoints.Judge, HookPhase.After, _ => order.Add("after-b")));
    Check(!first.Subscribe(new HookPoint<int>(GameHookPoints.Judge.Id, GameHookPoints.Judge.Version), HookPhase.Before, _ => { }).Success, "Wrong payload accepted.");
    Check(!first.Subscribe(GameHookPoints.Judge with { Version = new(2, 0) }, HookPhase.Before, _ => { }).Success, "Wrong version accepted.");
    Check(!first.Subscribe(GameHookPoints.Judge, (HookPhase)88, _ => { }).Success && !first.RequestMutation(GameHookPoints.Judge.Id).Success, "Unsupported phase or mutation accepted.");
    dispatcher.Publish(GameHookPoints.Judge, HookPhase.Before, new(1, 0, 0)); dispatcher.Publish(GameHookPoints.Judge, HookPhase.After, new(1, 0, 0));
    Check(order.SequenceEqual(new[] { "high-b", "normal-a", "after-b" }), "Dispatcher ordering failed.");
    a.Dispose(); order.Clear(); dispatcher.Publish(GameHookPoints.Judge, HookPhase.Before, new(2, 0, 0));
    Check(order.SequenceEqual(new[] { "high-b" }), "Stopped scope retained hook observer.");
    Throws(() => first.GetTargets(), "ObjectDisposedException");
}

static void HostFaultIntegration()
{
    using var main = new MainThreadDispatcher(); var log = new Logger(); using var hooks = new HookDispatcher(main, log);
    Good(hooks.Declare(GameHookPoints.Pause, "pause", "test", "test evidence"));
    var capabilities = new CapabilityRegistry(new("test", "test", "test", "test")); var events = new EventHub(log);
    using var host = new PluginHost(capabilities, main, events, log);
    hooks.ProviderFaulted += (id, error) => host.ReportPluginFault(id, error);
    capabilities.RegisterScoped<IHookService>(HookCapabilityIds.Observe, context => hooks.ForPlugin(context));
    var hits = 0;
    host.Register(new Plugin("broken", context => Need(context.GetService<IHookService>(HookCapabilityIds.Observe).Service!
        .Subscribe(GameHookPoints.Pause, HookPhase.Before, _ => throw new Exception("hook fault"), 10))));
    host.Register(new Plugin("healthy", context => Need(context.GetService<IHookService>(HookCapabilityIds.Observe).Service!
        .Subscribe(GameHookPoints.Pause, HookPhase.Before, _ => hits++))));
    host.EnableAll(); hooks.Publish(GameHookPoints.Pause, HookPhase.Before, new(1)); hooks.Publish(GameHookPoints.Pause, HookPhase.Before, new(2));
    Check(hits == 2 && host.Statuses.Single(x => x.Id == "broken").State == PluginState.Faulted
        && host.Statuses.Single(x => x.Id == "healthy").State == PluginState.Enabled, "Hook fault owner attribution/lifecycle cleanup failed.");
}

static class Addresses { public static long Next = 0x70000000; }
sealed class Engine : INativeDetourEngine, IPreparedNativeDetourEngine
{
    public Delegate? Replacement, Original; public int Installs; public bool Fail;
    public Delegate Install(VerifiedHookTarget target, Delegate replacement) { Installs++; Replacement = replacement; if (Fail) throw new Exception("install failed"); return Original!; }
    public int Prepares, Commits; public Action? PrepareAction, CommitAction;
    public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { Prepares++; PrepareAction?.Invoke(); return new(0x4422); }
    public void Commit(VerifiedHookTarget target) { Commits++; CommitAction?.Invoke(); }
    public void InvokeVoid() => ((ExactHookBroker.InstanceVoid)Replacement!)(new(0x111), new(0x222));
    public void InvokeJudge(IntPtr instance, int judge, float delta, IntPtr method) => ((ExactHookBroker.InstanceJudge)Replacement!)(instance, judge, delta, method);
}
sealed class NativeBridge : INativeJudgeContextBridge
{
    internal long InFlight; internal bool ActivityKnown = true, ThrowActivity;
    public NativeJudgeActivity ReadActivity()
    { if (ThrowActivity) throw new IOException("Controlled statistics read failure"); return new(Interlocked.Read(ref InFlight), Configured && ActivityKnown); }
    public IntPtr EntryAddress => new(0x4433); public bool Configured; public Action? ConfigureAction;
    public Action<NativeJudgeFrame> Original = _ => { };
    private Func<NativeJudgeFrame, nuint>? _enter; private Action<NativeJudgeFrame, nuint, NativeJudgeExitFlags>? _exit;
    public void Configure(IntPtr original, Func<NativeJudgeFrame, nuint> enter, Action<NativeJudgeFrame, nuint, NativeJudgeExitFlags> exit)
    { if (original == IntPtr.Zero || Configured) throw new Exception("bad fake configuration"); ConfigureAction?.Invoke(); _enter = enter; _exit = exit; Configured = true; }
    public void Invoke(NativeJudgeFrame frame)
    {
        if (!Configured) throw new Exception("entry published before configuration");
        Interlocked.Increment(ref InFlight);
        try {
        var token = _enter!(frame); var returned = false;
        try { Original(frame); returned = true; }
        finally { _exit!(frame, token, returned ? NativeJudgeExitFlags.Returned : NativeJudgeExitFlags.Abnormal); }
        } finally { Interlocked.Decrement(ref InFlight); }
    }
}
sealed class Fixture : IDisposable
{
    public MainThreadDispatcher Main = new(); public Logger Log = new(); public Engine Engine = new(); public ExactHookBroker Broker;
    public Fixture() => Broker = new(Engine, Main, Log);
    public void Dispose() => Main.Dispose();
}
sealed class Logger : ILogger { public List<string> Messages = new(); public void Log(LogLevel level, string message, Exception? exception = null) => Messages.Add(message); }
sealed class TestRelease:IDisposable{private Action? _action;public TestRelease(Action action)=>_action=action;public void Dispose(){var action=_action;_action=null;action?.Invoke();}}
sealed class RetainedContext : IDisposable
{
    private readonly Action? _release; public IntPtr Receiver { get; } public bool Disposed { get; private set; } public int DisposeCalls { get; private set; }
    public RetainedContext(IntPtr receiver, Action? release = null) { Receiver = receiver; _release = release; }
    public void Dispose() { DisposeCalls++; Disposed = true; _release?.Invoke(); }
}
sealed class Context : IPluginContext, IMainThreadDispatcher, IDisposable
{
    private readonly MainThreadDispatcher _main; private bool _active = true; private readonly List<IDisposable> _resources = new();
    public Context(string id, MainThreadDispatcher main) { PluginId = id; _main = main; }
    public string PluginId { get; } public ILogger Log { get; } = new Logger(); public IMainThreadDispatcher MainThread => this;
    public IEventBus Events => throw new NotSupportedException(); public GameBuildDescriptor GameBuild => new("test", "test", "test", "test");
    public CapabilityDescriptor GetCapability(string id) => throw new NotSupportedException();
    public ServiceLookup<T> GetService<T>(string id) where T : class => throw new NotSupportedException();
    public T Own<T>(T resource) where T : IDisposable { VerifyAccess(); _resources.Add(resource); return resource; }
    public bool IsMainThread => _main.IsMainThread; public void VerifyAccess() { _main.VerifyAccess(); if (!_active) throw new ObjectDisposedException(PluginId); }
    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) => _main.InvokeAsync(action, cancellationToken);
    public void Dispose() { _main.VerifyAccess(); if (!_active) return; _active = false; foreach (var resource in _resources.AsEnumerable().Reverse()) resource.Dispose(); }
}
sealed class Plugin : IRizPlugin
{
    private readonly Action<IPluginContext> _enable;
    public Plugin(string id, Action<IPluginContext> enable) { Manifest = new(id, new(1, 0), new(1, 0)); _enable = enable; }
    public PluginManifest Manifest { get; } public void Enable(IPluginContext context) => _enable(context); public void Disable() { }
}
