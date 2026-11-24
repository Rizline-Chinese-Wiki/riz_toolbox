using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

internal sealed record ObservedResultOrigin(string SessionId, string RunId, long Information, long Controller,
    long Revision, long SceneGeneration, long SourceGeneration);

/// <summary>One independently owned result/controller/information graph. Targets are read from live GC roots.</summary>
internal interface IResultOriginRetention : IDisposable
{
    long Result { get; }
    long Information { get; }
    long Controller { get; }
    RunFinished? TerminalSnapshot => null;
}

/// <summary>Bounded provenance of original ControllerOver returns. Never derives a run from chart metadata.
/// Pending entries and consumed tombstones keep independent roots, preventing address reuse until eviction.</summary>
internal sealed class ResultOriginRegistry
{
    internal const int MaximumPending = 32, MaximumConsumed = 32;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<Entry> _entries = new();
    private long _order;
    internal bool Active { get; private set; }
    internal long Generation { get; private set; }
    internal string UnavailableReason { get; private set; } = "Result-origin observer has not been installed.";
    internal long Accepted { get; private set; }
    internal long Rejected { get; private set; }
    internal long DuplicateCaptures { get; private set; }
    internal long DuplicateSamples { get; private set; }
    internal long Conflicts { get; private set; }
    internal long ReleaseFailures { get; private set; }
    internal int PendingCount => _entries.Count(x => !x.Consumed);
    internal int RetainedCount => _entries.Count;
    private sealed class Entry
    {
        internal readonly IResultOriginRetention Retention;
        internal readonly RunFinished? TerminalSnapshot;
        internal ObservedResultOrigin? Origin;
        internal RunFinished? PublishedResult;
        internal bool Consumed;
        internal long ConsumedOrder;
        internal Entry(IResultOriginRetention retention, ObservedResultOrigin origin)
            => (Retention, Origin, TerminalSnapshot) = (retention, origin, retention.TerminalSnapshot);
    }

    internal long Activate()
    {
        Verify(); Withdraw("Result-origin observer is being replaced.");
        Active = true; UnavailableReason = ""; return Generation;
    }

    internal void Withdraw(string reason)
    {
        Verify(); Active = false; Generation++;
        UnavailableReason = string.IsNullOrWhiteSpace(reason) ? "Result-origin observer was withdrawn." : reason;
        var retired = _entries.ToArray(); _entries.Clear();
        foreach (var entry in retired) Release(entry.Retention);
    }

    /// <summary>Takes ownership of retention on every path, including refusals and duplicates.</summary>
    internal bool Register(ObservedResultOrigin origin, IResultOriginRetention retention, out string reason)
    {
        Verify(); ArgumentNullException.ThrowIfNull(retention);
        var adopted = false;
        try
        {
            if (!Active || origin.SourceGeneration != Generation)
            { reason = "Result origin belongs to an unavailable or replaced observer."; Rejected++; return false; }
            if (retention.Result == 0 || retention.Information == 0 || retention.Controller == 0 ||
                string.IsNullOrWhiteSpace(origin.SessionId) || string.IsNullOrWhiteSpace(origin.RunId))
            { reason = "Result origin has no live rooted identity."; Rejected++; return false; }
            var previous = _entries.FirstOrDefault(x => x.Retention.Result == retention.Result);
            if (previous is not null)
            {
                if (previous.Origin is { } known && SameRun(known, origin)
                    && previous.Retention.Information == retention.Information && previous.Retention.Controller == retention.Controller)
                { DuplicateCaptures++; reason = "The same rooted result and origin were already retained."; return false; }
                previous.Origin = null; previous.PublishedResult = null; Conflicts++; Rejected++;
                reason = "Conflicting origins for the same rooted result; association refused."; return false;
            }
            if (PendingCount >= MaximumPending)
            { reason = "Result-origin pending capacity reached; association refused."; Rejected++; return false; }
            _entries.Add(new(retention, origin)); adopted = true; Accepted++;
            reason = "Original ControllerOver result retained with its entry run origin."; return true;
        }
        finally { if (!adopted) Release(retention); }
    }

    internal ObservedResultOrigin? Consume(long result, out bool duplicate, out string reason)
    {
        Verify(); duplicate = false;
        if (!Active) { reason = UnavailableReason; return null; }
        var entry = result == 0 ? null : _entries.FirstOrDefault(x => x.Retention.Result == result);
        if (entry is null) { reason = "Actual result has no captured original-return origin."; return null; }
        if (entry.Consumed)
        { DuplicateSamples++; duplicate = true; reason = "This rooted result was already sampled."; return null; }
        var origin = CurrentOrigin(entry);
        reason = origin is null ? "Result origin was conflicting or withdrawn." : "Actual result matched its retained original-return origin.";
        MarkConsumed(entry);
        return origin;
    }

    // A unique return with a copied terminal snapshot can be delivered without a live result screen.
    internal RunFinished? FinishTerminalReturn(ObservedResultOrigin current, string? chartId,
        Func<RunFinished, ObservedResultOrigin, RunFinished?> finish)
    {
        Verify();
        if (!Active || current.SourceGeneration != Generation) return null;
        Entry? candidate = null;
        foreach (var entry in _entries)
        {
            if (entry.Consumed || entry.TerminalSnapshot is not { } snapshot
                || chartId is not null && snapshot.ChartId is not null && chartId != snapshot.ChartId
                || entry.Origin is not { } origin || !SameRun(origin, current)
                || entry.Retention.Information != current.Information || entry.Retention.Controller != current.Controller
                || entry.Retention.Result == 0) continue;
            if (candidate is not null) return null;
            candidate = entry;
        }
        if (candidate is null || CurrentOrigin(candidate) is not { } exact
            || exact.Information != current.Information || exact.Controller != current.Controller) return null;
        MarkConsumed(candidate);
        var result = finish(candidate.TerminalSnapshot!, exact);
        if (candidate.Origin is not null && result is { AssociatedWithObservedStart: true }) candidate.PublishedResult = result;
        return result;
    }

    internal RunFinished? ReadPublishedResult(long result)
    {
        Verify();
        return Active && result != 0 ? _entries.FirstOrDefault(entry => entry.PublishedResult is not null
            && entry.Retention.Result == result)?.PublishedResult : null;
    }

    private ObservedResultOrigin? CurrentOrigin(Entry entry)
        => entry.Origin is { } origin && origin.SourceGeneration == Generation
            ? origin with { Information = entry.Retention.Information, Controller = entry.Retention.Controller } : null;

    private void MarkConsumed(Entry entry)
    {
        entry.Consumed = true; entry.ConsumedOrder = ++_order;
        while (_entries.Count(x => x.Consumed) > MaximumConsumed)
        {
            var oldest = _entries.Where(x => x.Consumed).MinBy(x => x.ConsumedOrder)!;
            _entries.Remove(oldest); Release(oldest.Retention);
        }
    }

    private static bool SameRun(ObservedResultOrigin left, ObservedResultOrigin right)
        => left.SessionId == right.SessionId && left.RunId == right.RunId && left.SourceGeneration == right.SourceGeneration;
    private void Release(IDisposable retention)
    { try { retention.Dispose(); } catch { ReleaseFailures++; } }
    private void Verify()
    { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Result origins require the verified main thread."); }
}
