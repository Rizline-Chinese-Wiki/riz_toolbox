namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekTargetStatePlan
{
    private readonly Dictionary<long, bool> _effectiveChallenge = new();
    private void Evaluate()
    {
        var byReference = _baseline.Notes.ToDictionary(note => note.Reference);
        foreach (var note in _baseline.Notes) _effectiveChallenge.Add(note.Reference, note.InChallengeTime);
        if (_baseline.InstantiateReady) ProjectedChallengeComboCount = _baseline.ChallengeComboCount;
        else
        {
            // This projects the actual pending initialization over its current
            // Judge.notes occurrences, not over a fabricated allNotes substitute.
            // Existing true flags are never cleared and never counted afresh.
            ProjectedChallengeComboCount = 0;
            foreach (var interval in _baseline.ChallengeIntervals)
                foreach (var reference in _baseline.InitializationNotes)
                {
                    if (_effectiveChallenge[reference]) continue;
                    var note = byReference[reference];
                    if (interval.Start < note.ChartTime && note.ChartTime <= interval.End)
                    {
                        _effectiveChallenge[reference] = true;
                        ProjectedChallengeComboCount += note.RawType == 2 ? 2 : 1;
                    }
                }
        }
        // Preserve every actual instance and expose the existing native OR projection
        // to the explicit writer; this target snapshot has no mutation admission.
        InitializationTargets = new(JudgeInitializationRequired, EffectiveWindows with { }, ProjectedChallengeComboCount,
            Array.AsReadOnly(_baseline.Notes.Select(note => new SeekChallengeFlagTarget(note.Reference, _effectiveChallenge[note.Reference])).ToArray()));
        var ordered = _baseline.Notes.ToArray();
        NativeSeed = Array.AsReadOnly(ordered.Select(note => note.Reference).ToArray());
        SortComparisons = ordered.Length < 2 ? 0 : (long)ordered.Length * (ordered.Length + 1) / 2 - 1;
        // Preserve the exact native nested exchange algorithm. Its epsilon is
        // nontransitive, its subtraction rounds to Single, and its threshold is
        // Double. A stable sort/comparator is not equivalent, including Hold/Hold.
        for (var first = 0; first + 1 < ordered.Length; ++first)
            for (var second = first; second < ordered.Length; ++second)
            {
                var a = ordered[first]; var b = ordered[second];
                if (a.ChartTime > b.ChartTime
                    || Math.Abs((double)(float)(a.ChartTime - b.ChartTime)) < 0.000001d && a.RawType == 2)
                    (ordered[first], ordered[second]) = (ordered[second], ordered[first]);
            }
        NativeSorted = Array.AsReadOnly(ordered.Select(note => note.Reference).ToArray());
        var remaining = new List<long>(); var tap = new List<SeekTargetNoteState>(); var drag = new List<SeekTargetNoteState>();
        var badEdge = (float)(Coordinates.SyncedSeconds + EffectiveWindows.BadTime);
        var dragEdge = (float)(Coordinates.SyncedSeconds + EffectiveWindows.NormalPerfectChallengeTime);
        for (var index = 0; index < ordered.Length; ++index)
        {
            var note = ordered[index];
            if (badEdge < note.RealTime)
            {
                for (var tail = index; tail < ordered.Length; ++tail) remaining.Add(ordered[tail].Reference);
                break;
            }
            if (note.RawType == 1 && dragEdge < note.RealTime) { remaining.Add(note.Reference); continue; }
            if (note.RawType == 1) drag.Add(note); else tap.Add(note);
        }
        var expired = new List<SeekExpiredHeadOccurrence>(); var waitingTap = new List<long>(); var waitingDrag = new List<long>();
        for (var ordinal = 0; ordinal < tap.Count; ++ordinal)
        {
            var note = tap[ordinal]; var reason = TapExpiration(note);
            if (reason != 0) expired.Add(new(note.Reference, SeekExpiredHeadQueue.Tap, ordinal, reason));
            else waitingTap.Add(note.Reference);
        }
        var normalBefore = (float)(Coordinates.SyncedSeconds - EffectiveWindows.NormalPerfectChallengeTime);
        for (var ordinal = 0; ordinal < drag.Count; ++ordinal)
        {
            var note = drag[ordinal];
            if (normalBefore > note.RealTime)
                expired.Add(new(note.Reference, SeekExpiredHeadQueue.Drag, ordinal, SeekExpiredHeadReason.NormalChallengeWindow));
            else waitingDrag.Add(note.Reference);
        }
        if (remaining.Count + waitingTap.Count + waitingDrag.Count + expired.Count != _baseline.Notes.Count)
            throw new InvalidOperationException("Native queue target projection lost an actual occurrence.");
        NativeQueueProjection = new(Array.AsReadOnly(remaining.ToArray()), Array.AsReadOnly(waitingTap.ToArray()), Array.AsReadOnly(waitingDrag.ToArray()));
        ExpiredHeads = Array.AsReadOnly(expired.ToArray());
        BuildPolicy(byReference);
    }
    private SeekExpiredHeadReason TapExpiration(SeekTargetNoteState note)
    {
        var reason = (SeekExpiredHeadReason)0;
        if ((float)(Coordinates.SyncedSeconds - EffectiveWindows.NormalPerfectChallengeTime) > note.RealTime)
            reason |= SeekExpiredHeadReason.NormalChallengeWindow;
        if (!(_effectiveChallenge[note.Reference] && _baseline.ChallengeCompletely)
            && (float)(Coordinates.SyncedSeconds - EffectiveWindows.JudgeTime) > note.RealTime)
            reason |= SeekExpiredHeadReason.JudgeWindow;
        return reason;
    }
}
