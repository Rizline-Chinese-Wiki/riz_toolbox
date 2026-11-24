namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekNoteVisibilityHead(long Note, bool Static, SeekRendererTransformState Transform);
internal sealed record SeekNoteVisibilitySnapshot(float ChartTime, float Speed, float ControlSpeed,
    NativeNoteCursorState Cursors, IReadOnlyList<long> Basis, IReadOnlyList<SeekNoteVisibilityHead> Heads,
    IReadOnlyList<long> RenderNotes);
internal sealed record SeekNoteVisibilityEffects(bool Entered, bool ActiveReturned, bool SetReturned,
    bool TargetObserved, bool SideEffectsUnknown, string? Failure, bool GrowthObserved = false);
internal interface INativeSeekNoteVisibilityAccess
{
    void VerifyVisibilityProfile(ISeekNoteVisibilityNativeProfile profile);
    SeekNoteVisibilitySnapshot ReadVisibility(ISeekNoteVisibilityNativeProfile profile);
    void WriteVisibilityControl(SeekNoteVisibilitySnapshot expected, float speed, NativeNoteCursorState cursors, Action entered);
    void DispatchVisibility(ISeekNoteVisibilityNativeProfile profile, bool active, SeekNoteVisibilitySnapshot expected, Action entered);
    void RestoreVisibilityHeads(ISeekNoteVisibilityNativeProfile profile, SeekNoteVisibilitySnapshot expected,
        SeekNoteVisibilitySnapshot baseline, Action entered);
}

internal static class SeekNoteVisibilityState
{
    internal static bool Basis(SeekNoteVisibilitySnapshot a, SeekNoteVisibilitySnapshot b)
        => SeekRendererState.Float(a.ChartTime, b.ChartTime) && SeekRendererState.Float(a.Speed, b.Speed)
            && a.Basis.SequenceEqual(b.Basis) && a.RenderNotes.SequenceEqual(b.RenderNotes) && a.Heads.Count == b.Heads.Count
            && a.Heads.Zip(b.Heads).All(pair => pair.First.Note == pair.Second.Note
                && pair.First.Static == pair.Second.Static && pair.First.Transform.Reference == pair.Second.Transform.Reference);
    internal static bool Heads(SeekNoteVisibilitySnapshot a, SeekNoteVisibilitySnapshot b)
        => a.Heads.Count == b.Heads.Count && a.Heads.Zip(b.Heads).All(pair => pair.First.Note == pair.Second.Note
            && pair.First.Static == pair.Second.Static && SeekRendererState.Same(pair.First.Transform, pair.Second.Transform));
    internal static bool Same(SeekNoteVisibilitySnapshot a, SeekNoteVisibilitySnapshot b)
        => Basis(a, b) && Heads(a, b) && SeekRendererState.Float(a.ControlSpeed, b.ControlSpeed) && a.Cursors == b.Cursors;
}

/// <summary>The private producer ticket belongs to the original rooted renderer owner.
/// It never treats a copied graph as permission or estimates activation from time.</summary>
internal sealed class NativeSeekNoteVisibilityCommands
{
    private readonly NativeSeekRendererCommands _owner;
    internal readonly ISeekNoteVisibilityNativeProfile Profile;
    internal readonly NativeSeekNoteFlagsParticipant Flags;
    internal readonly float Time, Speed;
    internal readonly SeekNoteVisibilitySnapshot Baseline;
    internal SeekNoteVisibilitySnapshot Current;
    internal bool Executed, Restored, ControlEntered, NativeEntered, ActiveReturned, SetReturned, Observed, Unknown, Growth;
    internal string? Failure;
    internal NativeSeekNoteVisibilityCommands(NativeSeekRendererCommands owner, ISeekNoteVisibilityNativeProfile profile,
        NativeSeekNoteFlagsParticipant flags, float time, float speed, SeekNoteVisibilitySnapshot baseline)
    { _owner = owner; Profile = profile; Flags = flags; Time = time; Speed = speed; Baseline = baseline; Current = baseline; }
    internal SeekNoteVisibilityEffects Execute() => _owner.ExecuteNoteVisibility(this);
    internal void VerifyAppliedValues() => _owner.VerifyNoteVisibility(this);
    internal void VerifyAppliedValuesAfterHold(NativeSeekHoldGeometryCommands hold) => _owner.VerifyNoteVisibilityAfterHold(this, hold);
    internal void RestoreSelectedValues() => _owner.RestoreNoteVisibility(this);
    internal bool VerifyRestoredValues() => _owner.VerifyRestoredNoteVisibility(this);
    internal bool SideEffectsUnknown => Unknown;
    internal SeekNoteVisibilityEffects GetJournal() => new(NativeEntered || ControlEntered, ActiveReturned, SetReturned, Observed, Unknown, Failure, Growth);
}

