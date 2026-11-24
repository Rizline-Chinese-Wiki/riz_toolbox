using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Judgements;

internal sealed record ObservedJudgementRun(string SessionId, string RunId, long SceneGeneration,
    long Revision, long Information, long Controller)
{
    internal bool SameGeneration(ObservedJudgementRun other) => SessionId == other.SessionId && RunId == other.RunId
        && SceneGeneration == other.SceneGeneration && Information == other.Information && Controller == other.Controller;
}
internal sealed record RuntimeNoteIdentity(string SessionId, string RunId, long Sequence);
internal sealed record ChartNoteLocation(int LineOrdinal, int NoteOrdinal);
internal readonly record struct ChartNoteReference(long Reference, int LineOrdinal, int NoteOrdinal);
internal enum NoteGraphAssociation { Missing, Unique, Ambiguous }

/// <summary>Internal object identities are usable only while rooted. No pointer or Unity object leaves this registry.
/// Run retention owns the actual information/chart graph; each note has its own independent root.
/// Retirement closes admission immediately and defers all root cleanup until the final native invocation exits.</summary>
internal sealed class RuntimeNoteRegistry : IDisposable
{
    internal const int MaximumNotes = 65536;
    internal const int MaximumInFlight = 4096;
    private readonly IMainThreadDispatcher _main;
    private readonly ObservedJudgementRun _run;
    private readonly int _capacity;
    private readonly Action<Exception>? _cleanupFailure;
    private readonly Dictionary<long, ChartNoteLocation?> _graph = new();
    private readonly Dictionary<long, Entry> _notes = new();
    private readonly List<Entry> _ordered = new();
    private IDisposable? _runRetention;
    private bool _capturing, _released;
    private long _sequence, _lastRevision;
    private int _inFlight;
    private sealed record Entry(long NoteData, RuntimeNoteIdentity Identity, ChartNoteLocation? Location,
        NoteGraphAssociation Association, IDisposable Root);

    /// <summary>Takes ownership of runRetention, including constructor failure. The supplied graph is a copied,
    /// bounded reference traversal of the retained actual chart; matching is never based on note time or value.</summary>
    internal RuntimeNoteRegistry(IMainThreadDispatcher main, ObservedJudgementRun run, IDisposable runRetention,
        IEnumerable<ChartNoteReference> graph, int capacity = 16384, Action<Exception>? cleanupFailure = null)
    {
        _main = main; _run = run; _runRetention = runRetention; _capacity = capacity; _cleanupFailure = cleanupFailure;
        try
        {
            main.VerifyAccess(); ArgumentNullException.ThrowIfNull(runRetention); ArgumentNullException.ThrowIfNull(graph);
            if (run is null || !ValidId(run.SessionId) || !ValidId(run.RunId) || run.Information == 0 || run.Controller == 0
                || run.SceneGeneration < 0 || run.Revision < 0 || capacity is < 1 or > MaximumNotes)
                throw new ArgumentException("A real observed run and bounded capacity are required.");
            _lastRevision = run.Revision;
            var count = 0;
            foreach (var item in graph)
            {
                if (++count > MaximumNotes) throw new ArgumentException("The retained chart graph exceeds the note-reference limit.");
                if (item.Reference == 0 || item.LineOrdinal < 0 || item.NoteOrdinal < 0)
                    throw new ArgumentException("Chart reference traversal contains an invalid location or null object.");
                if (!_graph.TryAdd(item.Reference, new(item.LineOrdinal, item.NoteOrdinal))) _graph[item.Reference] = null;
            }
        }
        catch
        {
            var retention = _runRetention; _runRetention = null; _graph.Clear(); ReleaseSafely(retention); throw;
        }
    }
    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
    internal int EntryCount => _notes.Count;
    internal int InFlight => _inFlight;
    internal bool IsRetired { get; private set; }
    internal bool ResourcesReleased => _released;
    internal string? RetirementReason { get; private set; }
    internal int CleanupFailures { get; private set; }

