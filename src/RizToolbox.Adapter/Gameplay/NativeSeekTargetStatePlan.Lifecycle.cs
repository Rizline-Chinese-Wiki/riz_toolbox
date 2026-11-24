namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekTargetStatePlan
{
    private SeekRuntimeNote[] _runtimeNotes = Array.Empty<SeekRuntimeNote>();
    private SeekChartNote[] _chartNotes = Array.Empty<SeekChartNote>();
    private readonly Dictionary<long, float> _holdEnds = new();

    internal static NativeSeekTargetStatePlan Capture(IRetainedSeekTargetReader source, SeekPracticePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source); source.VerifyOwnerThread();
        var value = new NativeSeekTargetStatePlan(source);
        try
        {
            source.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                ArgumentNullException.ThrowIfNull(policy);
                if (!Enum.IsDefined(typeof(SeekCrossingHoldPolicy), policy.CrossingHolds)) throw new ArgumentOutOfRangeException(nameof(policy));
                value.Policy = policy with { }; value.Access();
                value.Local(); var coordinates = source.Coordinates; value.Local();
                if (coordinates is null || !double.IsFinite(coordinates.ProgressSeconds) || !float.IsFinite(coordinates.SyncedSeconds)
                    || !float.IsFinite(coordinates.ChartCoordinate) || !SameFloat((float)coordinates.ProgressSeconds, coordinates.SyncedSeconds))
                    throw new InvalidDataException("The explicit target is not a finite progress/game-seconds and native chart-coordinate sample.");
                value.Coordinates = coordinates with { };
                value.Local(); var view = source.NoteView; value.Local();
                value.Local(); var chart = source.ChartPlan; value.Local();
                if (view is null || chart is null || chart.Run != view.Run
                    || BitConverter.DoubleToInt64Bits(chart.Target.Value) != BitConverter.DoubleToInt64Bits(coordinates.ProgressSeconds))
                    throw new InvalidOperationException("The independently owned chart plan and actual NoteView do not share the exact run/target.");
                value._runtimeNotes = view.Notes.ToArray(); value._chartNotes = chart.Notes.ToArray();
                if (value._runtimeNotes.Length > MaximumNotes || value._chartNotes.Length > MaximumNotes)
                    throw new NotSupportedException("The complete target source exceeds its actual-note/chart bound.");
                value.Coverage = view.Coverage with
                {
                    MissingLocations = Array.AsReadOnly(view.Coverage.MissingLocations.ToArray()),
                    MultipleInstanceLocations = Array.AsReadOnly(view.Coverage.MultipleInstanceLocations.ToArray())
                };
                foreach (var note in value._runtimeNotes)
                {
                    var raw = view.ChartLines[note.Location.LineOrdinal].Data.Notes[note.Location.NoteOrdinal];
                    if (raw.Reference != note.NoteData) throw new InvalidOperationException("An actual target note lost its exact chart data association.");
                    if (raw.Type == 2) value._holdEnds.Add(note.Reference, raw.OtherInformations[0]);
                }
                var first = value.Read(); var second = value.Read();
                if (!Same(first, second)) throw new InvalidOperationException("Actual target windows/notes/initialization data changed between complete snapshots.");
                if (second.Identity.Notes != view.Identity || second.Identity.Notes.Chart.Run != chart.Run)
                    throw new InvalidOperationException("The actual target snapshot belongs to another independently retained source.");
                value._baseline = second; value.ValidateSource(); value.Evaluate(); value.Access();
                if (!Same(value.Read(), value._baseline)) throw new InvalidOperationException("Actual target source changed during managed evaluation.");
                source.VerifyLocal(value.Identity); value.Local(); return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { AttachCleanup(error, value, cleanup); }
            throw;
        }
    }
    private void Lifetime()
    {
        _source.VerifyOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekTargetStatePlan));
        if (_faulted) throw new InvalidOperationException("This target-state source was already observed expired.");
    }
    private void Local()
    {
        Lifetime();
        if (_baseline is not null) _source.VerifyLocal(_baseline.Identity);
        Lifetime();
    }
    private void Access() { Local(); _source.VerifyAccess(); Local(); }
    private T Operation<T>(Func<T> operation)
    {
        _source.VerifyOwnerThread(); Lifetime();
        if (_busy || _releasing) throw new InvalidOperationException("A target-state plan operation cannot reenter itself.");
        _busy = true; Exception? failure = null;
        try { Local(); var result = operation(); Local(); return result; }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            _busy = false;
            if (_disposed)
                try { Release(); }
                catch (Exception cleanup) { if (failure is null) throw; AttachCleanup(failure, this, cleanup); }
        }
    }
    private static T[] CopyRows<T>(IReadOnlyList<T> input, int maximum, Func<T, T> copy, Action guard)
    {
        ArgumentNullException.ThrowIfNull(input); guard(); var count = input.Count; guard();
        if (count < 0 || count > maximum) throw new NotSupportedException("A complete target-state sequence exceeds its bound.");
        var rows = new T[count];
        for (var index = 0; index < count; ++index) { guard(); rows[index] = copy(input[index]); guard(); }
        guard(); if (input.Count != count) throw new InvalidOperationException("A target-state sequence changed while being copied."); guard();
        return rows;
    }
    private SeekTargetRawState Read()
    {
        Local(); var raw = _source.ReadSnapshot(); Local();
        if (raw is null || raw.Identity is null || raw.Identity.Notes is null || raw.Identity.Notes.Chart is null
            || raw.Identity.Notes.Chart.Run is null || raw.Identity.NoteJudgeControl == 0
            || raw.CachedWindows is null || raw.InformationWindows is null)
            throw new InvalidDataException("The complete actual target-state source is absent.");
        var intervals = CopyRows(raw.ChallengeIntervals, MaximumChallengeIntervals,
            row => row is null ? throw new InvalidDataException("A native challenge interval is absent.") : row with { }, Local);
        var notes = CopyRows(raw.Notes, MaximumNotes,
            row => row is null ? throw new InvalidDataException("An actual target note row is absent.") : row with { }, Local);
        var initializing = CopyRows(raw.InitializationNotes, MaximumNotes, reference => reference, Local);
        return raw with { CachedWindows = raw.CachedWindows with { }, InformationWindows = raw.InformationWindows with { },
            ChallengeIntervals = Array.AsReadOnly(intervals), Notes = Array.AsReadOnly(notes), InitializationNotes = Array.AsReadOnly(initializing) };
    }
    private static bool SameFloat(float first, float second) => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);
    private static bool Same(SeekTargetWindows a, SeekTargetWindows b)
        => SameFloat(a.JudgeTime, b.JudgeTime) && SameFloat(a.MaxPerfectChallengeTime, b.MaxPerfectChallengeTime)
            && SameFloat(a.NormalPerfectChallengeTime, b.NormalPerfectChallengeTime) && SameFloat(a.BadTime, b.BadTime);
    private static bool Same(SeekTargetRawState a, SeekTargetRawState b)
    {
        if (a.Identity != b.Identity || a.InstantiateReady != b.InstantiateReady || !Same(a.CachedWindows, b.CachedWindows)
            || !Same(a.InformationWindows, b.InformationWindows) || a.ChallengeCompletely != b.ChallengeCompletely
            || a.AutoPlay != b.AutoPlay || a.FreeHold != b.FreeHold || a.InstantiateBadHit != b.InstantiateBadHit
            || a.ChallengeComboCount != b.ChallengeComboCount || a.ChallengeTimes != b.ChallengeTimes
            || a.InitializationNotesList != b.InitializationNotesList || a.InitializationNotesBacking != b.InitializationNotesBacking
            || a.InitializationNotesVersion != b.InitializationNotesVersion || !a.InitializationNotes.SequenceEqual(b.InitializationNotes)
            || a.ChallengeIntervals.Count != b.ChallengeIntervals.Count || a.Notes.Count != b.Notes.Count) return false;
        for (var index = 0; index < a.ChallengeIntervals.Count; ++index)
        {
            var x = a.ChallengeIntervals[index]; var y = b.ChallengeIntervals[index];
            if (x.Reference != y.Reference || !SameFloat(x.Start, y.Start) || !SameFloat(x.End, y.End)) return false;
        }
        for (var index = 0; index < a.Notes.Count; ++index)
        {
            var x = a.Notes[index]; var y = b.Notes[index];
            if (x.Reference != y.Reference || x.NoteData != y.NoteData || x.RawType != y.RawType || !SameFloat(x.ChartTime, y.ChartTime)
                || !SameFloat(x.RealTime, y.RealTime) || !SameFloat(x.HoldEndRealTime, y.HoldEndRealTime)
                || x.InChallengeTime != y.InChallengeTime || x.IsActive != y.IsActive || x.Destroyed != y.Destroyed || x.Missed != y.Missed
                || x.FingerIds != y.FingerIds) return false;
        }
        return true;
    }
    private void ValidateSource()
    {
        var actual = _runtimeNotes.ToDictionary(note => note.Reference);
        var chart = _chartNotes.ToDictionary(note => note.Location);
        if (_baseline.Notes.Count != _runtimeNotes.Length || _baseline.InitializationNotesList == 0 || _baseline.InitializationNotesBacking == 0
            || _baseline.ChallengeTimes == 0 || chart.Count != Coverage.ChartLocationCount)
            throw new InvalidDataException("The target source lacks a complete actual note/initialization/chart association.");
        for (var ordinal = 0; ordinal < _baseline.Notes.Count; ++ordinal)
        {
            var row = _baseline.Notes[ordinal];
            if (row.Reference == 0 || row.Reference != _runtimeNotes[ordinal].Reference || !actual.TryGetValue(row.Reference, out var owned)
                || row.NoteData != owned.NoteData || !chart.TryGetValue(owned.Location, out var data)
                || row.RawType != data.RawType || !SameFloat(row.ChartTime, data.ChartCoordinate)
                || row.RawType is < 0 or > 2 || !float.IsFinite(row.ChartTime) || !float.IsFinite(row.RealTime)
                || row.RawType == 2 && !float.IsFinite(row.HoldEndRealTime))
                throw new InvalidDataException("A target note lost its exact actual/chart identity or finite native timing values.");
        }
        foreach (var reference in _baseline.InitializationNotes)
            if (reference == 0 || !actual.ContainsKey(reference))
                throw new InvalidDataException("An active Judge initialization occurrence is absent from the independently owned actual NoteView.");
        if (!_baseline.InstantiateReady)
            foreach (var interval in _baseline.ChallengeIntervals)
                if (interval.Reference == 0 || !float.IsFinite(interval.Start) || !float.IsFinite(interval.End))
                    throw new InvalidDataException("A required native challenge initialization interval is absent or nonfinite.");
        EffectiveWindows = (_baseline.InstantiateReady ? _baseline.CachedWindows : _baseline.InformationWindows) with { };
        if (!float.IsFinite(EffectiveWindows.JudgeTime) || !float.IsFinite(EffectiveWindows.MaxPerfectChallengeTime)
            || !float.IsFinite(EffectiveWindows.NormalPerfectChallengeTime) || !float.IsFinite(EffectiveWindows.BadTime))
            throw new InvalidDataException("The effective native judgement windows are nonfinite.");
    }
    internal void VerifySource() => Operation(() =>
    {
        try
        {
            Access(); var first = Read(); var second = Read();
            if (!Same(first, second) || !Same(second, _baseline))
                throw new InvalidOperationException("The original target-state source or native windows/notes/initialization membership changed.");
            _source.VerifyLocal(Identity); Local(); return 0;
        }
        catch { _faulted = true; throw; }
    });
    private static void AttachCleanup(Exception error, object owner, Exception cleanup)
    {
        error.Data["SeekTargetStateCleanupOwner"] = owner;
        error.Data["SeekTargetStateCleanupFailure"] = cleanup;
    }
    private void RecordCleanup(Exception error)
    {
        if (!_cleanupFailures.Contains(error)) _cleanupFailures.Add(error);
        CleanupFailure = string.Join("; ", _cleanupFailures.Select(failure => failure.Message));
    }
    private AggregateException CleanupError()
    {
        var error = new AggregateException("Target-state preparation retains unknown native cleanup responsibility without retry.", _cleanupFailures);
        error.Data["SeekTargetStateCleanupOwner"] = this; return error;
    }
    private bool _releaseAttempted;
    public void Dispose()
    {
        _source.VerifyOwnerThread();
        if (_notifyingDisposal || _releasing) return;
        var first = !_disposed; _disposed = true;
        if (first)
        {
            _notifyingDisposal = true;
            try { _source.RequestDispose(); }
            catch (Exception error) { RecordCleanup(error); }
            finally { _notifyingDisposal = false; }
        }
        if (!_busy) Release();
        if (_cleanupFailures.Count != 0) throw CleanupError();
    }
    private void Release()
    {
        if (_released || _releasing) return;
        if (_releaseAttempted) { if (_cleanupFailures.Count != 0) throw CleanupError(); return; }
        _releaseAttempted = true; _releasing = true;
        try { _source.Dispose(); _released = true; }
        catch (Exception error) { RecordCleanup(error); throw CleanupError(); }
        finally { _releasing = false; }
    }
}