internal sealed partial class NativeSeekRendererCommands
{
    private NativeSeekNoteVisibilityCommands? _visibility;
    internal bool HasVisibilityAllocationResponsibility => _visibility is { Growth: true };
    private NativeSeekNoteFlagsParticipant? _noteFlagsOwner;
    internal void RegisterNoteFlags(NativeSeekNoteFlagsParticipant flags) => Operation(() =>
    {
        if (_noteFlagsOwner is not null || !_prepared || _executionEntered)
            throw new InvalidOperationException("Register the original flags owner once before renderer execution.");
        var baseline = flags.ReadVisibilityBaseline();
        if (!SeekRendererState.Same(WithFlags(_baseline!, baseline), _baseline!))
            throw new InvalidOperationException("The flags owner did not capture this renderer's actual original flags.");
        _noteFlagsOwner = flags; return 0;
    });
    internal NativeSeekNoteVisibilityCommands PrepareNoteVisibility(ISeekNoteVisibilityNativeProfile profile,
        float targetChartTime, float targetSpeed, NativeSeekNoteFlagsParticipant flags) => Operation(() =>
    {
        if (!_executionFinished || _unknown || _visibility is not null || !ReferenceEquals(_noteFlagsOwner, flags)
            || _access is not INativeSeekNoteVisibilityAccess access)
            throw new InvalidOperationException("Visibility requires the same completed renderer owner and its real native access.");
        if (!float.IsFinite(targetChartTime) || !float.IsFinite(targetSpeed) || targetSpeed <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSpeed));
        access.VerifyVisibilityProfile(profile); VerifySource();
        var actual = Read(); var knownFlags = flags.VerifyVisibilityInput();
        var expected = SeekRendererState.WithExternalTransforms(WithFlags(_current!, knownFlags), actual);
        if (!SeekRendererState.Same(actual, expected))
            throw new InvalidOperationException("Only the typed flag owner's exact committed flags may precede visibility.");
        _current = expected;
        var baseline = access.ReadVisibility(profile);
        if (!SeekRendererState.Float(baseline.ChartTime, targetChartTime) || !SeekRendererState.Float(baseline.Speed, targetSpeed))
            throw new InvalidOperationException("The real information time/speed has not reached the requested target.");
        var possible = _access.RuntimeNotes.Select(note => new SeekRendererCommand(note.Reference, SeekRendererCommandKind.Appear)).ToArray();
        var budget = SeekRendererState.Estimate(_current, possible);
        access.VerifyVisibilityProfile(profile);
        _access.ReserveEffects(budget.NewChildren, budget.NewBackings, budget.Capacity);
        return _visibility = new(this, profile, flags, targetChartTime, targetSpeed, baseline);
    });
    private static SeekRendererGraphSnapshot WithFlags(SeekRendererGraphSnapshot graph, IReadOnlyList<SeekNoteFlagTarget> flags)
    {
        var byReference = flags.ToDictionary(row => row.Reference);
        if (byReference.Count != graph.Notes.Count) throw new InvalidOperationException("The flag owner has another complete note set.");
        return graph with { Notes = graph.Notes.Select(note => byReference.TryGetValue(note.Reference, out var flag)
            ? note with { IsActive = flag.IsActive, Destroyed = flag.Destroyed, Missed = flag.Missed }
            : throw new InvalidOperationException("The flag owner names another note.")).ToArray() };
    }
    private INativeSeekNoteVisibilityAccess VisibilityAccess(NativeSeekNoteVisibilityCommands ticket)
    {
        Local();
        if (!ReferenceEquals(_visibility, ticket) || _access is not INativeSeekNoteVisibilityAccess access)
            throw new InvalidOperationException("The visibility ticket belongs to another original renderer owner.");
        return access;
    }
    internal SeekNoteVisibilityEffects ExecuteNoteVisibility(NativeSeekNoteVisibilityCommands ticket) => Operation(() =>
    {
        var access = VisibilityAccess(ticket);
        if (ticket.Executed) throw new InvalidOperationException("A visibility producer executes once.");
        ticket.Executed = true;
        try
        {
            VerifySource(); ticket.Flags.VerifyVisibilityInput();
            if (!SeekRendererState.Same(Read(), _current!) || !SeekNoteVisibilityState.Same(access.ReadVisibility(ticket.Profile), ticket.Baseline))
                throw new InvalidOperationException("The prepared visibility source changed.");
            access.WriteVisibilityControl(ticket.Current, ticket.Speed, NativeNoteCursorState.Rewound, () => ticket.ControlEntered = true);
            ticket.Current = access.ReadVisibility(ticket.Profile);
            if (!SeekNoteVisibilityState.Basis(ticket.Baseline, ticket.Current) || !SeekNoteVisibilityState.Heads(ticket.Baseline, ticket.Current)
                || ticket.Current.Cursors != NativeNoteCursorState.Rewound || !SeekRendererState.Float(ticket.Current.ControlSpeed, ticket.Speed))
                throw new InvalidOperationException("Visibility control initialization did not read back.");
            var before = _current!;
            access.DispatchVisibility(ticket.Profile, true, ticket.Current, () => { ticket.NativeEntered = true; ++_attempted; });
            ticket.ActiveReturned = true;
            var after = Read(); var afterActive = access.ReadVisibility(ticket.Profile);
            var order = _access.RuntimeNotes.Select(note => note.Reference).ToArray();
            var beforeNotes = before.Notes.ToDictionary(note => note.Reference);
            var activeCursor = order.TakeWhile(reference => beforeNotes[reference].Destroyed).Count();
            if (!SeekNoteVisibilityState.Basis(ticket.Current, afterActive) || !SeekNoteVisibilityState.Heads(ticket.Current, afterActive)
                || afterActive.Cursors.StartIndex != 0 || afterActive.Cursors.HasNote
                || afterActive.Cursors.ActiveStartIndex != activeCursor || afterActive.Cursors.ActiveHasNote != (activeCursor < order.Length)
                || !SeekRendererState.Float(afterActive.ControlSpeed, ticket.Speed))
                throw new InvalidOperationException("ActiveNotes changed values outside its declared write set.");
            var effect = SeekRendererState.RecognizeVisibility(before, after, order, _knownReferences, out var retiredBackings);
            if (!effect.Recognized) throw new InvalidOperationException(effect.Failure);
            if (_restorePrepared && _access is INativeSeekRendererRestoreAccess inverse)
                inverse.AcceptRecognizedAllocations(after, retiredBackings);
            _current = after; Remember(after); _deferred |= effect.Deferred;
            if (effect.Growth) { ticket.Growth = true; _retained = true; }
            ticket.Flags.AcceptVisibilityResult(ticket, after.Notes.Select(note => new SeekNoteFlagTarget(note.Reference, note.IsActive, note.Destroyed, note.Missed)).ToArray());
            ticket.Current = afterActive;
            access.DispatchVisibility(ticket.Profile, false, ticket.Current, () => { ticket.NativeEntered = true; ++_attempted; });
            ticket.SetReturned = true;
            var final = access.ReadVisibility(ticket.Profile); var finalGraph = Read();
            var actualHeads = final.Heads.ToDictionary(head => head.Note);
            var renderCursor = final.RenderNotes.TakeWhile(reference => actualHeads[reference].Static).Count();
            if (!SeekNoteVisibilityState.Basis(afterActive, final) || final.Cursors.ActiveHasNote != afterActive.Cursors.ActiveHasNote
                || final.Cursors.ActiveStartIndex != afterActive.Cursors.ActiveStartIndex || !SeekRendererState.Float(final.ControlSpeed, ticket.Speed)
                || final.Cursors.StartIndex != renderCursor || final.Cursors.HasNote != (renderCursor < final.RenderNotes.Count))
                throw new InvalidOperationException("SetNotes changed target input or ActiveNotes cursors.");
            var expected = ReplaceHeadPositions(after, afterActive, final);
            if (!SeekRendererState.Same(finalGraph, expected))
                throw new InvalidOperationException("SetNotes changed renderer or pool state outside actual note-head positions.");
            _current = finalGraph; Remember(finalGraph); ticket.Current = final;
            ticket.Observed = true; return ticket.GetJournal();
        }
        catch (Exception error)
        {
            ticket.Failure = error.Message;
            if (ticket.ControlEntered || ticket.NativeEntered) { ticket.Unknown = true; _unknown = true; _retained = true; error.Data["SeekRendererEffectsOwner"] = this; }
            throw;
        }
    });
    private static SeekRendererGraphSnapshot ReplaceHeadPositions(SeekRendererGraphSnapshot graph,
        SeekNoteVisibilitySnapshot before, SeekNoteVisibilitySnapshot after)
    {
        var heads = before.Heads.ToDictionary(head => head.Transform.Reference);
        var next = after.Heads.ToDictionary(head => head.Transform.Reference);
        foreach (var head in heads.Values)
        {
            var target = next[head.Transform.Reference];
            if (head.Note != target.Note || head.Static != target.Static || head.Transform.Parent != target.Transform.Parent
                || !SeekRendererState.Same(head.Transform.LocalRotation, target.Transform.LocalRotation)
                || !SeekRendererState.Same(head.Transform.LocalScale, target.Transform.LocalScale)
                || head.Static && !SeekRendererState.Same(head.Transform, target.Transform))
                throw new InvalidOperationException("SetNotes changed a head outside its actual local-position write set.");
        }
        return graph with { Transforms = graph.Transforms.Select(transform => next.TryGetValue(transform.Reference, out var head)
            ? transform with { LocalPosition = head.Transform.LocalPosition } : transform).ToArray() };
    }
    internal void VerifyNoteVisibility(NativeSeekNoteVisibilityCommands ticket) => Operation(() =>
    {
        var access = VisibilityAccess(ticket);
        if (!ticket.Observed || ticket.Restored || ticket.Unknown) throw new InvalidOperationException("No known applied visibility result exists.");
        var actual = access.ReadVisibility(ticket.Profile);
        if (!SeekNoteVisibilityState.Same(actual, ticket.Current))
            throw new InvalidOperationException("The applied visibility control/source changed.");
        ticket.Flags.VerifyVisibilityInput(); return 0;
    });
    internal void VerifyNoteVisibilityAfterHold(NativeSeekNoteVisibilityCommands ticket, NativeSeekHoldGeometryCommands hold) => Operation(() =>
    {
        var access = VisibilityAccess(ticket);
        if (!ticket.Observed || ticket.Restored || ticket.Unknown) throw new InvalidOperationException("No known applied visibility result exists.");
        if (!hold.VerifyAppliedTransforms()) throw new InvalidOperationException("The subsequent Hold geometry did not independently read back.");
        var journal = hold.GetJournal(); var baseline = journal.Baseline;
        if (!journal.ExecutionFinished || baseline is null || baseline.Identity.Notes != _current!.Identity)
            throw new InvalidOperationException("The subsequent Hold owner has another actual source or no completed geometry result.");
        var prior = ticket.Current.Heads.ToDictionary(head => head.Note);
        var allowed = baseline.Notes.Select(note => note.Reference).ToHashSet();
        var holdTransforms = baseline.Transforms.ToDictionary(transform => transform.Reference);
        foreach (var note in baseline.Notes)
        {
            if (!prior.TryGetValue(note.Reference, out var head) || note.NoteTransform != head.Transform.Reference
                || !holdTransforms.TryGetValue(note.NoteTransform, out var transform)
                || transform.Parent != head.Transform.Parent || !SeekRendererState.Same(transform.LocalPosition, head.Transform.LocalPosition)
                || !SeekRendererState.Same(transform.LocalRotation, head.Transform.LocalRotation)
                || !SeekRendererState.Same(transform.LocalScale, head.Transform.LocalScale))
                throw new InvalidOperationException("The subsequent Hold owner did not begin at these actual visibility heads.");
        }
        var actual = access.ReadVisibility(ticket.Profile);
        var projected = actual with { Heads = actual.Heads.Select(head => allowed.Contains(head.Note)
            ? prior[head.Note] : head).ToArray() };
        if (!SeekNoteVisibilityState.Same(projected, ticket.Current))
            throw new InvalidOperationException("Values outside the actual subsequent Hold heads changed after visibility.");
        ticket.Flags.VerifyVisibilityInput(); return 0;
    });
    internal void RestoreNoteVisibility(NativeSeekNoteVisibilityCommands ticket) => Operation(() =>
    {
        var access = VisibilityAccess(ticket);
        if (!ticket.Observed || ticket.Restored || ticket.Unknown) throw new InvalidOperationException("Unknown visibility effects cannot be claimed restored.");
        var actual = access.ReadVisibility(ticket.Profile);
        if (!SeekNoteVisibilityState.Same(actual, ticket.Current) || !SeekRendererState.Same(Read(), _current!))
            throw new InvalidOperationException("Restore later geometry before the exact visibility inverse.");
        access.RestoreVisibilityHeads(ticket.Profile, actual, ticket.Baseline, () => _retained = true);
        var headsRestored = access.ReadVisibility(ticket.Profile);
        var nextGraph = ReplaceHeadPositions(_current!, ticket.Current, headsRestored);
        if (!SeekRendererState.Same(Read(), nextGraph)) throw new InvalidOperationException("Visibility head restoration changed another renderer value.");
        _current = nextGraph;
        access.WriteVisibilityControl(headsRestored, ticket.Baseline.ControlSpeed, ticket.Baseline.Cursors, () => _retained = true);
        var result = access.ReadVisibility(ticket.Profile);
        if (!SeekNoteVisibilityState.Same(result, ticket.Baseline)) throw new InvalidOperationException("Visibility selected values did not restore.");
        ticket.Restored = true; return 0;
    });
    internal bool VerifyRestoredNoteVisibility(NativeSeekNoteVisibilityCommands ticket) => Operation(() =>
    {
        var access = VisibilityAccess(ticket); var current = access.ReadVisibility(ticket.Profile);
        return ticket.Restored && current.Cursors == ticket.Baseline.Cursors
            && SeekRendererState.Float(current.ControlSpeed, ticket.Baseline.ControlSpeed) && SeekNoteVisibilityState.Heads(current, ticket.Baseline);
    });
    internal void AcceptRestoredNoteFlags(NativeSeekNoteFlagsParticipant flags) => Operation(() =>
    {
        if (!ReferenceEquals(_noteFlagsOwner, flags) || !flags.ConfirmRestored().OriginalStateRestored)
            throw new InvalidOperationException("The registered original flag owner has not restored its baseline.");
        var next = WithFlags(_current!, flags.ReadRestoredVisibilityFlags());
        if (!SeekRendererState.Same(Read(), next)) throw new InvalidOperationException("Values outside the restored flag owner's write set changed.");
        _current = next; return 0;
    });
}

