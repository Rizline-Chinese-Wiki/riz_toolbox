using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasCacheChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Canvas caches prepare real complete source and finite requested targets without stores", Preparation),
        ("Canvas caches store every physical slot in actual order and restore exact original bits", AllSlots),
        ("Canvas caches preserve cross-kind equal aliases once and reject conflicts before all stores", Aliases),
        ("Canvas caches keep both scalar targets for empty complete arrays and reject nonfinite requests", EmptyAndInvalid),
        ("Canvas caches preserve original NaN payloads and signed zero through actual native restoration", RawBits),
        ("Canvas caches reject exact field and complete array ABI bounds before a native store", FieldAndArrayAbi),
        ("Canvas caches reject warmed field Single and array metadata drift without retargeting", WarmMetadata),
        ("Canvas caches verify retained reference identities and complete actual source changes", ReferencesAndSource),
        ("Canvas caches retain scalar before and after partial native dispatch and conditionally restore", PartialScalars),
        ("Canvas caches stop before stores on pin withdrawal and preserve poststore pending ownership", PinAndPoststoreWithdrawal),
        ("Canvas caches stop exports after field array sample and scalar callback withdrawal", CallbackWithdrawal),
        ("Canvas caches retain independent roots after RunValue exit and isolate later cancelled construction", IndependentLifetime),
        ("Canvas caches never overwrite third states or externally changed unattempted scalar slots", ThirdStates),
        ("Canvas caches reject worker and synchronous reentry before native exports", ThreadAndReentry),
        ("Canvas caches revoke stale confirmation preserve immutable journals and keep position engine ownership", ConfirmationAndJournals),
        ("Canvas caches preserve exact unknown temporary and terminal cleanup owners without retry", UnknownCleanup)
    };
    private static readonly List<(FakeNative Native, object Owner, Exception? Failure)> RetainedOwners = new();
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected actual Canvas cache rejection."); }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static bool DoesNotConfirm(Func<bool> action) { try { return !action(); } catch { return true; } }
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unknown)
    {
        internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
            NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount);
    }
    private sealed record Calls(int Reads, int Invokes, int Roots, int Frees, int Writes, int Observed)
    {
        internal static Calls Read(FakeNative n) => new(n.FieldReads, n.Invocations, n.NewRoots, n.FreeHandleAttempts,
            n.FieldWrites, n.CanvasCacheObservedStores.Count);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        private readonly List<NativeSeekCanvasCacheParticipant> _participants = new();
        private readonly HashSet<NativeSeekCanvasCacheParticipant> _retained = new();
        private readonly List<NativeSeekCanvasPositionCommands> _positions = new();
        private readonly Dictionary<IntPtr, FakeNative.GeometryTrs> _transforms;
        private readonly bool _allowPositions;
        private Publications _expectedPublications = Publications.Read();
        internal Fixture(Action<FakeNative>? configure = null, bool positions = false)
        {
            Native.ConfigureCanvasCaches(positions); configure?.Invoke(Native); Native.CanvasSourceRemember();
            _transforms = new(Native.GeometryTransforms); _allowPositions = positions;
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-canvas-caches", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No real Canvas cache RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Canvas cache checks must enter the real backend RunValue.");
        }
        internal NativeSeekCanvasCacheParticipant Prepare(float time = 5, float speed = 3, ISeekCanvasNativeProfile? profile = null)
        {
            try
            {
                var value = Run.PrepareSeekCanvasCaches(profile ?? Native.CanvasSourceProfile, time, speed);
                _participants.Add(value); return value;
            }
            catch (Exception error)
            {
                if (error.Data["SeekCanvasCacheCleanupOwner"] is NativeSeekCanvasCacheParticipant owner) Retain(owner, error);
                throw;
            }
        }
        internal void Retain(NativeSeekCanvasCacheParticipant owner, Exception? failure = null)
        {
            if (!_participants.Contains(owner)) _participants.Add(owner);
            if (_retained.Add(owner)) RetainedOwners.Add((Native, owner, failure));
        }
        internal void RetainPosition(NativeSeekCanvasPositionCommands owner)
        { _positions.Add(owner); RetainedOwners.Add((Native, owner, null)); }
        internal void RetainPublication(NativeSeekCanvasCacheParticipant owner, Exception failure)
        {
            var after = Publications.Read();
            Check(after.Calls == _expectedPublications.Calls + 1 && after.Roots == _expectedPublications.Roots + 1
                && after.Arguments == _expectedPublications.Arguments && after.Unknown == _expectedPublications.Unknown,
                "Unknown cache pin cleanup did not preserve exactly one original publication/root record.");
            _expectedPublications = after; Retain(owner, failure);
        }
        internal void Clear()
        {
            Native.CanvasSourceProfile.OnVerify = null;
            if (Native.CanvasPositionProfile is not null) Native.CanvasPositionProfile.OnVerify = null;
            Native.BeforeCanvasCacheScalar = Native.AfterCanvasCacheScalar = null; Native.AfterCanvasCacheObservedStore = null;
            Native.FailCanvasCacheScalarBefore = Native.FailCanvasCacheScalarAfter = 0;
            Native.BeforeGeometryCall = Native.AfterGeometryStore = Native.AfterGeometryCall = null;
            Native.AfterGeometryGetter = Native.AfterGeometryArrayExport = null; Native.AfterGeometryRoot = null;
            Native.AfterCanvasSourceRead = null; Native.AfterCanvasSourceRange = null; Native.AfterCanvasSourceRoot = null;
            Native.AfterTargetFieldRead = Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = Native.AfterHoldStateRoot = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.CanvasSourceAuditExports = false;
        }
        internal void UnrelatedState()
        {
            var canvases = Native.CanvasPositionTransforms.ToHashSet();
            Check(Native.CanvasCacheUnrelatedUnchanged() && Native.CanvasSourceForbidden == 0
                && Native.FlagWrites.Count == 0 && Native.JudgeQueueWrites.Count == 0 && Native.FingerWrites.Count == 0
                && Native.RendererCalls.Count == 0 && Native.HoldStateWrites.Count == 0 && Native.CursorWriteLog.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.TargetForbiddenUpdates == 0
                && Native.GeometryCalls.All(call => _allowPositions && call.Operation == "set_localPosition" && canvases.Contains(call.Receiver))
                && _transforms.All(row => Native.GeometryTransforms[row.Key].Parent == row.Value.Parent
                    && Native.GeometryTransforms[row.Key].Rotation == row.Value.Rotation
                    && Native.GeometryTransforms[row.Key].Scale == row.Value.Scale
                    && (_allowPositions && canvases.Contains(row.Key) || Native.GeometryTransforms[row.Key].Position == row.Value.Position)),
                "Cache storage touched unrelated native fields, Transform state, or an initialization/consumer method.");
            Check(Native.CanvasCacheStoresPinned && Native.CanvasCacheScalarsRooted && Native.CanvasSourcePublished
                && Native.GeometryPublishedBeforeInspection && Native.JudgePublicationRooted
                && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted && Native.NoteViewDispatchViolations.Count == 0,
                "Canvas caches inspected/stored a publication without its independent source or pinned array responsibility.");
        }
        public void Dispose()
        {
            Clear();
            foreach (var p in _participants.AsEnumerable().Reverse())
            {
                if (p.HasRetainedResponsibility && !_retained.Contains(p)) Retain(p);
                if (_retained.Contains(p))
                {
                    var attempts = Native.FreeHandleAttempts; var failure = Reject(p.Dispose);
                    Check(ReferenceEquals(failure.Data["SeekCanvasCacheCleanupOwner"], p) && p.HasRetainedResponsibility
                        && Native.FreeHandleAttempts == attempts && !p.GetJournal().CompleteSeekPermitted,
                        "Retained cache disposal retried unknown cleanup, lost its exact owner, or granted full Seek.");
                }
                else p.Dispose();
            }
            foreach (var position in _positions)
            {
                var attempts = Native.FreeHandleAttempts; var error = Reject(position.Dispose);
                Check(ReferenceEquals(error.Data["SeekCanvasPositionEffectsOwner"], position) && Native.FreeHandleAttempts == attempts,
                    "Cache fixture discarded or retried another position command's entered engine responsibility.");
            }
            Run.Dispose(); Backend.Dispose(); UnrelatedState();
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Canvas cache roots were lost or any native free was retried.");
            Check(Publications.Read() == _expectedPublications, "Canvas cache operation leaked an unaccounted temporary publication responsibility.");
            if (_retained.Count == 0 && _positions.Count == 0)
            {
                Check(Native.ActiveRoots == 0, "Known cache cleanup leaked independent source roots: " + Native.CanvasSourceActiveRootDiagnostics());
                Native.Dispose();
            }
        }
    }
    private static void Revoke(Fixture fixture, string kind)
    {
        if (kind == "revision") fixture.State.ObservePause(true);
        else if (kind == "withdrawal") fixture.State.ControllerUnavailable();
        else if (kind == "backend") fixture.Backend.Dispose();
        else fixture.Run.Dispose();
    }
    private static void NoNative(FakeNative native, Action action)
    {
        var before = Calls.Read(native); var lookups = native.RequestedExports.Count;
        native.BeginLowerIl2CppAudit();
        try { action(); } finally { native.EndLowerIl2CppAudit(); }
        Check(native.LowerIl2CppCalls == 0 && native.LowerWorkerIl2CppCalls == 0 && Calls.Read(native) == before
            && native.RequestedExports.Count == lookups, "Rejected cache operation reached native/lazy exports or changed responsibility.");
    }
    private static void OnlyCleanup(FakeNative native, string reason)
        => Check(native.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"), reason);
}
