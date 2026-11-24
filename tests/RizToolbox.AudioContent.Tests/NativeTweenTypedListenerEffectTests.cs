using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static class NativeTweenTypedListenerEffectTests
{
    internal static readonly (string Name, Action Test)[] Cases = new (string Name, Action Test)[] {
        ("Single exact Play listener transfers every distinct new occurrence and completion waits for both duplicate queue exits", AllOccurrences),
        ("Single exact Play listener can transfer an InstantWork subtree without claiming its native return ended effects", InstantTransfer),
        ("Single Play transfer cannot close an unknown listener or a child with no actual Play", UnknownAndMissing),
        ("Single Play transfer retains changed delegate target method and multicast observations as Pending", ChangedConsumer),
        ("Single Play transfer refuses sibling or skipped calls and waits for the actual Play disposal", IncompleteSubtree),
        ("Single Play transfer preserves all abnormal child and source journal failure responsibility", AbnormalAndGap),
        ("Single Play receipt stays bound to its own child when a later listener creates new work", LaterChild),
        ("Single Play completion loan rejects source occurrences created during its validation callback", NewWorkInvalidatesLoan)
    }.Concat(NativeTweenTypedPlayNativeReaderTests.Cases).ToArray();

    private sealed class Invocation : IDisposable
    {
        internal readonly TweenCompletionFixture Fixture;
        internal readonly NativeLocalTweenWorkOwner.OperationContext Update, Execute;
        internal readonly NativeTweenSourceRecord ExecuteCall;
        internal readonly IDisposable Context;
        internal readonly NativeOperationFrame Parent;
        internal readonly NativeSingleExecutingList List;
        internal Invocation(TweenCompletionFixture fixture, int listeners = 1)
        {
            Fixture = fixture; Update = fixture.Begin(NativeTweenOperation.Update);
            ExecuteCall = fixture.Before(Update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            fixture.Publish(ExecuteCall);
            Execute = fixture.Begin(NativeTweenOperation.ExecuteTween, ExecuteCall.RelayReturnPc.ToInt64() + 5);
            List = new(107, 3001, 3002, listeners, 1, Enumerable.Range(0, listeners).Select(index => 4001L + index).ToArray());
            if (fixture.Backend.TypedEventGraph is { } graph)
                fixture.Backend.TypedEventGraph = graph with { RuntimeCalls = List, ExecutingCalls = List };
            (Context, Parent) = fixture.Backend.BeginTypedListenerInvocation();
            fixture.Owner.CaptureTypedExecutingList(Context, Frame(NativeTweenSingleEventStage.Mid), List);
            fixture.Backend.PlayConsumer = new(4001, 5001, 101, 700, 0x1803956f0, 0x1803956f0, 101);
        }
        internal NativeTweenSingleEventFrame Frame(NativeTweenSingleEventStage stage, int index = 0) => new() {
            Version = 1, Size = 200, Stage = stage, Site = 1, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId,
            Epoch = 9, ParentEpoch = 11, ParentManagedToken = 13, ParentSequence = Parent.Sequence,
            Event = Parent.Self, List = new(3001), ParentMethod = Parent.Method, ParentCaller = Parent.Caller,
            ValueBits = Parent.ScalarBits, ChildSequence = stage == NativeTweenSingleEventStage.Mid ? 0UL : (ulong)(201 + index),
            Index = (ulong)index, Invokable = stage == NativeTweenSingleEventStage.Mid ? IntPtr.Zero : new(4001 + index),
            ActualTarget = new(0x18154e5b0), ArgRcx = new(4001 + index), SourceRsp = 0x10000
        };
        internal NativeTweenSingleEventFrame Before(int index = 0)
        {
            var frame = Frame(NativeTweenSingleEventStage.Before, index);
            var target = new NativeSingleDispatchTarget(1, frame.Invokable.ToInt64(), frame.ActualTarget.ToInt64(),
                6001, frame.ValueBits, null, false, 0, Array.Empty<long>(), "controlled exact zero-argument source");
            Fixture.Owner.CaptureTypedDispatch(Context, frame, List, target); return frame;
        }
        internal void After(NativeTweenSingleEventFrame before, bool abnormal = false)
        {
            before.Stage = abnormal ? NativeTweenSingleEventStage.Abnormal : NativeTweenSingleEventStage.Returned;
            if (abnormal) before.Flags = NativeTweenSingleEventFlags.MissingPost;
            Fixture.Owner.CompleteTypedDispatch(Context, before);
        }
        internal void Finish()
        {
            Fixture.Owner.CompleteTyped(Context, Parent, NativeOperationExitFlags.Returned); Context.Dispose();
            Fixture.End(Execute); var after = ExecuteCall; after.Stage = NativeTweenSourceStage.After;
            after.Flags |= NativeTweenSourceFlags.Paired; Fixture.Publish(after);
            Fixture.Clear(Update, 0); Fixture.EndUpdate(Update, new long[4], 0);
        }
        public void Dispose() { }
    }

    private static TweenCompletionFixture Fixture() => new(typed: true, playing: new long[] { 101, 0, 0, 0 }, working: 1);
    private static NativeTweenSingleDispatchSnapshot Dispatch(TweenCompletionFixture f, int index = 0)
        => f.Owner.ReadTypedExecutingLists().Single().Dispatches[index];
    private static void AddTwice(TweenCompletionFixture f)
    {
        for (var index = 0; index < 2; ++index)
        {
            var add = f.Begin(NativeTweenOperation.AddTweenToWaitingList);
            f.Backend.SetReady(index == 0 ? new long[] { 101, 0, 0, 0 } : new long[] { 101, 101, 0, 0 }, index + 1, index + 2);
            f.End(add);
        }
    }
    private static void DrainReady(TweenCompletionFixture f)
    {
        var update = f.Begin(NativeTweenOperation.Update);
        for (var index = 1; index >= 0; --index)
        {
            var version = 4 - index;
            var pop = f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: index, count: index + 1, version: version);
            f.Pair(pop, count: index, version: version + 1, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
            f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertFront, 303, 303, destination: 0, count: 4, version: 0,
                occurrence: pop.Transaction));
            f.Clear(update, 0);
        }
        f.Backend.SetReady(new long[4], 0, 5); f.EndUpdate(update, new long[4], 0);
    }
    private static void AllOccurrences()
    {
        using var f = Fixture(); using var invocation = new Invocation(f); var before = invocation.Before();
        var play = f.Begin(NativeTweenOperation.Play); AddTwice(f); f.End(play); invocation.After(before);
        var transfer = Dispatch(f).Transfer; Check(transfer is not null && transfer.Occurrences.Count == 3);
        Check(f.Owner.ReadSourceReplay().Occurrences.Count == 4 && transfer!.Occurrences.Distinct().Count() == 3);
        invocation.Finish(); Check(f.Owner.ReadTypedEffects().Single().Pending.Length == 0);
        f.ConfirmDeaths(); Check(!f.Borrow() && !f.Owner.Unknown);
        Check(f.Owner.ReadWorkCompletion().Pending.Any(text => text.Contains("Occurrence")));
        DrainReady(f); Check(f.Borrow() && f.Owner.ReadWorkCompletion().Pending.Count == 0);
        Check(f.Backend.Frees == 0 && f.Backend.Native.Unregisters == 0);
    }
    private static void InstantTransfer()
    {
        using var f = Fixture(); using var invocation = new Invocation(f); var before = invocation.Before();
        var play = f.Begin(NativeTweenOperation.Play); f.End(play); invocation.After(before);
        Check(Dispatch(f).Transfer is { Occurrences.Count: 1 } && f.Owner.ReadTypedEffects().Single().Pending.Length != 0);
        invocation.Finish(); f.ConfirmDeaths(); Check(f.Borrow());
        Check(f.Owner.ReadSourceReplay().Occurrences.Count(row => row.Instant) == 1
            && f.Owner.ReadSourceReplay().Operations.Where(row => row.Operation == NativeTweenOperation.Play).All(row => row.Committed)
            && f.Backend.Frees == 0);
    }
    private static void UnknownAndMissing()
    {
        foreach (var unknown in new[] { false, true })
        {
            using var f = Fixture(); using var invocation = new Invocation(f);
            if (unknown) f.Backend.PlayConsumer = null;
            var before = invocation.Before();
            if (unknown) { var play = f.Begin(NativeTweenOperation.Play); f.End(play); }
            invocation.After(before); invocation.Finish(); f.ConfirmDeaths();
            Check(Dispatch(f).Transfer is null && !f.Borrow() && !f.Owner.Unknown);
        }
    }
    private static void ChangedConsumer()
    {
        foreach (var mode in new[] { "target", "method", "action", "multicast" })
        {
            using var f = Fixture(); using var invocation = new Invocation(f); var before = invocation.Before();
            var play = f.Begin(NativeTweenOperation.Play); f.End(play);
            f.Backend.PlayConsumer = mode switch {
                "target" => f.Backend.PlayConsumer! with { Tween = 999 },
                "method" => f.Backend.PlayConsumer! with { Method = 999 },
                "action" => f.Backend.PlayConsumer! with { Action = 999 }, _ => null };
            invocation.After(before); invocation.Finish(); f.ConfirmDeaths();
            Check(Dispatch(f).Transfer is null && !f.Borrow() && !f.Owner.Unknown);
        }
    }
    private static void IncompleteSubtree()
    {
        foreach (var mode in new[] { "sibling", "skipped", "undisposed" })
        {
            using var f = Fixture(); using var invocation = new Invocation(f); var before = invocation.Before();
            var play = f.Begin(NativeTweenOperation.Play);
            if (mode == "undisposed")
            {
                f.Owner.CompleteOperation(play, play.Frame, NativeOperationExitFlags.Returned);
                Throws<InvalidOperationException>(() => invocation.After(before));
                Check(Dispatch(f).Transfer is null); play.Dispose();
                invocation.After(before);
            }
            else
            {
                f.End(play, mode == "skipped" ? NativeOperationExitFlags.Skipped : NativeOperationExitFlags.Returned);
                if (mode == "sibling") { var sibling = f.Begin(NativeTweenOperation.Play); f.End(sibling); }
                invocation.After(before);
            }
            invocation.Finish(); f.ConfirmDeaths();
            if (mode == "undisposed") Check(Dispatch(f).Transfer is not null && f.Borrow());
            else Check(Dispatch(f).Transfer is null && !f.Borrow());
        }
    }
    private static void AbnormalAndGap()
    {
        using (var f = Fixture())
        {
            using var invocation = new Invocation(f); var before = invocation.Before();
            var play = f.Begin(NativeTweenOperation.Play); f.End(play); invocation.After(before, abnormal: true);
            Check(Dispatch(f).Transfer is null && f.Owner.ReadTypedEffects().Single().Pending.Length != 0 && f.Backend.Frees == 0);
        }
        using (var f = Fixture())
        {
            using var invocation = new Invocation(f); var before = invocation.Before();
            var play = f.Begin(NativeTweenOperation.Play); f.End(play); f.RefuseJournalReads();
            Throws<InvalidOperationException>(() => invocation.After(before));
            Check(f.Owner.Unknown && Dispatch(f).Transfer is null && f.Backend.Frees == 0);
        }
        using (var f = Fixture())
        {
            using var invocation = new Invocation(f); var before = invocation.Before();
            var play = f.Begin(NativeTweenOperation.Play); f.End(play); f.BreakNextSourceSequence(invocation.Execute);
            Throws<UnsupportedSignatureException>(() => invocation.After(before));
            Check(f.Owner.Unknown && Dispatch(f).Transfer is null && f.Backend.Frees == 0);
        }
    }
    private static void LaterChild()
    {
        using var f = Fixture(); using var invocation = new Invocation(f, listeners: 2); var first = invocation.Before();
        var play = f.Begin(NativeTweenOperation.Play); f.End(play); invocation.After(first);
        var original = Dispatch(f).Transfer!; Check(original is not null);
        f.Backend.PlayConsumer = null; var second = invocation.Before(1);
        play = f.Begin(NativeTweenOperation.Play); f.End(play); invocation.After(second); invocation.Finish(); f.ConfirmDeaths();
        Check(Dispatch(f).Transfer!.Occurrences.SequenceEqual(original!.Occurrences) && Dispatch(f, 1).Transfer is null);
        Check(f.Owner.ReadSourceReplay().Occurrences.Count(row => row.Instant) == 2 && !f.Borrow());
    }
    private static void NewWorkInvalidatesLoan()
    {
        using var f = Fixture(); using var invocation = new Invocation(f); var before = invocation.Before();
        var play = f.Begin(NativeTweenOperation.Play); f.End(play); invocation.After(before); invocation.Finish(); f.ConfirmDeaths();
        Check(f.Owner.TryBorrowControlledCompletionIntervalForTests(interval => {
            var later = f.Begin(NativeTweenOperation.Play); f.End(later);
            Throws<InvalidOperationException>(() => interval.ValidateCurrent(f.Owner, null, null));
        }));
        Check(f.Owner.ReadSourceReplay().Occurrences.Count(row => row.Instant) == 2 && Dispatch(f).Transfer!.Occurrences.Count == 1);
    }
}

