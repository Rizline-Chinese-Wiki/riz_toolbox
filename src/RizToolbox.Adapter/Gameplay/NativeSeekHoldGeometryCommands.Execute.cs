using System.Runtime.ExceptionServices;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldGeometryCommands
{
    internal void Execute() => Operation(() =>
    {
        if (!_prepared || _executionEntered || _restorationEntered) throw new InvalidOperationException("Prepared Hold geometry executes once only.");
        _executionEntered = true; VerifySource();
        if (!SeekHoldGeometryState.Same(Read(), CurrentSnapshot()))
            throw new InvalidOperationException("The complete prepared Hold geometry changed before execution.");
        for (var index = 0; index < _plan!.Steps.Count; ++index) ExecuteOne(index);
        VerifySource();
        if (!SeekHoldGeometryState.Same(Read(), CurrentSnapshot()))
            throw new InvalidOperationException("The complete Hold geometry differs after its command sequence.");
        _stateTargets = new(Array.AsReadOnly(_plan.HoldNotes.ToArray()), Array.AsReadOnly(_plan.RunningHoldNotes.ToArray()),
            Array.AsReadOnly(_noteTargets.Select(x => x with { }).ToArray()),
            new(_plan.Request.ChartTime, _plan.Request.Speed, _lastPendingLength));
        _executionFinished = true; return 0;
    });
    private void ExecuteOne(int index)
    {
        var step = _plan!.Steps[index]; var before = Read(step.Ordinal);
        if (!MatchesCurrent(before)) throw new InvalidOperationException("Selected Hold geometry changed outside the observed command sequence.");
        var row = new SeekHoldGeometryCommandEffects(index, step, null, before, null, false, false, false, false, false, false, null);
        _rows[index] = row;
        SeekHoldGeometryOperation? operation;
        try { operation = Resolve(step, before); }
        catch (Exception error) { _rows[index] = row with { Failure = error.Message }; throw; }
        _rows[index] = row with { Operation = operation };
        if (operation is null)
        {
            _rows[index] = _rows[index]! with { After = before, NoWriteRequired = true, CommandedPropertiesObserved = true };
            return;
        }
        Exception? failure = null;
        try
        {
            _access.Dispatch(operation, before, () =>
            {
                Local();
                if (_rows[index]!.NativeEntered) throw new InvalidOperationException("The final Hold geometry dispatch guard entered twice.");
                _rows[index] = _rows[index]! with { NativeEntered = true, ExternalEffectsUnclosed = true };
                ++_attempted; _externalEffects = true; _retained = true;
            });
            Local(); _rows[index] = _rows[index]! with { NativeReturned = true };
        }
        catch (Exception error) { failure = error; }
        if (_rows[index]!.NativeEntered)
        {
            try
            {
                var after = Read(step.Ordinal);
                _rows[index] = _rows[index]! with { After = after };
                var observed = CommandObserved(operation, before, after);
                _rows[index] = _rows[index]! with { CommandedPropertiesObserved = observed,
                    SideEffectsUnknown = failure is not null || !observed, Failure = failure?.Message };
                if (observed)
                {
                    foreach (var transform in after.Transforms) _transforms[transform.Reference] = transform;
                    _current = SeekHoldGeometryState.Merge(_current!, after);
                }
                else failure ??= new InvalidOperationException("Native Hold geometry has an unrecognized visible post-state.");
            }
            catch (Exception observation) { failure ??= observation; }
            if (failure is not null) { _unknown = true; _retained = true; }
        }
        if (failure is not null)
        {
            _rows[index] = _rows[index]! with { Failure = failure.Message, SideEffectsUnknown = _rows[index]!.NativeEntered };
            if (_rows[index]!.NativeEntered) failure.Data["SeekHoldGeometryEffectsOwner"] = this;
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
    private bool CommandObserved(SeekHoldGeometryOperation operation, SeekHoldGeometrySnapshot before, SeekHoldGeometrySnapshot after)
    {
        if (!SeekHoldGeometryState.SameBasis(before, after) || before.Notes.Count != after.Notes.Count
            || before.Notes.Where((x, i) => !SeekHoldGeometryState.Same(x, after.Notes[i])).Any()) return false;
        var observed = after.Transforms.ToDictionary(x => x.Reference);
        if (before.Transforms.Any(x => !observed.ContainsKey(x.Reference))
            || observed.Keys.Any(x => !_transforms.ContainsKey(x))) return false;
        if (!observed.TryGetValue(operation.Transform, out var target)) return false;
        var original = _transforms[operation.Transform];
        bool Descendant(long reference)
        {
            var remaining = _transforms.Count;
            for (var cursor = reference; cursor != 0 && remaining-- > 0; cursor = _transforms[cursor].Parent)
                if (cursor == operation.Transform) return true;
            return false;
        }
        foreach (var value in after.Transforms)
        {
            if (value.Reference == operation.Transform) continue;
            var old = _transforms[value.Reference];
            if (value.Parent != old.Parent || !SeekHoldGeometryState.Same(value.LocalPosition, old.LocalPosition)
                || !SeekHoldGeometryState.Same(value.LocalRotation, old.LocalRotation) || !SeekHoldGeometryState.Same(value.LocalScale, old.LocalScale)
                || !Descendant(value.Reference) && !SeekHoldGeometryState.Same(value.WorldPosition, old.WorldPosition)) return false;
        }
        if (operation.Kind == SeekHoldGeometryOperationKind.SetParent)
            return target.Parent == operation.Parent;
        if (target.Parent != original.Parent || !SeekHoldGeometryState.Same(target.LocalRotation, original.LocalRotation)) return false;
        return operation.Kind switch
        {
            SeekHoldGeometryOperationKind.SetLocalPosition => SeekHoldGeometryState.Same(target.LocalPosition, operation.Value)
                && SeekHoldGeometryState.Same(target.LocalScale, original.LocalScale),
            SeekHoldGeometryOperationKind.SetWorldPosition => SeekHoldGeometryState.Same(target.WorldPosition, operation.Value)
                && SeekHoldGeometryState.Same(target.LocalScale, original.LocalScale),
            SeekHoldGeometryOperationKind.SetLocalScale => SeekHoldGeometryState.Same(target.LocalScale, operation.Value)
                && SeekHoldGeometryState.Same(target.LocalPosition, original.LocalPosition)
                && SeekHoldGeometryState.Same(target.WorldPosition, original.WorldPosition),
            _ => false
        };
    }
}
