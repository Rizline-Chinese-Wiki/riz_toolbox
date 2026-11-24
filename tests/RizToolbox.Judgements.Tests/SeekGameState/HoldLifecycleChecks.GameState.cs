using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    private static void CompositionStage(string stage) => Console.WriteLine(JsonSerializer.Serialize(new
        { stage, observedUtc = DateTimeOffset.UtcNow.ToString("o") }));

    internal static readonly (string Name, Action Run)[] GameStateCases =
    {
        ("Seek same graph prepares all game-state stages and reads original baseline", GameStatePrepare),
        ("Seek same graph executes original five participants and reverse recovery", GameStateTransaction),
        ("Seek visibility reads complete actual Canvas and ease inputs with no native producer dispatch", GameStateVisibilityInputs),
        ("Seek five-participant scalar store failure restores the attempted prefix and original epoch", GameStateScalarFailure),
        ("Seek five-participant Canvas scalar before-store failure restores the original paused graph", GameStateBeforeCanvasFailure),
        ("Seek five-participant partial target Start failure reverses reconstruction before resuming predecessors", GameStatePartialTargetFailure),
        ("Seek five-participant abnormal native Start retains unknown responsibility without predecessor restart", GameStateUnknownTarget)
    };

    private sealed class GameStateImage : IDisposable
    {
        private readonly IDisposable _inner;
        internal readonly IntPtr Address;
        internal GameStateImage()
        {
            var type = typeof(LineRingSource.NativeSeekLineRingSourceChecks).GetNestedType("RendererSyntheticImage", BindingFlags.NonPublic)!;
            _inner = (IDisposable)Activator.CreateInstance(type, true)!;
            Address = (IntPtr)type.GetField("Address", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_inner)!;
        }
        public void Dispose() => _inner.Dispose();
    }

    private static NativeSeekGameStatePreparation GameState(EpochFixture f, SeekTargetCoordinates coordinates)
    {
        var n = f.Native;
        var profiles = new NativeSeekGameStatePreparation.Profiles(n.GameplayApi, n.SeekProfile, f.StartBinding, f.MoveBinding,
            n.TargetProfile, n.HoldStateProfile, n.GeometryProfile, n.RendererProfile, n.CanvasSourceProfile, n.CanvasPositionProfile,
            n.LineRingSourceProfile, n.LineRendererNativeProfile, n.GameStateVisibilityProfile);
        var association = f.State.CaptureCurrentPlaybackRun()!;
        return NativeSeekGameStatePreparation.Prepare(n.GameplayApi, f.State, association, coordinates, f.Access,
            profiles, (capture, target, profile) =>
            {
                Check(capture.Run.SameGeneration(f.Run.Run) && target.Coordinates == f.TargetPlan.Coordinates
                    && ReferenceEquals(profile, n.TargetProfile), "The same actual epoch was handed to another graph or target.");
                return f.Epoch;
            });
    }

    private static void GameStatePrepare()
    {
        using var image = new GameStateImage();
        using var f = new EpochFixture(compositionImage: image.Address);
        CompositionStage("game-state-epoch-prepared");
        using var state = GameState(f, f.TargetPlan.Coordinates);
        using var epoch = state.CaptureHoldEpochParticipant();
        state.VerifyPrepared(); epoch.VerifyPrepared();
        Check(state.ConfirmRestored().OriginalStateRestored && f.Native.GameStateScalarStores.Count == 0
            && f.Native.FingerWrites.Count == 0 && f.Native.HoldStateWrites.Count == 0 && f.Native.RendererCalls.Count == 0,
            "Preparation mutated an actual source or failed its complete baseline readback.");
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "actual-game-state-prepare", fullGameStatePrepared = true,
            fullTransactionExecuted = false, fixedOriginalBodyVerified = false, productionAdmission = false }));
    }

    private static void GameStateTransaction()
        => GameStateTransactionCore(null);

    private static void GameStateScalarFailure() => GameStateTransactionCore("scalar-prefix");
    private static void GameStateBeforeCanvasFailure() => GameStateTransactionCore("before-canvas");
    private static void GameStatePartialTargetFailure() => GameStateTransactionCore("partial-target");

    private static void GameStateTransactionCore(string? failureAt)
    {
        using var image = new GameStateImage();
        using var f = new EpochFixture(compositionImage: image.Address);
        CompositionStage("five-participant-epoch-prepared");
        var n = f.Native;
        try
        {
        using var source = new GameStateSource(f);
        CompositionStage("five-participant-source-prepared");
        var originalClock = source.Clock.Capture(); var originalHold = n.HoldStateSnapshot();
        var originalFingers = n.FingerSnapshot(); var originalQueues = n.JudgeQueueSnapshot();
        var request = new GameplayControlRequest(GameplayControlAction.Seek, source.Binding.ControllerGeneration,
            source.Binding.RunId, 1.25, GameplayControlValueUnit.GameSeconds);
        using var progress = NativeSeekProgressParticipant.Capture(n.GameplayApi, n.SeekProfile, source.Binding,
            n.GameplayPause.ToInt64(), request, source.VerifyAuthority, source.VerifyAuthority);
        CompositionStage("five-participant-progress-prepared");
        using var state = GameState(f, progress.Coordinates);
        CompositionStage("five-participant-game-state-prepared");
        using var transaction = NativePlaybackSeekParticipants.Prepare(request, source, progress, state, source.VerifyAuthority);
        CompositionStage("five-participant-transaction-prepared");
        n.ProducerBeginBoundedHistory();
        if (failureAt is not null)
        {
            RunGameStateFailure(f, source, progress, state, transaction, failureAt, originalClock,
                () => n.HoldStateEquals(originalHold) && n.FingerStorageEquals(originalFingers) && n.JudgeQueueStorageEquals(originalQueues));
            return;
        }
        try { transaction.Commit(); }
        catch (Exception commitError)
        {
            // Log while every prepared owner is still live. Their legitimate
            // retained-responsibility Dispose failures must not hide the first
            // failed production operation in the offline composition evidence.
            Console.Error.WriteLine("Original five-participant Commit failure: " + commitError);
            Console.Error.WriteLine(JsonSerializer.Serialize(new { stage = "five-participant-commit-failure",
                n.SeekHoldFactories, n.SeekHoldStarts, n.SeekHoldStops, epochUnknown = f.Epoch.SideEffectsUnknown,
                gameStateStages = n.GameStateStages, geometryCalls = n.GeometryCalls.Count,
                rendererCalls = n.RendererCalls.Count, canvasScalarCalls = n.CanvasCacheScalarCalls.Count }));
            throw;
        }
        CompositionStage("five-participant-committed");
        Check(transaction.Confirm().Observed == true && n.JudgeHoldValue == f.Epoch.RequiresContinuation.Count
            && source.Clock.Capture().MediaAnchorSeconds == progress.MediaSeconds
            && n.GameStateStages.Contains("ActiveNotes") && n.GameStateStages.Contains("SetNotes"),
            "The original five-participant transaction did not confirm all actual target state.");
        CompositionStage("five-participant-confirmed");
        transaction.Restore(); var restored = transaction.ConfirmRestored();
        CompositionStage("five-participant-inverse-readback-completed");
        Check(restored.OriginalStateRestored && n.HoldStateEquals(originalHold) && n.FingerStorageEquals(originalFingers)
            && n.JudgeQueueStorageEquals(originalQueues) && source.Clock.Capture().MediaAnchorSeconds == originalClock.MediaAnchorSeconds
            && source.Clock.Capture().DspAnchorSeconds == originalClock.DspAnchorSeconds
            && source.Clock.Capture().Rate == originalClock.Rate && n.GameplayPosition == 4,
            "The actual combined inverse did not restore complete original storage: " + restored.Evidence);
        f.VerifiedQueueWrites = n.JudgeQueueWrites.Count;
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "actual-five-participant-seek",
            fullGameStatePrepared = true, fullTransactionExecuted = true, originalStorageRestored = true,
            targetHoldCount = f.Epoch.RequiresContinuation.Count, queueWrites = n.JudgeQueueWrites.Count,
            fingerWrites = n.FingerWrites.Count, holdWrites = n.HoldStateWrites.Count,
            scalarWrites = n.GameStateScalarStores.Count, rendererCalls = n.RendererCalls.Count,
            geometryCalls = n.GeometryCalls.Count, stages = n.GameStateStages,
            historyCompactions = n.ProducerHistoryCompactions, compactedRootRecords = n.ProducerCompactedRootHistory,
            compactedFreeRecords = n.ProducerCompactedFreeHistory,
            controlledNativeProfiles = true, controlledTimeWriter = true, visibilityBodiesSynthetic = true,
            fixedOriginalBodyVerified = false, formalFreshIssued = false, productionAdmission = false }));
        }
        catch (Exception error)
        {
            f.Retained = f.Epoch.SideEffectsUnknown;
            Console.Error.WriteLine("Original five-participant failure: " + error);
            Console.Error.WriteLine(JsonSerializer.Serialize(new { stage = "five-participant-failure",
                roots = n.ActiveRoots, starts = n.SeekHoldStarts, stops = n.SeekHoldStops,
                epochUnknown = f.Epoch.SideEffectsUnknown, epochFailure = f.Epoch.Failure,
                stages = n.GameStateStages, scalars = n.GameStateScalarStores.Count }));
            throw;
        }
    }

    private static void RunGameStateFailure(EpochFixture f, GameStateSource source, NativeSeekProgressParticipant progress,
        NativeSeekGameStatePreparation state, GameplayPreparedControl transaction, string failureAt,
        NativePlaybackClockState originalClock, Func<bool> originalStorage)
    {
        var n = f.Native;
        var failure = new IOException("Controlled original five-participant failure at " + failureAt + ".");
        var injected = 0;
        if (failureAt == "scalar-prefix")
            n.AfterGameStateScalarStore = name =>
            {
                if (name != "_perfect" || injected != 0) return;
                ++injected; n.AfterGameStateScalarStore = null; throw failure;
            };
        else if (failureAt == "before-canvas")
            n.BeforeCanvasCacheScalar = _ =>
            {
                if (injected != 0) return;
                ++injected; n.BeforeCanvasCacheScalar = null; throw failure;
            };
        else if (failureAt == "partial-target")
        {
            Check(f.Epoch.RequiresContinuation.Count >= 2, "The actual combined target has no second Hold factory.");
            n.BeforeSeekHoldFactoryMetadata = () =>
            {
                if (n.SeekHoldFactories != 1 || injected != 0) return;
                ++injected; n.BeforeSeekHoldFactoryMetadata = null; throw failure;
            };
        }
        else throw new ArgumentException("Unknown composition failure.", nameof(failureAt));
        Exception actual;
        try { actual = Expect<IOException>(transaction.Commit); }
        finally
        {
            n.AfterGameStateScalarStore = null; n.BeforeCanvasCacheScalar = null; n.BeforeSeekHoldFactoryMetadata = null;
        }
        Check(ReferenceEquals(actual, failure) && injected == 1 && !f.Epoch.SideEffectsUnknown
            && source.Clock.Capture().MediaAnchorSeconds == originalClock.MediaAnchorSeconds,
            "The original known failure was hidden, poisoned the epoch or published an uncommitted Clock target.");
        if (failureAt == "scalar-prefix")
            Check(n.GameStateScalarStores.Any(row => row.Name == "_perfect" && row.Bits == 0)
                && n.HoldStateWrites.Count == 0 && n.CanvasCacheScalarCalls.Count == 0 && n.SeekHoldFactories == 0,
                "The scalar prefix failure did not stop at its actual written third counter.");
        else if (failureAt == "before-canvas")
            Check(n.HoldStateWrites.Count > 0 && n.CanvasCacheScalarCalls.Count == 1 && !n.CanvasCacheScalarCalls[0].Stored
                && n.LineRendererNativeCalls.Count == 0
                && !n.GameStateStages.Contains("ActiveNotes") && n.SeekHoldFactories == 0,
                "The first actual Canvas setter stored a value or dispatched later native reconstruction after its failure.");
        else
            Check(n.SeekHoldFactories == 1 && n.SeekHoldStarts == 1 && n.SeekHoldStops == 1 && n.JudgeHoldValue == 1
                && n.GameStateStages.Contains("SetNotes"), "The actual partial target did not stop before its second Factory.");
        f.Drained();
        CompositionStage("five-participant-known-failure-observed");
        transaction.Restore(); var restored = transaction.ConfirmRestored();
        if (!restored.OriginalStateRestored)
            Console.Error.WriteLine("Actual five-participant restoration failure: " + restored.Evidence);
        Check(restored.OriginalStateRestored && originalStorage() && f.Epoch.VerifyRestored()
            && source.Clock.Capture().MediaAnchorSeconds == originalClock.MediaAnchorSeconds
            && source.Clock.Capture().DspAnchorSeconds == originalClock.DspAnchorSeconds && source.Clock.Capture().Rate == originalClock.Rate
            && n.GameplayPosition == 4 && n.JudgeHoldValue == f.Predecessors.Length,
            "The known original composition failure did not recover complete baseline storage: " + restored.Evidence);
        f.VerifiedQueueWrites = n.JudgeQueueWrites.Count; f.Drained();
        Check(n.SeekHoldFactories == (failureAt == "partial-target" ? 1 : 0)
            && n.SeekHoldStarts == f.Predecessors.Length + (failureAt == "partial-target" ? 1 : 0),
            "Combined recovery retried a failed target or restarted an original more than once.");
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "actual-five-participant-" + failureAt,
            fullGameStatePrepared = true, fullTransactionExecuted = false, controlledTransactionAttempted = true,
            originalStorageRestored = true, knownFailureRestored = true, failureInjectedExactlyOnce = injected == 1,
            targetFactories = n.SeekHoldFactories, targetAndInverseStarts = n.SeekHoldStarts, actualStopCalls = n.SeekHoldStops,
            queueWrites = n.JudgeQueueWrites.Count, scalarWrites = n.GameStateScalarStores.Count,
            nativeVisibilityExecuted = n.GameStateStages.Contains("SetNotes"), epochUnknown = f.Epoch.SideEffectsUnknown,
            historyCompactions = n.ProducerHistoryCompactions, fixedOriginalBodyVerified = false,
            formalFreshIssued = false, productionAdmission = false }));
    }

    private static readonly List<IDisposable> UnknownGameStateOwners = new();
    private static (NativeRuntime.RootHandle Root, IntPtr Target)[] GameStateEpochRoots(EpochFixture f)
        => ((NativeRuntime.RootHandle?[])EpochFixture.Get(EpochFixture.Get(f.Epoch, "_roots"), "_roots"))
            .OfType<NativeRuntime.RootHandle>().Select(root => (root, root.Target)).ToArray();

    private static (int Media, long Progress, int Chart, int Real, int Synced) GameStateTimeBits(FakeNative n)
        => (BitConverter.SingleToInt32Bits(n.GameplayPosition),
            BitConverter.DoubleToInt64Bits(n.GameplayApi.Read<double>(n.GameplayProgress, "_nowTime")),
            BitConverter.SingleToInt32Bits(n.GameplayApi.Read<float>(n.GameplayInformation, "nowTime")),
            BitConverter.SingleToInt32Bits(n.GameplayApi.Read<float>(n.GameplayInformation, "nowRealTime")),
            BitConverter.SingleToInt32Bits(n.GameplayApi.Read<float>(n.GameplayInformation, "nowSyncedTime")));

    private static void GameStateUnknownTarget()
    {
        var image = new GameStateImage();
        var f = new EpochFixture(compositionImage: image.Address); var n = f.Native;
        var source = new GameStateSource(f);
        var originalClock = source.Clock.Capture(); var originalEpoch = f.Epoch; var originalRun = f.Epoch.Run;
        var originalLifecycle = f.Backend.HoldLifecycle.Snapshot(); var originalEpochRoots = GameStateEpochRoots(f);
        var originalIteratorStates = f.Predecessors.ToDictionary(iterator => iterator, iterator => n.TargetInt(iterator, "<>1__state"));
        var request = new GameplayControlRequest(GameplayControlAction.Seek, source.Binding.ControllerGeneration,
            source.Binding.RunId, 1.25, GameplayControlValueUnit.GameSeconds);
        var progress = NativeSeekProgressParticipant.Capture(n.GameplayApi, n.SeekProfile, source.Binding,
            n.GameplayPause.ToInt64(), request, source.VerifyAuthority, source.VerifyAuthority);
        var state = GameState(f, progress.Coordinates);
        var transaction = NativePlaybackSeekParticipants.Prepare(request, source, progress, state, source.VerifyAuthority);
        n.ProducerBeginBoundedHistory();
        var startsBefore = f.Start.Originals;
        f.Start.Original = frame =>
        {
            Check(f.Move.Invoke(new NativeCoroutineFrame { Version = 1, Size = 0x30, Kind = NativeCoroutineKind.MoveNext,
                Self = frame.Enumerator, Method = f.MoveTarget.MethodInfo, Caller = new(0x12346) }) == 0xff,
                "The abnormal controlled Start lost its synchronous actual Move.");
            throw new IOException("Controlled combined original Start failed after its actual Move.");
        };
        var commitFailure = Expect<InvalidOperationException>(transaction.Commit);
        f.Retained = f.Epoch.SideEffectsUnknown;
        // Preserve the entire failed composition before assertions or Dispose can
        // fail; the exact retained native owners must survive failed evidence too.
        UnknownGameStateOwners.AddRange(new IDisposable[] { image, f, transaction, state, progress, source });
        var unknownLifecycle = f.Backend.HoldLifecycle.Snapshot();
        Check(ReferenceEquals(commitFailure.Data["SeekHoldEpochOwner"], originalEpoch)
            && f.Epoch.SideEffectsUnknown && unknownLifecycle.Unknown
            && n.SeekHoldFactories == 1 && n.SeekHoldStarts == 1 && n.SeekHoldStops == 1 && n.JudgeHoldValue == 1
            && f.Start.Originals == startsBefore + 1 && f.Start.InFlight == 0 && f.Move.InFlight == 0,
            "The actual abnormal native Start became known after physical exit or lost its original responsibility owner.");
        var unknownTarget = unknownLifecycle.Rows.Single(row => !f.Predecessors.Contains(new IntPtr(row.Identity.Enumerator)));
        var retainedEpochRoots = GameStateEpochRoots(f);
        void VerifyOriginalEpochResources()
        {
            var actual = f.Backend.HoldLifecycle.Snapshot();
            Check(ReferenceEquals(f.Epoch, originalEpoch) && ReferenceEquals(EpochFixture.Get(f.Backend, "_seekHoldEpoch"), originalEpoch)
                && f.Epoch.Run.SameGeneration(originalRun) && actual.Run is not null && actual.Run.SameGeneration(originalRun)
                && ReferenceEquals(f.State.CaptureCurrentPlaybackRun(), source.Run)
                && originalEpochRoots.Length > 0 && originalEpochRoots.All(row => row.Root.Target == row.Target)
                && retainedEpochRoots.Any(row => row.Target.ToInt64() == unknownTarget.Identity.Enumerator)
                && retainedEpochRoots.All(row => row.Root.Target == row.Target)
                && actual.RootResponsibilities >= originalLifecycle.RootResponsibilities
                && originalLifecycle.Rows.All(prior => actual.Rows.Any(row => row.Sequence == prior.Sequence
                    && row.Identity == prior.Identity && row.Handle == prior.Handle && row.MoveCalls == prior.MoveCalls
                    && row.YieldedMoves == prior.YieldedMoves && !row.Retired))
                && originalIteratorStates.All(row => n.TargetInt(row.Key, "<>1__state") == row.Value),
                "Unknown combined recovery released or replaced exact original-epoch roots, scheduling records or iterator state.");
        }
        VerifyOriginalEpochResources();
        var stops = n.SeekHoldStops; var starts = n.SeekHoldStarts;
        var scalars = n.GameStateScalarStores.ToArray(); var timeStores = n.GameStateTimeStores.ToArray();
        var holdWrites = n.SeekHoldWrites.ToArray(); var targetTimes = GameStateTimeBits(n);
        Check(timeStores.Length == 1 && timeStores[0].Owner == n.GameplayPlayer
            && timeStores[0].Bits == BitConverter.SingleToInt32Bits(progress.MediaSeconds)
            && targetTimes == (BitConverter.SingleToInt32Bits(progress.MediaSeconds),
                BitConverter.DoubleToInt64Bits(progress.Coordinates.ProgressSeconds),
                BitConverter.SingleToInt32Bits(progress.Coordinates.ChartCoordinate),
                BitConverter.SingleToInt32Bits(progress.Coordinates.SyncedSeconds),
                BitConverter.SingleToInt32Bits(progress.Coordinates.SyncedSeconds))
            && source.Clock.Capture() == originalClock,
            "The abnormal target failed before its actual media/four-cache writes or published an uncommitted Clock.");
        transaction.Restore(); var restored = transaction.ConfirmRestored();
        Check(!restored.OriginalStateRestored && f.Epoch.SideEffectsUnknown && n.SeekHoldStops == stops && n.SeekHoldStarts == starts
            && n.GameStateScalarStores.SequenceEqual(scalars) && n.GameStateTimeStores.SequenceEqual(timeStores)
            && n.SeekHoldWrites.SequenceEqual(holdWrites) && GameStateTimeBits(n) == targetTimes
            && source.Clock.Capture() == originalClock && n.JudgeHoldValue == 1,
            "Unknown combined target responsibility permitted an actual inverse time/scalar store, fabricated a baseline or restarted a predecessor.");
        VerifyOriginalEpochResources();
        var cleanup = Expect<AggregateException>(transaction.Dispose);
        Check(cleanup.Flatten().InnerExceptions.Any(error => error is InvalidOperationException
                && error.Message == "The original Hold epoch retains unresolved Stop/Start responsibility."),
            "Combined cleanup did not report the original unknown Hold epoch responsibility.");
        VerifyOriginalEpochResources();
        var roots = n.ActiveRoots; var frees = n.FreeHandleAttempts;
        transaction.Dispose();
        Check(roots > 0 && n.ActiveRoots == roots && n.FreeHandleAttempts == frees,
            "Unknown combined cleanup retried its retained native free.");
        f.Dispose(); VerifyOriginalEpochResources();
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "actual-five-participant-abnormal-target-start",
            fullGameStatePrepared = true, fullTransactionExecuted = false, controlledTransactionAttempted = true,
            originalStorageRestored = false, unknownNativeEffectsRetained = true, retainedRoots = roots,
            originalEpochRootsRetained = originalEpochRoots.Length, targetEpochRootsRetained = retainedEpochRoots.Length,
            inverseTimeWritesBlocked = true, originalClockUnchanged = true,
            progressStores = scalars.Where(row => row.Owner == n.GameplayProgress || row.Owner == n.GameplayInformation)
                .Select(row => new { owner = row.Owner.ToInt64(), row.Name, row.Bits }),
            mediaStores = timeStores.Select(row => new { owner = row.Owner.ToInt64(), row.Bits }),
            actualStopCalls = n.SeekHoldStops, actualStartCalls = n.SeekHoldStarts, physicalStartMoveDrained = true,
            predecessorRestarted = false, fixedOriginalBodyVerified = false, formalFreshIssued = false, productionAdmission = false }));
    }

    private static void GameStateVisibilityInputs()
    {
        using var image = new GameStateImage();
        using var f = new EpochFixture(compositionImage: image.Address);
        var n = f.Native;
        using var capture = f.Backend.CaptureRun()!;
        using var flags = capture.PrepareSeekNoteFlags(n.SeekProfile, n.FlagValues());
        using var renderer = capture.PrepareSeekRendererCommands(n.RendererProfile,
            n.NoteViewRuntimeNotes.Select(note => new SeekRendererCommand(note.ToInt64(), SeekRendererCommandKind.Preserve)).ToArray());
        renderer.RequireRestorable(); renderer.RegisterNoteFlags(flags);
        renderer.Execute(); flags.Commit(); Check(flags.Confirm().Observed == true, "The original flags did not read back.");
        var visibility = renderer.PrepareNoteVisibility(n.GameStateVisibilityProfile,
            n.TargetFloat(n.GameplayInformation, "nowTime"), n.TargetFloat(n.GameplayInformation, "speed"), flags);
        var baseline = visibility.Baseline;
        var transforms = n.CanvasPositionTransforms;
        Check(transforms.Length == 3 && transforms.Distinct().Count() == 3
            && transforms.All(transform => baseline.Basis.Contains(transform.ToInt64()))
            && baseline.Heads.Count == n.NoteViewRuntimeNotes.Count && baseline.Basis.Count > 100
            && !visibility.GetJournal().Entered && n.GameStateScalarStores.Count == 0 && n.RendererCalls.Count == 0
            && n.SeekHoldStops == 0 && n.SeekHoldFactories == 0,
            "The actual visibility preparation omitted a Canvas/ease input or dispatched a native producer.");
        flags.Restore(); Check(flags.ConfirmRestored().OriginalStateRestored, "The visibility input flags did not restore.");
        renderer.AcceptRestoredNoteFlags(flags); renderer.Restore();
        Check(renderer.VerifyRestoredValues(), "The visibility input renderer baseline did not restore.");
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "actual-visibility-basis-inputs",
            completeVisibilityBasisRead = true, canvasSlots = transforms.Length, basisWords = baseline.Basis.Count,
            actualHeads = baseline.Heads.Count, nativeProducerDispatched = false, originalStorageRestored = true,
            fixedOriginalBodyVerified = false, formalFreshIssued = false, productionAdmission = false }));
    }

    private sealed class GameStateSource : INativePlaybackSeekSource
    {
        private readonly EpochFixture _f;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private bool _disposed;
        public NativePlaybackBinding Binding { get; }
        public NativePlaybackClock Clock { get; }
        public GameEventState.PlaybackRunAssociation Run { get; }
        internal GameStateSource(EpochFixture f)
        {
            _f = f; var n = f.Native; Run = f.State.CaptureCurrentPlaybackRun()!;
            Binding = new(Run.RunId, Run.SceneGeneration, 1, 1, n.GameplayController.ToInt64(), n.GameplayInformation.ToInt64(),
                n.GameplayProgress.ToInt64(), n.GameplayPlayer.ToInt64(), n.GameplaySource.ToInt64(), n.GameplayClip.ToInt64(), n.GameplayGetTimeMethod.ToInt64());
            try
            {
                foreach (var pointer in new[] { n.GameplayController, n.GameplayInformation, n.GameplayProgress, n.GameplayPlayer,
                    n.GameplaySource, n.GameplayClip, n.GameplayPause })
                    _roots.Add(n.GameplayApi.RetainExactObject(pointer, n.GameplayApi.ObjectClass(pointer)));
                Clock = new(Binding, n.GameplayDsp, n.GameplayPosition, n.GameplayPitch, n.GameplayLength);
            }
            catch
            {
                foreach (var root in _roots.AsEnumerable().Reverse()) root.Dispose();
                throw;
            }
        }
        public void VerifyAuthority()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GameStateSource));
            Run.VerifyHostCurrent(); _f.Access.VerifyCurrent();
            if (!ReferenceEquals(_f.State.CaptureCurrentPlaybackRun(), Run) || Clock.Capture().Phase == NativePlaybackPhase.Withdrawn)
                throw new InvalidOperationException("The controlled original source lost its actual run or Clock.");
        }
        public NativePlaybackRateSample Read()
        {
            VerifyAuthority(); var n = _f.Native; var api = n.GameplayApi;
            return new(Binding, 1, 1, true, false, false, false, true, false, false, n.GameplayDsp,
                api.Unbox<float>(api.Call(n.GameplaySource, "get_time")), n.GameplayLength, n.GameplayPitch,
                api.Read<double>(n.GameplayProgress, "_nowTime"), api.Read<float>(n.GameplayInformation, "nowTime"),
                api.Read<double>(n.GameplayProgress, "_offset"), false, 0);
        }
        public INativePlaybackTimeWrite PrepareTimeWrite() { VerifyAuthority(); return new GameStateTimeWrite(this); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            foreach (var root in _roots.AsEnumerable().Reverse()) root.Dispose();
        }
        private sealed class GameStateTimeWrite : INativePlaybackTimeWrite
        {
            private readonly GameStateSource _source;
            private readonly float _original;
            private float _expected;
            private bool _disposed, _written, _restored;
            internal GameStateTimeWrite(GameStateSource source) { _source = source; _original = _expected = source._f.Native.GameplayPosition; }
            public void VerifyCurrent()
            {
                _source.VerifyAuthority();
                if (_disposed || _source._f.Native.GameplayPosition != _expected)
                    throw new InvalidOperationException("The controlled private media writer differs from its last actual store.");
            }
            public void Write(float seconds)
            {
                VerifyCurrent(); if (_written) throw new InvalidOperationException("The actual media store is once-only.");
                _written = true; Dispatch(seconds);
            }
            private void Dispatch(float value)
            {
                var n = _source._f.Native;
                n.GameplayApi.CallTyped(n.GameplayPlayer, "SetTime", new[] { "System.Single" }, NativeRuntime.Arg.Value(value));
                _expected = value; VerifyCurrent();
            }
            public void RestoreOriginal()
            {
                VerifyCurrent(); if (_restored) throw new InvalidOperationException("The actual media inverse is once-only.");
                _restored = true; Dispatch(_original);
            }
            public void Dispose() => _disposed = true;
        }
    }
}
