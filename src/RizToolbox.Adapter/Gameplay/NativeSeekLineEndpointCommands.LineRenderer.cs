using System.Runtime.ExceptionServices;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

internal enum SeekLineRendererCommandKind { Prefix, SetPositions, RestorePrefix, RestoreCount, RestoreArray, Cull,
    ProducerControl, ArraySlice, RestoreProducerControl, RestoreArraySlice }

internal sealed record SeekLineRendererCommandEffect(int Sequence, SeekLineRendererCommandKind Kind,
    int Occurrence, long Line, bool AdmissionEntered, bool NativeEntered, bool NativeReturned,
    NativeRootedVector3Buffer? BeforeScratch, NativeRootedVector3Buffer? TargetScratch,
    NativeRootedVector3Buffer? ReadbackScratch, SeekLineRendererSavedState? BeforeSaved,
    SeekLineRendererSavedState? TargetSaved, SeekLineRendererSavedState? ReadbackSaved,
    bool BeforeObserved, bool TargetObserved, string? Failure)
{
    internal int PrefixLength { get; init; }
    internal NativeLineProducerControlSnapshot? BeforeControl { get; init; }
    internal NativeLineProducerControlSnapshot? TargetControl { get; init; }
    internal NativeLineProducerControlSnapshot? ReadbackControl { get; init; }
    internal NativeLineProducerPatch? ControlPatch { get; init; }
    internal NativeVector3BufferSlice? ArraySlice { get; init; }
    internal NativeVector3BufferBits? BeforePoint { get; init; }
    internal NativeVector3BufferBits? TargetPoint { get; init; }
    internal NativeVector3BufferBits? ReadbackPoint { get; init; }
}

internal sealed record SeekLineRendererJournal(bool Prepared, bool ExecutionEntered, bool ExecutionFinished,
    bool RestorationEntered, bool RestorationFinished, bool RestoredValuesVerified,
    bool UnrecognizedState, bool SideEffectsUnknown, bool HasRetainedResponsibility,
    int DispatchedOccurrences, int SkippedOccurrences, NativeRootedVector3Buffer? BaselineScratch,
    NativeRootedVector3Buffer? ExpectedScratch, IReadOnlyList<SeekLineRendererSavedState> Baseline,
    IReadOnlyList<SeekLineRendererSavedState> Expected, IReadOnlyList<SeekLineRingDynamicOccurrence> Occurrences,
    IReadOnlyList<SeekLineRendererCommandEffect> Commands, NativeSeekLineRingSourceCapture? OriginalSource,
    object? EffectsOwner, object? SourceCleanupOwner)
{
    internal bool AtomicSnapshot => false;
    internal bool ExternalConsumersClosed => false;
    internal bool CompleteSeekPermitted => false;
    internal NativeLineProducerControlSnapshot? BaselineControl { get; init; }
    internal NativeLineProducerControlSnapshot? ExpectedControl { get; init; }
}

internal sealed partial class NativeSeekLineEndpointCommands
{
    private bool _rendererStarting, _rendererPreparationEntered, _rendererPrepared;
    private bool _rendererExecutionEntered, _rendererExecutionFinished, _rendererAdmitted;
    private bool _rendererRestorationEntered, _rendererRestorationFinished, _rendererRestoredVerified, _rendererUnrecognized;
    private int _rendererPending = -1, _rendererDispatched, _rendererCulled;
    private NativeRootedVector3Buffer? _rendererBaselineScratch, _rendererExpectedScratch;
    private NativeLineProducerControlSnapshot? _rendererBaselineControl, _rendererExpectedControl;
    private SeekLineRendererSavedState[] _rendererBaseline = Array.Empty<SeekLineRendererSavedState>();
    private SeekLineRendererSavedState[] _rendererExpected = Array.Empty<SeekLineRendererSavedState>();
    private SeekLineRingObservedDynamicPlan? _rendererPlan;
    private readonly List<SeekLineRendererCommandEffect> _rendererCommands = new();
    private readonly List<int> _rendererForwardEntered = new();

