using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekJudgeStateSourceIdentity(SeekRuntimeNoteSourceIdentity Notes, long NoteJudgeControl);
internal sealed record SeekJudgeListStorage(long Reference, long Class, long Backing, long ArrayClass,
    int Size, int Version, IReadOnlyList<long> CapacitySlots);
internal sealed record SeekJudgeRawState(SeekJudgeStateSourceIdentity Identity, SeekJudgeListStorage Notes,
    SeekJudgeListStorage TapWaiting, SeekJudgeListStorage DragWaiting, int HoldOnJudgeCount);
internal sealed record SeekJudgeActiveNote(int Slot, SeekRuntimeNote Note);
internal sealed record SeekJudgeListView(SeekJudgeListStorage Storage, IReadOnlyList<SeekJudgeActiveNote> ActiveSlots);

/// <summary>Owns an independent actual NoteView and the complete Judge storage.
/// RequestDispose closes both logical lifetimes immediately; physical reader
/// roots are released only after this view's in-flight operation returns.</summary>
internal interface IRetainedSeekJudgeStateReader : IDisposable
{
    void VerifyOwnerThread();
    void VerifyAccess();
    void BindLifetimeGuard(Action guard);
    IReadOnlyList<SeekRuntimeNote> RuntimeNotes { get; }
    SeekRuntimeNoteCoverageReport Coverage { get; }
    SeekJudgeRawState ReadSnapshot();
    void VerifyLocal(SeekJudgeStateSourceIdentity expected);
    void RequestDispose();
}

/// <summary>Read-only, independently retained baseline of three actual Judge
/// lists and the signed Hold counter. Preserves all storage slots and active
/// occurrences. No ordering reconstruction, reset/restore, coroutine-idle,
/// target-time, DSP or public Seek permission is issued by this observation.</summary>
internal sealed class NativeSeekJudgeStateView : IDisposable
{
    internal const int MaximumCapacity = NativeSeekRuntimeNoteView.MaximumNotes;
    private readonly IRetainedSeekJudgeStateReader _source;
    private readonly SeekJudgeRawState _baseline;
    private readonly List<Exception> _cleanupFailures = new();
    private bool _disposed, _faulted, _released, _releasing, _notifyingDisposal;
    private int _inFlight;
    internal ObservedJudgementRun Run => Identity.Notes.Chart.Run;
    internal SeekJudgeStateSourceIdentity Identity => _baseline.Identity;
    internal SeekJudgeListView Notes { get; }
    internal SeekJudgeListView TapWaiting { get; }
    internal SeekJudgeListView DragWaiting { get; }
    internal int HoldOnJudgeCount => _baseline.HoldOnJudgeCount;
    internal IReadOnlyList<SeekRuntimeNote> RuntimeNotes { get; }
    internal SeekRuntimeNoteCoverageReport Coverage { get; }
    internal string? CleanupFailure { get; private set; }

    private NativeSeekJudgeStateView(IRetainedSeekJudgeStateReader source, SeekJudgeRawState baseline,
        IReadOnlyList<SeekRuntimeNote> runtimeNotes, SeekRuntimeNoteCoverageReport coverage)
    {
        _source = source; _baseline = baseline;
        var copied = runtimeNotes.ToArray();
        if (copied.Length > MaximumCapacity) throw new InvalidDataException("The owned runtime-note map exceeds its complete bound.");
        var byReference = new Dictionary<long, SeekRuntimeNote>();
        foreach (var note in copied)
            if (note is null || note.Reference == 0 || !byReference.TryAdd(note.Reference, note))
                throw new InvalidDataException("The owned runtime-note map contains an absent or duplicate identity.");
        RuntimeNotes = Array.AsReadOnly(copied);
        Coverage = coverage with
        {
            MissingLocations = Array.AsReadOnly(coverage.MissingLocations.ToArray()),
            MultipleInstanceLocations = Array.AsReadOnly(coverage.MultipleInstanceLocations.ToArray())
        };
        Notes = Join(baseline.Notes, byReference);
        TapWaiting = Join(baseline.TapWaiting, byReference);
        DragWaiting = Join(baseline.DragWaiting, byReference);
    }

