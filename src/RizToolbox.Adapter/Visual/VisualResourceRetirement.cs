using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Visual;

/// <summary>Process-rooted cleanup work survives capability withdrawal. Reservations bound both
/// live resources and failed retirements; an unsafe item is never evicted to make room.</summary>
internal sealed class VisualResourceRetirement
{
    internal const int MaximumItems = 512, MaximumAttemptsPerTick = 4;
    private static readonly HashSet<VisualResourceRetirement> ProcessRoots = new();
    private static int ProcessReserved;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly List<Ticket> _tickets = new();
    private readonly int _capacity;
    private double _now;
    private bool _ticking;
    private long _attempts;
    internal VisualResourceRetirement(IMainThreadDispatcher main, ILogger log, int capacity = MaximumItems)
    { _main = main; _log = log; _capacity = Math.Clamp(capacity, 1, MaximumItems); }
    internal int Reserved { get { _main.VerifyAccess(); return _tickets.Count; } }
    internal int Pending { get { _main.VerifyAccess(); return _tickets.Count(x => x.Cleanup is not null || x.Quarantined); } }
    internal long Attempts { get { _main.VerifyAccess(); return _attempts; } }
    internal bool HasPending(VisualTargetKind kind)
    { _main.VerifyAccess(); return ProcessRoots.Any(owner => owner._main.IsMainThread && owner._tickets.Any(x => x.Kind == kind && (x.Quarantined || x.BlocksProvider && x.Cleanup is not null))); }
    internal bool HasQuarantined(VisualTargetKind kind)
    { _main.VerifyAccess(); return ProcessRoots.Any(owner => owner._main.IsMainThread && owner._tickets.Any(x => x.Kind == kind && x.Quarantined)); }
    internal Ticket Reserve(VisualTargetKind kind, bool blocksProvider = false)
    {
        _main.VerifyAccess();
        if (_tickets.Count >= _capacity) throw new VisualOperationException(ErrorCode.QuotaExceeded,"Live/retired resources exhaust the process cleanup reservation limit.");
        if (Interlocked.Increment(ref ProcessReserved) > MaximumItems)
        { Interlocked.Decrement(ref ProcessReserved); throw new VisualOperationException(ErrorCode.QuotaExceeded,"Process cleanup reservation limit reached."); }
        var ticket = new Ticket(this, kind, blocksProvider);
        try { _tickets.Add(ticket); return ticket; }
        catch { Interlocked.Decrement(ref ProcessReserved); throw; }
    }
    // Adapter replacement must keep retrying earlier providers' physical cleanup. The absolute
    // monotonic clock is shared across adapter generations; at most four attempts run per host tick.
    internal static void TickPending(double monotonicSeconds)
    {
        var remaining = MaximumAttemptsPerTick;
        foreach (var owner in ProcessRoots.ToArray())
        {
            if (!owner._main.IsMainThread) continue;
            var before = owner._attempts;
            try { owner.Tick(monotonicSeconds, remaining); }
            catch (Exception error) { owner.SafeLog("Visual owner retirement failed; its pending reservations remain rooted.", error); }
            remaining -= (int)(owner._attempts - before); if (remaining <= 0) break;
        }
    }
    internal void Tick(double monotonicSeconds)
        => Tick(monotonicSeconds, MaximumAttemptsPerTick);
    private void Tick(double monotonicSeconds, int limit)
    {
        _main.VerifyAccess();
        if (_ticking || !double.IsFinite(monotonicSeconds) || monotonicSeconds < _now) return;
        _now = monotonicSeconds; _ticking = true;
        try
        {
            foreach (var ticket in _tickets.Where(x => x.Cleanup is not null && !x.Quarantined && x.Next <= _now)
                         .OrderBy(x => x.Next).Take(limit).ToArray()) Attempt(ticket);
        }
        finally { _ticking = false; }
    }
    private void Attempt(Ticket ticket)
    {
        if (ticket.Owner is null || ticket.Cleanup is null || ticket.Running || ticket.Quarantined) return;
        ticket.Running = true; ticket.Attempts++; _attempts++;
        try
        {
            if (ticket.Cleanup() && !ticket.Quarantined)
            {
                ticket.Dispose();
                // Completion of this predicate and reservation says nothing about GPU memory.
                // Log only after Dispose removes the ticket and returns its process reservation.
                SafeLog($"Visual cleanup completed: {ticket.Description}; kind={ticket.Kind}; attempts={ticket.Attempts}; " +
                    $"cleanupPredicateCompleted=true; retirementReservationReleased=true; ownerReserved={_tickets.Count}; " +
                    $"ownerPending={_tickets.Count(x => x.Cleanup is not null)}; globalReserved={Volatile.Read(ref ProcessReserved)}.",
                    level: LogLevel.Info);
                return;
            }
        }
        catch (Exception error) { SafeLog("Visual retirement attempt failed for " + ticket.Description + ".", error); }
        finally { ticket.Running = false; }
        if (ticket.Owner is null || ticket.Quarantined) return;
        ticket.Failures = Math.Min(ticket.Failures + 1, 31);
        ticket.Next = _now + Math.Min(30, .25 * Math.Pow(2, Math.Min(ticket.Failures - 1, 7)));
        if (ticket.Failures == 1 || (ticket.Failures & (ticket.Failures - 1)) == 0)
            SafeLog($"Visual cleanup pending: {ticket.Description}; attempts={ticket.Failures}; nextDelay={ticket.Next - _now:F2}s; roots/budget retained.");
    }
    private void SafeLog(string message, Exception? error = null, LogLevel level = LogLevel.Warning)
    { try { _log.Log(level, message, error); } catch { } }
    internal sealed class Ticket : IDisposable
    {
        internal VisualResourceRetirement? Owner;
        internal readonly VisualTargetKind Kind;
        internal readonly bool BlocksProvider;
        internal Func<bool>? Cleanup;
        internal string Description = "visual resource";
        internal double Next;
        internal int Failures;
        internal long Attempts;
        internal bool Running, Quarantined;
        // One existing reservation roots its bounded responsibility graph after an
        // unknown free. Arbitrary IDisposable calls are never retried after throwing.
        internal object? QuarantineResponsibility;
        internal Ticket(VisualResourceRetirement owner, VisualTargetKind kind, bool blocksProvider) { Owner = owner; Kind = kind; BlocksProvider = blocksProvider; }
        internal void Retire(string description, Func<bool> cleanup, bool attemptNow = true)
        {
            var owner = Owner; if (owner is null) return; owner._main.VerifyAccess();
            if (Cleanup is not null) return;
            Description = description; Cleanup = cleanup; Next = owner._now;
            ProcessRoots.Add(owner);
            if (attemptNow && !owner._ticking) owner.Attempt(this);
        }
        internal void Quarantine(string description, object responsibility, Exception error)
        {
            var owner = Owner; if (owner is null) throw new InvalidOperationException("A released retirement reservation cannot quarantine cleanup.");
            owner._main.VerifyAccess(); if (Quarantined) return;
            Description = description; QuarantineResponsibility = responsibility; Quarantined = true;
            ProcessRoots.Add(owner);
            owner.SafeLog($"Visual cleanup quarantined: {description}; kind={Kind}; attempts={Attempts}; " +
                "freeOutcome=unknown; reservationRetained=true; retryDisabled=true; processRestartRequired=true.", error);
        }
        public void Dispose()
        {
            var owner = Owner; if (owner is null) return; owner._main.VerifyAccess();
            if (Quarantined) return;
            Owner = null; Cleanup = null; owner._tickets.Remove(this);
            Interlocked.Decrement(ref ProcessReserved);
            if (!owner._tickets.Any(x => x.Cleanup is not null || x.Quarantined)) ProcessRoots.Remove(owner);
        }
    }
}
