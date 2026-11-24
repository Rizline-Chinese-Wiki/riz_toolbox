using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldStateChecks
{
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        internal readonly FakeNative.HoldStateStorage Baseline;
        private readonly List<NativeSeekHoldStateParticipant> _participants = new();
        private readonly HashSet<NativeSeekHoldStateParticipant> _unknownFree = new(), _unknownState = new();
        private readonly Publications _publications = Publications.Read();
        internal Fixture(FakeNative.NoteViewShape shape = FakeNative.NoteViewShape.Complete,
            FakeNative.HoldStateAlias alias = FakeNative.HoldStateAlias.None, Action<FakeNative>? configure = null)
        {
            Native.ConfigureHoldState(shape, alias); configure?.Invoke(Native); Native.HoldStateRememberOther(); Baseline = Native.HoldStateSnapshot();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-explicit-hold-state", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual Hold RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5 && Native.NoteViewInitialStorageValid(shape),
                "Hold tests must use real NativeRuntime/backend RunValue with the complete original graph metadata.");
        }
        internal SeekHoldStateTargets Targets
        {
            get
            {
                var h = Native.HoldStateNotes.Select(x => x.ToInt64()).ToArray();
                return new(new[] { h[1], h[0], h[1], h[^1] }, new[] { h[0], h[0], h[^1] },
                    h.Select((reference, ordinal) => new SeekHoldNoteTarget(reference, ordinal % 2 != 0, 1.25f + ordinal)).Reverse().ToArray(),
                    new(-23.5f, -0.75f, 432.25f));
            }
        }
        internal NativeSeekHoldStateParticipant Prepare(SeekHoldStateTargets? targets = null, ISeekHoldNativeProfile? profile = null)
        {
            var p = Run.PrepareSeekHoldState(profile ?? Native.HoldStateProfile, targets ?? Targets); _participants.Add(p); return p;
        }
        internal void RetainState(NativeSeekHoldStateParticipant p, Exception? error = null)
        {
            if (!_unknownState.Add(p)) return;
            var failure = ExpectFailure(p.Dispose);
            Check(ReferenceEquals(failure.Data["SeekHoldStateCleanupOwner"], p) && p.HasRetainedResponsibility && Native.ActiveRoots > 0,
                "Unrecognized Hold dispatch/inverse failed to retain its exact participant and independent native roots after logical withdrawal.");
            UnknownOwners.Add((Native, p, error ?? failure));
        }
        internal void RetainFree(NativeSeekHoldStateParticipant p, Exception error)
        { _unknownFree.Add(p); UnknownOwners.Add((Native, p, error)); }
        internal void ClearCallbacks()
        {
            Native.BeforeHoldStateStore = Native.AfterHoldStateStore = null; Native.DuringHoldStateStore = null; Native.AfterHoldStateRoot = null;
            Native.FailHoldStateBefore = Native.FailHoldStateAfter = 0; Native.HoldStateProfile.OnVerify = null; Native.TargetProfile.OnVerify = null;
            Native.AfterTargetFieldRead = Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null;
        }
        public void Dispose()
        {
            ClearCallbacks();
            foreach (var p in _participants.AsEnumerable().Reverse())
            {
                if (_unknownFree.Contains(p) || _unknownState.Contains(p))
                {
                    var attempts = Native.FreeHandleAttempts; var failure = ExpectFailure(p.Dispose);
                    Check(ReferenceEquals(failure.Data["SeekHoldStateCleanupOwner"], p) && attempts == Native.FreeHandleAttempts,
                        "Unknown Hold responsibility lost its exact participant owner or retried a native free.");
                }
                else p.Dispose();
            }
            Run.Dispose(); Backend.Dispose();
            Check((_unknownState.Count == 0 ? Native.ActiveRoots == 0 : Native.ActiveRoots > 0)
                && Native.NewRoots == Native.FreedRoots + Native.ActiveRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Hold roots leaked, were freed twice, or bypassed the audited official root surface.");
            Check(Native.HoldStateReceiversRooted && Native.HoldStateArgumentsRooted && Native.JudgePublicationRooted
                && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted,
                "A Hold source, List, array, actual note or native reference argument was unrooted.");
            Check(Native.HoldStateOtherUnchanged() && Native.HoldStateWriteViolations.Count == 0
                && Native.JudgeWriteViolations.Count == 0 && Native.NoteViewDispatchViolations.Count == 0
                && Native.JudgeQueueWrites.Count == 0 && Native.FlagWrites.Count == 0 && Native.FingerWrites.Count == 0
                && Native.CursorWriteLog.Count == 0 && Native.RendererCalls.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.GameplayPitchWrites == 0 && Native.TargetForbiddenUpdates == 0,
                "Hold state changed unrelated fields, queued renderer/consumer work or left the exact native surface.");
            Check(Publications.Read() == _publications, "Hold operations leaked native publication responsibility.");
            if (_unknownFree.Count == 0 && _unknownState.Count == 0) Native.Dispose();
        }
    }
    private static void Restored(Fixture f, NativeSeekHoldStateParticipant p)
    {
        p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && !p.SideEffectsUnknown && f.Native.HoldStateEquals(f.Baseline),
            "Hold restore failed independent complete backing/tail/size/version and bit-exact scalar comparison.");
    }
}
