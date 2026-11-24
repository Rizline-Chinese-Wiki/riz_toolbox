using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>One disjoint part of a prepared value change. Preparation/verification are read-only.
/// Restore uses private source/write authority; Dispose releases preparation resources without undo.</summary>
internal interface IGameplayValueParticipant : IDisposable
{
    string Id { get; }
    void VerifyPrepared();
    void Commit();
    GameplayControlConfirmation Confirm();
    void Restore();
    GameplayControlRestoration ConfirmRestored();
}

/// <summary>Orders a complete, backend-adapted set of participants. Supplies no native access or
/// production admission; the backend holds the exact run/source authority across callbacks.</summary>
internal sealed class GameplayPreparedValueControl : GameplayPreparedControl
{
    private readonly GameplayControlRequest _request;
    private readonly IGameplayValueParticipant[] _participants;
    private readonly Action _verifyOrigin;
    private readonly double _tolerance;
    private readonly List<string> _restoreErrors = new();
    private int _attempted;
    private bool _entered, _committed, _restoring, _disposed;
    private GameplayPreparedValueControl(GameplayControlRequest request,
        IGameplayValueParticipant[] participants, Action verifyOrigin, double tolerance)
        => (_request, _participants, _verifyOrigin, _tolerance) = (request, participants, verifyOrigin, tolerance);

    internal static GameplayPreparedValueControl Prepare(GameplayControlRequest request,
        IReadOnlyList<Func<IGameplayValueParticipant>> factories, Action verifyOrigin, double valueTolerance = .00001)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(verifyOrigin);
        if (request.ControllerGeneration <= 0 || string.IsNullOrWhiteSpace(request.ObservedRunId)
            || request.Value is not double value || !double.IsFinite(value)
            || request.Action == GameplayControlAction.SetSpeed && (value <= 0 || request.Unit != GameplayControlValueUnit.PlaybackRateMultiplier)
            || request.Action == GameplayControlAction.Seek && request.Unit is not (GameplayControlValueUnit.GameSeconds or GameplayControlValueUnit.ChartCoordinates)
            || request.Action is not (GameplayControlAction.SetSpeed or GameplayControlAction.Seek)
            || !double.IsFinite(valueTolerance) || valueTolerance < 0 || factories.Count == 0)
            throw new ArgumentException("A finite typed value request, observed run and participant set are required.");
        var inputs = factories.ToArray(); var prepared = new List<IGameplayValueParticipant>();
        var identities = new HashSet<IGameplayValueParticipant>(ReferenceEqualityComparer.Instance);
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            verifyOrigin();
            foreach (var factory in inputs)
            {
                verifyOrigin();
                var participant = factory?.Invoke() ?? throw new InvalidOperationException("A participant was not prepared.");
                if (!identities.Add(participant)) throw new InvalidOperationException("A participant cannot be committed twice.");
                prepared.Add(participant);
                if (string.IsNullOrWhiteSpace(participant.Id) || !names.Add(participant.Id))
                    throw new InvalidOperationException("Participant identities must be nonempty and unique.");
                participant.VerifyPrepared();
            }
            verifyOrigin(); return new(request, prepared.ToArray(), verifyOrigin, valueTolerance);
        }
        catch
        {
            foreach (var participant in prepared.AsEnumerable().Reverse()) try { participant.Dispose(); } catch { }
            throw;
        }
    }
    internal override bool CanRestore => !_disposed;
    internal override double ValueTolerance => _tolerance;
    private void Alive() { if (_disposed) throw new ObjectDisposedException(nameof(GameplayPreparedValueControl)); }
    private void Current() { Alive(); _verifyOrigin(); Alive(); }
    internal override void Commit()
    {
        Alive(); if (_entered || _restoring) throw new InvalidOperationException("Commit is once-only and cannot follow restoration.");
        _entered = true; Current();
        // All baselines are checked before any native participant is allowed to write.
        foreach (var participant in _participants) { Current(); participant.VerifyPrepared(); }
        Current();
        for (var i = 0; i < _participants.Length; ++i)
        {
            Current(); _participants[i].VerifyPrepared(); Current();
            _attempted = i + 1; // Includes a setter that writes and then throws.
            _participants[i].Commit();
        }
        Current(); _committed = true;
    }
    internal override GameplayControlConfirmation Confirm()
    {
        Alive(); if (!_committed || _restoring) throw new InvalidOperationException("Commit must finish before confirmation.");
        var evidence = new List<string>(); double? actual = null;
        foreach (var participant in _participants)
        {
            Current(); var observed = participant.Confirm(); Current();
            evidence.Add(participant.Id + ": " + observed.Evidence);
            if (observed.Observed != true) return new(false, string.Join("; ", evidence));
            if (observed.Value is double value)
            {
                if (!double.IsFinite(value) || observed.Unit != _request.Unit || Math.Abs(value - _request.Value!.Value) > _tolerance)
                    return new(false, "Readback value/unit differs. " + string.Join("; ", evidence));
                actual ??= value;
            }
            else if (observed.Unit != GameplayControlValueUnit.None)
                return new(false, "Readback unit has no actual value. " + string.Join("; ", evidence));
        }
        return actual is null ? new(false, "No numeric readback. " + string.Join("; ", evidence))
            : new(true, string.Join("; ", evidence), actual, _request.Unit);
    }
    internal override void Restore()
    {
        Alive(); if (_restoring) throw new InvalidOperationException("Restoration is once-only.");
        _restoring = true;
        // Continue other independent restorations after failure, then read all baselines separately.
        for (var i = _attempted - 1; i >= 0; --i)
            try { Current(); _participants[i].Restore(); }
            catch (Exception error) { _restoreErrors.Add(_participants[i].Id + ": " + error.Message); }
    }
    internal override GameplayControlRestoration ConfirmRestored()
    {
        Alive(); if (!_restoring) throw new InvalidOperationException("Attempt restoration before baseline readback.");
        var restored = true; var evidence = new List<string>(_restoreErrors.Select(error => "Restore reported " + error));
        foreach (var participant in _participants)
            try
            {
                Current(); var actual = participant.ConfirmRestored(); Current();
                restored &= actual.OriginalStateRestored; evidence.Add(participant.Id + ": " + actual.Evidence);
            }
            catch (Exception error) { restored = false; evidence.Add(participant.Id + ": readback failed: " + error.Message); }
        return new(restored, string.Join("; ", evidence));
    }
    public override void Dispose()
    {
        if (_disposed) return; _disposed = true; var errors = new List<Exception>();
        foreach (var participant in _participants.Reverse())
            try { participant.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Preparation cleanup failed.", errors);
    }
}
