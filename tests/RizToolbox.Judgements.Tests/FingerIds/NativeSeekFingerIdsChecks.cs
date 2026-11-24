using System.Diagnostics;
using System.Reflection;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekFingerIdsChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Finger ids copy complete explicit signed targets and preserve repeated values", ExplicitTargets),
        ("Finger ids distinguish original null empty and independent scratch references", NullAndEmpty),
        ("Finger ids reject missing extra duplicate zero and oversized target identities", InvalidTargets),
        ("Finger ids retain actual partial coverage and distinct runtime instances", ActualRuntimeNotes),
        ("Finger ids mutate consistent same-list aliases once and reject conflicting targets", AliasedLists),
        ("Finger ids reject shared positive backing and permit shared empty arrays", AliasedBackings),
        ("Finger ids recognize both complete Int32 Clear policies without changing tails", ClearPolicies),
        ("Finger ids grow official lists and reject out-of-bound predicted growth before allocation", GrowthAndBound),
        ("Finger ids retain constructor responsibility through allocation and native faults", ConstructorFailures),
        ("Finger ids constructor publication cancellation never publishes a live note field", PublicationCancellation),
        ("Finger ids recover recognized native Clear Add and field before-after failures", NativeFaults),
        ("Finger ids restore array descriptors and original note fields without blind retry", RestorationFaults),
        ("Finger ids preserve foreign unattempted and already restored storage", ExternalStorage),
        ("Finger ids refuse unrecognized partial writes and third-reference restoration", UnknownStates),
        ("Finger ids reject foreign growth capacity intermediate arrays and positive aliases", ForeignGrowth),
        ("Finger ids final managed guard and ready-paused source precede live writes", GuardAndSource),
        ("Finger ids reject participant and same-RunValue constructor reentry", Reentry),
        ("Finger ids Dispose callbacks close the owned view and defer receiver roots", CallbackDispose),
        ("Finger ids reject worker access before every synthetic export", WrongThread),
        ("Finger ids preserve unknown accessor free responsibility without retry", UnknownFree),
        ("Finger ids runtime reads pinned complete signed capacity without boxed getters", RuntimeRead),
        ("Finger ids runtime rejects inconsistent array metadata header and second samples", RuntimeRejects),
        ("Finger ids runtime uses exact boxed Int32 SetValue and final dispatch guard", RuntimeSet),
        ("Finger ids runtime retains unknown pinned and boxed temporary frees", RuntimeUnknownFrees),
        ("Finger ids runtime thread and publication-capacity rejection adopts no inputs", RuntimeAdmission)
    };
    private sealed record Responsibility(FakeNative Native, object Owner, Exception Failure);
    private static readonly List<Responsibility> UnknownOwners = new();
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static object Field(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object[] Items(object value) => ((System.Collections.IEnumerable)value).Cast<object>().ToArray();
    private static object[] PublicationOwners() => Items(typeof(NativePublicationCall)
        .GetField("ProcessOwners", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
    private static bool InMethod(string method, string? type = null) => new StackTrace(false).GetFrames()
        .Any(frame => frame.GetMethod() is { } member && member.Name == method && (type is null || member.DeclaringType?.Name == type));
    private static bool ActiveEquals(FakeNative native, IReadOnlyList<SeekFingerIdsTarget> targets)
    {
        var byReference = targets.ToDictionary(row => row.Reference);
        return native.FingerSnapshot().All(row => byReference[row.Reference.ToInt64()].FingerIds is { } desired
            ? row.Storage is { } list && list.Size == desired.Count && list.Capacity.Take(list.Size).SequenceEqual(desired)
            : row.List == IntPtr.Zero);
    }
    private sealed record Calls(int Reads, int Invocations, int Roots, int Writes, int Allocations)
    { internal static Calls Read(FakeNative native) => new(native.FieldReads, native.Invocations, native.NewRoots, native.FingerWrites.Count, native.ObjectAllocations); }
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unconfirmed)
    { internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
        NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount); }
}
