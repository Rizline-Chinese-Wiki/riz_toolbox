using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasSourceChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Canvas native source uses real fields static vectors range and initialized samples independently of RunValue", ActualProjection),
        ("Canvas native source preserves array and repeated move/keypoint occurrence order and accepts empty vectors", OrderAndEmpty),
        ("Canvas native source samples actual neighboring EaseInfos values rather than identity or theoretical easing", ActualSamples),
        ("Canvas native source rejects nonfinite request and overflowing actual projection before target writes", InvalidRequests),
        ("Canvas native source refuses malformed full Transform move and Ease vectors", InvalidContainers),
        ("Canvas native source refuses absent malformed and changing exact KeyPoint list storage", InvalidLists),
        ("Canvas native source rejects exact field class offset static type and current descriptor drift", InvalidMetadata),
        ("Canvas native source validates the official Double Double Single range MethodInfo before native entry", InvalidRange),
        ("Canvas native source rejects missing invalid and nonfinite complete keypoint and Ease sample inputs", InvalidSamples),
        ("Canvas native source verification detects retained array list field and static identity changes", SourceChanges),
        ("Canvas native source double observation rejects callback changes including exact signed-zero bits", DoublePass),
        ("Canvas native source publishes and roots actual references before inspecting even rejected foreign values", Publication),
        ("Canvas native source callback revision backend withdrawal and disposal prevent later exports", Withdrawal),
        ("Canvas native source construction and verification reentry are rejected without nested native calls", Reentry),
        ("Canvas native source worker capture verify and dispose reject before exports without consuming the main owner", WrongThread),
        ("Canvas native source cleanup failures retain the exact public capture owner and never retry unknown frees", UnknownCleanup)
    };
    private static readonly List<(FakeNative Native, NativeSeekCanvasTargetCapture Owner, Exception Failure)> UnknownOwners = new();
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static Exception Reject(Action action, string? diagnostic = null)
    { try { action(); } catch (Exception e) { return e; } throw new InvalidOperationException("Expected actual Canvas source rejection." + (diagnostic is null ? "" : " " + diagnostic)); }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static void Float(float actual, float expected, string reason) => Check(Bits(actual) == Bits(expected), reason);
    private static void Vector(NativeVector3 actual, NativeVector3 expected)
    { Float(actual.X, expected.X, "Canvas local X differs."); Float(actual.Y, expected.Y, "Canvas local Y differs."); Float(actual.Z, expected.Z, "Canvas local Z differs."); }
    private sealed record PublicationCounts(int Calls, int Roots, int Arguments, int Unknown)
    {
        internal static PublicationCounts Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
            NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        internal string Context = "Canvas source fixture";
        private Exception? _lastCaptureFailure;
        private readonly List<NativeSeekCanvasTargetCapture> _captures = new();
        private readonly HashSet<NativeSeekCanvasTargetCapture> _retained = new();
        private readonly PublicationCounts _publications = PublicationCounts.Read();
        internal Fixture(Action<FakeNative>? configure = null)
        {
            Native.ConfigureCanvasSource(); configure?.Invoke(Native); Native.CanvasSourceRemember();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-canvas-source", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No real Canvas source RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Canvas capture tests must use the real backend RunValue.");
        }
        internal NativeSeekCanvasTargetCapture Capture(float time = 5, float speed = 3, ISeekCanvasNativeProfile? profile = null)
        {
            try
            {
                var p = Run.CaptureSeekCanvasTargetSource(profile ?? Native.CanvasSourceProfile, time, speed); _captures.Add(p);
                Check(p.Plan is NativeSeekCanvasTargetPlan && !p.Plan.CompleteSeekPermitted,
                    "Actual source was confused with complete target environment rebuilding or a Seek permit."); return p;
            }
            catch (Exception error) { _lastCaptureFailure = error; throw; }
        }
        internal void Clear()
        {
            Native.CanvasSourceProfile.OnVerify = null; Native.AfterCanvasSourceRange = null; Native.AfterCanvasSourceRead = null;
            Native.AfterCanvasSourceRoot = null;
            Native.CanvasSourceRangeException = IntPtr.Zero;
            Native.AfterTargetFieldRead = Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterGeometryRoot = null; Native.AfterGeometryArrayExport = null; Native.DuringHandleTarget = null;
            Native.BeforeExport = null; Native.CanvasSourceAuditExports = false;
        }
        internal void Retain(NativeSeekCanvasTargetCapture p, Exception failure)
        {
            Check(ReferenceEquals(failure.Data["SeekCanvasSourceCleanupOwner"], p)
                && failure.Data["SeekCanvasSourceCleanupFailure"] is Exception,
                "Unknown cleanup has no exact public Canvas capture owner and failure.");
            if (!_captures.Contains(p)) _captures.Add(p);
            if (_retained.Add(p)) UnknownOwners.Add((Native, p, failure));
        }
        internal void ReadOnly()
        {
            Check(Native.CanvasSourceForbidden == 0 && Native.CanvasSourceUnchanged() && Native.GeometryCalls.Count == 0
                && Native.FlagWrites.Count == 0 && Native.JudgeQueueWrites.Count == 0 && Native.FingerWrites.Count == 0
                && Native.RendererCalls.Count == 0 && Native.HoldStateWrites.Count == 0 && Native.CursorWriteLog.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.TargetForbiddenUpdates == 0,
                "Canvas source wrote source/target state, borrowed Hold lists, or entered a consumer/Transform/initializer.");
            Check(Native.CanvasSourcePublished && Native.GeometryPublishedBeforeInspection && Native.JudgePublicationRooted
                && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted && Native.NoteViewDispatchViolations.Count == 0,
                "Canvas source inspected a reference before its publication was independently rooted.");
        }
        public void Dispose()
        {
            Clear();
            foreach (var p in _captures.AsEnumerable().Reverse())
                if (_retained.Contains(p))
                {
                    var attempts = Native.FreeHandleAttempts; var failure = Reject(p.Dispose);
                    Check(ReferenceEquals(failure.Data["SeekCanvasSourceCleanupOwner"], p) && attempts == Native.FreeHandleAttempts,
                        "Unknown public Canvas owner retried native cleanup.");
                }
                else p.Dispose();
            Run.Dispose(); Backend.Dispose(); ReadOnly();
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(x => x.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Canvas root bookkeeping lost a root or retried an unknown handle.");
            if (_retained.Count == 0)
            {
                Check(Native.ActiveRoots == 0 && PublicationCounts.Read() == _publications,
                    "Known Canvas cleanup leaked physical/publication ownership. " + Context
                    + "; roots=" + Native.NewRoots + "/" + Native.FreedRoots + "/" + Native.ActiveRoots
                    + "; publication before=" + _publications + "; after=" + PublicationCounts.Read()
                    + "; range=" + Native.CanvasSourceRangeAttempts + "; active=" + Native.CanvasSourceActiveRootDiagnostics()
                    + "; exports=" + string.Join(",", Native.CanvasSourceExportLog.TakeLast(12))
                    + "; capture failure=" + _lastCaptureFailure?.GetType().Name + ": " + _lastCaptureFailure?.Message);
                Native.Dispose();
            }
        }
    }
    private static void ActualProjection()
    {
        using var f = new Fixture(); var p = f.Capture(); var plan = p.Plan;
        Check(plan.Rows.Count == 3 && plan.Moves.Count == 3 && plan.Request.ChartTime == 5 && plan.Request.Speed == 3,
            "Capture used stale Canvas nowTime/speed, dropped occurrences, or did not expose the existing pure plan.");
        Float(plan.Request.ScreenWidth, 4, "Canvas actual screenWidth was not read.");
        Vector(plan.Request.Down, new(-2, .5f, 4)); Vector(plan.Request.Right, new(.25f, -3, -.5f));
        var a = plan.Rows[0]; var b = plan.Rows[1];
        Check(a.Ordinal == 0 && a.MoveReference == f.Native.CanvasSourceMoves[0].ToInt64() && a.SpeedSlot == 0 && a.XSlot == 0,
            "Strict equality did not select actual lower source slots.");
        Float(a.FloorPosition, 8.5f, "Official interval output was replaced by a time difference.");
        Float(a.FloorSpeed, 2, "Actual speed KeyPoint.value was not copied."); Float(a.XPosition, 0, "Actual .25 ease samples were not used.");
        Vector(a.LocalPosition, new(-51, 12.75f, 102));
        Check(b.Ordinal == 1 && b.SpeedSlot == 2 && b.XSlot == 2, "Unsorted move was reordered instead of scanned by actual array slot.");
        Float(b.FloorPosition, 26.75f, "Second actual interval/FP is incorrect."); Float(b.FloorSpeed, 3, "Second speed value is incorrect.");
        Float(b.XPosition, 32, "Selected .375 EaseInfos sample is incorrect."); Vector(b.LocalPosition, new(-152.5f, -55.875f, 305));
        Check(f.Native.CanvasSourceRanges.Count == 3 && f.Native.CanvasSourceRanges[0] == (0d, 5d, 2.75f)
            && f.Native.CanvasSourceRanges[1] == (1d, 5d, 2.25f), "Range did not receive Double/Double actual lower and requested chart coordinates.");
        foreach (var name in new[] { "canvasTransforms", "canvasMoveData", "speedKeyPoints", "xPositionKeyPoints", "downVector", "rightVector", "EaseInfos", "progress" })
            Check(f.Native.CanvasSourceReads.Any(row => row.Name == name), "Actual source getter was never exercised: " + name);
        foreach (var list in f.Native.CanvasSourceLists)
            Check(f.Native.CanvasSourceCountReads.Contains(list)
                && f.Native.CanvasSourceItemReads.Where(row => row.List == list).Select(row => row.Slot).Distinct()
                    .OrderBy(slot => slot).SequenceEqual(Enumerable.Range(0, f.Native.TargetInt(list, "_size"))),
                "Capture did not cross-check every actual KeyPoint active-prefix slot with official Count/Item.");
        f.Run.Dispose(); p.VerifySource();
        var physical = new[] { f.Native.GameplayInformation, f.Native.GameplayProgress, f.Native.GeometryCanvasControl,
            f.Native.GeometryCanvasArray, f.Native.CanvasSourceMovesArray, f.Native.CanvasSourceEaseArray }
            .Concat(f.Native.CanvasSourceLists).Concat(f.Native.CanvasSourceSamples)
            .Concat(plan.Moves.Select(m => new IntPtr(m.Reference)))
            .Concat(plan.Moves.SelectMany(m => m.SpeedKeyPoints.Concat(m.XPositionKeyPoints)).Select(k => new IntPtr(k.Reference)));
        Check(physical.All(pointer => f.Native.HasRootFor(pointer)), "Source borrowed caller RunValue roots instead of retaining physical source references.");
        Check(ReferenceEquals(p.Plan, plan) && !plan.CompleteSeekPermitted, "Source verification replaced the immutable plan or enabled complete Seek.");
        if (plan.Moves is IList<SeekCanvasTargetMove> mutable)
            Check(Reject(() => mutable[0] = plan.Moves[1]) is NotSupportedException, "Actual-source pure plan exposes writable moves.");
        f.ReadOnly();
    }
    private static void OrderAndEmpty()
    {
        using (var f = new Fixture())
        {
            var p = f.Capture(BitConverter.Int32BitsToSingle(0x40a00001));
            Check(p.Plan.Moves.Select(m => m.Index).SequenceEqual(new[] { 999, -5, 999 })
                && p.Plan.Rows.Select(r => r.MoveReference).SequenceEqual(f.Native.CanvasSourceMoves.Select(v => v.ToInt64())),
                "Move.index reordered or merged actual repeated native source occurrences.");
            var row = p.Plan.Rows[0]; Check(row.SpeedSlot == 2 && row.XSlot == 2, "Just-after equality did not select the final actual duplicate point.");
            Float(row.FloorPosition, 37.5f, "Single-rounded just-after interval projection is wrong."); Float(row.XPosition, 36, "Final self point was eased instead of copied.");
            Vector(row.LocalPosition, new(-216, -51.75f, 432));
        }
        using (var f = new Fixture())
        {
            var list = f.Native.CanvasSourceList(0, false); var first = f.Native.CanvasSourcePoint(0, false, 0);
            f.Native.CanvasSourceSlot(f.Native.CanvasSourceBacking(list), 1, first);
            var p = f.Capture(); Check(p.Plan.Moves[0].XPositionKeyPoints.Count == 3
                && p.Plan.Moves[0].XPositionKeyPoints[0].Reference == p.Plan.Moves[0].XPositionKeyPoints[1].Reference
                && p.Plan.Rows[0].XSlot == 1, "Repeated KeyPoint references were deduplicated or source order was lost.");
            Float(p.Plan.Rows[0].XPosition, 6, "Repeated point source selection did not use its actual next point.");
        }
        using var empty = new Fixture(n => n.CanvasSourceEmpty()); var q = empty.Capture(); q.VerifySource();
        Check(q.Plan.Moves.Count == 0 && q.Plan.Rows.Count == 0 && empty.Native.CanvasSourceRangeAttempts == 0,
            "Equal empty actual Canvas vectors were rejected or caused a numerical converter call.");
    }
    private static void ActualSamples()
    {
        using var f = new Fixture(n => n.CanvasSourceInterpolate()); var p = f.Capture(50.5f);
        // Single(50.5/100)*100 == 50.5: captured samples[50]=.375,
        // samples[51]=.875 interpolate to .625, not identity .505.
        var row = p.Plan.Rows[0]; Float(row.FloorPosition, 174, "Actual target interval source is wrong.");
        Float(row.XPosition, 14, "Actual neighboring 101-sample interpolation was replaced by identity/theoretical ease.");
        Vector(row.LocalPosition, new(-1040.5f, 219, 2081));
        Check(f.Native.CanvasSourceReads.Count(r => r.Name == "EaseInfos") >= 2,
            "The actual initialized ease container was not observed consistently across capture.");
        var remembered = p.Plan.Rows.ToArray(); f.Native.GeometrySingleSlot(f.Native.CanvasSourceSamples[1], 51, .125f); f.Native.CanvasSourceRemember();
        Reject(p.VerifySource);
        Check(p.Plan.Rows.SequenceEqual(remembered), "Ease source verification retargeted an earlier immutable projection.");
    }
    private static void InvalidRequests()
    {
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            foreach (var argument in new[] { "time", "speed" })
            {
                using var f = new Fixture(); var roots = f.Native.ActiveRoots;
                Reject(() => f.Capture(argument == "time" ? bad : 5, argument == "speed" ? bad : 3));
                Check(f.Native.CanvasSourceRangeAttempts == 0 && f.Native.ActiveRoots == roots,
                    "Nonfinite explicit Canvas request entered converter or leaked source roots.");
            }
        foreach (var mode in new[] { "range", "speed", "width", "down", "right" })
        {
            using var f = new Fixture();
            if (mode == "range") f.Native.CanvasSourceRangeResult = float.MaxValue;
            else if (mode == "width") f.Native.CanvasSourceSet(f.Native.GeometryCanvasControl, "screenWidth", float.MaxValue);
            else if (mode == "down") f.Native.CanvasSourceSetVector("downVector", new(float.MaxValue, .5f, 4));
            else if (mode == "right") f.Native.CanvasSourceSetVector("rightVector", new(.25f, float.MaxValue, -.5f));
            Reject(() => f.Capture(5, mode == "speed" ? float.MaxValue : 3)); f.ReadOnly();
        }
    }
}
