using System.Diagnostics;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldStateChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Hold state copies explicit ordered lists and all actual Hold values", ExplicitTargets),
        ("Hold state actual type-two subset preserves partial coverage and multiple instances", ActualHolds),
        ("Hold state rejects foreign non-Hold missing extra and duplicate identities", InvalidIdentities),
        ("Hold state rejects null oversized and nonfinite explicit targets", InvalidTargets),
        ("Hold state same actual List requires equal occurrence targets and writes once", AliasedLists),
        ("Hold state rejects nonempty shared backing and allows empty shared backing", AliasedBackings),
        ("Hold state official Add grows zero-to-four and doubles complete capacity", CapacityGrowth),
        ("Hold state restores all tail slots backing identities size and signed versions", CompleteStorage),
        ("Hold state baseline NaN payloads and signed zero restore bit exactly", FloatBits),
        ("Hold state empty targets still Clear without changing inactive tails", EmptyLists),
        ("Hold state recovers every native before and after forward fault", ForwardFaults),
        ("Hold state restoration before and after faults keep once-only inverse responsibility", RestoreFaults),
        ("Hold state internal partial List operations retain unknown inverse ownership", InternalPartial),
        ("Hold state external changes deny all restoration writes", ExternalChanges),
        ("Hold state changed grown and intermediate backing storage stays untouched", ForeignGrowth),
        ("Hold state requires ready-paused unchanged actual source and managed revision", SourceChanges),
        ("Hold state malformed exact field and closed List metadata fail before writes", InvalidMetadata),
        ("Hold state same-owner construction and participant callback reentry are denied", Reentry),
        ("Hold state callback withdrawal stops later exports and defers active roots", CallbackDispose),
        ("Hold state final dispatch rechecks managed lifetime after marshalling roots", FinalDispatchGuard),
        ("Hold state worker rejection precedes every native export", WrongThread),
        ("Hold state unknown native-free owner remains retained without retry", UnknownFree)
    };
    private static readonly List<(FakeNative Native, NativeSeekHoldStateParticipant Owner, Exception? Failure)> UnknownOwners = new();
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static T Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static Exception ExpectFailure(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected rejected Hold state operation."); }
    private static void TryRestore(NativeSeekHoldStateParticipant participant)
    { try { participant.Restore(); } catch (Exception) { } }
    private static bool RestorationDenied(NativeSeekHoldStateParticipant participant)
    { try { return !participant.ConfirmRestored().OriginalStateRestored; } catch (Exception) { return participant.SideEffectsUnknown; } }
    private static bool InMethod(string method, string? type = null)
        => new StackTrace(false).GetFrames().Any(frame => frame.GetMethod() is { } member && member.Name == method
            && (type is null || member.DeclaringType?.Name == type));
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static IReadOnlyList<long>[] Lists(SeekHoldStateTargets target) => new[] { target.HoldNotes, target.RunningHoldNotes };
    private sealed record Calls(int Reads, int Invokes, int Resolutions, int Roots, int Writes)
    {
        internal static Calls Read(FakeNative n) => new(n.FieldReads, n.Invocations, n.NoteViewMethodResolutions, n.NewRoots, n.HoldStateWrites.Count);
    }
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unconfirmed)
    {
        internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
            NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount);
    }
}