    internal static NativeSeekJudgeStateView Capture(IRetainedSeekJudgeStateReader source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.VerifyOwnerThread();
        NativeSeekJudgeStateView? view = null;
        try
        {
            var first = ReadSnapshot(source);
            var second = ReadSnapshot(source);
            if (!Same(first, second)) throw new InvalidOperationException("The actual Judge storage changed between complete snapshots.");
            source.VerifyAccess();
            var notes = source.RuntimeNotes ?? throw new InvalidDataException("The independent runtime-note map is absent.");
            source.VerifyLocal(second.Identity);
            var coverage = source.Coverage ?? throw new InvalidDataException("The independent runtime-note coverage is absent.");
            source.VerifyLocal(second.Identity);
            view = new NativeSeekJudgeStateView(source, second, notes, coverage);
            source.BindLifetimeGuard(view.VerifyLifetime);
            source.VerifyLocal(second.Identity);
            view.VerifyLifetime();
            return view;
        }
        catch (Exception error)
        {
            if (view is not null)
            {
                try { view.Dispose(); }
                catch (Exception cleanup) { AttachCleanup(error, view, cleanup); }
            }
            else
            {
                var failures = new List<Exception>();
                try { source.RequestDispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
                try { source.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
                if (failures.Count != 0) AttachCleanup(error, source, new AggregateException("Judge-state preparation cleanup failed.", failures));
            }
            throw;
        }
    }

    private static void AttachCleanup(Exception error, object owner, Exception cleanup)
    {
        error.Data["SeekJudgeStateCleanupOwner"] = owner;
        error.Data["SeekJudgeStateCleanupFailure"] = cleanup;
    }

    private static SeekJudgeListView Join(SeekJudgeListStorage storage, IReadOnlyDictionary<long, SeekRuntimeNote> notes)
    {
        var active = new SeekJudgeActiveNote[storage.Size];
        for (var slot = 0; slot < active.Length; ++slot)
        {
            var token = storage.CapacitySlots[slot];
            if (token == 0 || !notes.TryGetValue(token, out var note))
                throw new InvalidDataException("An active Judge slot has no exact reference in the independently owned NoteView.");
            active[slot] = new(slot, note);
        }
        // Capacity tails remain in Storage only. Repeated active references are
        // distinct occurrences; lists and arrays are never reordered or merged.
        return new(storage, Array.AsReadOnly(active));
    }

    private static SeekJudgeRawState ReadSnapshot(IRetainedSeekJudgeStateReader source)
    {
        source.VerifyAccess();
        var input = source.ReadSnapshot() ?? throw new InvalidDataException("The actual Judge snapshot is absent.");
        var identity = input.Identity;
        if (identity is null || identity.Notes is null || identity.Notes.Chart is null
            || identity.Notes.Chart.Run is null || identity.NoteJudgeControl == 0
            || identity.Notes.Chart.Chart == 0 || identity.Notes.Chart.Progress == 0 || identity.Notes.Chart.Lines == 0
            || identity.Notes.NoteControl == 0 || identity.Notes.RenderNotes == 0 || identity.Notes.AllNotes == 0
            || identity.Notes.Chart.Run.Information == 0 || identity.Notes.Chart.Run.Controller == 0
            || string.IsNullOrWhiteSpace(identity.Notes.Chart.Run.SessionId) || string.IsNullOrWhiteSpace(identity.Notes.Chart.Run.RunId)
            || identity.Notes.Chart.Run.Revision < 0 || identity.Notes.Chart.Run.SceneGeneration < 0)
            throw new InvalidDataException("The actual Judge source identity is incomplete.");
        var value = new SeekJudgeRawState(identity, Copy(input.Notes), Copy(input.TapWaiting), Copy(input.DragWaiting), input.HoldOnJudgeCount);
        var lists = new[] { value.Notes, value.TapWaiting, value.DragWaiting };
        for (var index = 0; index < lists.Length; ++index)
            for (var previous = 0; previous < index; ++previous)
            {
                var a = lists[previous]; var b = lists[index];
                if (a.Reference == b.Reference && !Same(a, b))
                    throw new InvalidDataException("Aliased actual Judge lists gave contradictory complete storage.");
                if (a.Backing == b.Backing && (a.ArrayClass != b.ArrayClass || !a.CapacitySlots.SequenceEqual(b.CapacitySlots)))
                    throw new InvalidDataException("Aliased Judge backing arrays gave contradictory capacity slots.");
            }
        source.VerifyLocal(identity);
        return value;
    }

    private static SeekJudgeListStorage Copy(SeekJudgeListStorage input)
    {
        if (input is null || input.Reference == 0 || input.Class == 0 || input.Backing == 0 || input.ArrayClass == 0
            || input.Size < 0 || input.Size > MaximumCapacity || input.CapacitySlots is null
            || input.CapacitySlots.Count > MaximumCapacity || input.Size > input.CapacitySlots.Count)
            throw new InvalidDataException("An actual Judge list lacks complete bounded official storage.");
        // Version is the complete signed Int32 value, including wraparound.
        return input with { CapacitySlots = Array.AsReadOnly(input.CapacitySlots.ToArray()) };
    }

    private static bool Same(SeekJudgeListStorage first, SeekJudgeListStorage second)
        => first.Reference == second.Reference && first.Class == second.Class && first.Backing == second.Backing
            && first.ArrayClass == second.ArrayClass && first.Size == second.Size && first.Version == second.Version
            && first.CapacitySlots.SequenceEqual(second.CapacitySlots);
    private static bool Same(SeekJudgeRawState first, SeekJudgeRawState second)
        => first.Identity == second.Identity && first.HoldOnJudgeCount == second.HoldOnJudgeCount
            && Same(first.Notes, second.Notes) && Same(first.TapWaiting, second.TapWaiting) && Same(first.DragWaiting, second.DragWaiting);

    private void VerifyLifetime()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekJudgeStateView));
        if (_faulted) throw new InvalidOperationException("This Judge-state view was already observed expired.");
    }

    internal void VerifySource()
    {
        _source.VerifyOwnerThread();
        VerifyLifetime();
        if (_inFlight != 0) throw new InvalidOperationException("Judge-state verification cannot reenter itself.");
        ++_inFlight;
        Exception? operationFailure = null;
        try
        {
            if (!Same(_baseline, ReadSnapshot(_source)))
                throw new InvalidOperationException("The retained Judge source or complete storage has changed.");
            _source.VerifyLocal(Identity);
            VerifyLifetime();
        }
        catch (Exception error) { _faulted = true; operationFailure = error; throw; }
        finally
        {
            --_inFlight;
            if (_disposed)
            {
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (operationFailure is null) throw;
                    AttachCleanup(operationFailure, this, cleanup);
                }
            }
        }
    }

    private void RecordCleanup(Exception error)
    {
        if (!_cleanupFailures.Contains(error)) _cleanupFailures.Add(error);
        CleanupFailure = string.Join("; ", _cleanupFailures.Select(value => value.Message));
    }
    private AggregateException CleanupError()
    {
        var error = new AggregateException("Judge-state cleanup retains its native responsibility without unknown-free retry.", _cleanupFailures);
        error.Data["SeekJudgeStateCleanupOwner"] = this;
        return error;
    }
    public void Dispose()
    {
        _source.VerifyOwnerThread();
        if (_notifyingDisposal || _releasing) return;
        _disposed = true;
        _notifyingDisposal = true;
        try { _source.RequestDispose(); }
        catch (Exception error) { RecordCleanup(error); }
        finally { _notifyingDisposal = false; }
        if (_inFlight == 0) Release();
        if (_cleanupFailures.Count != 0) throw CleanupError();
    }
    private void Release()
    {
        if (_released || _releasing) return;
        _releasing = true;
        try { _source.Dispose(); _released = true; }
        catch (Exception error) { RecordCleanup(error); throw CleanupError(); }
        finally { _releasing = false; }
    }
}
