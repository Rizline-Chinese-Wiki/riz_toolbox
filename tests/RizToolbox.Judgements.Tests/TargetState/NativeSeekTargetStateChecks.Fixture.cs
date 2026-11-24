using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static T Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object[] Items(object value) => ((System.Collections.IEnumerable)value).Cast<object?>().Where(item => item is not null).Select(item => item!).ToArray();
    private static readonly List<(FakeNative Native, object Owner, Exception Failure)> UnknownOwners = new();
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unconfirmed)
    {
        internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
            NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        internal readonly FakeNative.JudgeQueueStorage[] Baseline;
        private readonly List<IDisposable> _owners = new(); private readonly HashSet<IDisposable> _unknown = new();
        private readonly Publications _publications = Publications.Read();
        internal Fixture(FakeNative.NoteViewShape shape = FakeNative.NoteViewShape.Complete, Action<FakeNative>? configure = null)
        {
            Native.ConfigureTargetState(shape); configure?.Invoke(Native); Native.JudgeQueueRememberOtherStorage(); Baseline = Native.JudgeQueueSnapshot();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-target-state", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual target-state RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Target-state regression must use the retained official backend RunValue.");
        }
        internal T Own<T>(T owner) where T : IDisposable { _owners.Add(owner); return owner; }
        internal void Retain(IDisposable owner, Exception error) { _unknown.Add(owner); UnknownOwners.Add((Native, owner, error)); }
        internal void ClearCallbacks()
        {
            Native.TargetProfile.OnVerify = null; Native.AfterTargetFieldRead = null;
            Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = null; Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = null;
            Native.BeforeJudgeQueueStore = null; Native.AfterJudgeQueueStore = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.DuringSeekConversion = null;
        }
        public void Dispose()
        {
            ClearCallbacks();
            foreach (var owner in _owners.AsEnumerable().Reverse())
                if (_unknown.Contains(owner))
                {
                    var frees = Native.FreeHandleAttempts; var failure = Expect<AggregateException>(owner.Dispose);
                    Check(Native.FreeHandleAttempts == frees && ReferenceEquals(failure.Data["SeekTargetStateCleanupOwner"], owner),
                        "Unknown target-source cleanup retried a native free or discarded its exact public owner.");
                }
                else owner.Dispose();
            Run.Dispose(); Backend.Dispose();
            Check(Native.ActiveRoots == 0 && Native.NewRoots == Native.FreedRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Target capture or auto queues lost a source root or freed it twice.");
            Check(Native.JudgeQueueOtherStorageUnchanged() && Native.JudgeQueueReceiversRooted && Native.JudgeQueueArgumentsRooted
                && Native.JudgePublicationRooted && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted
                && Native.JudgeWriteViolations.Count == 0 && Native.FlagWrites.Count == 0 && Native.FingerWrites.Count == 0
                && Native.RendererCalls.Count == 0 && Native.GameplayCommandCalls == 0 && Native.TargetForbiddenUpdates == 0
                && Native.NoteViewDispatchViolations.Count == 0, "A target projection mutated unrelated source, replayed consumers or used unrooted receivers.");
            Check(Publications.Read() == _publications, "A target-source publication responsibility was lost or leaked.");
            if (_unknown.Count == 0) Native.Dispose();
        }
    }
    private static void Queues(SeekJudgeQueueTargets actual, IReadOnlyList<long> notes, IReadOnlyList<long> taps, IReadOnlyList<long> drags)
        => Check(actual.Notes.SequenceEqual(notes) && actual.TapWaiting.SequenceEqual(taps) && actual.DragWaiting.SequenceEqual(drags),
            "The literal expected ordered actual-reference queue differs from target projection.");
    private static void Apply(Fixture f, SeekJudgeQueueTargets expected)
    {
        var p = f.Own(f.Run.PrepareSeekJudgeQueues(f.Native.TargetProfile, expected)); p.VerifyPrepared(); p.Commit();
        Check(p.Confirm().Observed == true && f.Native.JudgeQueueActive()[0].SequenceEqual(expected.Notes)
            && f.Native.JudgeQueueActive()[1].SequenceEqual(expected.TapWaiting) && f.Native.JudgeQueueActive()[2].SequenceEqual(expected.DragWaiting),
            "Automatically projected target queues did not reach the actual official List.Clear/Add storage.");
        p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && f.Native.JudgeQueueStorageEquals(f.Baseline),
            "Auto-queue integration failed to restore exact original backing, every tail slot, size and signed version.");
    }
}
