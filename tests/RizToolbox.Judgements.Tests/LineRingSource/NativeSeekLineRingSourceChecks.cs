using System.Collections;
using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.LineRingSource;

// Every case enters CaptureRun -> the actual RunValue -> its original Canvas
// basis -> CaptureLineRingSource. No retained reader/access is mocked.
internal static partial class NativeSeekLineRingSourceChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Line ring source preserves the original Canvas capture Plan NoteView and construction serial", SameProvenance),
        ("Line ring source captures the complete actual graph without Hold or any runtime Note", FullGraphWithoutHold),
        ("Line ring source preserves complete empty control and group arrays without claiming a rebuilt environment", EmptyGraphs),
        ("Line ring source preserves every alias occurrence and rejects contradictory repeated observations", OccurrencesAndAliases),
        ("Line ring source preserves multiple Note instances and proven joins while leaving no Note segments unresolved", ChartRuntimeJoins),
        ("Line ring source retains static group only segments and external end line closure", StaticAndGroupOnlySegments),
        ("Line ring source rejects wrong exact classes descriptors and warmed getter ABI drift before writes", ExactMetadata),
        ("Line ring source bounds complete arrays and parent closure and rejects replacement or changing slots", ArrayIntegrityAndBudgets),
        ("Line ring source expires endpoint point and group source drift without accepting a later restoration", SourceDrift),
        ("Line ring source separates bitwise scalar selection and pose baselines from source identity", BaselineSeparation),
        ("Line ring source rejects changes to the original Canvas and NoteView without recapturing", OriginalCanvasDrift),
        ("Line ring source roots publications before callback withdrawal and keeps ordinary sibling cancellation local", PublicationAndWithdrawal),
        ("Line ring source rejects worker and shared reader cache position and common sweep reentry before native", WorkerAndSharedReentry),
        ("Line ring source keeps independent reader lifetime through Run and aggregate disposal orders", IndependentDisposal),
        ("Line ring source revokes old proofs preserves immutable history and keeps the original effects owner", ProofsAndImmutableHistory),
        ("Line ring source reserves public owners before acquisition and preserves exact unknown cleanup owners without retry", BudgetsAndUnknownCleanup),
        ("Line ring source numeric basis owns every original initialized Ease sample and exact Canvas slot Transform", NumericBasisOriginalCopy),
        ("Line ring source numeric basis first reads after Run and aggregate disposal using its independent hold", NumericBasisAfterAggregateDisposal),
        ("Line ring source numeric basis rejects source drift and managed reentry while preserving historical owned values", NumericBasisGuards),
        ("Line ring source scene reads actual original camera UI and scene inputs without promoting them to targets", SceneCurrentInputs),
        ("Line ring source scene rejects changed UI identity and keeps the original source expired", SceneIdentityDrift),
        ("Line ring source scene refuses unstable and nonfinite actual scalars without publishing a success", SceneScalarRefusal),
        ("Line ring source scene stops native inspection on caller withdrawal at a camera field read", SceneWithdrawal),
        ("Line ring source scene binds post-write world and camera UI inputs to one original writer and reader", SceneAfterAppliedWorld),
        ("Line ring source dynamic consumer projects actual no Note alias buffers from applied owners", DynamicAppliedNoNotes),
        ("Line ring source dynamic consumer refuses missing applied Canvas values and another owner", DynamicOriginalOwnerGates),
        ("Line ring source dynamic consumer keeps culls and static group only graphs distinct", DynamicCullAndEmpty),
        ("Line ring source dynamic consumer refuses changing current speed and world inputs", DynamicChangingInputs),
        ("Line ring source dynamic consumer rejects restoration and preserves historical buffers", DynamicRestoration),
        ("Line ring source dynamic consumer separates raw floor indices from physical CanvasMove slots", DynamicRawFloorVsPhysicalSlot)
    };

    // Focused RED/repair selector; these same checks also run in groups 7/10.
    internal static readonly (string Name, Action Run)[] SourceReviewCases =
    {
        ("Line ring source rejects same named foreign getter reference classes", CanonicalGetterReferences),
        ("Line ring source rejects same named foreign nullable field reference classes", CanonicalFieldReferences),
        ("Line ring source keeps baseline observation instability retryable", BaselineObservationInstability)
    };

    private static readonly List<(FakeNative Native, object Owner, Exception? Failure)> RetainedOwners = new();
    private sealed record PublicationCounts(int Calls, int Roots, int Arguments, int Unknown)
    {
        internal static PublicationCounts Read() => new(NativePublicationCall.RetainedCallCount,
            NativePublicationCall.RetainedRootRecordCount, NativePublicationCall.RetainedArgumentRecordCount,
            NativePublicationCall.UnconfirmedPublicationCount);
    }
    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected actual line/ring source rejection."); }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static bool Same(NativeVector3 left, NativeVector3 right)
        => Bits(left.X) == Bits(right.X) && Bits(left.Y) == Bits(right.Y) && Bits(left.Z) == Bits(right.Z);
    private static long Serial(JudgementRunCapture run) => (long)(run.GetType().GetField("_canvasSourceConstructionSerial",
        BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(run) ?? throw new InvalidOperationException("No real Canvas serial."));
    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(owner) ?? throw new InvalidOperationException("Absent original retained source field: " + name);
    private static NativeSeekRuntimeNoteView OriginalView(NativeSeekCanvasBasisCommands basis)
        => (NativeSeekRuntimeNoteView)Field(Field(Field(Field(basis, "_basis"), "_source"), "_core"), "_view");
    private static IEnumerable<Exception> Failures(Exception error)
    {
        var seen = new HashSet<Exception>(); var pending = new Stack<Exception>(); pending.Push(error);
        while (pending.Count != 0)
        {
            var next = pending.Pop(); if (!seen.Add(next)) continue; yield return next;
            if (next.InnerException is Exception inner) pending.Push(inner);
            if (next is AggregateException aggregate) foreach (var item in aggregate.InnerExceptions) pending.Push(item);
            foreach (DictionaryEntry entry in next.Data)
            {
                if (entry.Value is Exception failure) pending.Push(failure);
                else if (entry.Value is IEnumerable<Exception> failures) foreach (var item in failures) pending.Push(item);
                else if (entry.Value is IEnumerable<SeekCanvasBasisFailure> basisFailures)
                    foreach (var item in basisFailures) pending.Push(item.Failure);
            }
        }
    }
    private static object? Data(Exception error, string key)
        => Failures(error).Select(item => item.Data[key]).FirstOrDefault(value => value is not null);
    private static T? Owner<T>(Exception error, string key) where T : class
        => Failures(error).Select(item => item.Data[key]).OfType<T>().FirstOrDefault();
    private static void NoNative(FakeNative n, Action action)
    {
        var before = (n.FieldReads, n.Invocations, n.NewRoots, n.FreeHandleAttempts, n.FieldWrites,
            n.CanvasCacheObservedStores.Count, n.GeometryCalls.Count, n.RequestedExports.Count);
        n.BeginLowerIl2CppAudit();
        try { action(); } finally { n.EndLowerIl2CppAudit(); }
        Check(n.LowerIl2CppCalls == 0 && n.LowerWorkerIl2CppCalls == 0
            && before == (n.FieldReads, n.Invocations, n.NewRoots, n.FreeHandleAttempts, n.FieldWrites,
                n.CanvasCacheObservedStores.Count, n.GeometryCalls.Count, n.RequestedExports.Count),
            "Managed line/ring refusal reached an official native export or consumed native responsibility.");
    }
    private static void NoWrites(FakeNative n, string reason)
        => Check(n.CanvasCacheObservedStores.Count == 0 && n.CanvasCacheScalarCalls.Count == 0
            && n.GeometryCalls.Count == 0 && n.FlagWrites.Count == 0 && n.JudgeQueueWrites.Count == 0
            && n.FingerWrites.Count == 0 && n.RendererCalls.Count == 0 && n.HoldStateWrites.Count == 0
            && n.CursorWriteLog.Count == 0 && n.GameplayCommandCalls == 0 && n.TargetForbiddenUpdates == 0, reason);
    private static void OriginalRootsRemain(FakeNative n, IReadOnlyDictionary<uint, IntPtr> roots, string reason)
        => Check(roots.All(row => n.CanvasBasisCurrentRoots().TryGetValue(row.Key, out var value) && value == row.Value)
            && n.NoteViewFreeLog.All(row => !roots.ContainsKey(row.Handle)), reason);
    private static void Readonly<T>(IReadOnlyList<T> values, string reason)
    {
        if (values is IList<T> typed) Check(Reject(typed.Clear) is NotSupportedException, reason);
        if (values is IList untyped) Check(Reject(untyped.Clear) is NotSupportedException, reason);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        private readonly List<NativeSeekCanvasBasisCommands> _bases = new();
        private readonly List<NativeSeekLineRingSourceCapture> _sources = new();
        private readonly HashSet<object> _preserved = new();
        private PublicationCounts _expectedPublications = PublicationCounts.Read();
        internal bool AllowBasisWrites;
        internal Fixture(Action<FakeNative>? configure = null)
        {
            Native.CanvasBasisUseStableBorrowedMetadata(); Native.ConfigureLineRingSource();
            configure?.Invoke(Native); Native.CanvasSourceRemember();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-line-ring-source", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun()
                ?? throw new InvalidOperationException("No real line/ring RunValue.");
            Check(Run.GetType().Name == "RunValue", "Line/ring tests replaced the actual backend RunValue.");
        }
        internal NativeSeekCanvasBasisCommands Prepare(float time = 5, float speed = 3)
        {
            try
            {
                var basis = Run.PrepareSeekCanvasBasis(Native.CanvasSourceProfile, Native.CanvasPositionProfile, time, speed);
                _bases.Add(basis); return basis;
            }
            catch (Exception error)
            {
                if (Owner<NativeSeekCanvasBasisCommands>(error, "SeekCanvasBasisOwner") is { } basis && !_bases.Contains(basis)) _bases.Add(basis);
                throw;
            }
        }
        internal NativeSeekLineRingSourceCapture Capture(NativeSeekCanvasBasisCommands? basis = null)
        {
            basis ??= Prepare();
            try { var value = basis.CaptureLineRingSource(Native.LineRingSourceProfile); _sources.Add(value); return value; }
            catch (Exception error)
            {
                if (Owner<NativeSeekLineRingSourceCapture>(error, "SeekLineRingSourceCleanupOwner") is { } owner && !_sources.Contains(owner)) _sources.Add(owner);
                throw;
            }
        }
        internal void Clear()
        {
            Native.LineRingSourceProfile.OnVerify = null; Native.LineRingSourceProfile.WrongMember = null;
            Native.CanvasSourceProfile.OnVerify = Native.CanvasPositionProfile.OnVerify = null;
            Native.BeforeCanvasCacheScalar = Native.AfterCanvasCacheScalar = null; Native.AfterCanvasCacheObservedStore = null;
            Native.BeforeGeometryCall = Native.AfterGeometryStore = Native.AfterGeometryCall = null;
            Native.AfterGeometryGetter = Native.AfterGeometryArrayExport = null; Native.AfterGeometryRoot = null;
            Native.AfterCanvasSourceRead = null; Native.AfterCanvasSourceRange = null; Native.AfterCanvasSourceRoot = null;
            Native.AfterTargetFieldRead = Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = Native.AfterHoldStateRoot = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.CanvasSourceAuditExports = false;
        }
        internal void Preserve(object owner, Exception? failure = null)
        { if (_preserved.Add(owner)) RetainedOwners.Add((Native, owner, failure)); }
        internal void AcceptUnknownPublication()
        {
            var actual = PublicationCounts.Read();
            Check(actual.Calls == _expectedPublications.Calls + 1 && actual.Roots == _expectedPublications.Roots + 1
                && actual.Arguments == _expectedPublications.Arguments && actual.Unknown == _expectedPublications.Unknown,
                "Unknown line/ring temporary cleanup lost its original single publication/root record.");
            _expectedPublications = actual;
        }
        public void Dispose()
        {
            Clear();
            foreach (var source in _sources.Distinct().Reverse())
            {
                Exception? error = null; try { source.Dispose(); } catch (Exception failure) { error = failure; }
                var journal = source.GetJournal();
                Check(!source.CompleteSeekPermitted && !journal.CompleteSeekPermitted && journal.AttemptedWrites == 0,
                    "Read-only source cleanup granted Seek or reported native stores.");
                if (source.HasRetainedResponsibility)
                {
                    Check(error is not null && ReferenceEquals(Data(error, "SeekLineRingSourceCleanupOwner"), source),
                        "Retained line/ring disposal lost its exact public owner.");
                    Preserve(source, error); NoNative(Native, () => Reject(source.Dispose));
                }
                else Check(error is null, "Known line/ring disposal failed without responsibility: " + error);
            }
            foreach (var basis in _bases.Distinct().Reverse())
            {
                Exception? error = null; try { basis.Dispose(); } catch (Exception failure) { error = failure; }
                if (basis.HasRetainedResponsibility)
                {
                    Check(error is not null && ReferenceEquals(Data(error, "SeekCanvasBasisOwner"), basis),
                        "Shared source disposal lost its original Canvas coordinator.");
                    Preserve(basis, error); NoNative(Native, () => Reject(basis.Dispose));
                }
                else Check(error is null, "Known Canvas cleanup failed without retained responsibility: " + error);
            }
            Run.Dispose(); Backend.Dispose();
            if (!AllowBasisWrites) NoWrites(Native, "Read-only line/ring collection dispatched a producer, setter, or unrelated writer.");
            Check(Native.CanvasSourceForbidden == 0 && Native.CanvasSourcePublished && Native.GeometryPublishedBeforeInspection
                && Native.GeometryReceiversRooted && Native.JudgePublicationRooted && Native.AllGameplayReceiversRooted
                && Native.AllArrayReadsRooted && Native.NoteViewDispatchViolations.Count == 0,
                "Line/ring read used Hold, a producer, an unrooted publication, or an unapproved dispatch.");
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Line/ring cleanup lost a root or retried an unknown free.");
            Check(PublicationCounts.Read() == _expectedPublications, "Line/ring cleanup left an unaccounted publication owner.");
            if (_preserved.Count == 0)
            {
                Check(Native.ActiveRoots == 0, "Known line/ring cleanup leaked original roots: " + Native.CanvasSourceActiveRootDiagnostics());
                Native.Dispose();
            }
            else Native.CanvasBasisTrimFinishedDiagnostics();
        }
    }
}
