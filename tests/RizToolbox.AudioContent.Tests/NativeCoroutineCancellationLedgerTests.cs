using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

/// <summary>Production scalar ledger with controlled identity providers. No Stop,
/// native/game call, GC root or engine cancellation completion is simulated.</summary>
internal static class NativeCoroutineCancellationLedgerTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Cancellation requests match only exact current enumerator or confirmed handle without engine completion", ExactReferences),
        ("Cancellation StopAll freezes same receiver records and rejects Starts or reused generations during Stop", StopAllSnapshots),
        ("Cancellation nested wrappers preserve physical sequences and ancestry without logical cancel credit", NestedWrappers),
        ("Cancellation immutable provider registration and foreign or worker faults never read tracker providers", ForeignAndWorkers),
        ("Cancellation provider faults register responsibility before reads and unknown requests still clean up", ProviderFailures),
        ("Cancellation abnormal named and null requests preserve native facts without proving engine cancellation", AbnormalNamedAndNull),
        ("Cancellation wrong frame owner duplicate and parent exits retain responsibility until exact cleanup", InvalidCompletion),
        ("Cancellation bounded records retain untracked overflow responsibility and immutable diagnostics", CapacityAndSnapshots)
    };
    private static void Check(bool value, string message = "Cancellation ledger assertion failed.")
    { if (!value) throw new InvalidOperationException(message); }

    private static void ExactReferences()
    {
        using var f = new Fixture(); var row = f.First.Add(0, 7, 1001, unchecked((long)0x8123456789abcdefUL));
        f.First.Add(1, 8, 1002, 5002, confirmed: false);
        var closed = f.First.Add(2, 0, 1003, 5003);
        f.First.Rows[2] = closed with { Published = false, Retiring = true, Known = false };
        var initial = f.Ledger.Read(); Check(!initial.HasObservedRequests && !initial.Pending && !initial.Unknown
            && initial.CanConfirmRequestIdle && !initial.EngineCancelled && initial.Outstanding == 0);
        Check(!NativeCoroutineCancellationObserver.RequiresRetention(new(0, true), initial)
            && NativeCoroutineCancellationObserver.RequiresRetention(new(1, true), initial)
            && NativeCoroutineCancellationObserver.RequiresRetention(new(0, false), initial));
        var frame = f.Frame(NativeCoroutineCancellationOperation.StopEnumerator, row.Enumerator);
        var context = f.Capture(NativeCoroutineCancellationOperation.StopEnumerator, frame);
        var entered = f.Ledger.Read(); var record = entered.Requests.Single();
        Check(entered.HasObservedRequests && entered.Pending && !entered.CanConfirmRequestIdle && entered.Outstanding == 1
            && record.Requested && record.Captured && !record.MatchUnproven && !record.NativeCompleted
            && record.Matched.Single().Equals(row) && !record.NativeReturned && !record.Abnormal);
        f.Ledger.Complete(context, frame, NativeOperationExitFlags.Returned);
        var completed = f.Ledger.Read(); Check(completed.Outstanding == 1 && completed.NativeReturnedRequests == 1
            && completed.Requests.Single().NativeCompleted && !completed.Requests.Single().Disposed);
        context.Dispose(); var drained = f.Ledger.Read();
        Check(drained.Outstanding == 0 && drained.CanConfirmRequestIdle && drained.Pending && !drained.EngineCancelled && !drained.Unknown
            && drained.Requests.Single().Before.Count == 2 && NativeCoroutineCancellationObserver.RequiresRetention(new(0, true), drained));
        frame = f.Frame(NativeCoroutineCancellationOperation.StopHandle, row.ObservedHandle);
        context = f.Capture(NativeCoroutineCancellationOperation.StopHandle, frame);
        Check(f.Ledger.Read().Requests.Last().Matched.Single().ObservedHandle == unchecked((long)0x8123456789abcdefUL));
        f.Return(context, frame);
        foreach (var pair in new[] { (NativeCoroutineCancellationOperation.StopEnumerator, row.ObservedHandle),
            (NativeCoroutineCancellationOperation.StopHandle, row.Enumerator), (NativeCoroutineCancellationOperation.StopHandle, 5002L) })
        {
            frame = f.Frame(pair.Item1, pair.Item2); context = f.Capture(pair.Item1, frame);
            Check(f.Ledger.Read().Requests.Last() is { Captured: true, MatchUnproven: true, Matched.Count: 0 }); f.Return(context, frame);
        }
        Check(!f.Ledger.Read().Unknown && f.Second.ReadCalls == 0 && f.First.Rows.All(identity => !identity.NaturalTerminal));
        f.First.Add(3, 9, 1004, row.ObservedHandle);
        frame = f.Frame(NativeCoroutineCancellationOperation.StopHandle, row.ObservedHandle);
        context = f.Capture(NativeCoroutineCancellationOperation.StopHandle, frame);
        Check(f.Ledger.Unknown && f.Ledger.Read().Requests.Last() is { Captured: false, MatchUnproven: true, Matched.Count: 2 });
        f.Return(context, frame); Check(f.Ledger.Read().Outstanding == 0);
        // Actual closed initialization tracker output remains diagnostic (Known
        // false) and must not poison a later active owner on the same controller.
        using var init = new NativeLocalInitializationCoroutineRegression.Fixture(); init.Finish(); Check(init.Owner.TryReleaseAfterEnded());
        var diagnostics = init.Owner.ReadCancellationIdentities();
        Check(diagnostics.Count == 2 && diagnostics.All(value => !value.Known && value.Retiring));
        var live = new ProviderState(init.Owner.ControllerIdentity); var active = live.Add(0, 1, 9001, 9002);
        var combined = new NativeCoroutineCancellationLedger(init.Main, new[] {
            new NativeCoroutineCancellationIdentityProvider(init.Owner, init.Owner.ControllerIdentity,
                init.Owner.ReadCancellationIdentities, init.Owner.IsCurrentCancellationIdentity), live.Provider });
        var actualFrame = f.Frame(NativeCoroutineCancellationOperation.StopEnumerator, active.Enumerator, active.Receiver);
        var actualContext = combined.Capture(NativeCoroutineCancellationOperation.StopEnumerator, actualFrame)!;
        Check(actualContext is not null && combined.Read().Requests.Single().Matched.Single().Equals(active));
        combined.Complete(actualContext!, actualFrame, NativeOperationExitFlags.Returned); actualContext!.Dispose();
        Check(!combined.Unknown && combined.Read() is { Pending: true, EngineCancelled: false, Outstanding: 0 });
    }

    private static void StopAllSnapshots()
    {
        using (var f = new Fixture(secondReceiver: 101))
        {
            f.First.Add(0, 1, 1001, 5001); f.First.Add(1, 2, 1002, 5002); f.Second.Add(0, 1, 2001, 6001);
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopAll); var context = f.Capture(NativeCoroutineCancellationOperation.StopAll, frame);
            var saved = f.Ledger.Read(); var before = saved.Requests.Single().Before; Equal(3, before.Count);
            Check(saved.Requests.Single().Matched.Count == 3 && saved.Requests.Single().Matched.Any(value => ReferenceEquals(value.Owner, f.Second.Owner)));
            var fresh = f.First.Add(2, 3, 1003, 5003); f.Return(context, frame);
            var result = f.Ledger.Read(); Check(result.Unknown && result.Pending && !result.EngineCancelled && result.Outstanding == 0);
            var record = result.Requests.Single(); Check(record.IdentityChanged && record.NativeReturned && record.After.Count == 4
                && record.Matched.Count == 3 && !record.Matched.Contains(fresh));
            Equal(3, before.Count); Check(!saved.Requests.Single().NativeCompleted);
            var reads = f.First.ReadCalls + f.Second.ReadCalls; frame = f.Frame(NativeCoroutineCancellationOperation.StopAll);
            context = f.Capture(NativeCoroutineCancellationOperation.StopAll, frame); f.Return(context, frame);
            Equal(reads, f.First.ReadCalls + f.Second.ReadCalls); Check(!f.Ledger.Read().Requests.Last().Captured);
        }
        using (var f = new Fixture())
        {
            var old = f.First.Add(0, 1, 1001, 5001);
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopHandle, 5001); var context = f.Capture(NativeCoroutineCancellationOperation.StopHandle, frame);
            f.First.Rows[0] = old with { Generation = 2 }; f.Return(context, frame);
            Check(f.Ledger.Read() is { Unknown: true, Pending: true, EngineCancelled: false }
                && f.Ledger.Read().Requests.Single().IdentityChanged);
        }
        using (var f = new Fixture())
        {
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopAll); var context = f.Capture(NativeCoroutineCancellationOperation.StopAll, frame);
            Check(f.Ledger.Read().Requests.Single() is { Captured: true, Matched.Count: 0, MatchUnproven: false }); f.Return(context, frame);
            Check(f.Ledger.Read() is { HasObservedRequests: true, Pending: true, Unknown: false, EngineCancelled: false, Outstanding: 0 });
        }
    }

    private static void NestedWrappers()
    {
        using var f = new Fixture(); f.First.Add(0, 1, 1001, 5001); f.Second.Add(0, 1, 2001, 6001);
        var outerFrame = f.Frame(NativeCoroutineCancellationOperation.StopHandle, 5001);
        var outer = f.Capture(NativeCoroutineCancellationOperation.StopHandle, outerFrame);
        // Keep the observed native sequence and exact operation for each wrapper;
        // parent/root ancestry is correlation, never logical cancel credit.
        var innerFrame = f.Frame(NativeCoroutineCancellationOperation.StopManagedHandle, 5001);
        var inner = f.Capture(NativeCoroutineCancellationOperation.StopManagedHandle, innerFrame);
        var rows = f.Ledger.Read().Requests; Equal(2L, f.Ledger.Read().Outstanding);
        Check(rows[0].NativeSequence == outerFrame.Sequence && rows[1].NativeSequence == innerFrame.Sequence
            && rows[0].RequestId != rows[1].RequestId
            && rows[1].ParentRequestId == rows[0].RequestId && rows[1].RootRequestId == rows[0].RootRequestId
            && rows[1].Operation == NativeCoroutineCancellationOperation.StopManagedHandle
            && rows[1].Matched.Single().Equals(rows[0].Matched.Single()));
        f.Return(inner, innerFrame); Equal(1L, f.Ledger.Read().Outstanding);
        var enumFrame = f.Frame(NativeCoroutineCancellationOperation.StopManagedEnumerator, 1001);
        var enumContext = f.Capture(NativeCoroutineCancellationOperation.StopManagedEnumerator, enumFrame); f.Return(enumContext, enumFrame);
        var crossFrame = f.Frame(NativeCoroutineCancellationOperation.StopAll, receiver: 202);
        var cross = f.Capture(NativeCoroutineCancellationOperation.StopAll, crossFrame);
        Check(f.Ledger.Read().Requests.Last().Matched.Single().Receiver == 202
            && f.Ledger.Read().Requests.Last().ParentRequestId == rows[0].RequestId); f.Return(cross, crossFrame);
        f.Return(outer, outerFrame);
        var state = f.Ledger.Read(); Check(state.ObservedRequests == 4 && state.NativeReturnedRequests == 4
            && state.Outstanding == 0 && state.CanConfirmRequestIdle && !state.Unknown && state.Pending && !state.EngineCancelled);
    }

    private static void ForeignAndWorkers()
    {
        using (var f = new Fixture())
        {
            var foreign = f.Frame(NativeCoroutineCancellationOperation.StopEnumerator, 1001, 9999);
            Check(f.Ledger.Capture(NativeCoroutineCancellationOperation.StopEnumerator, foreign) is null);
            f.Ledger.ObserveFault(NativeCoroutineCancellationOperation.StopAll, foreign, "Foreign fault");
            Equal(0, f.First.ReadCalls); Equal(0L, f.Ledger.Read().ObservedRequests); Check(!f.Ledger.Unknown);
            Check(!NativeCoroutineCancellationObserver.RequiresRetention(new(0, true), f.Ledger.Read()));
            var owned = f.Frame(NativeCoroutineCancellationOperation.StopAll);
            Task.Run(() => {
                Throws<InvalidOperationException>(() => f.Ledger.Capture(NativeCoroutineCancellationOperation.StopAll, owned));
                f.Ledger.ObserveFault(NativeCoroutineCancellationOperation.StopAll, foreign, "Foreign worker");
            }).GetAwaiter().GetResult(); Check(!f.Ledger.Unknown);
            Task.Run(() => f.Ledger.ObserveFault(NativeCoroutineCancellationOperation.StopAll, owned, "Owned worker")).GetAwaiter().GetResult();
            Equal(0, f.First.ReadCalls); Equal(0, f.Second.ReadCalls);
            Check(f.Ledger.Read() is { Unknown: true, Pending: true, HasObservedRequests: false, Outstanding: 0, CanConfirmRequestIdle: false });
            Check(NativeCoroutineCancellationObserver.RequiresRetention(new(0, true), f.Ledger.Read()));
            var context = f.Capture(NativeCoroutineCancellationOperation.StopAll, owned); f.Return(context, owned);
            Equal(0, f.First.ReadCalls); Check(f.Ledger.Read().Requests.Single() is { Captured: false, NativeReturned: true, Disposed: true });
        }
        using (var f = new Fixture())
        {
            f.First.Add(0, 1, 1001, 5001); f.Input[0] = f.Second.Provider;
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopEnumerator, 1001);
            var context = f.Capture(NativeCoroutineCancellationOperation.StopEnumerator, frame); f.Return(context, frame);
            Check(ReferenceEquals(f.Ledger.Read().Requests.Single().Matched.Single().Owner, f.First.Owner)); Equal(0, f.Second.ReadCalls);
        }
        using var main = new MainThreadDispatcher(); var provider = new ProviderState(101);
        Throws<ArgumentOutOfRangeException>(() => new NativeCoroutineCancellationLedger(main, Array.Empty<NativeCoroutineCancellationIdentityProvider>()));
        Throws<ArgumentOutOfRangeException>(() => new NativeCoroutineCancellationLedger(main, Enumerable.Repeat(provider.Provider, 33).ToArray()));
        foreach (var bad in new[] { provider.Provider with { Owner = null! }, provider.Provider with { Receiver = 0 }, provider.Provider with { Read = null! } })
            Throws<ArgumentException>(() => new NativeCoroutineCancellationLedger(main, new[] { bad }));
        Throws<ArgumentException>(() => new NativeCoroutineCancellationLedger(main, new[] { provider.Provider, provider.Provider }));
        Equal(0, provider.ReadCalls);
    }

    private static void ProviderFailures()
    {
        foreach (var stage in new[] { "read", "current", "stale", "unknown", "foreign", "duplicate", "too-many", "source-fault" })
        {
            using var f = new Fixture(); var row = f.First.Add(0, 1, 1001, 5001); var enteredChecks = 0;
            f.First.DuringRead = () => {
                Check(f.Ledger.Read() is { HasObservedRequests: true, Pending: true, Outstanding: 1 }
                    && f.Ledger.Read().Requests.Single().Requested); enteredChecks++;
                if (stage == "source-fault") f.Ledger.ObserveFault(NativeCoroutineCancellationOperation.StopAll, f.Frame(NativeCoroutineCancellationOperation.StopAll), "Source became unknown");
            };
            if (stage == "read") f.First.ThrowRead = true;
            if (stage == "current") f.First.ThrowCurrent = true;
            if (stage == "stale") f.First.ForceNotCurrent = true;
            if (stage == "unknown") f.First.Rows[0] = row with { Known = false };
            if (stage == "foreign") f.First.Rows[0] = row with { Owner = f.Second.Owner };
            if (stage == "duplicate") f.First.Rows.Add(row);
            if (stage == "too-many") for (var i = 1; i <= NativeCoroutineCancellationLedger.MaximumIdentities; ++i) f.First.Add(i, 1, 1001 + i, 5001 + i);
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopEnumerator, 1001); var context = f.Capture(NativeCoroutineCancellationOperation.StopEnumerator, frame);
            Check(enteredChecks == 1 && f.Ledger.Unknown && !f.Ledger.Read().Requests.Single().Captured);
            var reads = f.First.ReadCalls; var currents = f.First.CurrentCalls;
            f.Return(context, frame); var state = f.Ledger.Read();
            Check(state.Outstanding == 0 && state.NativeReturnedRequests == 1 && state.Pending && !state.EngineCancelled && !state.CanConfirmRequestIdle);
            Equal(reads, f.First.ReadCalls); Equal(currents, f.First.CurrentCalls);
        }
        using (var f = new Fixture())
        {
            f.First.Add(0, 1, 1001, 5001); var frame = f.Frame(NativeCoroutineCancellationOperation.StopEnumerator, 1001);
            var context = f.Capture(NativeCoroutineCancellationOperation.StopEnumerator, frame); f.First.ThrowRead = true;
            f.Return(context, frame); Check(f.Ledger.Read() is { Unknown: true, Outstanding: 0, NativeReturnedRequests: 1, Pending: true }
                && f.Ledger.Read().Requests.Single() is { Captured: true, NativeCompleted: true, NativeReturned: true, Disposed: true });
        }
    }

    private static void AbnormalNamedAndNull()
    {
        foreach (var flags in new[] { NativeOperationExitFlags.Abnormal, NativeOperationExitFlags.Abnormal | NativeOperationExitFlags.EnterFaulted,
            NativeOperationExitFlags.Returned | NativeOperationExitFlags.EnterFaulted, (NativeOperationExitFlags)0,
            NativeOperationExitFlags.Returned | NativeOperationExitFlags.Abnormal })
        {
            using var f = new Fixture(); f.First.Add(0, 1, 1001, 5001);
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopHandle, 5001); var context = f.Capture(NativeCoroutineCancellationOperation.StopHandle, frame);
            var reads = f.First.ReadCalls; var currents = f.First.CurrentCalls;
            f.Ledger.Complete(context, frame, flags); context.Dispose(); var state = f.Ledger.Read(); var row = state.Requests.Single();
            var returned = (flags & NativeOperationExitFlags.Returned) != 0 && (flags & NativeOperationExitFlags.Abnormal) == 0;
            var abnormal = (flags & NativeOperationExitFlags.Abnormal) != 0;
            Check(row.NativeCompleted && row.Disposed && row.NativeReturned == returned && row.Abnormal == abnormal
                && row.Flags == (uint)flags && state.NativeReturnedRequests == (returned ? 1 : 0)
                && state.AbnormalRequests == (abnormal ? 1 : 0) && state.Outstanding == 0 && state.Unknown && state.Pending && !state.EngineCancelled);
            Equal(reads, f.First.ReadCalls); Equal(currents, f.First.CurrentCalls);
        }
        using (var f = new Fixture())
        {
            f.First.Add(0, 1, 1001, 5001); f.First.Add(1, 2, 1002, 5002);
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopNamed, unchecked((long)0x8abc000000001234UL));
            var context = f.Capture(NativeCoroutineCancellationOperation.StopNamed, frame); f.Return(context, frame);
            Check(f.Ledger.Read().Requests.Single() is { Captured: true, MatchUnproven: true, Matched.Count: 0, Before.Count: 2, After.Count: 2 }
                && f.Ledger.Read() is { Unknown: false, Pending: true, EngineCancelled: false });
        }
        foreach (var operation in new[] { NativeCoroutineCancellationOperation.StopHandle, NativeCoroutineCancellationOperation.StopEnumerator })
        {
            using var f = new Fixture(); var frame = f.Frame(operation); var context = f.Capture(operation, frame);
            Check(f.Ledger.Read().Requests.Single() is { Requested: true, MatchUnproven: true, Matched.Count: 0 });
            f.Ledger.Complete(context, frame, NativeOperationExitFlags.Abnormal); context.Dispose();
            Check(f.Ledger.Read() is { HasObservedRequests: true, Pending: true, AbnormalRequests: 1, Outstanding: 0, EngineCancelled: false });
        }
    }

    private static void InvalidCompletion()
    {
        foreach (var field in new[] { "version", "size", "kind", "reserved", "self", "arg0", "arg1", "method", "caller", "sequence" })
        {
            using var f = new Fixture(); f.First.Add(0, 1, 1001, 5001); var frame = f.Frame(NativeCoroutineCancellationOperation.StopEnumerator, 1001);
            var context = f.Capture(NativeCoroutineCancellationOperation.StopEnumerator, frame); var changed = frame;
            switch (field) {
                case "version": changed.Version++; break; case "size": changed.Size++; break; case "kind": changed.Kind = NativeOperationKind.Reference2; break;
                case "reserved": changed.Reserved++; break; case "self": changed.Self = new(102); break; case "arg0": changed.Arg0 = new(1002); break;
                case "arg1": changed.Arg1 = new(7); break; case "method": changed.Method = new(8); break; case "caller": changed.Caller = new(9); break;
                default: changed.Sequence++; break;
            }
            var reads = f.First.ReadCalls; f.Ledger.Complete(context, changed, NativeOperationExitFlags.Returned); context.Dispose();
            Check(f.Ledger.Read() is { Unknown: true, Outstanding: 1, NativeReturnedRequests: 0 }
                && f.Ledger.Read().Requests.Single() is { NativeCompleted: false, Disposed: false }); Equal(reads, f.First.ReadCalls);
            f.Return(context, frame); Check(f.Ledger.Read() is { Unknown: true, Outstanding: 0, NativeReturnedRequests: 1, EngineCancelled: false });
        }
        using (var f = new Fixture())
        {
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopAll); var outer = f.Capture(NativeCoroutineCancellationOperation.StopAll, frame);
            var innerFrame = f.Frame(NativeCoroutineCancellationOperation.StopAll); var inner = f.Capture(NativeCoroutineCancellationOperation.StopAll, innerFrame);
            f.Ledger.Complete(outer, frame, NativeOperationExitFlags.Returned); outer.Dispose(); Equal(2L, f.Ledger.Read().Outstanding);
            f.Return(inner, innerFrame); Equal(1L, f.Ledger.Read().Outstanding); f.Return(outer, frame);
            Check(f.Ledger.Read() is { Unknown: true, Outstanding: 0, NativeReturnedRequests: 2, Pending: true });
        }
        using (var f = new Fixture())
        using (var foreign = new Fixture())
        {
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopAll); var context = f.Capture(NativeCoroutineCancellationOperation.StopAll, frame);
            foreign.Ledger.Complete(context, frame, NativeOperationExitFlags.Returned);
            Check(foreign.Ledger.Unknown && !f.Ledger.Unknown && f.Ledger.Read().Outstanding == 1);
            Task.Run(() => Throws<InvalidOperationException>(() => f.Ledger.Complete(context, frame, NativeOperationExitFlags.Returned))).GetAwaiter().GetResult();
            f.Ledger.Complete(context, frame, NativeOperationExitFlags.Returned); f.Ledger.Complete(context, frame, NativeOperationExitFlags.Returned); context.Dispose(); context.Dispose();
            Check(f.Ledger.Read() is { Unknown: true, Outstanding: 0, NativeReturnedRequests: 1 });
        }
        foreach (var field in new[] { "operation", "abi", "frame" })
        {
            using var f = new Fixture(); var operation = field == "operation" ? (NativeCoroutineCancellationOperation)99 : NativeCoroutineCancellationOperation.StopAll;
            var frame = f.Frame(NativeCoroutineCancellationOperation.StopAll);
            if (field == "abi") frame.Kind = NativeOperationKind.Reference1;
            if (field == "frame") frame.Version = 9;
            var context = f.Capture(operation, frame); Check(f.Ledger.Unknown && f.First.ReadCalls == 0); f.Return(context, frame);
            Check(f.Ledger.Read() is { Outstanding: 0, Pending: true, EngineCancelled: false });
        }
    }

    private static void CapacityAndSnapshots()
    {
        using var f = new Fixture(); var initial = f.Ledger.Read();
        for (var i = 0; i < NativeCoroutineCancellationLedger.MaximumRequests; ++i)
        { var frame = f.Frame(NativeCoroutineCancellationOperation.StopAll); var context = f.Capture(NativeCoroutineCancellationOperation.StopAll, frame); f.Return(context, frame); }
        var full = f.Ledger.Read(); Check(full.Requests.Count == NativeCoroutineCancellationLedger.MaximumRequests && full.Outstanding == 0
            && full.CanConfirmRequestIdle && full.Pending && !full.Unknown && !full.EngineCancelled); Equal(0, initial.Requests.Count);
        var reads = f.First.ReadCalls; var extra = f.Frame(NativeCoroutineCancellationOperation.StopAll);
        Check(f.Ledger.Capture(NativeCoroutineCancellationOperation.StopAll, extra) is null);
        Check(f.Ledger.Capture(NativeCoroutineCancellationOperation.StopAll, extra) is null);
        var overflow = f.Ledger.Read(); Check(overflow.Unknown && overflow.Pending && !overflow.CanConfirmRequestIdle
            && overflow.UntrackedRequests == 2 && overflow.Outstanding == 2
            && overflow.ObservedRequests == NativeCoroutineCancellationLedger.MaximumRequests + 2
            && overflow.Requests.Count == NativeCoroutineCancellationLedger.MaximumRequests && !overflow.EngineCancelled
            && NativeCoroutineCancellationObserver.RequiresRetention(new(0, true), overflow));
        Equal(reads, f.First.ReadCalls); Check(!full.Unknown && full.UntrackedRequests == 0);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly ProviderState First = new(101), Second;
        internal readonly NativeCoroutineCancellationIdentityProvider[] Input;
        internal readonly NativeCoroutineCancellationLedger Ledger;
        private ulong _sequence;
        internal Fixture(long secondReceiver = 202)
        { Second = new(secondReceiver); Input = new[] { First.Provider, Second.Provider }; Ledger = new(Main, Input); }
        internal NativeOperationFrame Frame(NativeCoroutineCancellationOperation operation, long argument = 0, long receiver = 101)
            => new() { Version = 1, Size = 64, Kind = operation == NativeCoroutineCancellationOperation.StopAll ? NativeOperationKind.Void0 : NativeOperationKind.Reference1,
                Self = new(receiver), Arg0 = new(argument), Method = new(0x123), Caller = new(unchecked((long)0x8123456789abcdefUL)), Sequence = ++_sequence };
        internal NativeCoroutineCancellationLedger.RequestContext Capture(NativeCoroutineCancellationOperation operation, NativeOperationFrame frame)
            => Ledger.Capture(operation, frame) ?? throw new InvalidOperationException("Owned cancellation request was not registered.");
        internal void Return(NativeCoroutineCancellationLedger.RequestContext context, NativeOperationFrame frame)
        { Ledger.Complete(context, frame, NativeOperationExitFlags.Returned); context.Dispose(); }
        public void Dispose() => Main.Dispose(); // No engine completion or responsibility is cleared by fixture disposal.
    }
    private sealed class ProviderState
    {
        internal readonly object Owner = new();
        internal readonly long Receiver;
        internal readonly List<NativeCoroutineCancellationIdentity> Rows = new();
        internal readonly NativeCoroutineCancellationIdentityProvider Provider;
        internal int ReadCalls, CurrentCalls;
        internal bool ThrowRead, ThrowCurrent, ForceNotCurrent;
        internal Action? DuringRead;
        internal ProviderState(long receiver)
        {
            Receiver = receiver; Provider = new(Owner, receiver, () => {
                ++ReadCalls; DuringRead?.Invoke(); if (ThrowRead) throw new IOException("Controlled scalar snapshot failure."); return Rows;
            }, identity => { ++CurrentCalls; if (ThrowCurrent) throw new IOException("Controlled scalar identity failure."); return !ForceNotCurrent && Rows.Contains(identity); });
        }
        internal NativeCoroutineCancellationIdentity Add(int slot, long generation, long enumerator, long handle, bool confirmed = true)
        {
            var identity = new NativeCoroutineCancellationIdentity(Owner, Receiver, slot, generation, enumerator, handle,
                confirmed, confirmed, true, false, false, true); Rows.Add(identity); return identity;
        }
    }
}