internal static partial class SeekRendererState
{
    internal static (bool Recognized, bool Growth, bool Deferred, string? Failure) RecognizeVisibility(
        SeekRendererGraphSnapshot before, SeekRendererGraphSnapshot after, IReadOnlyList<long> actualOrder, IReadOnlySet<long> known)
        => RecognizeVisibility(before, after, actualOrder, known, out _);

    internal static (bool Recognized, bool Growth, bool Deferred, string? Failure) RecognizeVisibility(
        SeekRendererGraphSnapshot before, SeekRendererGraphSnapshot after, IReadOnlyList<long> actualOrder, IReadOnlySet<long> known,
        out IReadOnlyList<SeekRendererRetiredBackingState> retiredBackings)
    {
        retiredBackings = Array.Empty<SeekRendererRetiredBackingState>();
        try
        {
            Validate(before); Validate(after);
            var prior = before.Notes.ToDictionary(note => note.Reference); var next = after.Notes.ToDictionary(note => note.Reference);
            if (actualOrder.Count != prior.Count || actualOrder.Distinct().Count() != prior.Count || next.Count != prior.Count)
                throw new InvalidOperationException("ActiveNotes changed complete note membership.");
            foreach (var reference in actualOrder)
            {
                var a = prior[reference]; var b = next[reference];
                if (!Same(a, b with { IsActive = a.IsActive }) || a.IsActive && !b.IsActive || a.Destroyed && a.IsActive != b.IsActive)
                    throw new InvalidOperationException("ActiveNotes has a flag change outside its actual inactive-to-active transition.");
            }
            var withFlags = before with { Notes = after.Notes };
            var model = new RendererModel(withFlags, withFlags, after, known);
            foreach (var reference in actualOrder)
                if (!prior[reference].IsActive && next[reference].IsActive)
                    foreach (var planReference in prior[reference].Plans)
                    { var plan = model.Plans[planReference]; if (!plan.Active) model.Appear(plan); }
            if (model.Matches())
            { retiredBackings = model.RetiredBackings(); return (true, model.Growth, model.Deferred, null); }
            return (false, model.Growth, model.Deferred, "ActiveNotes pool effects differ from its actual ordered Appear calls.");
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or KeyNotFoundException or ArgumentException or OverflowException)
        { return (false, false, false, error.Message); }
    }
}
