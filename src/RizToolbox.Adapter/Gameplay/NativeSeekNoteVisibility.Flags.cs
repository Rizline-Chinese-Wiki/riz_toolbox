namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekNoteFlagsParticipant
{
    private NativeSeekNoteVisibilityCommands? _visibilityOwner;
    internal IReadOnlyList<SeekNoteFlagTarget> ReadVisibilityBaseline() => Operation(() =>
    {
        if (!_prepared || _entered || _restoring || !Read().SequenceEqual(_baseline))
            throw new InvalidOperationException("The original typed flag preparation is not current.");
        return (IReadOnlyList<SeekNoteFlagTarget>)Array.AsReadOnly(_baseline.ToArray());
    });
    internal IReadOnlyList<SeekNoteFlagTarget> VerifyVisibilityInput() => Operation(() =>
    {
        if (!_committed || _restoring || !Read().SequenceEqual(_targets))
            throw new InvalidOperationException("The complete typed note-flags result is not current.");
        _confirmed = true; SideEffectsUnknown = false;
        return (IReadOnlyList<SeekNoteFlagTarget>)Array.AsReadOnly(_targets.ToArray());
    });
    internal void AcceptVisibilityResult(NativeSeekNoteVisibilityCommands owner, IReadOnlyList<SeekNoteFlagTarget> actual) => Operation(() =>
    {
        if (!ReferenceEquals(owner.Flags, this) || !owner.ActiveReturned || owner.SetReturned || _visibilityOwner is not null
            || !_committed || _restoring)
            throw new InvalidOperationException("The actual ActiveNotes result has no matching private producer owner.");
        var copied = Copy(actual).ToDictionary(row => row.Reference);
        var next = _targets.Select(old => copied.TryGetValue(old.Reference, out var row)
            && row.Destroyed == old.Destroyed && row.Missed == old.Missed
            && (!old.IsActive || row.IsActive) && (!old.Destroyed || row.IsActive == old.IsActive)
            ? row : throw new InvalidOperationException("The actual visibility result has another flag write set.")).ToArray();
        if (copied.Count != next.Length || !Read().SequenceEqual(next))
            throw new InvalidOperationException("The independently rooted flags do not match the actual producer result.");
        _visibilityOwner = owner; _targets = next; _confirmed = true; SideEffectsUnknown = false; return 0;
    });
    internal IReadOnlyList<SeekNoteFlagTarget> ReadRestoredVisibilityFlags() => Operation(() =>
    {
        if (!_restored || !Read().SequenceEqual(_baseline)) throw new InvalidOperationException("The original actual note flags are not restored.");
        return (IReadOnlyList<SeekNoteFlagTarget>)Array.AsReadOnly(_baseline.ToArray());
    });
}
