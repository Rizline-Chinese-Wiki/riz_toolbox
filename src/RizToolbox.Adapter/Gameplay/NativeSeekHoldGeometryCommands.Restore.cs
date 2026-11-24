using System.Runtime.ExceptionServices;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldGeometryCommands
{
    private readonly List<SeekHoldGeometryRestoreEffect> _restoreEffects = new();
    private bool _restorationEntered, _restorationFinished;

    internal IReadOnlyList<SeekHoldGeometryRestoreEffect> RestorationJournal
    {
        get { Lifetime(); return Array.AsReadOnly(_restoreEffects.ToArray()); }
    }

    internal void VerifyPrepared() => Operation(() =>
    {
        if (!_prepared || _executionEntered || _restorationEntered)
            throw new InvalidOperationException("Hold geometry preparation must precede execution and restoration.");
        VerifyExpectedGeometry(); return 0;
    });

    internal bool VerifyAppliedValues() => Operation(() =>
    {
        if (!_executionFinished || _restorationEntered)
            throw new InvalidOperationException("Complete Hold geometry execution before confirming its applied values.");
        VerifyExpectedGeometry(); return true;
    });

    // The following Hold-state participant owns only lists/control and these
    // two scalar fields. All geometry, other flags and identities remain ours.
    internal bool VerifyAppliedTransforms() => Operation(() =>
    {
        if (!_executionFinished || _restorationEntered || _unknown)
            throw new InvalidOperationException("The original applied Hold geometry is unavailable.");
        VerifySource(); var actual = Read(); var expected = CurrentSnapshot();
        var notes = expected.Notes.ToDictionary(row => row.Reference);
        if (actual.Notes.Count != notes.Count || actual.Notes.Any(row => !notes.ContainsKey(row.Reference)))
            throw new InvalidOperationException("The complete Hold geometry membership changed.");
        var selected = actual with
        {
            Lists = expected.Lists, Control = expected.Control,
            Notes = actual.Notes.Select(row => row with { IsStaticNote = notes[row.Reference].IsStaticNote,
                LastFrameHoldLineLength = notes[row.Reference].LastFrameHoldLineLength }).ToArray()
        };
        if (!SeekHoldGeometryState.Same(selected, expected))
            throw new InvalidOperationException("The complete applied Hold Transform state changed outside its command owner.");
        return true;
    });

    private void VerifyExpectedGeometry()
    {
        if (_unknown) throw new InvalidOperationException("Unknown Hold effects forbid another mutation or inverse.");
        VerifySource();
        if (!SeekHoldGeometryState.Same(Read(), CurrentSnapshot()))
            throw new InvalidOperationException("The complete Hold geometry differs from this owner's expected state.");
        Local();
    }

    // Selected value recovery does not certify Unity callbacks or engine consumers.
    internal void RestoreSelectedValues()
    {
        RevokeTransformOwnershipAcknowledgement();
        Operation(() =>
        {
        if (!_prepared || _restorationEntered)
            throw new InvalidOperationException("Hold selected-value restoration is once-only after preparation.");
        VerifyExpectedGeometry(); _restorationEntered = true;
        var restored = new HashSet<long>();
        foreach (var row in _rows.Reverse())
        {
            if (row?.NativeEntered != true || row.Operation is not { } operation
                || !restored.Add(operation.Transform)) continue;
            var original = _baseline!.Transforms.Single(value => value.Reference == operation.Transform);
            foreach (var field in new[] { SeekHoldGeometryRestoreField.Parent,
                SeekHoldGeometryRestoreField.LocalRotation, SeekHoldGeometryRestoreField.LocalScale,
                SeekHoldGeometryRestoreField.LocalPosition })
                RestoreOne(operation.Ordinal, original, field);
        }
        VerifyExpectedGeometry();
        if (!SeekHoldGeometryState.Same(Read(), _baseline!))
            throw new InvalidOperationException("The complete original Hold geometry was not independently observed after its inverse.");
            _restorationFinished = true; return 0;
        });
    }

    internal bool VerifyRestoredValues() => Operation(() =>
    {
        if (!_restorationEntered || !_restorationFinished)
            throw new InvalidOperationException("Complete the Hold inverse before confirming original values.");
        VerifyExpectedGeometry(); return SeekHoldGeometryState.Same(Read(), _baseline!);
    });

    private static bool OriginalField(SeekHoldGeometryTransformState value,
        SeekHoldGeometryTransformState original, SeekHoldGeometryRestoreField field) => field switch
    {
        SeekHoldGeometryRestoreField.Parent => value.Parent == original.Parent,
        SeekHoldGeometryRestoreField.LocalPosition => SeekHoldGeometryState.Same(value.LocalPosition, original.LocalPosition),
        SeekHoldGeometryRestoreField.LocalRotation => SeekHoldGeometryState.Same(value.LocalRotation, original.LocalRotation),
        SeekHoldGeometryRestoreField.LocalScale => SeekHoldGeometryState.Same(value.LocalScale, original.LocalScale),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private void RestoreOne(int ordinal, SeekHoldGeometryTransformState original, SeekHoldGeometryRestoreField field)
    {
        VerifyExpectedGeometry(); var before = Read(ordinal);
        if (!MatchesCurrent(before)) throw new InvalidOperationException("The selected Hold inverse lost its own expected state.");
        var target = before.Transforms.Single(value => value.Reference == original.Reference);
        if (OriginalField(target, original, field)) return;
        var index = _restoreEffects.Count;
        _restoreEffects.Add(new(ordinal, original.Reference, field, before, null, false, false, false, null));
        Exception? failure = null;
        try
        {
            _access.RestoreTransform(ordinal, original, field, before, () =>
            {
                Local();
                if (_restoreEffects[index].NativeEntered)
                    throw new InvalidOperationException("The Hold inverse dispatch guard entered twice.");
                _restoreEffects[index] = _restoreEffects[index] with { NativeEntered = true };
                ++_attempted; _externalEffects = true; _retained = true;
            });
            Local(); _restoreEffects[index] = _restoreEffects[index] with { NativeReturned = true };
        }
        catch (Exception error) { failure = error; }
        if (_restoreEffects[index].NativeEntered)
        {
            try
            {
                var after = Read(ordinal);
                var observed = RestoreObserved(original, field, before, after);
                _restoreEffects[index] = _restoreEffects[index] with { After = after, OriginalValueObserved = observed };
                if (!observed) failure ??= new InvalidOperationException("The conditional Hold inverse has an unrecognized visible result.");
                else
                {
                    foreach (var transform in after.Transforms) _transforms[transform.Reference] = transform;
                    _current = SeekHoldGeometryState.Merge(_current!, after);
                }
            }
            catch (Exception observation) { failure ??= observation; }
        }
        if (failure is null) return;
        _restoreEffects[index] = _restoreEffects[index] with { Failure = failure.Message };
        if (_restoreEffects[index].NativeEntered) { _unknown = true; _retained = true; }
        failure.Data["SeekHoldGeometryEffectsOwner"] = this;
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private bool RestoreObserved(SeekHoldGeometryTransformState original, SeekHoldGeometryRestoreField field,
        SeekHoldGeometrySnapshot before, SeekHoldGeometrySnapshot after)
    {
        if (!SeekHoldGeometryState.SameBasis(before, after) || before.Notes.Count != after.Notes.Count
            || before.Notes.Where((value, i) => !SeekHoldGeometryState.Same(value, after.Notes[i])).Any()) return false;
        var values = after.Transforms.ToDictionary(value => value.Reference);
        if (before.Transforms.Count != values.Count || before.Transforms.Any(value => !values.ContainsKey(value.Reference))) return false;
        bool Descendant(long reference)
        {
            var remaining = _transforms.Count;
            for (var cursor = reference; cursor != 0 && remaining-- > 0; cursor = _transforms[cursor].Parent)
                if (cursor == original.Reference) return true;
            return false;
        }
        foreach (var old in before.Transforms)
        {
            var value = values[old.Reference];
            if (value.Reference != original.Reference)
            {
                if (value.Parent != old.Parent || !SeekHoldGeometryState.Same(value.LocalPosition, old.LocalPosition)
                    || !SeekHoldGeometryState.Same(value.LocalRotation, old.LocalRotation)
                    || !SeekHoldGeometryState.Same(value.LocalScale, old.LocalScale)
                    || !Descendant(value.Reference) && !SeekHoldGeometryState.Same(value.WorldPosition, old.WorldPosition)) return false;
                continue;
            }
            if (!OriginalField(value, original, field)) return false;
            if (field == SeekHoldGeometryRestoreField.Parent) continue;
            if (value.Parent != old.Parent
                || field != SeekHoldGeometryRestoreField.LocalPosition && !SeekHoldGeometryState.Same(value.LocalPosition, old.LocalPosition)
                || field != SeekHoldGeometryRestoreField.LocalRotation && !SeekHoldGeometryState.Same(value.LocalRotation, old.LocalRotation)
                || field != SeekHoldGeometryRestoreField.LocalScale && !SeekHoldGeometryState.Same(value.LocalScale, old.LocalScale)) return false;
        }
        return true;
    }
}
