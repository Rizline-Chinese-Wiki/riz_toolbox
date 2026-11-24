using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasPositionChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Canvas positions prepare the real complete source and explicit requested target without writes", Preparation),
        ("Canvas positions dispatch in actual Transform order and keep immutable complete journals", ActualOrder),
        ("Canvas positions preserve repeated slots write equal aliases once and reject conflicting aliases before writes", Aliases),
        ("Canvas positions accept complete empty vectors and reject nonfinite explicit requests", EmptyAndInvalid),
        ("Canvas positions reject malformed exact localPosition getter metadata before native entry", GetterMetadata),
        ("Canvas positions reject malformed exact localPosition setter metadata before native entry", SetterMetadata),
        ("Canvas positions reject warm Vector3 ABI and saved profile or MethodInfo drift", WarmMetadata),
        ("Canvas positions preserve signed zero and NaN payload baseline bits through conditional restoration", RawBits),
        ("Canvas positions retain native before after and exception partial dispatch responsibility", NativeFaults),
        ("Canvas positions never overwrite third states and retain sticky unrecognized state evidence", ThirdStates),
        ("Canvas positions reject changed actual source and revoked owner before another setter", SourceAndWithdrawal),
        ("Canvas positions retain independent roots across RunValue disposal and later cancelled construction", IndependentLifetime),
        ("Canvas positions stop exports after getter setter and native exception callback withdrawal", CallbackWithdrawal),
        ("Canvas positions reject worker and synchronous reentry before any native export", ThreadAndReentry),
        ("Canvas positions withdraw stale confirmation and retain engine responsibility after value restoration", ConfirmationAndEffects),
        ("Canvas positions preserve exact unknown cleanup owner and never retry any native free", UnknownCleanup)
    };
    private static readonly List<(FakeNative Native, NativeSeekCanvasPositionCommands Owner, Exception? Failure)> RetainedOwners = new();
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected actual Canvas position rejection."); }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static bool Same(NativeVector3 a, NativeVector3 b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z);
    private static void Vector(NativeVector3 a, NativeVector3 b, string reason) => Check(Same(a, b), reason);
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unknown)
    {
        internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
            NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount);
    }
    private sealed record Calls(int Reads, int Invokes, int Roots, int Frees, int Getters, int Setters)
    {
        internal static Calls Read(FakeNative n) => new(n.FieldReads, n.Invocations, n.NewRoots, n.FreeHandleAttempts,
            n.CanvasPositionGetterCalls.Count, n.GeometryCalls.Count);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        private readonly List<NativeSeekCanvasPositionCommands> _commands = new();
        private readonly HashSet<NativeSeekCanvasPositionCommands> _retained = new();
        private Publications _expectedPublications = Publications.Read();
        private readonly Dictionary<IntPtr, FakeNative.GeometryTrs> _transforms;
        private Exception? _lastFailure;
        internal Fixture(Action<FakeNative>? configure = null)
        {
            Native.ConfigureCanvasPositions(); configure?.Invoke(Native); Native.CanvasSourceRemember();
            _transforms = new(Native.GeometryTransforms);
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-canvas-positions", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No real Canvas position RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Canvas position tests must enter the real backend RunValue.");
        }
        internal NativeSeekCanvasPositionCommands Prepare(float time = 5, float speed = 3,
            ISeekCanvasNativeProfile? source = null, ISeekCanvasPositionNativeProfile? position = null)
        {
            try
            {
                var p = Run.PrepareSeekCanvasPositions(source ?? Native.CanvasSourceProfile, position ?? Native.CanvasPositionProfile, time, speed);
                _commands.Add(p); return p;
            }
            catch (Exception error)
            {
                _lastFailure = error;
                var owner = error.Data["SeekCanvasPositionEffectsOwner"] as NativeSeekCanvasPositionCommands
                    ?? error.Data["SeekCanvasPositionCleanupOwner"] as NativeSeekCanvasPositionCommands;
                if (owner is not null) Retain(owner, error);
                throw;
            }
        }
        internal void Execute(NativeSeekCanvasPositionCommands p)
        {
            Exception? failure = null;
            try { p.Execute(); }
            catch (Exception error) { failure = error; throw; }
            finally { if (p.GetJournal().Commands.Any(row => row.NativeEntered)) Retain(p, failure); }
        }
        internal void Retain(NativeSeekCanvasPositionCommands p, Exception? failure = null)
        {
            if (!_commands.Contains(p)) _commands.Add(p);
            if (_retained.Add(p)) RetainedOwners.Add((Native, p, failure));
        }
        internal void RetainPublication(NativeSeekCanvasPositionCommands p, Exception failure)
        {
            var after = Publications.Read();
            Check(after.Calls == _expectedPublications.Calls + 1 && after.Roots == _expectedPublications.Roots + 1
                && after.Arguments == _expectedPublications.Arguments && after.Unknown == _expectedPublications.Unknown,
                "Getter unknown temporary cleanup did not retain exactly its one publication/root record.");
            _expectedPublications = after; Retain(p, failure);
        }
        internal void Clear()
        {
            Native.CanvasPositionProfile.OnVerify = Native.CanvasSourceProfile.OnVerify = null;
            Native.BeforeGeometryCall = Native.AfterGeometryStore = Native.AfterGeometryCall = null;
            Native.AfterGeometryGetter = Native.AfterGeometryArrayExport = null; Native.AfterGeometryRoot = null;
            Native.AfterCanvasSourceRead = null; Native.AfterCanvasSourceRange = null; Native.AfterCanvasSourceRoot = null;
            Native.AfterTargetFieldRead = Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = Native.AfterHoldStateRoot = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null;
            Native.CanvasSourceAuditExports = false;
            Native.CanvasPositionGetterException = Native.CanvasPositionSetterException = IntPtr.Zero;
            Native.FailGeometryBefore = Native.FailGeometryAfter = 0;
        }
        internal void UnrelatedState()
        {
            var canvases = Native.CanvasPositionTransforms.ToHashSet();
            Check(Native.CanvasSourceUnchanged() && Native.CanvasSourceForbidden == 0 && Native.GeometryOtherUnchanged()
                && Native.FlagWrites.Count == 0 && Native.JudgeQueueWrites.Count == 0 && Native.FingerWrites.Count == 0
                && Native.RendererCalls.Count == 0 && Native.HoldStateWrites.Count == 0 && Native.CursorWriteLog.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.TargetForbiddenUpdates == 0
                && Native.GeometryCalls.All(call => call.Operation == "set_localPosition" && canvases.Contains(call.Receiver))
                && _transforms.All(row => Native.GeometryTransforms[row.Key].Parent == row.Value.Parent
                    && Native.GeometryTransforms[row.Key].Rotation == row.Value.Rotation && Same(Native.GeometryTransforms[row.Key].Scale, row.Value.Scale)
                    && (canvases.Contains(row.Key) || Same(Native.GeometryTransforms[row.Key].Position, row.Value.Position))),
                "Canvas writer changed unrelated native storage, parent/rotation/scale, or called a consumer/other Transform method.");
            Check(Native.CanvasSourcePublished && Native.GeometryPublishedBeforeInspection && Native.GeometryReceiversRooted
                && Native.JudgePublicationRooted && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted
                && Native.NoteViewDispatchViolations.Count == 0,
                "Canvas writer inspected a physical publication or actual Transform before independent rooting.");
        }
        public void Dispose()
        {
            Clear();
            foreach (var p in _commands.AsEnumerable().Reverse())
                if (_retained.Contains(p))
                {
                    var attempts = Native.FreeHandleAttempts; var failure = Reject(p.Dispose);
                    Check(ReferenceEquals(failure.Data["SeekCanvasPositionEffectsOwner"], p)
                        || ReferenceEquals(failure.Data["SeekCanvasPositionCleanupOwner"], p),
                        "Canvas retained failure lost its exact public position owner.");
                    Check(Native.FreeHandleAttempts == attempts && p.HasRetainedResponsibility && !p.GetJournal().CompleteSeekPermitted,
                        "Canvas logical disposal retried unknown cleanup, freed entered-effect roots, or enabled complete Seek.");
                }
                else p.Dispose();
            Run.Dispose(); Backend.Dispose(); UnrelatedState();
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Canvas position ownership lost a native root or retried a free.");
            Check(Publications.Read() == _expectedPublications, "Canvas position operation leaked an unaccounted publication responsibility.");
            if (_retained.Count == 0)
            {
                Check(Native.ActiveRoots == 0, "Known Canvas position cleanup leaked roots: " + _lastFailure?.Message
                    + "; " + Native.CanvasSourceActiveRootDiagnostics()); Native.Dispose();
            }
        }
    }
    private static void Revoke(Fixture f, string kind)
    {
        if (kind == "revision") f.State.ObservePause(true);
        else if (kind == "withdrawal") f.State.ControllerUnavailable();
        else if (kind == "backend") f.Backend.Dispose();
        else f.Run.Dispose();
    }
    private static void NoNative(FakeNative n, Action action)
    {
        var before = Calls.Read(n); var lookups = n.RequestedExports.Count;
        n.BeginLowerIl2CppAudit();
        try { action(); } finally { n.EndLowerIl2CppAudit(); }
        Check(n.LowerIl2CppCalls == 0 && n.LowerWorkerIl2CppCalls == 0 && Calls.Read(n) == before && n.RequestedExports.Count == lookups,
            "Rejected Canvas operation reached native/lazy exports or changed root/effect responsibility.");
    }
}