    private T RendererOperation<T>(Func<SeekLineRingSourceSnapshot, IRetainedSeekLineRendererAccess, T> action)
    {
        CheckThread(); ArgumentNullException.ThrowIfNull(action);
        if (_busy) throw new InvalidOperationException("The renderer cannot synchronously reenter its original endpoint owner.");
        _busy = true; _appliedVerified = false; _restoredVerified = false;
        _lastIssuedWorldObservation = null; _rendererRestoredVerified = false;
        Exception? failure = null;
        try
        {
            // Once a complete selected view is unrecognized, even entering
            // the source operation would perform an unowned native reread.
            RequireRendererKnown();
            var source = _source ?? throw new InvalidOperationException("Prepare the original endpoint owner first.");
            return source.WithVerifiedLineRendererOperation(this, (snapshot, access) =>
            { Local(); var result = action(snapshot, access); Local(); return result; });
        }
        catch (Exception error)
        {
            _targetOwnershipAcknowledged = false;
            failure = error; _failure ??= error.Message; AdoptCleanup(error);
            if (_rendererAdmitted)
            { _unknown = true; _retained = true; Attach(error); AttachRenderer(error); }
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposed && !_rendererStarting)
            {
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) ExceptionDispatchInfo.Capture(cleanup).Throw();
                    failure.Data["SeekLineRendererOperationCleanup"] = cleanup;
                }
            }
        }
    }

    private void RendererUnknown()
    {
        _rendererUnrecognized = true; _rendererRestoredVerified = false;
        _failure ??= "Saved renderer fields, associations or the complete original scratch reached an unrecognized state.";
        if (_rendererAdmitted) { _unknown = true; _retained = true; }
    }
    private void RequireRendererKnown()
    {
        Local();
        if (_rendererUnrecognized)
            throw new InvalidOperationException("An unrecognized renderer state permanently forbids another dispatch or selected inverse.");
    }

    private (NativeRootedVector3Buffer Scratch, SeekLineRendererSavedState[] Saved,
        NativeLineProducerControlSnapshot Control) ReadRendererState(
        IRetainedSeekLineRendererAccess access)
    {
        Local();
        var scratch = access.ReadOriginalLinePositions();
        var saved = access.ReadRendererSnapshot().ToArray(); Local();
        var control = access.ReadProducerControl(); Local();
        var laterScratch = access.ReadOriginalLinePositions();
        var laterSaved = access.ReadRendererSnapshot().ToArray(); Local();
        var laterControl = access.ReadProducerControl(); Local();
        if (!SameBuffer(scratch, laterScratch) || !saved.SequenceEqual(laterSaved) || control != laterControl)
            throw new InvalidOperationException("The complete renderer state changed between ordered observations.");
        return (laterScratch, laterSaved, laterControl);
    }

    private void ObserveRendererExpected(IRetainedSeekLineRendererAccess access)
    {
        RequireRendererKnown();
        var actual = ReadRendererState(access);
        if (_rendererExpectedScratch is null || !SameBuffer(actual.Scratch, _rendererExpectedScratch)
            || !actual.Saved.SequenceEqual(_rendererExpected) || actual.Control != _rendererExpectedControl)
        { RendererUnknown(); RequireRendererKnown(); }
    }

    private static bool SameRendererScene(SeekLineRingSceneSample a, SeekLineRingSceneSample b)
        => (a with { GameSceneLocalScale = default }) == (b with { GameSceneLocalScale = default })
            && Same(a.GameSceneLocalScale, b.GameSceneLocalScale);

    private void VerifyRendererInputs(IRetainedSeekLineRendererAccess access)
    {
        RequireRendererKnown();
        var plan = _rendererPlan ?? throw new InvalidOperationException("The original dynamic projection was not retained.");
        if (!ReferenceEquals(plan.OriginalEndpointOwner, this) || !ReferenceEquals(plan.OriginalSource, _source))
            throw new InvalidOperationException("The renderer projection belongs to another original owner.");
        var canvas = plan.OriginalCanvasOwner;
        canvas.RequireAppliedLineRendererSourceInOperation(plan.OriginalSource, this);
        access.VerifyAppliedCanvasInOperation(plan.ObservedInputs.Scene.SourceIdentity.CanvasPlan);
        var expectedWorld = plan.ObservedInputs.AppliedWorld.Endpoints;
        for (var pass = 0; pass < 2; ++pass)
        {
            var current = ReadAppliedWorldOnce(access);
            if (current.Length != expectedWorld.Count)
                throw new InvalidOperationException("The complete original world endpoint view changed length.");
            for (var i = 0; i < current.Length; ++i)
                if (!Same(current[i], expectedWorld[i]))
                    throw new InvalidOperationException("An original applied endpoint world tuple changed after dynamic projection.");
            if (!SameRendererScene(access.ReadSceneSample(), plan.ObservedInputs.Scene.Current))
                throw new InvalidOperationException("The actual original dynamic producer scalars changed after projection.");
        }
        ObserveRendererExpected(access);
        // Bracket world/scene/saved-field getter callbacks with complete actual
        // Canvas values, as the original pure producer projection does. These
        // ordered samples remain non-atomic at the external engine boundary.
        access.VerifyAppliedCanvasInOperation(plan.ObservedInputs.Scene.SourceIdentity.CanvasPlan);
        canvas.RequireAppliedLineRendererSourceInOperation(plan.OriginalSource, this);
        Local();
    }

    /// <summary>Projects and dispatches the original ordered dynamic occurrences
    /// over the original shared scratch. Only selected fields/array storage can
    /// be recovered; Unity consumers remain unknown even after that recovery.</summary>
    internal SeekLineRendererJournal ApplyDynamicRenderer(NativeSeekCanvasBasisCommands canvas,
        ISeekLineSetPositionsNativeProfile profile)
    {
        CheckThread(); Local(); ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(profile);
        if (_busy || _rendererStarting || _rendererPreparationEntered || _lineBufferPrepared || _lineBufferStoreAdmitted
            || !_executionFinished || _restorationEntered)
            throw new InvalidOperationException("Renderer preparation is once-only on an applied original endpoint owner before restoration/manual scratch use.");
        // Reserve before preparation/profile/Canvas callbacks. Public mutation
        // methods cannot interleave between this method's separate operations.
        _rendererStarting = true; _rendererPreparationEntered = true;
        Exception? failure = null;
        try
        {
            RendererOperation((snapshot, access) =>
            {
                access.BindRendererProfile(profile, snapshot.Lines.Select(row => row.Reference).ToArray());
                var baseline = ReadRendererState(access);
                foreach (var line in baseline.Saved)
                    if (line.SavedCount < 0 || line.SavedCount > (line.SavedArrayLength ?? 0))
                        throw new InvalidDataException("An original saved renderer count exceeds its actual retained array.");
                _rendererBaselineScratch = baseline.Scratch; _rendererExpectedScratch = baseline.Scratch;
                _rendererBaselineControl = baseline.Control; _rendererExpectedControl = baseline.Control;
                _rendererBaseline = baseline.Saved; _rendererExpected = baseline.Saved.ToArray();
                _rendererPrepared = true; return 0;
            });
            // This calls the original public owners outside the shared reader
            // gate. A caller-supplied historical/forged plan is never admitted.
            _rendererPlan = NativeSeekLineRingDynamicConsumer.Project(canvas, this);
            RendererOperation((_, access) =>
            {
                VerifyRendererInputs(access);
                foreach (var row in _rendererPlan.Occurrences)
                {
                    var index = Array.FindIndex(_rendererExpected, value => value.Line == row.Line);
                    if (index < 0) throw new InvalidDataException("A dynamic occurrence is outside the complete original saved-state domain.");
                    if (row.WouldSetPositions && (!_rendererExpected[index].CanDispatch
                        || row.Projection.PositionCount != row.Projection.Points.Count
                        || row.Projection.Points.Count < 1
                        || row.Projection.Points.Count > _rendererExpectedScratch!.Length))
                        throw new NotSupportedException("A complete dynamic occurrence lacks its actual plan/consumer or exceeds the original scratch capacity.");
                }
                _rendererExecutionEntered = true;
                var points = _rendererPlan.ObservedInputs.AppliedWorld.LocalProjection!.Source.Points
                    .ToDictionary(point => point.Reference);
                foreach (var row in _rendererPlan.Occurrences)
                {
                    VerifyRendererInputs(access);
                    // The frozen floor branches jump before either world
                    // getter/store. World-Y branches have already saved both
                    // world vectors, even though they skip the renderer.
                    var floorCull = row.Projection.Kind is SeekLineRingDynamicProjectionKind.OutsideFutureFloor
                        or SeekLineRingDynamicProjectionKind.OutsidePastFloor;
                    if (!floorCull)
                    {
                        StoreProducerWorld(access, row, NativeLineProducerField.StartPosition, row.Projection.Input.StartWorld);
                        StoreProducerWorld(access, row, NativeLineProducerField.EndPosition, row.Projection.Input.EndWorld);
                    }
                    if (!row.WouldSetPositions)
                    {
                        _rendererCommands.Add(new(_rendererCommands.Count, SeekLineRendererCommandKind.Cull,
                            row.Ordinal, row.Line, false, false, false, null, null, null, null, null, null, true, true, null));
                        ++_rendererCulled; continue;
                    }
                    if (!points.TryGetValue(row.StartPoint, out var start))
                        throw new InvalidDataException("The ordered producer occurrence lost its original start LinePoint.");
                    StoreProducerControl(access, row.Ordinal, row.Line,
                        new(NativeLineProducerField.EaseType, 0, 1, new(start.EaseType, 0, 0)), restore: false);
                    for (var sample = 0; sample < row.Projection.Points.Count; ++sample)
                    {
                        var value = row.Projection.Points[sample];
                        var bits = new NativeVector3BufferBits(BitConverter.SingleToInt32Bits(value.X),
                            BitConverter.SingleToInt32Bits(value.Y), BitConverter.SingleToInt32Bits(value.Z));
                        // Exactly the source word order: newLinePosition Z,
                        // X, Y, then this array element's XY and Z. Earlier
                        // elements are not repeatedly rewritten as a prefix.
                        StoreProducerControl(access, row.Ordinal, row.Line,
                            new(NativeLineProducerField.NewLinePosition, 2, 1, bits), restore: false);
                        StoreProducerControl(access, row.Ordinal, row.Line,
                            new(NativeLineProducerField.NewLinePosition, 0, 1, bits), restore: false);
                        StoreProducerControl(access, row.Ordinal, row.Line,
                            new(NativeLineProducerField.NewLinePosition, 1, 1, bits), restore: false);
                        StoreProducerSlice(access, row.Ordinal, row.Line, new(sample, 0, 2, bits), restore: false);
                        StoreProducerSlice(access, row.Ordinal, row.Line, new(sample, 2, 1, bits), restore: false);
                    }
                    DispatchRenderer(access, row.Ordinal, row.Line, row.Projection.Points.Count);
                    ++_rendererDispatched;
                }
                VerifyRendererInputs(access); return 0;
            });
            _rendererExecutionFinished = true;
            return RendererJournalCore();
        }
        catch (Exception error)
        { failure = error; AdoptCleanup(error); if (_rendererAdmitted) AttachRenderer(error); throw; }
        finally
        {
            _rendererStarting = false;
            if (_disposed && !_busy)
            {
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) ExceptionDispatchInfo.Capture(cleanup).Throw();
                    failure.Data["SeekLineRendererPreparationCleanup"] = cleanup;
                }
            }
        }
    }

    private int AddRendererStep(SeekLineRendererCommandKind kind, int occurrence, long line,
        NativeRootedVector3Buffer? beforeScratch = null, NativeRootedVector3Buffer? targetScratch = null,
        SeekLineRendererSavedState? beforeSaved = null, SeekLineRendererSavedState? targetSaved = null)
    {
        if (_rendererCommands.Count >= checked(NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences
            * (10 * NativeSeekLineRingNumericMath.EaseSamples + 16)))
            throw new NotSupportedException("The complete renderer command journal exceeded its occurrence bound.");
        var index = _rendererCommands.Count;
        _rendererCommands.Add(new(index, kind, occurrence, line, false, false, false,
            beforeScratch, targetScratch, null, beforeSaved, targetSaved, null, false, false, null));
        return index;
    }

    private void AdmitRendererStep(int index)
    {
        Local();
        if (_rendererPending >= 0 || _rendererCommands[index].AdmissionEntered)
        { RendererUnknown(); throw new InvalidOperationException("Renderer step admission entered twice."); }
        _targetOwnershipAcknowledged = false;
        _rendererPending = index; _rendererAdmitted = true; _retained = true; _unknown = true;
        _rendererCommands[index] = _rendererCommands[index] with { AdmissionEntered = true };
    }

    private void ConfirmRendererEntry(int index, bool returned)
    {
        var command = _rendererCommands[index];
        if (!command.AdmissionEntered) RendererUnknown();
        if (!command.NativeEntered && command.Kind is SeekLineRendererCommandKind.Prefix or SeekLineRendererCommandKind.SetPositions
            or SeekLineRendererCommandKind.ProducerControl or SeekLineRendererCommandKind.ArraySlice)
            _rendererForwardEntered.Add(index);
        _rendererCommands[index] = command with { NativeEntered = true, NativeReturned = returned || command.NativeReturned };
    }

    private static bool SameRendererEdges(SeekLineRendererSavedState a, SeekLineRendererSavedState b)
        => (a with { SavedArray = 0, SavedCount = 0, SavedArrayLength = null })
            == (b with { SavedArray = 0, SavedCount = 0, SavedArrayLength = null });

    private void ResolveRendererStep(IRetainedSeekLineRendererAccess access, int index)
    {
        var command = _rendererCommands[index];
        var actual = ReadRendererState(access);
        var scratchBefore = SameBuffer(actual.Scratch, _rendererExpectedScratch!);
        var scratchTarget = command.TargetScratch is not null && SameBuffer(actual.Scratch, command.TargetScratch);
        var isPrefix = command.Kind is SeekLineRendererCommandKind.Prefix or SeekLineRendererCommandKind.RestorePrefix;
        var isSlice = command.Kind is SeekLineRendererCommandKind.ArraySlice or SeekLineRendererCommandKind.RestoreArraySlice;
        var isControl = command.Kind is SeekLineRendererCommandKind.ProducerControl or SeekLineRendererCommandKind.RestoreProducerControl;
        if (isSlice) scratchTarget = SameBuffer(actual.Scratch,
            NativeRuntime.PatchLineVector3BufferSlice(_rendererExpectedScratch!, command.ArraySlice!.Value));
        var known = actual.Saved.Length == _rendererExpected.Length;
        var beforeObserved = true; var targetObserved = true;
        SeekLineRendererSavedState? selected = null;
        for (var i = 0; i < actual.Saved.Length && known; ++i)
        {
            var prior = _rendererExpected[i]; var current = actual.Saved[i];
            if (isPrefix || isSlice || isControl || current.Line != command.Line)
            { if (current != prior) known = false; continue; }
            selected = current;
            var before = command.BeforeSaved!; var target = command.TargetSaved!;
            beforeObserved = current == before; targetObserved = current == target;
            // The generated method saves array, then count. An entered call
            // may fail between them. All associations remain exact and only
            // the selected before/target scalar pair is recognized.
            var recognized = SameRendererEdges(current, before)
                && (current.SavedArray == before.SavedArray && current.SavedArrayLength == before.SavedArrayLength
                    || current.SavedArray == target.SavedArray && current.SavedArrayLength == target.SavedArrayLength)
                && (current.SavedCount == before.SavedCount || current.SavedCount == target.SavedCount);
            if (!recognized || !beforeObserved && !command.NativeEntered) known = false;
        }
        if (isPrefix || isSlice)
        {
            beforeObserved = scratchBefore; targetObserved = scratchTarget;
            if (!scratchBefore && (!scratchTarget || !command.NativeEntered)) known = false;
        }
        else if (isControl)
        {
            beforeObserved = actual.Control == command.BeforeControl;
            targetObserved = actual.Control == command.TargetControl;
            if (!scratchBefore || !beforeObserved && (!targetObserved || !command.NativeEntered)) known = false;
        }
        else if (!scratchBefore || selected is null) known = false;
        if (!isControl && actual.Control != _rendererExpectedControl) known = false;
        _rendererCommands[index] = command with
        {
            // Slice records retain their changed point, not a full array copy
            // for every instruction. The baseline/expected views stay complete.
            ReadbackScratch = isSlice || isControl ? null : actual.Scratch, ReadbackSaved = selected,
            ReadbackControl = isControl ? actual.Control : null,
            ReadbackPoint = isSlice ? actual.Scratch.Items[command.ArraySlice!.Value.Index] : null,
            BeforeObserved = beforeObserved, TargetObserved = targetObserved
        };
        if (!known) RendererUnknown();
        else { _rendererExpectedScratch = actual.Scratch; _rendererExpected = actual.Saved; _rendererExpectedControl = actual.Control; }
        _rendererPending = -1;
        RequireRendererKnown();
    }

    private void FinishRendererStep(IRetainedSeekLineRendererAccess access, int index, Exception? failure)
    {
        if (_cleanupFailure is null)
        {
            try { ResolveRendererStep(access, index); }
            catch (Exception observation)
            {
                RendererUnknown();
                if (failure is not null && !ReferenceEquals(failure, observation))
                    failure.Data["SeekLineRendererObservationFailure"] = observation;
                failure ??= observation;
            }
        }
        if (failure is null && !_rendererCommands[index].TargetObserved)
            failure = new InvalidOperationException("The entered renderer command did not leave its complete commanded state.");
        if (failure is null) return;
        _rendererCommands[index] = _rendererCommands[index] with { Failure = failure.Message };
        _failure ??= failure.Message; if (_rendererAdmitted) { Attach(failure); AttachRenderer(failure); }
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void StoreRendererPrefix(IRetainedSeekLineRendererAccess access, int occurrence,
        long line, NativeVector3BufferBits[] prefix, bool restore)
    {
        ObserveRendererExpected(access);
        var before = _rendererExpectedScratch!;
        var target = TargetBuffer(before, prefix);
        var index = AddRendererStep(restore ? SeekLineRendererCommandKind.RestorePrefix : SeekLineRendererCommandKind.Prefix,
            occurrence, line, before, target);
        _rendererCommands[index] = _rendererCommands[index] with { PrefixLength = prefix.Length };
        Exception? failure = null;
        try
        {
            void Verify() { if (restore) ObserveRendererExpected(access); else VerifyRendererInputs(access); }
            var result = access.WriteRendererPrefix(before, prefix, Verify, () => AdmitRendererStep(index));
            if (result.StoreEntered) ConfirmRendererEntry(index, returned: true);
            if (!SameBuffer(result.Before, before) || !SameBuffer(result.After, target))
                throw new InvalidOperationException("The renderer prefix result differs from its original full-array plan.");
        }
        catch (Exception error)
        {
            failure = error; AdoptCleanup(error);
            if (error.Data["LineVector3BufferStoreEntered"] is true) ConfirmRendererEntry(index, returned: false);
        }
        FinishRendererStep(access, index, failure);
    }

    private void DispatchRenderer(IRetainedSeekLineRendererAccess access, int occurrence, long line, int count)
    {
        VerifyRendererInputs(access);
        var before = _rendererExpected.Single(value => value.Line == line);
        var target = before with { SavedArray = _rendererExpectedScratch!.Array.ToInt64(), SavedCount = count,
            SavedArrayLength = _rendererExpectedScratch.Length };
        var index = AddRendererStep(SeekLineRendererCommandKind.SetPositions, occurrence, line,
            beforeSaved: before, targetSaved: target);
        Exception? failure = null;
        try
        {
            access.InvokeRendererSetPositions(line, count, () => VerifyRendererInputs(access), () => AdmitRendererStep(index));
            ConfirmRendererEntry(index, returned: true);
        }
        catch (Exception error)
        {
            failure = error; AdoptCleanup(error);
            if (error.Data["LineSetPositionsNativeEntered"] is true)
                ConfirmRendererEntry(index, error.Data["LineSetPositionsReturned"] is true);
        }
        FinishRendererStep(access, index, failure);
    }

    private void RestoreRendererField(IRetainedSeekLineRendererAccess access, int occurrence, long line,
        bool array, SeekLineRendererSavedState target)
    {
        ObserveRendererExpected(access);
        var before = _rendererExpected.Single(value => value.Line == line);
        var after = array ? before with { SavedArray = target.SavedArray, SavedArrayLength = target.SavedArrayLength }
            : before with { SavedCount = target.SavedCount };
        if (before == after) return;
        var index = AddRendererStep(array ? SeekLineRendererCommandKind.RestoreArray : SeekLineRendererCommandKind.RestoreCount,
            occurrence, line, beforeSaved: before, targetSaved: after);
        Exception? failure = null;
        try
        {
            access.RestoreRendererField(line, array, array ? before.SavedArray : before.SavedCount,
                array ? after.SavedArray : after.SavedCount, () => ObserveRendererExpected(access), () => AdmitRendererStep(index));
            ConfirmRendererEntry(index, returned: true);
        }
        catch (Exception error)
        {
            failure = error; AdoptCleanup(error);
            if (error.Data["LineRendererSavedFieldStoreEntered"] is true) ConfirmRendererEntry(index, returned: false);
        }
        FinishRendererStep(access, index, failure);
    }

    internal void RestoreDynamicRendererSavedValues()
    {
        RevokeTargetOwnershipAcknowledgement();
        RendererOperation((_, access) =>
        {
            if (!_rendererPrepared || _rendererStarting || _rendererRestorationEntered)
                throw new InvalidOperationException("Selected renderer restoration runs once after renderer preparation.");
            _rendererRestorationEntered = true;
            ObserveRendererExpected(access);
            // One sequence covers both scratch overwrites and saved fields.
            // Replaying SetPositions here would introduce new Unity effects.
            foreach (var index in _rendererForwardEntered.AsEnumerable().Reverse())
            {
                ObserveRendererExpected(access);
                var command = _rendererCommands[index];
                if (command.Kind == SeekLineRendererCommandKind.SetPositions)
                {
                    var current = _rendererExpected.Single(value => value.Line == command.Line);
                    var before = command.BeforeSaved!; var target = command.TargetSaved!;
                    if (!SameRendererEdges(current, before)
                        || current.SavedArray != before.SavedArray && current.SavedArray != target.SavedArray
                        || current.SavedCount != before.SavedCount && current.SavedCount != target.SavedCount)
                    { RendererUnknown(); RequireRendererKnown(); }
                    RestoreRendererField(access, command.Occurrence, command.Line, array: false, before);
                    RestoreRendererField(access, command.Occurrence, command.Line, array: true, before);
                }
                else if (command.Kind == SeekLineRendererCommandKind.ProducerControl)
                {
                    if (_rendererExpectedControl == command.BeforeControl) continue;
                    if (_rendererExpectedControl != command.TargetControl)
                    { RendererUnknown(); RequireRendererKnown(); }
                    var patch = command.ControlPatch!.Value;
                    var before = command.BeforeControl!;
                    var bits = patch.Field switch
                    {
                        NativeLineProducerField.StartPosition => before.StartPosition,
                        NativeLineProducerField.EndPosition => before.EndPosition,
                        NativeLineProducerField.NewLinePosition => before.NewLinePosition,
                        _ => new NativeVector3BufferBits(before.EaseType, 0, 0)
                    };
                    StoreProducerControl(access, command.Occurrence, command.Line, patch with { Value = bits }, restore: true);
                }
                else if (command.Kind == SeekLineRendererCommandKind.ArraySlice)
                {
                    var slice = command.ArraySlice!.Value;
                    var current = _rendererExpectedScratch!.Items[slice.Index];
                    if (current == command.BeforePoint) continue;
                    if (current != command.TargetPoint) { RendererUnknown(); RequireRendererKnown(); }
                    StoreProducerSlice(access, command.Occurrence, command.Line,
                        slice with { Value = command.BeforePoint!.Value }, restore: true);
                }
                else
                {
                    if (SameBuffer(_rendererExpectedScratch!, command.BeforeScratch!)) continue;
                    if (!SameBuffer(_rendererExpectedScratch!, command.TargetScratch!))
                    { RendererUnknown(); RequireRendererKnown(); }
                    // The journal owns the full original selected prefix,
                    // including equal trailing points and raw NaN/-0 bits.
                    StoreRendererPrefix(access, command.Occurrence, command.Line,
                        command.BeforeScratch!.Items.Take(command.PrefixLength).ToArray(), restore: true);
                }
            }
            ObserveRendererExpected(access);
            if (!SameBuffer(_rendererExpectedScratch!, _rendererBaselineScratch!)
                || !_rendererExpected.SequenceEqual(_rendererBaseline) || _rendererExpectedControl != _rendererBaselineControl)
                throw new InvalidOperationException("Selected renderer storage did not return to the original complete baseline.");
            return 0;
        });
        _rendererRestorationFinished = true;
    }

    internal bool VerifyAppliedDynamicRendererValues() => RendererOperation((_, access) =>
    {
        if (!_rendererExecutionFinished || _rendererRestorationEntered)
            throw new InvalidOperationException("Complete the original dynamic renderer execution before confirming its selected values.");
        ObserveRendererExpected(access); return true;
    });

    internal bool VerifyRestoredDynamicRendererSavedValues()
    {
        var verified = RendererOperation((_, access) =>
        {
            if (!_rendererRestorationFinished) throw new InvalidOperationException("Restore selected renderer values before verification.");
            ObserveRendererExpected(access);
            return SameBuffer(_rendererExpectedScratch!, _rendererBaselineScratch!) && _rendererExpected.SequenceEqual(_rendererBaseline)
                && _rendererExpectedControl == _rendererBaselineControl;
        });
        _rendererRestoredVerified = verified; return verified;
    }

    private SeekLineRendererJournal RendererJournalCore()
        => new(_rendererPrepared, _rendererExecutionEntered, _rendererExecutionFinished,
            _rendererRestorationEntered, _rendererRestorationFinished, _rendererRestoredVerified,
            _rendererUnrecognized, _rendererAdmitted || _rendererUnrecognized, _retained,
            _rendererDispatched, _rendererCulled, _rendererBaselineScratch, _rendererExpectedScratch,
            Array.AsReadOnly(_rendererBaseline.ToArray()), Array.AsReadOnly(_rendererExpected.ToArray()),
            _rendererPlan?.Occurrences ?? Array.Empty<SeekLineRingDynamicOccurrence>(),
            Array.AsReadOnly(_rendererCommands.ToArray()), _source, _rendererAdmitted ? this : null, _sourceCleanupOwner)
        { BaselineControl = _rendererBaselineControl, ExpectedControl = _rendererExpectedControl };

    internal SeekLineRendererJournal GetDynamicRendererJournal() { Diagnostic(); return RendererJournalCore(); }

    private void AttachRenderer(Exception error)
    {
        if (!_rendererAdmitted) return;
        error.Data["SeekLineRendererEffectsOwner"] = this;
        if (_source is not null) error.Data["SeekLineRendererOriginalSource"] = _source;
        error.Data["SeekLineRendererJournal"] = RendererJournalCore();
    }
}