    /// <summary>The exact native source must establish object/type/info relationships before calling this method.
    /// A retained note cannot silently change noteData within this fixed-build run.</summary>
    internal Result<InvocationLease> Acquire(ObservedJudgementRun current, long note, long noteData, Func<IDisposable> retainNote)
    {
        _main.VerifyAccess();
        if (IsRetired) return Failure(ErrorCode.ScopeExpired, "This note registry is retired.");
        if (_capturing) return Failure(ErrorCode.Conflict, "A native note root capture is already in progress.");
        if (current is null || !_run.SameGeneration(current) || current.Revision < _lastRevision)
            return Failure(ErrorCode.TargetExpired, "The judgement source does not match the current observed run generation.");
        if (note == 0 || noteData == 0 || retainNote is null)
            return Failure(ErrorCode.InvalidArgument, "Verified non-null note and noteData references are required.");
        if (_inFlight >= MaximumInFlight) return Failure(ErrorCode.QuotaExceeded, "Too many nested judgement invocations.");
        if (_notes.TryGetValue(note, out var known))
        {
            if (known.NoteData != noteData)
            {
                Retire("A retained NoteInformation changed its chart-note reference.");
                return Failure(ErrorCode.ObservationUnavailable, "Note identity changed while retained; complete note coverage was withdrawn.");
            }
            _lastRevision = current.Revision; return Result<InvocationLease>.Ok(NewLease(known, current));
        }
        if (_notes.Count >= _capacity)
        {
            Retire("The per-run retained-note capacity was reached.");
            return Failure(ErrorCode.QuotaExceeded, "The note registry reached its bounded capacity; no partial identity is invented.");
        }
        IDisposable? root = null;
        _capturing = true;
        try
        {
            root = retainNote() ?? throw new InvalidOperationException("The native note root was not created.");
            // Retaining a native object may synchronously trigger host cleanup or an observer callback.
            if (IsRetired) return Failure(ErrorCode.ScopeExpired, "The registry retired during native note capture.");
            var present = _graph.TryGetValue(noteData, out var location);
            var association = !present ? NoteGraphAssociation.Missing : location is null ? NoteGraphAssociation.Ambiguous : NoteGraphAssociation.Unique;
            var entry = new Entry(noteData, new(_run.SessionId, _run.RunId, ++_sequence), location, association, root);
            _notes.Add(note, entry);
            try { _ordered.Add(entry); }
            catch { _notes.Remove(note); throw; }
            root = null; _lastRevision = current.Revision;
            return Result<InvocationLease>.Ok(NewLease(entry, current));
        }
        catch (Exception)
        {
            return Failure(ErrorCode.ObservationUnavailable, "The verified native note could not be retained; no entry was admitted.");
        }
        finally { ReleaseSafely(root); _capturing = false; if (IsRetired) ReleaseWhenIdle(); }
    }
    private InvocationLease NewLease(Entry entry, ObservedJudgementRun captured)
    {
        var lease = new InvocationLease(this, entry.Identity, entry.Location, entry.Association, captured);
        ++_inFlight; return lease;
    }
    private static Result<InvocationLease> Failure(ErrorCode code, string message) => Result<InvocationLease>.Fail(new(code, message));
    internal void Retire(string reason)
    {
        _main.VerifyAccess();
        if (!IsRetired) { IsRetired = true; RetirementReason = string.IsNullOrWhiteSpace(reason) ? "Observed run ended." : reason; }
        ReleaseWhenIdle();
    }
    private void ReleaseWhenIdle()
    {
        if (!IsRetired || _released || _capturing || _inFlight != 0) return;
        _released = true;
        // Detach managed state before cleanup callbacks so reentry cannot release any root twice.
        _notes.Clear(); _graph.Clear();
        var retention = _runRetention; _runRetention = null;
        // Cleanup must not allocate a second array after the registry has been marked released.
        // Removing each entry before its callback also makes synchronous retirement reentry harmless.
        while (_ordered.Count != 0)
        {
            var index = _ordered.Count - 1; var entry = _ordered[index]; _ordered.RemoveAt(index);
            ReleaseSafely(entry.Root);
        }
        ReleaseSafely(retention);
    }
    private void ReleaseSafely(IDisposable? root)
    {
        try { root?.Dispose(); }
        catch (Exception error) { ++CleanupFailures; try { _cleanupFailure?.Invoke(error); } catch { } }
    }
    private void ReleaseInvocation()
    {
        _main.VerifyAccess(); --_inFlight; ReleaseWhenIdle();
    }
    public void Dispose() => Retire("The note observer was withdrawn.");

    internal sealed class InvocationLease : IDisposable
    {
        private RuntimeNoteRegistry? _owner;
        internal RuntimeNoteIdentity Identity { get; }
        internal ChartNoteLocation? ChartLocation { get; }
        internal NoteGraphAssociation GraphAssociation { get; }
        internal ObservedJudgementRun CapturedRun { get; }
        internal InvocationLease(RuntimeNoteRegistry owner, RuntimeNoteIdentity identity, ChartNoteLocation? location,
            NoteGraphAssociation association, ObservedJudgementRun captured)
            => (_owner, Identity, ChartLocation, GraphAssociation, CapturedRun) = (owner, identity, location, association, captured);
        public void Dispose()
        {
            var owner = _owner; if (owner is null) return;
            owner._main.VerifyAccess(); _owner = null; owner.ReleaseInvocation();
        }
    }
}
