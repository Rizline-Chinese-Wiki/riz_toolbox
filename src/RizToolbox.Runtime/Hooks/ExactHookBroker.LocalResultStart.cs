using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    /// <summary>Internal policy authority is restricted to the pinned, metadata-verified
    /// LevelOverControl.Start. It is not an SDK hook mutation or an arbitrary address API.</summary>
    internal sealed class LocalResultStartTarget
    {
        private const string SupportedImage = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
        internal VerifiedHookTarget Target { get; }
        private LocalResultStartTarget(VerifiedHookTarget target) => Target = target;

        internal static LocalResultStartTarget Resolve(NativeRuntime api, IntPtr gameAssembly, string gameAssemblySha256)
        {
            if (!string.Equals(gameAssemblySha256, SupportedImage, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("LOCAL result isolation requires the pinned GameAssembly image.");
            ArgumentNullException.ThrowIfNull(api);
            if (gameAssembly == IntPtr.Zero)
                throw new UnsupportedSignatureException("LOCAL result isolation requires the verified GameAssembly module.");
            var cls = api.Class("Assembly-CSharp.dll", "", "LevelOverControl");
            return new(api.ResolveExactHook("local.result.start", gameAssembly, cls, "Start",
                Array.Empty<string>(), 0x3D0B20, ExactHookAbi.InstanceVoid));
        }
    }

    internal enum LocalResultStartDecision { ContinueOriginal, ClaimLocal }
    internal sealed record LocalResultStartDiagnostics(long Classified, long Continued, long Claimed,
        long Skipped, long Faults, long WrongThreadClaims, long SuppressedPolicyReentries);

    internal sealed class LocalResultStartPolicy
    {
        internal readonly VerifiedHookTarget Target;
        internal readonly Func<IntPtr, LocalResultStartDecision> Classify;
        internal readonly Action<IntPtr> HandleLocal;
        internal readonly string Operation;
        internal readonly Action<IntPtr, bool>? MarkTerminalClaim;
        internal long Classified, Continued, Claimed, Skipped, Faults, WrongThreadClaims, SuppressedPolicyReentries;
        internal LocalResultStartPolicy(VerifiedHookTarget target, Func<IntPtr, LocalResultStartDecision> classify,
            Action<IntPtr> handleLocal, string operation = "Start", Action<IntPtr, bool>? markTerminalClaim = null)
            => (Target, Classify, HandleLocal, Operation, MarkTerminalClaim) = (target, classify, handleLocal, operation, markTerminalClaim);
        internal LocalResultStartDiagnostics Describe() => new(Interlocked.Read(ref Classified), Interlocked.Read(ref Continued),
            Interlocked.Read(ref Claimed), Interlocked.Read(ref Skipped), Interlocked.Read(ref Faults),
            Interlocked.Read(ref WrongThreadClaims), Interlocked.Read(ref SuppressedPolicyReentries));
    }

    internal sealed class LocalResultStartBinding : IDisposable
    {
        private readonly Binding _binding;
        private readonly LocalResultStartPolicy _policy;
        internal LocalResultStartBinding(Binding binding, LocalResultStartPolicy policy) => (_binding, _policy) = (binding, policy);
        internal LocalResultStartDiagnostics DescribePolicy() => _policy.Describe();
        internal HookDiagnostics DescribeHook() => _binding.Describe();
        internal ManagedBoundaryActivity ReadActivity() => _binding.ReadActivity();
        // This restores original forwarding. The host must retain this binding
        // until every committed LOCAL session has ended; plugin withdrawal alone
        // must not dispose its still-required session isolation.
        public void Dispose() => _binding.Dispose();
    }

    /// <param name="classify">A thread-safe, non-native identity lookup only: it
    /// may read host-owned rooted session reservations but must not dereference
    /// receiver/native metadata or require the Unity main thread. It runs before
    /// any ordinary observer and is also consulted for off-main native entries.</param>
    /// <param name="handleLocal">Main-thread LOCAL initialization. A claim remains
    /// a skip even if this fails, reenters, or logically detaches the binding.</param>
    internal LocalResultStartBinding BindLocalResultStartPolicy(LocalResultStartTarget target,
        Func<IntPtr, LocalResultStartDecision> classify, Action<IntPtr> handleLocal)
    {
        _main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(classify);
        ArgumentNullException.ThrowIfNull(handleLocal);
        return BindLocalVoidPolicy(target.Target, classify, handleLocal, "Start");
    }

    // All sealed LOCAL void authorities share exactly the same pre-original dispatch,
    // fault, worker and sticky in-flight rules. No arbitrary address API is added.
    private LocalResultStartBinding BindLocalVoidPolicy(VerifiedHookTarget target,
        Func<IntPtr, LocalResultStartDecision> classify, Action<IntPtr> handleLocal, string operation,
        Action<IntPtr, bool>? markTerminalClaim = null)
    {
        var policy = new LocalResultStartPolicy(target, classify, handleLocal, operation, markTerminalClaim);
        var binding = BindInternal(target, _ => false, (_, _, _, _) => { }, null, null, null,
            localResultPolicy: policy);
        return new(binding, policy);
    }

    private sealed class LocalPolicyInvocation
    {
        internal readonly LocalResultStartPolicy Policy;
        internal IntPtr ClaimedReceiver;
        internal LocalPolicyInvocation(LocalResultStartPolicy policy) => Policy = policy;
    }
    [ThreadStatic] private static Dictionary<IntPtr, LocalPolicyInvocation>? EnteredLocalResultPolicies;

    private static bool SkipLocalResultStart(Entry entry, IntPtr instance, IntPtr method)
    {
        if (EnteredLocalResultPolicies is not null
            && EnteredLocalResultPolicies.TryGetValue(entry.Target.Address, out var inFlight))
        {
            // An in-flight claim stays protected even if its handler logically
            // detaches the binding before making a nested native call.
            try { inFlight.Policy.MarkTerminalClaim?.Invoke(instance, inFlight.ClaimedReceiver != IntPtr.Zero && inFlight.ClaimedReceiver == instance); }
            catch { Interlocked.Increment(ref inFlight.Policy.Faults); }
            Interlocked.Increment(ref inFlight.Policy.SuppressedPolicyReentries);
            Interlocked.Increment(ref inFlight.Policy.Skipped);
            return true;
        }
        Sink? sink;
        lock (Gate) sink = entry.Sinks.FirstOrDefault(x => x.Active && x.LocalResultPolicy is not null);
        if (sink is null) return false; // No new policy semantics for ordinary hooks.
        var policy = sink.LocalResultPolicy!;
        EnteredLocalResultPolicies ??= new();
        var policyInvocation = new LocalPolicyInvocation(policy);
        EnteredLocalResultPolicies.Add(entry.Target.Address, policyInvocation);
        var countedSkip = false;
        try
        {
            // Null MethodInfo is valid for direct IL2CPP calls. A non-null wrong
            // identity cannot be safely classified as this verified Start.
            if (method != IntPtr.Zero && method != policy.Target.MethodInfo)
                throw new UnsupportedSignatureException("LOCAL " + policy.Operation + " entry has a different trailing MethodInfo.");
            Interlocked.Increment(ref policy.Classified);
            var decision = policy.Classify(instance);
            if (decision == LocalResultStartDecision.ContinueOriginal)
            {
                Interlocked.Increment(ref policy.Continued);
                return false;
            }
            if (decision != LocalResultStartDecision.ClaimLocal)
                throw new InvalidOperationException("LOCAL result classifier returned an unknown decision.");
            Interlocked.Increment(ref policy.Claimed);
            Interlocked.Increment(ref policy.Skipped); countedSkip = true;
            policyInvocation.ClaimedReceiver = instance;
            // Only the sealed LevelOver authority supplies this pure runtime
            // marker. It runs before worker/handler/detach rejection can return
            // into Progress.Update's terminal Timeline tail.
            policy.MarkTerminalClaim?.Invoke(instance, true);
            // Once claimed, detachment/initialization failure never falls back
            // to stock save/achievement processing for this native invocation.
            if (!sink.Active) return true;
            if (!sink.Main.IsMainThread)
            {
                Interlocked.Increment(ref policy.WrongThreadClaims);
                SafeLog(sink, "LOCAL " + policy.Operation + " was suppressed off the main thread; handler was not run.",
                    new InvalidOperationException("LOCAL result handling requires the verified main thread."));
                return true;
            }
            policy.HandleLocal(instance);
            return true;
        }
        catch (Exception error)
        {
            // An already exact progress/controller token can still protect its
            // tail when method/classifier validation itself faults. Unknown or
            // ordinary frames require an actual LOCAL classifier claim.
            try { policy.MarkTerminalClaim?.Invoke(instance, countedSkip); } catch { }
            Interlocked.Increment(ref policy.Faults);
            if (!countedSkip) Interlocked.Increment(ref policy.Skipped);
            SafeLog(sink, "LOCAL policy failed; stock " + policy.Operation + " remains suppressed for this invocation.", error);
            return true;
        }
        finally { EnteredLocalResultPolicies.Remove(entry.Target.Address); }
    }
}
