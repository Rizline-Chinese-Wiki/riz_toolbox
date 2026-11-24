using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

// Read-only exact-ledger predicate shared with the isolated owned-machine test.
// It does not accept a caller Ready/FutureClosed boolean, issue a receipt, clear
// a launch hold, or mutate any EngineCancelled state. Production must separately
// verify its actual coordinator-issued source seal before calling this method.
internal static class NativeEngineCancellationTerminalVerifier
{
    internal static bool Verify(NativeCoroutineCancellationLedger requests,
        IReadOnlyList<NativeCoroutineCancellationLedger.CompletionCandidate> candidates,
        NativeEngineRuntimeCoordinator runtime, NativeEngineCoroutineIdentityLedger identities,
        NativeEngineCoroutineInlineLedger inline, NativeEngineSchedulerRowLedger scheduler, NativeEngineWaitLedger wait)
    {
        ArgumentNullException.ThrowIfNull(requests); ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(runtime); ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(inline); ArgumentNullException.ThrowIfNull(scheduler); ArgumentNullException.ThrowIfNull(wait);
        if (!requests.HasCompleteRequestCoverage(candidates)) return false;
        var activity=runtime.ReadActivity();var identityState=identities.Read();var inlineState=inline.Read();
        if (!activity.Known||activity.InFlight!=0||identityState.Unknown||identityState.Untracked!=0||identityState.Outstanding!=0
            ||identityState.Nodes.Unknown||identityState.Nodes.Untracked!=0||identityState.Nodes.Outstanding!=0
            ||inlineState.Unknown||inlineState.Outstanding!=0||scheduler.Read().Pending||wait.Read().Pending) return false;
        foreach(var candidate in candidates)
        {
            if(!candidate.IsForIdentityLedger(identities)||!candidate.TryReadTerminal(requests,out var selected)||selected is null
                ||!inline.HasCompletedStart(selected)||!scheduler.MayRetireNodeGeneration(selected)||!wait.MayRetireNodeGeneration(selected)
                ||!NativeEngineCoroutineObserverRetirement.YieldChildrenFinal(identityState,selected))return false;
            var nodes=identityState.Nodes.Nodes.Where(value=>value.Node==selected.Node&&value.Generation==selected.EngineGeneration
                &&selected.StartSequence<=(ulong)long.MaxValue&&value.StartSequence==checked((long)selected.StartSequence)).ToArray();
            if(nodes.Length!=1||nodes[0].InlineCleanupUnproven||!nodes[0].FinalCleanupObserved||!nodes[0].DetachReturned)return false;
        }
        return candidates.Count!=0;
    }
}
