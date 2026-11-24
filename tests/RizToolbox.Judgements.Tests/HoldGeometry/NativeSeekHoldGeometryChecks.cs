using System.Diagnostics;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldGeometryChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Hold geometry preparation captures actual source without target-time closure or writes", ReadOnly),
        ("Hold geometry positions use nonidentity parent translation rotation and scale", ParentOracle),
        ("Hold geometry strict head and tail boundaries preserve native occurrence order", Boundaries),
        ("Hold geometry pending epsilon compares rounded float differences as double", Epsilon),
        ("Hold geometry rejects zero division nonfinite and overflowing target operations", ArithmeticRejections),
        ("Hold geometry keeps partial chart coverage and distinct actual Hold instances", ActualInstances),
        ("Hold geometry shared transforms preserve one actual identity and conditional commands", Aliases),
        ("Hold geometry rereads actual local coordinates after world-preserving reparent", ReparentReadback),
        ("Hold geometry native before after and callback partial failures retain immutable effects", NativeFaults),
        ("Hold geometry external source and parent changes deny later commands", SourceChanges),
        ("Hold geometry immutable journals retain independent complete snapshots", ImmutableJournal),
        ("Hold geometry exact Single array reader preserves NaN payloads and signed zero", SingleArrays),
        ("Hold geometry malformed Single arrays and changing second-pass slots fail before writes", InvalidArrays),
        ("Hold geometry malformed exact field and method metadata fail before dispatch", InvalidMetadata),
        ("Hold geometry same-RunValue and execution callback reentry reach no exports", Reentry),
        ("Hold geometry callback withdrawal closes logical access while retaining entered effects", CallbackDispose),
        ("Hold geometry worker and final managed guard rejection precede native dispatch", ThreadAndGuard),
        ("Hold geometry unknown root cleanup retains its exact public owner without retry", UnknownFree)
        ,("Hold geometry inverse restores original parent position rotation and scale through actual access", RestoreOriginalTransforms)
        ,("Hold geometry inverse refuses an unowned transform change before any inverse setter", RestoreRejectsForeignTransform)
        ,("Hold geometry acknowledged returned values release independent roots without erasing external effects", AcknowledgedOwnership)
        ,("Hold geometry revoked ownership keeps original roots and rejects copied acknowledgements", RevokedOwnership)
        ,("Hold geometry and actual Hold state compose and restore their distinct write sets", ComposedHoldState)
    };
    private static readonly List<(FakeNative Native, NativeSeekHoldGeometryCommands Owner, Exception Failure)> UnknownOwners = new();
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static T Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static Exception ExpectFailure(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected rejected actual Hold geometry operation."); }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static bool Same(NativeVector3 first, NativeVector3 second)
        => Bits(first.X) == Bits(second.X) && Bits(first.Y) == Bits(second.Y) && Bits(first.Z) == Bits(second.Z);
    private static bool InMethod(string name) => new StackTrace(false).GetFrames().Any(frame => frame.GetMethod()?.Name == name);
    private sealed record Calls(int Reads, int Invokes, int Roots, int Commands)
    { internal static Calls Read(FakeNative n) => new(n.FieldReads, n.Invocations, n.NewRoots, n.GeometryCalls.Count); }
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unconfirmed)
    { internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
        NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount); }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        internal readonly FakeNative.HoldStateStorage HoldBaseline;
        internal readonly Dictionary<IntPtr, FakeNative.GeometryTrs> TransformBaseline;
        private readonly List<NativeSeekHoldGeometryCommands> _commands = new();
        private readonly HashSet<NativeSeekHoldGeometryCommands> _retained = new();
        private readonly Publications _publications = Publications.Read();
        internal bool ExpectHoldStateWrites;
        internal Fixture(FakeNative.NoteViewShape shape = FakeNative.NoteViewShape.Complete, Action<FakeNative>? configure = null)
        {
            Native.ConfigureHoldGeometry(shape); configure?.Invoke(Native); Native.GeometryRememberOther();
            HoldBaseline = Native.HoldStateSnapshot(); TransformBaseline = new(Native.GeometryTransforms);
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-hold-geometry", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No real geometry RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5 && Native.NoteViewInitialStorageValid(shape),
                "Hold geometry checks must use the actual independently retained backend RunValue and its complete original graph.");
        }
        internal NativeSeekHoldGeometryCommands Prepare(SeekHoldGeometryRequest? request = null)
        { var p = Run.PrepareSeekHoldGeometry(Native.GeometryProfile, request ?? new(3, 2)); _commands.Add(p); return p; }
        internal void ClearCallbacks()
        {
            Native.BeforeGeometryCall = Native.AfterGeometryStore = Native.AfterGeometryCall = null;
            Native.AfterGeometryGetter = Native.AfterGeometryArrayExport = null; Native.AfterGeometryRoot = null;
            Native.GeometryProfile.OnVerify = Native.HoldStateProfile.OnVerify = Native.TargetProfile.OnVerify = null;
            Native.AfterTargetFieldRead = Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = Native.AfterHoldStateRoot = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.FailGeometryBefore = Native.FailGeometryAfter = 0;
        }
        internal void Retain(NativeSeekHoldGeometryCommands p, Exception? cause = null)
        {
            if (!_retained.Add(p)) return;
            if (!_commands.Contains(p)) _commands.Add(p);
            var failure = ExpectFailure(p.Dispose);
            Check(ReferenceEquals(failure.Data["SeekHoldGeometryEffectsOwner"], p) && p.HasRetainedResponsibility && Native.ActiveRoots > 0,
                "Entered Hold geometry effects lost their exact public owner or physical roots after logical disposal.");
            UnknownOwners.Add((Native, p, cause ?? failure));
        }
        public void Dispose()
        {
            ClearCallbacks();
            foreach (var p in _commands.AsEnumerable().Reverse())
                if (_retained.Contains(p))
                {
                    var attempts = Native.FreeHandleAttempts; var failure = ExpectFailure(p.Dispose);
                    Check(attempts == Native.FreeHandleAttempts && ReferenceEquals(failure.Data["SeekHoldGeometryEffectsOwner"], p),
                        "Unknown geometry owner retried native cleanup or discarded its exact public responsibility.");
                }
                else p.Dispose();
            Run.Dispose(); Backend.Dispose();
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts
                && (_retained.Count != 0 || Native.ActiveRoots == 0), "Hold geometry root ownership was lost or a native free was retried.");
            Check(Native.GeometryOtherUnchanged() && Native.HoldStateEquals(HoldBaseline) && Native.GeometryReceiversRooted
                && Native.GeometryPublishedBeforeInspection && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted
                && (ExpectHoldStateWrites || Native.HoldStateWrites.Count == 0) && Native.FlagWrites.Count == 0 && Native.JudgeQueueWrites.Count == 0
                && Native.FingerWrites.Count == 0 && Native.RendererCalls.Count == 0 && Native.CursorWriteLog.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.TargetForbiddenUpdates == 0 && Native.NoteViewDispatchViolations.Count == 0,
                "Hold geometry changed unrelated source/list/scalar storage, called a native consumer or inspected an unrooted publication.");
            Check(Publications.Read() == _publications, "Hold geometry leaked an unaccounted native publication owner.");
            if (_retained.Count == 0) Native.Dispose();
        }
    }
}
