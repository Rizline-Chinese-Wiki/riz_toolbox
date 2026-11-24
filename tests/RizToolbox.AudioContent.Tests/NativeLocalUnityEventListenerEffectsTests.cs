using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime.Hooks;
using static Assert;

// Reuses the existing controlled native bridge through the actual broker and
// ledger callbacks. These cases exercise the private receipt issuance path;
// none creates a production launch, installs a native patch or claims that an
// arbitrary game listener's asynchronous effects have completed.
internal static partial class NativeLocalUnityEventLedgerRegression
{
    internal static readonly (string Name, Action Test)[] ListenerWriterSourceCases = {
        ("Listener past empty receipts cannot issue a future writer-source closure from a controlled owner", EffectsPastEmptyNeedsWriterSource),
        ("Listener future writer closure ignores a caller-copied diagnostic closed scalar", EffectsWriterScalarIsNotIssuer),
        ("Listener foreign owner empty receipt cannot supply the receiving owner's writer-source authority", EffectsForeignWriterClosure),
        ("Listener earlier nonempty effects remain Pending before writer-source classification", EffectsNonemptyBeforeWriterClosure)
    };
    internal static readonly (string Name, Action Test)[] ListenerEffectsCases = {
        ("Listener effects archive issues one empty receipt only after actual After Dispose and physical exit", EffectsEmptyPhysicalCompletion),
        ("Listener effects archive preserves distinct receipts for repeated actual empty Invoke generations", EffectsRepeatedEmpty),
        ("Listener effects archive never forgets an earlier nonempty Invoke when the next list is empty", EffectsEarlierNonempty),
        ("Listener effects archive refuses empty completion when the actual inline snapshot is missing", EffectsMissingInline),
        ("Listener effects archive keeps opaque and multicast leaves unaccounted despite zero observed child dispatches", EffectsOpaqueAndMulticast),
        ("Listener effects archive retains foreign nested Invoke responsibility under an otherwise empty parent", EffectsForeignChild),
        ("Listener effects archive refuses a changed actual exit frame and retains the failed generation", EffectsChangedExit),
        ("Listener effects archive detects physical owned worker entries that never acquired a main-thread source", EffectsWorker),
        ("Listener effects membership removal retains the earlier invoked effect and its exact registration source", EffectsRemoveMembership),
        ("Listener effects archive rejects a mutation after the actual empty inline observation", EffectsMutationDuringInvoke),
        ("Listener effects returned snapshots preserve earlier generation identities after context reuse", EffectsImmutableHistory),
        ("Listener effects controlled receipt cannot answer a production launch closure request", EffectsControlledNotProduction)
    };

    private static void SetEffectsEmpty(Fixture fixture)
    {
        var graph = fixture.Backend.Graphs[0];
        fixture.Backend.Graphs[0] = graph with {
            ExecutingCalls = graph.ExecutingCalls with { Rows = Array.Empty<NativeUnityEventInvokable>() } };
    }
    private static void ObserveEffectsInline(Fixture fixture, long receiver)
        => Check(fixture.Backend.Inline(receiver, fixture.Backend.Graphs[0].ExecutingCalls.Object));

