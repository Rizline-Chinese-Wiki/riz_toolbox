using System.Diagnostics;
using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekJudgeQueuesChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Judge queues copy explicit ordered targets and preserve repeated occurrences", ExplicitTargets),
        ("Judge queues retain distinct runtime instances and independent caller lifetime", IndependentRuntimeNotes),
        ("Judge queues reject null foreign zero and oversized targets before writes", InvalidTargets),
        ("Judge queues write consistent same-list aliases once and reject contradictory targets", AliasedLists),
        ("Judge queues reject shared positive-capacity backing and allow shared empty backing", AliasedBackings),
        ("Judge queues grow official backing and restore original complete storage", CapacityGrowth),
        ("Judge queues reject unrecognized growth capacity and changed intermediate old arrays", ForeignGrowth),
        ("Judge queues reject a final grown Add externally aliased to another mutable List", GrowthAlias),
        ("Judge queues reject predicted doubling above the complete capacity bound", GrowthBound),
        ("Judge queues Clear empty lists without clearing nonzero capacity tails", EmptyQueues),
        ("Judge queues recover complete storage after partial native before and after faults", PartialWrites),
        ("Judge queues resolve restoration before and after native faults without retry", RestorationFaults),
        ("Judge queues leave unrecognized partial native and restore third states untouched", UnknownPartialStates),
        ("Judge queues preserve externally changed unattempted capacity storage", UnattemptedForeignStorage),
        ("Judge queues preserve capacity changed externally after restoration", RestoredForeignStorage),
        ("Judge queues final dispatch guard rejects revision changes during native rooting", FinalDispatchGuard),
        ("Judge queues reject participant and same-RunValue constructor reentry", Reentry),
        ("Judge queues Dispose callbacks close the owned view and defer writer roots", CallbackDispose),
        ("Judge queues owner-thread rejection precedes every synthetic export", WrongThread),
        ("Judge queues require ready-paused and unchanged source identities", SourceChanges),
        ("Judge queues reject invalid official closed-list metadata before writes", InvalidMetadata),
        ("Judge queues retain unknown native-free responsibility without retry", UnknownFree),
        ("Judge queues retain an unknown original-backing temporary free after guarded restore", UnknownBackingTemporaryFree)
    };
    private sealed record Responsibility(FakeNative Native, NativeSeekJudgeQueuesParticipant Participant, Exception Failure);
    private static readonly List<Responsibility> UnknownOwners = new();
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static object Field(object owner, string name)
        => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object[] Items(object value) => ((System.Collections.IEnumerable)value).Cast<object>().ToArray();
    private static bool InMethod(string method, string? type = null)
        => new StackTrace(false).GetFrames().Any(frame => frame.GetMethod() is { } member
            && member.Name == method && (type is null || member.DeclaringType?.Name == type));
    private static IReadOnlyList<long>[] Lists(SeekJudgeQueueTargets value) => new[] { value.Notes, value.TapWaiting, value.DragWaiting };
    private static bool ActiveEquals(FakeNative native, SeekJudgeQueueTargets targets)
        => native.JudgeQueueActive().Where((row, slot) => !row.SequenceEqual(Lists(targets)[slot])).Any() == false;
    private sealed record Calls(int Reads, int Invocations, int Resolutions, int Roots, int Writes)
    {
        internal static Calls Read(FakeNative n) => new(n.FieldReads, n.Invocations, n.NoteViewMethodResolutions,
            n.NewRoots, n.JudgeQueueWrites.Count);
    }
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unconfirmed)
    {
        internal static Publications Read() => new(NativePublicationCall.RetainedCallCount,
            NativePublicationCall.RetainedRootRecordCount, NativePublicationCall.RetainedArgumentRecordCount,
            NativePublicationCall.UnconfirmedPublicationCount);
    }
}
