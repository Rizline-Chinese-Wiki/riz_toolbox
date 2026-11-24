namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekRendererCommands
{
    internal static NativeSeekRendererCommands Prepare(INativeSeekRendererAccess access, IReadOnlyList<SeekRendererCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(access); access.VerifyOwnerThread();
        var value = new NativeSeekRendererCommands(access);
        value.ReserveOwner(); // Rejection precedes adoption of caller-owned access.
        return PrepareOwned(value, commands);
    }
    internal static NativeSeekRendererCommands Prepare(Func<INativeSeekRendererAccess> createAccess, IReadOnlyList<SeekRendererCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(createAccess);
        var value = new NativeSeekRendererCommands(null); value.ReserveOwner();
        try { value._access = createAccess() ?? throw new InvalidOperationException("The independent renderer access was not created."); }
        catch (Exception error)
        {
            value._disposed = true; value._failure = error.Message;
            if (error.Data["SeekRendererCleanupFailure"] is Exception || error.Data["SeekRuntimeNoteCleanupFailure"] is Exception)
            {
                value._constructionFailure = error; value._cleanupFailure = error.Message; value._retained = true;
                error.Data["SeekRendererEffectsSourceOwner"] = error.Data["SeekRendererEffectsOwner"];
                error.Data["SeekRendererEffectsOwner"] = value;
            }
            else value.ReleaseOwner();
            throw;
        }
        return PrepareOwned(value, commands);
    }
    private static NativeSeekRendererCommands PrepareOwned(NativeSeekRendererCommands value, IReadOnlyList<SeekRendererCommand> commands)
    {
        var access = value._access;
        try
        {
            access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                value.VerifySource(); value.CopyCommands(commands);
                value._baseline = value.Read(); value._current = value._baseline;
                value.Remember(value._baseline);
                SeekRendererState.Validate(value._baseline, value._references);
                var budget = SeekRendererState.Estimate(value._baseline, value._commands);
                if ((long)value._baseline.Children.Count + budget.NewChildren > MaximumTotalCapacity
                    || (long)value._baseline.Transforms.Count + budget.NewChildren > MaximumTotalCapacity)
                    throw new NotSupportedException("Projected renderer growth exceeds the complete retained child/Transform observation budget.");
                value.Local(); access.ReserveEffects(budget.NewChildren, budget.NewBackings, budget.Capacity); value.Local();
                value.VerifySource();
                if (!SeekRendererState.Same(value.Read(), value._baseline))
                    throw new InvalidOperationException("The complete actual renderer/pool graph changed during preparation.");
                value._prepared = true; return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekRendererEffectsOwner"] = value; error.Data["SeekRendererCleanupFailure"] = cleanup; }
            throw;
        }
    }
    private void Lifetime()
    {
        _access.VerifyOwnerThread();
        if (!ReferenceEquals(_registered, this)) throw new InvalidOperationException("A copied renderer owner cannot use the original retained graph.");
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekRendererCommands));
    }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private void VerifySource() { Local(); _access.VerifySource(); Local(); }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread(); Lifetime();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A renderer command operation cannot reenter itself.");
        _busy = true; Exception? failure = null;
        try { Local(); var result = operation(); Local(); return result; }
        catch (Exception error)
        {
            failure = error; _failure ??= error.Message;
            if (_executionEntered && _attempted != 0)
            {
                _unknown = true; _retained = true;
                error.Data["SeekRendererEffectsOwner"] = this;
            }
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposed)
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) throw;
                    failure.Data["SeekRendererEffectsOwner"] = this; failure.Data["SeekRendererCleanupFailure"] = cleanup;
                }
        }
    }
    private void CopyCommands(IReadOnlyList<SeekRendererCommand> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Local(); var notes = _access.RuntimeNotes; Local();
        Local(); var count = notes.Count; Local();
        if (count is < 0 or > MaximumNotes) throw new NotSupportedException("The actual renderer note map exceeds its complete bound.");
        _references = new long[count]; var byReference = new Dictionary<long, int>();
        for (var ordinal = 0; ordinal < count; ++ordinal)
        {
            Local(); var note = notes[ordinal]; Local();
            if (note is null || note.Reference == 0 || !byReference.TryAdd(note.Reference, ordinal))
                throw new InvalidDataException("The independently owned renderer NoteView has an absent or duplicate identity.");
            _references[ordinal] = note.Reference;
        }
        Local(); if (notes.Count != count || input.Count != count) throw new InvalidDataException("Renderer commands must cover every actual note exactly once."); Local();
        _commands = new SeekRendererCommand[count]; _rows = new SeekRendererCommandEffects?[count]; _ordinals = new int[count];
        var seen = new HashSet<long>();
        for (var index = 0; index < count; ++index)
        {
            Local(); var command = input[index]; Local();
            if (command is null || !Enum.IsDefined(typeof(SeekRendererCommandKind), command.Kind) || !byReference.TryGetValue(command.Reference, out var ordinal) || !seen.Add(command.Reference))
                throw new InvalidDataException("Renderer commands require each independent actual note once and a supported explicit command.");
            _commands[index] = command with { }; _ordinals[index] = ordinal;
        }
        Local(); if (input.Count != count) throw new InvalidOperationException("The complete renderer command input changed during copying."); Local();
        // Input order is the explicit native order. It is not reconstructed
        // from chart positions or the independently owned NoteView's order.
    }
    private SeekRendererGraphSnapshot Read(int? selected = null)
    {
        Local(); var snapshot = _access.ReadSnapshot(selected); Local();
        return SeekRendererState.Copy(snapshot, Local);
    }
    internal SeekRendererEffectsJournal Execute() => Operation(() =>
    {
        if (!_prepared || _executionEntered) throw new InvalidOperationException("Prepared renderer commands execute once only.");
        _executionEntered = true; VerifySource();
        if (!SeekRendererState.Same(Read(), _current!)) throw new InvalidOperationException("The prepared complete renderer/pool source changed before execution.");
        for (var index = 0; index < _commands.Length; ++index) ExecuteOne(index);
        VerifySource();
        if (!SeekRendererState.Same(Read(), _current!)) throw new InvalidOperationException("The complete renderer/pool graph differs after the command sequence.");
        _executionFinished = true; return GetJournal();
    });
    private void ExecuteOne(int index)
    {
        var command = _commands[index]; var ordinal = _ordinals[index];
        var before = Read(ordinal);
        if (!SeekRendererState.IsProjection(_current!, before)) throw new InvalidOperationException("A selected renderer/pool source changed outside the recognized command sequence.");
        var row = new SeekRendererCommandEffects(index, command, before, null, false, false, false, false, false, false, null);
        _rows[index] = row;
        if (command.Kind == SeekRendererCommandKind.Preserve)
        {
            _rows[index] = row with { After = before, ExpectedReachableStateObserved = true };
            return;
        }
        Exception? failure = null;
        try
        {
            _access.Dispatch(ordinal, command.Kind, before, () =>
            {
                Local();
                if (_rows[index]!.NativeEntered) throw new InvalidOperationException("The final native renderer dispatch guard entered twice.");
                var enteredRow = _rows[index]! with { NativeEntered = true };
                _rows[index] = enteredRow; ++_attempted;
            });
            Local(); _rows[index] = _rows[index]! with { NativeReturned = true };
        }
        catch (Exception error) { failure = error; }
        var entered = _rows[index]!.NativeEntered;
        if (entered)
        {
            var knownBefore = new HashSet<long>(_knownReferences);
            SeekRendererGraphSnapshot? after = null;
            try
            {
                after = Read(ordinal);
                _rows[index] = _rows[index]! with { After = after };
                var effect = SeekRendererState.Recognize(_current!, before, after, command, failure is not null, knownBefore, out var retiredBackings);
                _rows[index] = _rows[index]! with { ExpectedReachableStateObserved = effect.Recognized,
                    GrowthObserved = effect.Growth, DeferredEffectsPending = effect.Deferred,
                    SideEffectsUnknown = failure is not null || !effect.Recognized, Failure = failure?.Message ?? effect.Failure };
                _deferred |= effect.Deferred;
                if (effect.Recognized)
                {
                    if (_restorePrepared && _access is INativeSeekRendererRestoreAccess inverse)
                        inverse.AcceptRecognizedAllocations(after, retiredBackings);
                    _current = SeekRendererState.Merge(_current!, after);
                }
                if (!effect.Recognized && failure is null) failure = new InvalidOperationException(effect.Failure ?? "Native renderer effects have no recognized complete reachable state.");
            }
            catch (Exception observation)
            {
                _rows[index] = _rows[index]! with { SideEffectsUnknown = true, Failure = observation.Message };
                failure ??= observation;
            }
            finally { if (after is not null) Remember(after); }
            if (failure is not null) { _unknown = true; _retained = true; }
        }
        else if (failure is not null) _rows[index] = _rows[index]! with { Failure = failure.Message };
        if (failure is not null)
        {
            _failure ??= failure.Message;
            if (entered) failure.Data["SeekRendererEffectsOwner"] = this;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
    public void Dispose()
    {
        if (!ReferenceEquals(_registered, this)) throw new InvalidOperationException("A copied renderer owner cannot dispose the original retained graph.");
        if (_access is null)
        {
            if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("The retained renderer construction responsibility belongs to its owner thread.");
            _disposed = true;
            if (_constructionFailure is not null) throw ResponsibilityError("Renderer source construction cleanup retains its original owner and failure.");
            ReleaseOwner(); return;
        }
        _access.VerifyOwnerThread();
        if (_cleanupBusy || _notifyingDisposal) return;
        _disposed = true; _notifyingDisposal = true;
        try { _access.RequestDispose(); }
        catch (Exception error) { _cleanupFailure ??= error.Message; _retained = true; }
        finally { _notifyingDisposal = false; }
        if (!_busy) Release();
    }
    private Exception ResponsibilityError(string message)
    {
        var error = new InvalidOperationException(message); error.Data["SeekRendererEffectsOwner"] = this; return error;
    }
    private void Release()
    {
        if (_released || _cleanupBusy) return;
        if (_unknown || _restorePrepared && HasRecognizedAllocations && !_restored && !_poolOwnershipAcknowledged)
        {
            _retained = true;
            throw ResponsibilityError("Native renderer effects or allocated objects lack a confirmed original-pool handoff or completed inverse; logical disposal retains their independent owner and roots.");
        }
        _cleanupBusy = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error) { _cleanupFailure ??= error.Message; _retained = true; error.Data["SeekRendererEffectsOwner"] = this; throw; }
        finally { _cleanupBusy = false; }
        if (_cleanupFailure is not null) throw ResponsibilityError("Renderer source cleanup retains an unknown native responsibility: " + _cleanupFailure);
        _retained = false;
        ReleaseOwner();
    }
}
