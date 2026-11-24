using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekFingerIdsChecks
{
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend;
        internal readonly JudgementRunCapture Run;
        internal readonly FakeNative.FingerNoteState[] Baseline;
        private readonly List<NativeSeekFingerIdsParticipant> _participants = new();
        private readonly HashSet<NativeSeekFingerIdsParticipant> _unknown = new();
        private Publications _expectedPublications = Publications.Read();
        private bool _retainNative;
        private int _unknownActiveRoots;
        internal SeekFingerIdsTarget[] Targets => Native.FingerNotes.Select((note, index) => new SeekFingerIdsTarget(note.ToInt64(), index switch
        {
            0 => new[] { int.MinValue, 17, int.MinValue },
            1 => new[] { 42, int.MaxValue, 42, int.MinValue },
            2 => null, 3 => Array.Empty<int>(), 4 => new[] { 0 }, _ => new[] { int.MaxValue }
        })).ToArray();
        internal Fixture(FakeNative.NoteViewShape shape = FakeNative.NoteViewShape.Complete,
            FakeNative.FingerAlias alias = FakeNative.FingerAlias.None, Action<FakeNative>? configure = null)
        {
            Native.ConfigureFingerIds(shape, alias); configure?.Invoke(Native);
            Native.FingerRememberOtherStorage(); Baseline = Native.FingerSnapshot();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-finger-ids-only", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Finger ids must use the retained actual backend RunValue.");
        }
        internal NativeSeekFingerIdsParticipant Prepare(IReadOnlyList<SeekFingerIdsTarget>? targets = null, ISeekChartNativeProfile? profile = null)
        {
            var p = Run.PrepareSeekFingerIds(profile ?? Native.SeekProfile, targets ?? Targets); _participants.Add(p); return p;
        }
        internal void ClearCallbacks()
        {
            Native.BeforeFingerStore = null; Native.AfterFingerStore = null; Native.AfterFingerAllocation = null;
            Native.AfterFingerRoot = null; Native.AfterFingerMetadata = null; Native.AfterFingerArrayExport = null;
            Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = null; Native.AfterJudgeRoot = null; Native.AfterJudgeMethodResolution = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.OnObjectAllocation = null;
            Native.FailFingerBeforeStore = Native.FailFingerAfterStore = 0; Native.FailFingerAllocation = false;
            Native.FingerWrongBoxClass = Native.FingerWrongBoxValue = false;
            Native.CorruptArrayByteLength = Native.CorruptArrayObjectSize = false;
            Native.FingerHeaderOverride = Native.FingerLengthOverride = null;
        }
        internal void RetainUnknown(NativeSeekFingerIdsParticipant p, Exception error)
        { _unknown.Add(p); _retainNative = true; UnknownOwners.Add(new(Native, p, error)); }
        internal void RetainPublication(object owner, Exception error, int activeRoots = 0)
        {
            _retainNative = true; _unknownActiveRoots += activeRoots; _expectedPublications = Publications.Read();
            UnknownOwners.Add(new(Native, owner, error));
        }
        public void Dispose()
        {
            ClearCallbacks();
            foreach (var p in _participants.AsEnumerable().Reverse())
                if (_unknown.Contains(p))
                {
                    var frees = Native.FreeHandleAttempts; Expect<AggregateException>(p.Dispose);
                    Check(frees == Native.FreeHandleAttempts, "Unknown fingerIds accessor free was retried.");
                }
                else p.Dispose();
            Run.Dispose(); Backend.Dispose();
            Check(Native.ActiveRoots == _unknownActiveRoots && Native.NewRoots - Native.FreedRoots == _unknownActiveRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Finger ids leaked an unowned root or repeated a native free.");
            Check(Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted && Native.FingerReceiversRooted && Native.FingerArgumentsRooted,
                "A fingerIds receiver, pinned input or boxed argument was not independently rooted.");
            Check(Native.FingerOtherStorageUnchanged() && Native.JudgeQueueWrites.Count == 0 && Native.FlagWrites.Count == 0
                && Native.CursorWriteLog.Count == 0 && Native.NoteViewDispatchViolations.Count == 0 && Native.JudgeWriteViolations.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.GameplayPitchWrites == 0 && Native.SeekConversions == 0,
                "Finger ids changed a Judge queue, flag, unrelated field, cursor, renderer or clock.");
            Check(Publications.Read() == _expectedPublications, "Finger ids lost or leaked a bounded publication owner.");
            if (!_retainNative) Native.Dispose();
        }
    }
    private static void Restored(Fixture f, NativeSeekFingerIdsParticipant p)
    {
        p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && !p.SideEffectsUnknown && f.Native.FingerStorageEquals(f.Baseline),
            "Complete fingerIds restoration did not recover field references, original arrays, capacity tails, sizes and signed versions.");
    }
    private static void TryRestore(NativeSeekFingerIdsParticipant p)
    { try { p.Restore(); } catch (InvalidOperationException) { } }
}