internal sealed partial class TweenCompletionFixture
{
    internal void BreakNextSourceSequence(NativeLocalTweenWorkOwner.OperationContext context)
    { _journal.Add(Before(context, NativeTweenSourceSite.ExecuteOnComplete, 106)); _journal.BreakNextSequence = true; }

    internal sealed partial class CompletionBackend : INativeTweenTypedListenerEffectBackend
    {
        internal NativeTweenTypedPlayConsumer? PlayConsumer;
        private ulong _typedSequence = 800;
        NativeTweenTypedPlayConsumer? INativeTweenTypedListenerEffectBackend.ReadTypedPlayConsumer(
            NativeLocalTweenWorkOwner owner, IDisposable context, NativeTweenSingleEventFrame frame, NativeSingleDispatchTarget target)
        {
            owner.RequireTypedListenerEffectRead(this, context, frame);
            if (NativePlayReader is { } reader) return reader(owner, context, frame, target);
            return PlayConsumer is { } consumer && consumer.Invokable == target.Invokable ? consumer : null;
        }
        internal (IDisposable Context, NativeOperationFrame Frame) BeginTypedListenerInvocation()
        {
            var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1,
                Self = new(107), Method = new(900), Caller = new(0x180397f8a), Sequence = ++_typedSequence, Arg0 = new(0x3f800000) };
            return (_capture!(frame)!, frame);
        }
    }
}
