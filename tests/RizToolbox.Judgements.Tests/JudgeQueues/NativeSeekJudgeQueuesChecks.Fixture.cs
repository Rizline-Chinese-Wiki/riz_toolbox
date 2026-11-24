using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekJudgeQueuesChecks
{
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend;
        internal readonly JudgementRunCapture Run;
        internal readonly FakeNative.JudgeQueueStorage[] Baseline;
        internal SeekJudgeQueueTargets Targets
        {
            get
            {
                var r = Native.JudgeRuntimeReferences;
                return new(new[] { r[1], r[0], r[1], r[^1] }, new[] { r[2], r[2], r[0] }, Array.Empty<long>());
            }
        }
        private readonly List<NativeSeekJudgeQueuesParticipant> _participants = new();
        private readonly HashSet<NativeSeekJudgeQueuesParticipant> _unknown = new();
        private readonly Publications _publications = Publications.Read();
        private bool _unknownPublication;
        internal Fixture(FakeNative.NoteViewShape shape = FakeNative.NoteViewShape.Complete,
            FakeNative.JudgeAlias alias = FakeNative.JudgeAlias.None, Action<FakeNative>? configure = null)
        {
            Native.ConfigureJudgeQueues(shape, alias); configure?.Invoke(Native);
            Native.JudgeQueueRememberOtherStorage(); Baseline = Native.JudgeQueueSnapshot();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-judge-queues-only", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State);
            Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual RunValue was captured.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5,
                "Judge queue checks must use the actual backend retained RunValue.");
        }
        internal NativeSeekJudgeQueuesParticipant Prepare(SeekJudgeQueueTargets? targets = null,
            ISeekChartNativeProfile? profile = null)
        {
            var value = Run.PrepareSeekJudgeQueues(profile ?? Native.SeekProfile, targets ?? Targets);
            _participants.Add(value); return value;
        }
        internal void ClearCallbacks()
        {
            Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = null; Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = null;
            Native.BeforeJudgeQueueStore = null; Native.AfterJudgeQueueStore = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null;
            Native.FailJudgeQueueBeforeStore = Native.FailJudgeQueueAfterStore = 0;
        }
        internal void RetainUnknown(NativeSeekJudgeQueuesParticipant participant, Exception error)
        { _unknown.Add(participant); UnknownOwners.Add(new(Native, participant, error)); }
        internal void RetainPublicationUnknown(NativeSeekJudgeQueuesParticipant participant, Exception error)
        { _unknownPublication = true; UnknownOwners.Add(new(Native, participant, error)); }
        public void Dispose()
        {
            ClearCallbacks();
            foreach (var participant in _participants.AsEnumerable().Reverse())
                if (_unknown.Contains(participant))
                {
                    var attempts = Native.FreeHandleAttempts; Expect<AggregateException>(participant.Dispose);
                    Check(attempts == Native.FreeHandleAttempts, "Judge queue cleanup retried an unknown native free.");
                }
                else participant.Dispose();
            Run.Dispose(); Backend.Dispose();
            Check(Native.ActiveRoots == 0 && Native.NewRoots == Native.FreedRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "A Judge queue native responsibility escaped cleanup or was freed twice.");
            Check(Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted && Native.JudgePublicationRooted
                && Native.JudgeQueueReceiversRooted && Native.JudgeQueueArgumentsRooted,
                "A Judge source, backing array, official writer receiver or reference argument was not retained.");
            Check(Native.JudgeWriteViolations.Count == 0 && Native.NoteViewDispatchViolations.Count == 0
                && Native.JudgeQueueOtherStorageUnchanged() && Native.JudgeHoldValue == int.MinValue
                && Native.CursorWriteLog.Count == 0 && Native.FlagWrites.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.GameplayPitchWrites == 0 && Native.SeekConversions == 0,
                "A Judge queue transaction changed unrelated storage, Hold count, flags, cursors, clocks or gameplay commands.");
            var expectedPublications = _unknownPublication ? _publications with
                { Calls = _publications.Calls + 1, Roots = _publications.Roots + 1 } : _publications;
            Check(Publications.Read() == expectedPublications, "A Judge queue operation lost or leaked publication ownership.");
            if (_unknown.Count == 0 && !_unknownPublication) Native.Dispose();
        }
    }
    private static void Restored(Fixture f, NativeSeekJudgeQueuesParticipant participant)
    {
        participant.Restore();
        Check(participant.ConfirmRestored().OriginalStateRestored && !participant.SideEffectsUnknown
            && f.Native.JudgeQueueStorageEquals(f.Baseline),
            "Restoration did not independently recover original list/backing/size/version and every capacity tail slot.");
    }
}
