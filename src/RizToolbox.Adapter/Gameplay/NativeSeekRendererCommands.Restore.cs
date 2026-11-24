namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekRendererCommands
{
    private bool _restorePrepared, _restoreEntered, _restored;

    internal void RequireRestorable() => Operation(() =>
    {
        if (!_prepared || _executionEntered || _access is not INativeSeekRendererRestoreAccess)
            throw new NotSupportedException("This renderer source has no prepared native inverse.");
        VerifySource();
        if (!SeekRendererState.Same(Read(), _baseline!))
            throw new InvalidOperationException("The renderer baseline changed before inverse preparation.");
        var plans = _baseline!.Plans.ToDictionary(value => value.Reference);
        var notes = _baseline.Notes.ToDictionary(value => value.Reference);
        var children = _baseline.Children.ToDictionary(value => value.Reference);
        if (_baseline.Pools.Any(pool => pool.Dirty.Size != 0)
            || _commands.Where(command => command.Kind != SeekRendererCommandKind.Preserve)
                .SelectMany(command => notes[command.Reference].Plans).Select(reference => plans[reference])
                .Any(plan => plan.Child != 0 && children[plan.Child].Dirty))
            throw new NotSupportedException("This renderer target has deferred dirty pool consumers; their actual completion is required before reversible Seek.");
        ((INativeSeekRendererRestoreAccess)_access).PrepareRestore(_baseline);
        _restorePrepared = true; return 0;
    });

    internal void VerifyExpectedValues() => Operation(() =>
    {
        VerifySource();
        if (!SeekRendererState.Same(Read(), _current!))
            throw new InvalidOperationException("The current renderer state differs from the retained command result.");
        return 0;
    });

    // Note flags/cursors and final target geometry have separate owners in the
    // composed transaction. This check retains exact note/plan/pool identity and
    // all pool storage while leaving those separately confirmed values to them.
    internal void VerifyExpectedPoolState() => Operation(() =>
    {
        VerifyExpectedPoolStateCore(); return 0;
    });
    private void VerifyExpectedPoolStateCore()
    {
        VerifySource(); var actual = Read(); var expected = _current!;
        var notes = expected.Notes.ToDictionary(note => note.Reference);
        if (actual.Notes.Count != expected.Notes.Count || actual.Notes.Any(note => !notes.ContainsKey(note.Reference)))
            throw new InvalidOperationException("The renderer note membership changed.");
        if (!actual.Transforms.Select(value => value.Reference).ToHashSet()
                .SetEquals(expected.Transforms.Select(value => value.Reference)))
            throw new InvalidOperationException("The renderer Transform membership changed.");
        expected = SeekRendererState.WithExternalTransforms(expected, actual);
        var projected = actual with
        {
            Notes = actual.Notes.Select(note => note with { Index = notes[note.Reference].Index,
                IsActive = notes[note.Reference].IsActive, Destroyed = notes[note.Reference].Destroyed,
                Missed = notes[note.Reference].Missed }).ToArray()
        };
        if (!SeekRendererState.Same(projected, expected))
            throw new InvalidOperationException("The renderer plans or complete pool storage changed outside the transaction.");
    }

    internal void Restore()
    {
        RevokePoolOwnershipAcknowledgement();
        Operation(() =>
    {
        if (!_restorePrepared || _restoreEntered)
            throw new InvalidOperationException("A prepared renderer inverse restores once only.");
        _restoreEntered = true; VerifySource();
        if (_unknown || _deferred || _rows.Any(row => row is { DeferredEffectsPending: true }))
            throw new InvalidOperationException("Unknown or deferred renderer effects cannot be restored by overwriting pool storage.");
        var actual = Read();
        var expected = SeekRendererState.WithExternalTransforms(_current!, actual);
        if (!SeekRendererState.Same(actual, expected))
            throw new InvalidOperationException("Another writer replaced the exact renderer state before restoration.");
        var recognized = new List<SeekRendererGraphSnapshot> { _baseline! };
        foreach (var row in _rows)
            if (row is { ExpectedReachableStateObserved: true })
            { if (row.Before is { } before) recognized.Add(before); if (row.After is { } after) recognized.Add(after); }
        recognized.Add(_current!);
        ((INativeSeekRendererRestoreAccess)_access).Restore(expected, _baseline!, recognized, () =>
        {
            Local(); _retained = true;
        });
        actual = Read();
        var restored = SeekRendererState.WithExternalTransforms(_baseline!, actual);
        if (!SeekRendererState.Same(actual, restored))
            throw new InvalidOperationException("The complete original renderer graph did not read back after restoration.");
        _current = restored; _restored = true; _retained = false;
        return 0;
        });
    }

    internal bool VerifyRestoredValues() => Operation(() =>
    {
        VerifySource();
        var actual = Read();
        return (_restored || !_executionEntered) && SeekRendererState.Same(actual,
            SeekRendererState.WithExternalTransforms(_baseline!, actual));
    });
}

internal static partial class SeekRendererState
{
    // These external transforms are confirmed by their Canvas/line owners.
    // This producer owns only its actual child transforms and pool storage.
    internal static SeekRendererGraphSnapshot WithExternalTransforms(SeekRendererGraphSnapshot expected,
        SeekRendererGraphSnapshot actual)
    {
        var owned = expected.Children.Select(child => child.Transform).ToHashSet();
        var values = actual.Transforms.ToDictionary(value => value.Reference);
        if (expected.Transforms.Count != values.Count || expected.Transforms.Any(value => !values.ContainsKey(value.Reference)))
            throw new InvalidOperationException("The renderer Transform membership changed.");
        return expected with { Transforms = expected.Transforms.Select(value => owned.Contains(value.Reference)
            ? value : values[value.Reference]).ToArray() };
    }
}
