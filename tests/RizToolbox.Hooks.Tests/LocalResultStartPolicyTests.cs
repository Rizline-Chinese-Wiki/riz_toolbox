using System.Reflection;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class LocalResultStartPolicyTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("LOCAL Start authority rejects an unsupported image before native metadata lookup", TargetAuthority),
        ("LOCAL Start ordinary decision preserves ABI and all original observer ordering exactly once", ContinueOriginal),
        ("LOCAL Start claim skips stock and publishes no fictitious original observer phases", ClaimLocal),
        ("LOCAL Start claimed initialization failure remains fail-closed and releases policy scope", ClaimedFailure),
        ("LOCAL Start classifier failure and unknown decision remain fail-closed", ClassificationFailure),
        ("LOCAL Start ordinary native exception propagates unchanged without retry or fallback", OriginalFailure),
        ("LOCAL Start callback reentry cannot escape through ordinary pass-through", PolicyReentry),
        ("LOCAL Start ordinary original reentry still forwards each native invocation once", OrdinaryReentry),
        ("LOCAL Start pure classifier distinguishes worker ordinary entry from worker LOCAL claim", WorkerClassification),
        ("LOCAL Start in-flight detach cannot undo a claim and next independent call restores original", InFlightDetach),
        ("LOCAL Start handler detach keeps nested entry suppressed until outer scope exits", HandlerDetachReentry),
        ("LOCAL Start coordinator is exclusive and rejects unrelated method identity aliases", Ownership),
        ("LOCAL Start policy rejects an unrelated active alias attached before policy registration", EarlierAliasOwnership),
        ("LOCAL Start wrong non-null MethodInfo is suppressed while null direct-call identity is preserved", MethodInfoBoundary),
        ("LOCAL Start binding registration and detach require the owning main thread", BindingThreadOwnership),
    };

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static VerifiedHookTarget Target() => new("local.result.start.test",
        new IntPtr(Interlocked.Add(ref Addresses.Next, 16)), new(0x222), new(0x4321), ExactHookAbi.InstanceVoid);

    // Production can only obtain this sealed authority through Resolve, which
    // pins the image and asks NativeRuntime for exact LevelOverControl.Start.
    // Tests intentionally bypass its private ctor to exercise fake native entry
    // dispatch without claiming real GameAssembly/MethodInfo verification.
    private static ExactHookBroker.LocalResultStartTarget Token(VerifiedHookTarget target)
        => (ExactHookBroker.LocalResultStartTarget)typeof(ExactHookBroker.LocalResultStartTarget)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(VerifiedHookTarget) }, null)!.Invoke(new object[] { target });

    private static void TargetAuthority()
    {
        Throws<UnsupportedSignatureException>(() => ExactHookBroker.LocalResultStartTarget.Resolve(null!, new(1), "unsupported"));
        Throws<ArgumentNullException>(() => ExactHookBroker.LocalResultStartTarget.Resolve(null!, new(1),
            "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"));
        Check(typeof(ExactHookBroker.LocalResultStartTarget).GetConstructors().Length == 0,
            "An unverified public address constructor was exposed.");
    }

    private static void ContinueOriginal()
    {
        using var f = new Fixture(); var target = Target(); var order = new List<string>(); var calls = 0; var handlers = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((receiver, method) =>
        {
            Check(receiver == new IntPtr(0x111) && method == new IntPtr(0x222), "Original ABI arguments changed.");
            calls++; order.Add("original");
        });
        using var ordinary = f.Broker.Bind(target, _ => true, (phase, _, _, _) => order.Add(phase.ToString()));
        using var retained = f.Broker.BindRetainedObservation(target, _ => true,
            receiver => { order.Add("retain"); return new RetainedContext(receiver, () => order.Add("release")); },
            _ => order.Add("retained-after"));
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ =>
        { order.Add("classify"); return ExactHookBroker.LocalResultStartDecision.ContinueOriginal; }, _ => handlers++);
        f.Engine.InvokeVoid();
        Check(calls == 1 && handlers == 0 && f.Engine.Installs == 1
            && order.SequenceEqual(new[] { "classify", "Before", "retain", "original", "retained-after", "After", "release" }),
            "An ordinary decision changed original forwarding or observer order.");
        var state = policy.DescribePolicy();
        Check(state.Classified == 1 && state.Continued == 1 && state.Skipped == 0 && state.Faults == 0
            && policy.DescribeHook().NativeCalls == 1 && policy.DescribeHook().OriginalCalls == 1, "Ordinary policy diagnostics differ.");
    }

    private static void ClaimLocal()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var phases = 0; var captures = 0; var handlers = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        using var ordinary = f.Broker.Bind(target, _ => { phases++; return true; }, (_, _, _, _) => phases++);
        using var retained = f.Broker.BindRetainedObservation(target, _ => true,
            receiver => { captures++; return new RetainedContext(receiver); }, _ => captures++);
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal,
            receiver => { Check(receiver == new IntPtr(0x111), "LOCAL receiver changed."); handlers++; });
        f.Engine.InvokeVoid();
        Check(calls == 0 && handlers == 1 && phases == 0 && captures == 0
            && policy.DescribePolicy() is { Claimed: 1, Skipped: 1, Faults: 0 }
            && policy.DescribeHook().NativeCalls == 1 && policy.DescribeHook().OriginalCalls == 0,
            "Claimed LOCAL Start escaped into stock or original observers.");
    }

    private static void ClaimedFailure()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var handlers = 0; var claim = true;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ => claim
            ? ExactHookBroker.LocalResultStartDecision.ClaimLocal : ExactHookBroker.LocalResultStartDecision.ContinueOriginal,
            _ => { handlers++; throw new IOException("LOCAL result UI initialization failed after claim"); });
        f.Engine.InvokeVoid(); claim = false; f.Engine.InvokeVoid();
        Check(calls == 1 && handlers == 1 && policy.DescribePolicy() is { Claimed: 1, Skipped: 1, Continued: 1, Faults: 1 }
            && f.Log.Messages.Any(x => x.Contains("stock Start remains suppressed")),
            "Failed LOCAL initialization returned to stock or leaked its policy scope.");
    }

    private static void ClassificationFailure()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var handlers = 0; var mode = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ => mode switch
        {
            0 => throw new IOException("identity reservation lookup failed"),
            1 => (ExactHookBroker.LocalResultStartDecision)42,
            _ => ExactHookBroker.LocalResultStartDecision.ContinueOriginal
        }, _ => handlers++);
        f.Engine.InvokeVoid(); mode = 1; f.Engine.InvokeVoid(); mode = 2; f.Engine.InvokeVoid();
        Check(calls == 1 && handlers == 0 && policy.DescribePolicy() is { Classified: 3, Continued: 1, Skipped: 2, Faults: 2 },
            "Unknown classification forwarded stock or poisoned the next independent invocation.");
    }

    private static void OriginalFailure()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var phases = 0; var fail = true;
        var error = new IOException("original native trampoline failure");
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { calls++; if (fail) throw error; });
        using var ordinary = f.Broker.Bind(target, _ => true, (_, _, _, _) => { phases++; throw new IOException("observer"); });
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ContinueOriginal,
            _ => throw new InvalidOperationException("ordinary entry reached LOCAL handler"));
        Check(ReferenceEquals(Throws<IOException>(f.Engine.InvokeVoid), error), "Original exception was caught or replaced by policy.");
        fail = false; f.Engine.InvokeVoid();
        Check(calls == 2 && phases == 3 && policy.DescribePolicy() is { Continued: 2, Faults: 0, Skipped: 0 }
            && policy.DescribeHook().OriginalCalls == 2, "Original was retried, swallowed, or its exception leaked a reentry gate.");
    }

    private static void PolicyReentry()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var classify = 0; var handlers = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ =>
        { classify++; f.Engine.InvokeVoid(); return ExactHookBroker.LocalResultStartDecision.ClaimLocal; },
            _ => { handlers++; f.Engine.InvokeVoid(); });
        f.Engine.InvokeVoid();
        Check(calls == 0 && classify == 1 && handlers == 1
            && policy.DescribePolicy() is { Classified: 1, Claimed: 1, Skipped: 3, SuppressedPolicyReentries: 2 }
            && policy.DescribeHook().NativeCalls == 3, "Policy callback reentry escaped into stock or recursively redispatched policy.");
    }

    private static void OrdinaryReentry()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var phases = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { if (++calls == 1) f.Engine.InvokeVoid(); });
        using var ordinary = f.Broker.Bind(target, _ => true, (_, _, _, _) => phases++);
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ContinueOriginal, _ => { });
        f.Engine.InvokeVoid();
        Check(calls == 2 && phases == 2 && policy.DescribePolicy() is { Continued: 2, Skipped: 0, SuppressedPolicyReentries: 0 }
            && policy.DescribeHook().OriginalCalls == 2 && policy.DescribeHook().SuppressedReentries == 1,
            "Ordinary original reentry no longer preserves the existing pass-through contract.");
    }

    private static void WorkerClassification()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var handlers = 0; var classifications = 0;
        var local = false; Exception? failure = null;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => Interlocked.Increment(ref calls));
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ =>
        { Interlocked.Increment(ref classifications); return local ? ExactHookBroker.LocalResultStartDecision.ClaimLocal : ExactHookBroker.LocalResultStartDecision.ContinueOriginal; },
            _ => Interlocked.Increment(ref handlers));
        void RunWorker()
        {
            var worker = new Thread(() => { try { f.Engine.InvokeVoid(); } catch (Exception error) { failure = error; } });
            worker.Start(); worker.Join();
        }
        RunWorker(); local = true; RunWorker();
        Check(failure is null && calls == 1 && handlers == 0 && classifications == 2
            && policy.DescribePolicy() is { Continued: 1, Claimed: 1, Skipped: 1, WrongThreadClaims: 1, Faults: 0 },
            "Worker ordinary entry was blanket suppressed or LOCAL ran a main-thread handler/stock path.");
    }

    private static void InFlightDetach()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var handlers = 0;
        ExactHookBroker.LocalResultStartBinding? binding = null;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        binding = f.Broker.BindLocalResultStartPolicy(Token(target), _ =>
        { binding!.Dispose(); return ExactHookBroker.LocalResultStartDecision.ClaimLocal; }, _ => handlers++);
        f.Engine.InvokeVoid(); f.Engine.InvokeVoid(); binding.Dispose();
        Check(calls == 1 && handlers == 0 && binding.DescribePolicy() is { Classified: 1, Claimed: 1, Skipped: 1 }
            && binding.DescribeHook().NativeCalls == 2 && binding.DescribeHook().OriginalCalls == 1 && f.Engine.Installs == 1,
            "In-flight detach undid its claim or later independent original forwarding did not resume.");
    }

    private static void HandlerDetachReentry()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0;
        ExactHookBroker.LocalResultStartBinding? binding = null;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        binding = f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal,
            _ => { binding!.Dispose(); f.Engine.InvokeVoid(); throw new IOException("detached LOCAL initialization"); });
        f.Engine.InvokeVoid(); Check(calls == 0, "A detached LOCAL handler's nested call escaped into stock.");
        f.Engine.InvokeVoid(); binding.Dispose();
        Check(calls == 1 && binding.DescribePolicy() is { Claimed: 1, Skipped: 2, Faults: 1, SuppressedPolicyReentries: 1 },
            "Sticky in-flight protection was lost or persisted into the next independent entry.");
    }

    private static void Ownership()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var handlers = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        var first = f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal, _ => handlers++);
        Throws<InvalidOperationException>(() => f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ContinueOriginal, _ => { }));
        Throws<UnsupportedSignatureException>(() => f.Broker.Bind(target with { MethodInfo = new(3) }, _ => true, (_, _, _, _) => { }));
        using var ordinary = f.Broker.Bind(target with { Id = "same.method.alias" }, _ => true, (_, _, _, _) => { });
        f.Engine.InvokeVoid(); first.Dispose(); f.Engine.InvokeVoid();
        using var second = f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal, _ => handlers++);
        f.Engine.InvokeVoid();
        Check(calls == 1 && handlers == 2 && f.Engine.Installs == 1, "Policy ownership conflicted, repatched an address, or failed rebind.");
    }

    private static void MethodInfoBoundary()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var classifications = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((receiver, method) =>
        { Check(receiver == new IntPtr(0x111) && method == IntPtr.Zero, "Direct-call null MethodInfo was changed."); calls++; });
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ =>
        { classifications++; return ExactHookBroker.LocalResultStartDecision.ContinueOriginal; }, _ => { });
        var invoke = (ExactHookBroker.InstanceVoid)f.Engine.Replacement!;
        invoke(new(0x111), new(0x333)); invoke(new(0x111), IntPtr.Zero);
        Check(calls == 1 && classifications == 1 && policy.DescribePolicy() is { Classified: 1, Continued: 1, Skipped: 1, Faults: 1 },
            "Wrong MethodInfo reached classifier/original or null direct-call identity was rejected.");
    }

    private static void EarlierAliasOwnership()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var handlers = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        using var startObserver = f.Broker.Bind(target, _ => true, (_, _, _, _) => { });
        var alias = f.Broker.Bind(target with { Id = "older.unrelated.alias", MethodInfo = new(0x333) },
            _ => true, (_, _, _, _) => { });
        Throws<UnsupportedSignatureException>(() => f.Broker.BindLocalResultStartPolicy(Token(target),
            _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal, _ => handlers++));
        f.Engine.InvokeVoid(); Check(calls == 1 && handlers == 0, "A rejected policy changed the existing ordinary address.");
        alias.Dispose();
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target),
            _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal, _ => handlers++);
        f.Engine.InvokeVoid();
        Check(calls == 1 && handlers == 1 && f.Engine.Installs == 1 && policy.DescribePolicy().Claimed == 1,
            "Retiring the unrelated alias did not allow one exact policy without repatching.");
    }

    private static void BindingThreadOwnership()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; Exception? bindError = null; Exception? detachError = null;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
        var token = Token(target);
        var worker = new Thread(() =>
        { try { f.Broker.BindLocalResultStartPolicy(token, _ => ExactHookBroker.LocalResultStartDecision.ContinueOriginal, _ => { }); } catch (Exception e) { bindError = e; } });
        worker.Start(); worker.Join();
        Check(bindError is not null && f.Engine.Installs == 0, "Off-main registration installed a native policy.");
        using var policy = f.Broker.BindLocalResultStartPolicy(token, _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal, _ => { });
        worker = new Thread(() => { try { policy.Dispose(); } catch (Exception e) { detachError = e; } });
        worker.Start(); worker.Join(); f.Engine.InvokeVoid();
        Check(detachError is not null && calls == 0 && policy.DescribePolicy().Claimed == 1,
            "Off-main detach removed the still-required LOCAL policy.");
    }
}