    private static void EffectsEmptyPhysicalCompletion()
    {
        using var f = new Fixture(); SetEffectsEmpty(f);
        f.Backend.DuringInvoke = receiver => {
            ObserveEffectsInline(f, receiver);
            var open = f.Owner.ReadListenerEffects();
            Equal(1L, open.PhysicalInvocations); Equal(1, open.TrackedInvocations); Equal(0, open.CompletedEmptyInvocations);
            Check(!open.AllInvokedEffectsAccounted && !open.Invocations.Single().AfterObserved
                && !open.Invocations.Single().ContextDisposed && !open.Invocations.Single().PhysicalExited);
        };
        f.Backend.Call(101);
        var done = f.Owner.ReadListenerEffects(); var row = done.Invocations.Single();
        Check(!done.Unknown && done.AllInvokedEffectsAccounted && !done.FutureMembershipWritersClosed);
        Equal(1, done.CompletedEmptyInvocations); Equal(NativeUnityEventListenerEffectState.NoListenerEffects, row.State);
        Check(row.NativeObserved && row.NativeToken != 0 && row.Caller != 0 && row.Method != 0 && row.MidReturnPc != 0
            && row.AfterObserved && row.ContextDisposed && row.PhysicalExited && row.Inline is not null && row.Post is not null);
        // Full source/writer coverage remains deliberately outside this receipt.
        Check(f.Owner.Read().Pending && !f.Owner.Read().MutationClosed && f.Owner.TryConfirmNativeIdle());
    }
    private static void EffectsRepeatedEmpty()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver);
        f.Backend.Call(101); f.Backend.Call(101);
        var state = f.Owner.ReadListenerEffects();
        Equal(2L, state.PhysicalInvocations); Equal(2, state.TrackedInvocations); Equal(2, state.CompletedEmptyInvocations);
        Check(state.AllInvokedEffectsAccounted && !state.Unknown && state.Invocations.All(row => row.PhysicalExited));
        Check(state.Invocations.Select(row => row.Generation).SequenceEqual(new long[] { 1, 2 }));
        Check(state.Invocations.Select(row => row.NativeToken).Distinct().Count() == 2);
    }
    private static void EffectsEarlierNonempty()
    {
        using var f = new Fixture(); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver);
        f.Backend.Call(101); SetEffectsEmpty(f); f.Backend.Call(101);
        var state = f.Owner.ReadListenerEffects();
        Equal(2, state.TrackedInvocations); Equal(1, state.CompletedEmptyInvocations); Equal(1, state.UnaccountedInvocations);
        Check(!state.Unknown && !state.AllInvokedEffectsAccounted && state.Invocations[0].Invokables == 1
            && state.Invocations[0].State == NativeUnityEventListenerEffectState.Unaccounted
            && state.Invocations[1].State == NativeUnityEventListenerEffectState.NoListenerEffects);
        Equal(0L, f.Owner.Read().InvokableDispatches); Check(f.Owner.TryConfirmNativeIdle());
    }
    private static void EffectsMissingInline()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); f.Backend.Call(101);
        var state = f.Owner.ReadListenerEffects(); var row = state.Invocations.Single();
        Check(!state.Unknown && !state.AllInvokedEffectsAccounted && row.Inline is null
            && row.AfterObserved && row.ContextDisposed && row.PhysicalExited && row.State == NativeUnityEventListenerEffectState.Pending);
        Equal(0, state.CompletedEmptyInvocations);
    }
    private static void EffectsOpaqueAndMulticast()
    {
        foreach (var multicast in new[] { false, true })
        {
            using var f = new Fixture(); var graph = f.Backend.Graphs[0];
            var original = graph.ExecutingCalls.Rows[0];
            var row = multicast ? original with { Opaque = false, Leaves = new[] {
                Leaf(1701) with { Opaque = false }, Leaf(1801) with { Opaque = false } } }
                : original with { Opaque = true, Leaves = Array.Empty<NativeUnityEventLeaf>() };
            f.Backend.Graphs[0] = graph with { ExecutingCalls = graph.ExecutingCalls with { Rows = new[] { row } } };
            f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver); f.Backend.Call(101);
            var effects = f.Owner.ReadListenerEffects(); var invoked = effects.Invocations.Single();
            Check(!effects.Unknown && !effects.AllInvokedEffectsAccounted && invoked.State == NativeUnityEventListenerEffectState.Unaccounted);
            Equal(1, effects.UnaccountedInvocations); Equal(multicast ? 2 : 0, invoked.Leaves); Equal(0, invoked.Dispatches);
        }
    }
    private static void EffectsForeignChild()
    {
        using var f = new Fixture(); SetEffectsEmpty(f);
        f.Backend.DuringInvoke = receiver => { if (receiver == 101) { f.Backend.Call(999); ObserveEffectsInline(f, receiver); } };
        f.Backend.Call(101); var state = f.Owner.ReadListenerEffects();
        Check(!state.Unknown && !state.AllInvokedEffectsAccounted && state.Invocations.Single().ForeignChildInvoked
            && state.Invocations.Single().State == NativeUnityEventListenerEffectState.Unaccounted);
        Equal(1, state.TrackedInvocations); Equal(2, f.Backend.Originals);
    }
    private static void EffectsChangedExit()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver);
        f.Backend.ChangeExitCaller = true; f.Backend.Call(101);
        var state = f.Owner.ReadListenerEffects();
        Check(state.Unknown && !state.AllInvokedEffectsAccounted && state.Invocations.Count == 1
            && state.Invocations.Single().State == NativeUnityEventListenerEffectState.Unknown);
        Equal(0, state.CompletedEmptyInvocations);
    }
    private static void EffectsWorker()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); var reads = f.Backend.GraphReads;
        Task.Run(() => f.Backend.Call(101)).GetAwaiter().GetResult();
        var state = f.Owner.ReadListenerEffects();
        Check(state.Unknown && !state.AllInvokedEffectsAccounted); Equal(1L, state.PhysicalInvocations);
        Equal(0, state.TrackedInvocations); Equal(reads, f.Backend.GraphReads);
    }
    private static NativeOperationFrame EffectMutationFrame(UnityEventOperation operation, ulong sequence)
        => new() { Version = 1, Size = 64, Kind = UnityEventOperationProfile.Get(operation).Kind,
            Self = new(101), Arg0 = UnityEventOperationProfile.Get(operation).Kind == NativeOperationKind.Void0 ? IntPtr.Zero : new(703),
            Arg1 = UnityEventOperationProfile.Get(operation).Kind == NativeOperationKind.Reference2 ? new(709) : IntPtr.Zero,
            Caller = new(0x20001234), Method = new(601), Sequence = sequence };
    private static void EffectsRemoveMembership()
    {
        using var f = new Fixture(); var graph = f.Backend.Graphs[0]; var listener = graph.ExecutingCalls.Rows[0];
        f.Backend.Graphs[0] = graph with { RuntimeCalls = graph.RuntimeCalls with { Rows = new[] { listener } } };
        f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver); f.Backend.Call(101);
        var frame = EffectMutationFrame(UnityEventOperation.RemoveListener, 1001);
        var mutation = f.Owner.CaptureMutation(UnityEventOperation.RemoveListener, frame)!;
        SetEffectsEmpty(f); var after = f.Backend.Graphs[0];
        f.Backend.Graphs[0] = after with { RuntimeCalls = after.RuntimeCalls with { Version = 3, Rows = Array.Empty<NativeUnityEventInvokable>() } };
        f.Owner.CompleteMutation(mutation, frame, NativeOperationExitFlags.Returned); mutation.Dispose();
        var state = f.Owner.ReadListenerEffects(); var source = state.MembershipSources.Single();
        Check(!state.Unknown && !state.AllInvokedEffectsAccounted && state.UnaccountedInvocations == 1
            && source.ConfirmedReturned && source.NativeSequence == 1001 && source.Operation == UnityEventOperation.RemoveListener
            && source.RemovedInvokables.SequenceEqual(new[] { listener.Object }) && source.AddedInvokables.Count == 0);
        Check(!state.FutureMembershipWritersClosed && state.Invocations.Single().Inline!.Executing.Rows.Count == 1);
    }
    private static void EffectsMutationDuringInvoke()
    {
        using var f = new Fixture(); SetEffectsEmpty(f);
        f.Backend.DuringInvoke = receiver => {
            ObserveEffectsInline(f, receiver);
            var frame = EffectMutationFrame(UnityEventOperation.RemoveAllListeners, 1002);
            var mutation = f.Owner.CaptureMutation(UnityEventOperation.RemoveAllListeners, frame)!;
            f.Owner.CompleteMutation(mutation, frame, NativeOperationExitFlags.Returned); mutation.Dispose();
        };
        f.Backend.Call(101); var state = f.Owner.ReadListenerEffects();
        Check(!state.Unknown && !state.AllInvokedEffectsAccounted && state.Invocations.Single().State == NativeUnityEventListenerEffectState.Pending);
        Equal(0, state.CompletedEmptyInvocations); Check(state.MembershipSources.Single().ConfirmedReturned);
    }
    private static void EffectsImmutableHistory()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver);
        f.Backend.Call(101); var first = f.Owner.ReadListenerEffects(); var saved = first.Invocations.Single();
        var graph = f.Backend.Graphs[0];
        f.Backend.Graphs[0] = graph with { ExecutingCalls = graph.ExecutingCalls with { Version = 4, Rows = new[] {
            new NativeUnityEventInvokable(901, 902, true, Array.Empty<NativeUnityEventLeaf>()) } } };
        f.Backend.Call(101); var second = f.Owner.ReadListenerEffects();
        Equal(1, first.Invocations.Count); Equal(0, saved.Invokables); Equal(1L, saved.Generation);
        Equal(NativeUnityEventListenerEffectState.NoListenerEffects, saved.State);
        Equal(saved.NativeToken, second.Invocations[0].NativeToken); Equal(2, second.Invocations.Count);
        Check(!second.AllInvokedEffectsAccounted && second.Invocations[1].Invokables == 1
            && second.Invocations[1].NativeToken != saved.NativeToken);
    }
    private static void EffectsControlledNotProduction()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver); f.Backend.Call(101);
        Check(f.Owner.ReadListenerEffects().AllInvokedEffectsAccounted && !f.Owner.IsFor(null!));
        var source = (INativeLocalActiveConsumerClosureSource)f.Owner;
        Throws<ArgumentNullException>(() => source.ReadActiveConsumerClosure(null!, NativeLocalActiveConsumerObligation.UnityEventConsumers));
        Check(f.Owner.Read().Pending && !f.Owner.Read().MutationClosed && !f.Owner.Read().DispatchPathVerified);
    }

    // Invoke the real private production composition seam, without creating a
    // test setter or issuer. Passing a copied diagnostic snapshot is deliberate:
    // it cannot replace the exact production writer-source owner it consults.
    private static NativeLocalActiveConsumerCheck ReadEffectsConsumerSeam(NativeLocalUnityEventLedger owner,
        NativeUnityEventListenerEffectsSnapshot effects)
        => (NativeLocalActiveConsumerCheck)typeof(NativeLocalUnityEventLedger)
            .GetMethod("ReadListenerConsumersClosure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(owner, new object[] { effects, NativeLocalActiveConsumerObligation.UnityEventConsumers })!;
    private static void EffectsPastEmptyNeedsWriterSource()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver);
        f.Backend.Call(101); f.Backend.Call(101); var effects = f.Owner.ReadListenerEffects();
        Equal(2, effects.CompletedEmptyInvocations); Check(effects.AllInvokedEffectsAccounted);
        var writers = f.Owner.ReadListenerWriterSources();
        Equal(NativeLocalActiveConsumerProofState.Missing, writers.State);
        Check(writers.Detail.Contains("production launch/backend", StringComparison.Ordinal));
        Check(ReadEffectsConsumerSeam(f.Owner, effects).State != NativeLocalActiveConsumerProofState.Proven);
    }
    private static void EffectsWriterScalarIsNotIssuer()
    {
        using var f = new Fixture(); SetEffectsEmpty(f); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver);
        f.Backend.Call(101); var effects = f.Owner.ReadListenerEffects(); Check(effects.AllInvokedEffectsAccounted);
        var copied = effects with { FutureMembershipWritersClosed = true, Detail = "caller claims all writers ended" };
        var checkedSource = ReadEffectsConsumerSeam(f.Owner, copied);
        Equal(NativeLocalActiveConsumerProofState.Missing, checkedSource.State);
        Check(checkedSource.Detail.Contains("future listener sources", StringComparison.Ordinal));
        Check(!f.Owner.ReadListenerEffects().FutureMembershipWritersClosed);
    }
    private static void EffectsForeignWriterClosure()
    {
        using var a = new Fixture(); using var b = new Fixture(); SetEffectsEmpty(a); SetEffectsEmpty(b);
        a.Backend.DuringInvoke = receiver => ObserveEffectsInline(a, receiver);
        a.Backend.Call(101); var foreign = a.Owner.ReadListenerEffects();
        Check(foreign.AllInvokedEffectsAccounted && foreign.Invocations.Single().NativeToken != 0);
        // Both fixtures have the same scalar Event identity, but distinct
        // actual broker/owner/source responsibilities. Neither is production.
        Equal(a.Owner.Read().EventSnapshots.Single().Event, b.Owner.Read().EventSnapshots.Single().Event);
        Equal(NativeLocalActiveConsumerProofState.Missing, ReadEffectsConsumerSeam(b.Owner, foreign).State);
        Equal(0L, b.Owner.ReadListenerEffects().PhysicalInvocations);
    }
    private static void EffectsNonemptyBeforeWriterClosure()
    {
        using var f = new Fixture(); f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver);
        f.Backend.Call(101); SetEffectsEmpty(f); f.Backend.Call(101);
        var effects = f.Owner.ReadListenerEffects(); Equal(1, effects.UnaccountedInvocations); Equal(1, effects.CompletedEmptyInvocations);
        var result = ReadEffectsConsumerSeam(f.Owner, effects with { FutureMembershipWritersClosed = true });
        Equal(NativeLocalActiveConsumerProofState.Pending, result.State);
        Check(result.Detail.Contains("unaccounted", StringComparison.Ordinal));
    }

    private sealed partial class Backend
    {
        public NativeUnityEventGraph CaptureMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
        { _main.VerifyAccess(); return Graphs[eventSlot]; }
    }
}
