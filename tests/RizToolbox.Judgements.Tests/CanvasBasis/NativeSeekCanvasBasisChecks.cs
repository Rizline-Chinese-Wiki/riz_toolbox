using System.Collections;
using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.CanvasBasis;

// Every case enters CaptureRun -> the real RunValue. The official-export fake
// supplies native inputs; no Canvas access or aggregate is replaced here.
internal static partial class NativeSeekCanvasBasisChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Canvas basis captures one actual source and one ordinal Plan for both original owners", SingleSourceAndPlan),
        ("Canvas basis cross-checks both complete baselines source and exact metadata before all writes", PreparationBoundaries),
        ("Canvas basis keeps empty scalar targets and rejects illegal requests before acquisition", EmptyAndInvalid),
        ("Canvas basis preserves both kinds of equal aliases and rejects either conflicting kind globally", Aliases),
        ("Canvas basis publishes all explicit cache values before the first actual position setter", DependencyOrder),
        ("Canvas basis rejects fresh metadata ABI method and reference drift without retargeting", FreshMetadataAndReferences),
        ("Canvas basis preserves cache before and after partial native evidence and exact conditional inverses", PartialCache),
        ("Canvas basis blocks the first position setter after cache success and phase withdrawal", BetweenCacheAndPosition),
        ("Canvas basis preserves partial position owners and restores positions before cache values", PartialPosition),
        ("Canvas basis rejects sticky third states introduced through the other child callbacks", CrossChildThirdStates),
        ("Canvas basis expires complete retained sources across stages without a second numeric Plan", CompleteSourceDrift),
        ("Canvas basis preserves independent RunValue lifetime and the single shared construction gate", IndependentLifetimeAndSerial),
        ("Canvas basis isolates child withdrawal and drops each physical hold only through its original owner", ChildDisposalAndHolds),
        ("Canvas basis rejects shared scope worker and callback reentry before native dispatch", ScopeThreadAndCallbackDisposal),
        ("Canvas basis revokes old combined proof preserves immutable journals and retains original effects", ProofsJournalsAndDisposal),
        ("Canvas basis bounds all factories and preserves every unknown cleanup origin without retry", BudgetsAndUnknownCleanup),
        ("Canvas basis target ownership releases duplicate roots and revokes before recovery", TargetOwnershipCleanup),
        ("Canvas basis restored ownership releases actual roots and rejects changed originals", RestoredOwnershipCleanup)
    };

    // An entered Unity setter has no proved engine exit. Preserve its original
    // owner and fake native graph exactly as production must preserve its roots.
    private static readonly List<(FakeNative Native, object Owner, Exception? Failure)> RetainedOwners = new();
    private sealed record PublicationCounts(int Calls, int Roots, int Arguments, int Unknown)
    {
        internal static PublicationCounts Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
            NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount);
    }
    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected real Canvas basis rejection."); }
    private static bool DoesNotConfirm(Func<bool> action)
    { try { return !action(); } catch { return true; } }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static bool Same(NativeVector3 left, NativeVector3 right)
        => Bits(left.X) == Bits(right.X) && Bits(left.Y) == Bits(right.Y) && Bits(left.Z) == Bits(right.Z);
    private static void Vector(NativeVector3 actual, NativeVector3 expected, string reason) => Check(Same(actual, expected), reason);
    private static bool InArrayWriter() => Environment.StackTrace.Contains("WriteRootedCanvasCacheSingle", StringComparison.Ordinal);
    private static long Serial(JudgementRunCapture run) => (long)(run.GetType()
        .GetField("_canvasSourceConstructionSerial", BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(run) ?? throw new InvalidOperationException("No real Canvas construction serial."));
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
                else if (entry.Value is IEnumerable<SeekCanvasBasisFailure> basisFailures) foreach (var item in basisFailures) pending.Push(item.Failure);
            }
        }
    }
    private static object? Data(Exception error, string key)
        => Failures(error).Select(failure => failure.Data[key]).FirstOrDefault(value => value is not null);
    private static T? Owner<T>(Exception error, string key) where T : class
        => Failures(error).Select(failure => failure.Data[key]).OfType<T>().FirstOrDefault();
    private static void NoNative(FakeNative native, Action action)
    {
        var before = (native.FieldReads, native.Invocations, native.NewRoots, native.FreeHandleAttempts,
            native.FieldWrites, native.CanvasCacheObservedStores.Count, native.GeometryCalls.Count, native.RequestedExports.Count);
        native.BeginLowerIl2CppAudit();
        try { action(); }
        finally { native.EndLowerIl2CppAudit(); }
        Check(native.LowerIl2CppCalls == 0 && native.LowerWorkerIl2CppCalls == 0
            && before == (native.FieldReads, native.Invocations, native.NewRoots, native.FreeHandleAttempts,
                native.FieldWrites, native.CanvasCacheObservedStores.Count, native.GeometryCalls.Count, native.RequestedExports.Count),
            "Rejected basis admission reached an official native export or consumed responsibility.");
    }
    private static void NoWrites(FakeNative native, string reason)
        => Check(native.CanvasCacheObservedStores.Count == 0 && native.CanvasCacheScalarCalls.Count == 0 && native.GeometryCalls.Count == 0, reason);
    private static void OriginalRootsRemain(FakeNative native, IReadOnlyDictionary<uint, IntPtr> roots, string reason)
        => Check(roots.All(row => native.CanvasBasisCurrentRoots().TryGetValue(row.Key, out var target) && target == row.Value)
            && native.NoteViewFreeLog.All(row => !roots.ContainsKey(row.Handle)), reason);

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        private readonly List<NativeSeekCanvasBasisCommands> _bases = new();
        private readonly Dictionary<IntPtr, FakeNative.GeometryTrs> _transforms;
        private readonly HashSet<NativeSeekCanvasBasisCommands> _preserved = new();
        private readonly List<Exception> _constructionFailures = new();
        private PublicationCounts _expectedPublications = PublicationCounts.Read();
        internal Fixture(Action<FakeNative>? configure = null)
        {
            Native.CanvasBasisUseStableBorrowedMetadata();
            Native.ConfigureCanvasCaches(positions: true); configure?.Invoke(Native); Native.CanvasSourceRemember();
            _transforms = new(Native.GeometryTransforms);
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-canvas-basis", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun()
                ?? throw new InvalidOperationException("No real Canvas basis RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Basis checks bypassed the real backend RunValue.");
        }
        internal NativeSeekCanvasBasisCommands Prepare(float time = 5, float speed = 3)
        {
            try
            {
                var value = Run.PrepareSeekCanvasBasis(Native.CanvasSourceProfile, Native.CanvasPositionProfile, time, speed);
                _bases.Add(value); return value;
            }
            catch (Exception error)
            {
                _constructionFailures.Add(error);
                if (Owner<NativeSeekCanvasBasisCommands>(error, "SeekCanvasBasisOwner") is { } owner)
                { if (!_bases.Contains(owner)) _bases.Add(owner); }
                throw;
            }
        }
        internal void Preserve(NativeSeekCanvasBasisCommands basis, Exception? failure = null)
        {
            if (!_bases.Contains(basis)) _bases.Add(basis);
            if (_preserved.Add(basis)) RetainedOwners.Add((Native, basis, failure));
        }
        internal void AcceptUnknownPublication()
        {
            var after = PublicationCounts.Read();
            Check(after.Calls == _expectedPublications.Calls + 1 && after.Roots == _expectedPublications.Roots + 1
                && after.Arguments == _expectedPublications.Arguments && after.Unknown == _expectedPublications.Unknown,
                "Unknown temporary basis cleanup failed to preserve precisely one original publication/root record.");
            _expectedPublications = after;
        }
        internal void Clear()
        {
            Native.CanvasSourceProfile.OnVerify = Native.CanvasPositionProfile.OnVerify = null;
            Native.BeforeCanvasCacheScalar = Native.AfterCanvasCacheScalar = null; Native.AfterCanvasCacheObservedStore = null;
            Native.FailCanvasCacheScalarBefore = Native.FailCanvasCacheScalarAfter = 0;
            Native.BeforeGeometryCall = Native.AfterGeometryStore = Native.AfterGeometryCall = null;
            Native.AfterGeometryGetter = Native.AfterGeometryArrayExport = null; Native.AfterGeometryRoot = null;
            Native.FailGeometryBefore = Native.FailGeometryAfter = 0;
            Native.CanvasPositionGetterException = Native.CanvasPositionSetterException = IntPtr.Zero;
            Native.AfterCanvasSourceRead = null; Native.AfterCanvasSourceRange = null; Native.AfterCanvasSourceRoot = null;
            Native.AfterTargetFieldRead = Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterJudgeMetadata = Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = Native.AfterHoldStateRoot = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.CanvasSourceAuditExports = false;
        }
        internal void Revoke(string kind)
        {
            if (kind == "revision") State.ObservePause(true);
            else if (kind == "backend") Backend.Dispose();
            else if (kind == "withdrawal") State.ControllerUnavailable();
            else Run.Dispose();
        }
        internal void UnrelatedState()
        {
            var canvases = Native.CanvasPositionTransforms.ToHashSet();
            Check(Native.CanvasCacheUnrelatedUnchanged() && Native.CanvasSourceForbidden == 0
                && Native.FlagWrites.Count == 0 && Native.JudgeQueueWrites.Count == 0 && Native.FingerWrites.Count == 0
                && Native.RendererCalls.Count == 0 && Native.HoldStateWrites.Count == 0 && Native.CursorWriteLog.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.TargetForbiddenUpdates == 0
                && Native.GeometryCalls.All(call => call.Operation == "set_localPosition" && canvases.Contains(call.Receiver))
                && _transforms.All(row => Native.GeometryTransforms[row.Key].Parent == row.Value.Parent
                    && Native.GeometryTransforms[row.Key].Rotation == row.Value.Rotation
                    && Native.GeometryTransforms[row.Key].Scale == row.Value.Scale
                    && (canvases.Contains(row.Key) || Same(Native.GeometryTransforms[row.Key].Position, row.Value.Position))),
                "Basis touched unrelated native fields, Transform components, or producer/consumer methods.");
            Check(Native.CanvasCacheStoresPinned && Native.CanvasCacheScalarsRooted && Native.CanvasSourcePublished
                && Native.GeometryPublishedBeforeInspection && Native.GeometryReceiversRooted && Native.JudgePublicationRooted
                && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted && Native.NoteViewDispatchViolations.Count == 0,
                "Basis inspected or wrote an unrooted official publication.");
        }
        public void Dispose()
        {
            Clear();
            foreach (var basis in _bases.Distinct().Reverse())
            {
                Exception? error = null; try { basis.Dispose(); } catch (Exception failure) { error = failure; }
                var journal = basis.GetJournal();
                Check(!journal.CompleteSeekPermitted && !basis.CompleteSeekPermitted,
                    "Basis disposal granted full Seek admission.");
                if (journal.HasRetainedResponsibility)
                {
                    Check(error is not null && ReferenceEquals(Data(error, "SeekCanvasBasisOwner"), basis),
                        "Retained basis disposal lost its exact original public coordinator.");
                    Preserve(basis, error);
                    var attempts = Native.FreeHandleAttempts;
                    NoNative(Native, () => Reject(basis.Dispose));
                    Check(Native.FreeHandleAttempts == attempts, "Repeated retained disposal retried a native free.");
                }
                else Check(error is null, "Known basis disposal unexpectedly failed without retained responsibility: " + error);
            }
            Run.Dispose(); Backend.Dispose(); UnrelatedState();
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Basis lost a native root or retried an unknown handle free.");
            Check(PublicationCounts.Read() == _expectedPublications,
                "Basis left an unaccounted temporary publication or dropped its unknown cleanup record.");
            if (_preserved.Count == 0)
            {
                Check(Native.ActiveRoots == 0, "Known basis cleanup leaked original independent roots: " + Native.CanvasSourceActiveRootDiagnostics());
                Native.Dispose();
            }
            else Native.CanvasBasisTrimFinishedDiagnostics();
        }
    }
}
