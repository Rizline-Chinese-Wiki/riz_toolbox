namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekTargetStatePlan
{
    private void BuildPolicy(IReadOnlyDictionary<long, SeekTargetNoteState> byReference)
    {
        var expired = ExpiredHeads.Select(row => row.Reference).ToHashSet();
        var skipped = new HashSet<long>(); var crossing = new List<SeekCrossingHoldTarget>();
        var work = new List<SeekTargetRequiredWork>
        {
            new(SeekTargetRequiredWorkKind.InputEpochClosure, null, null,
                "Clearing observed finger members is a new input epoch, and requires closure of the old input consumers; it does not reproduce physical input."),
            new(SeekTargetRequiredWorkKind.HoldEngineClosure, null, null,
                "A captured state or zero local Hold records does not certify old Hold coroutine/engine source closure."),
            new(SeekTargetRequiredWorkKind.TargetVisibilityAndGeometry, null, null,
                "All flags/Disappear commands below are explicit epoch reset requests. Native target canvas/line geometry, activation and renderer deferred consumers remain to be reconciled.")
        };
        if (JudgeInitializationRequired)
        {
            work.Add(new(SeekTargetRequiredWorkKind.JudgeInitialization, null, null,
                "Information windows are a projection of the pending native initialization, not proof that Judge caches and ready flag were written."));
            work.Add(new(SeekTargetRequiredWorkKind.ChallengeFlagsAndCount, null, null,
                "Projected challenge flags use current Judge.notes and OR with existing true values; flag56/count/cache synchronization has no completed writer in this plan."));
        }
        var charts = _chartNotes.ToDictionary(note => note.Location);
        RuntimeTimeTargets = Array.AsReadOnly(_runtimeNotes.Select(actual =>
        {
            var chart = charts[actual.Location];
            return new SeekRuntimeTimeTarget(actual.Reference, chart.StartGameSeconds,
                chart.RawType == 2 ? chart.HoldEndGameSeconds : null);
        }).ToArray());
        if (_chartNotes.Any(note => note.RawType == 2))
            work.Add(new(SeekTargetRequiredWorkKind.HoldGeometry, null, null,
                "Hold pending/running Lists, isStaticNote, line length and note/hold/ring/canvas transforms require their own target geometry operation."));
        foreach (var actual in _runtimeNotes)
        {
            var note = byReference[actual.Reference]; var chart = charts[actual.Location];
            if (!SameFloat(note.RealTime, chart.StartGameSeconds)
                || note.RawType == 2 && (chart.HoldEndGameSeconds is not float endSeconds || !SameFloat(note.HoldEndRealTime, endSeconds)))
                work.Add(new(SeekTargetRequiredWorkKind.RuntimeTimeMismatch, note.Reference, actual.Location,
                    "Actual cached timing differs from this chart conversion. Queue projection deliberately uses actual realTime; cache/geometry synchronization remains required."));
            if (note.RawType != 2 || !_holdEnds.TryGetValue(note.Reference, out var end)
                || !(note.ChartTime < Coordinates.ChartCoordinate && Coordinates.ChartCoordinate <= end)) continue;
            var headExpired = expired.Contains(note.Reference);
            SeekCrossingHoldDisposition disposition;
            if (Policy.CrossingHolds == SeekCrossingHoldPolicy.Skip)
            {
                skipped.Add(note.Reference); disposition = SeekCrossingHoldDisposition.SkippedByPolicy;
                work.Add(new(SeekTargetRequiredWorkKind.ExplicitlySkippedCrossingHold, note.Reference, actual.Location,
                    "This crossing Hold was explicitly omitted by practice policy. It is not implemented continuation or complete Hold support."));
            }
            else if (headExpired || TapExpiration(note) != 0)
            {
                disposition = SeekCrossingHoldDisposition.ContinuationRequired;
                work.Add(new(SeekTargetRequiredWorkKind.HoldContinuation, note.Reference, actual.Location,
                    "The Hold tail has not been strictly passed and its head window is closed. Native continuation/input/geometry must be supplied; no blanket skip or invented iterator is applied."));
            }
            else disposition = SeekCrossingHoldDisposition.HeadWindowOpen;
            crossing.Add(new(note.Reference, actual.Location, note.ChartTime, end, headExpired, disposition));
        }
        foreach (var location in Coverage.MissingLocations)
            work.Add(new(SeekTargetRequiredWorkKind.MissingRuntimeInstance, null, location,
                "This chart location has no actual instance in the owned NoteView. No native reference, queue member or renderer is fabricated."));
        QueueTargets = new(Array.AsReadOnly(NativeQueueProjection.Notes.Where(reference => !skipped.Contains(reference)).ToArray()),
            Array.AsReadOnly(NativeQueueProjection.TapWaiting.Where(reference => !skipped.Contains(reference)).ToArray()),
            Array.AsReadOnly(NativeQueueProjection.DragWaiting.Where(reference => !skipped.Contains(reference)).ToArray()));
        FlagTargets = Array.AsReadOnly(_baseline.Notes.Select(note => new SeekNoteFlagTarget(note.Reference,
            false, expired.Contains(note.Reference) || skipped.Contains(note.Reference), false)).ToArray());
        FingerTargets = Array.AsReadOnly(_baseline.Notes.Select(note => new SeekFingerIdsTarget(note.Reference,
            note.FingerIds == 0 ? null : Array.Empty<int>())).ToArray());
        RendererCommands = Array.AsReadOnly(_baseline.Notes.Select(note => new SeekRendererCommand(note.Reference, SeekRendererCommandKind.Disappear)).ToArray());
        CrossingHolds = Array.AsReadOnly(crossing.ToArray()); RequiredWork = Array.AsReadOnly(work.ToArray());
    }
}
