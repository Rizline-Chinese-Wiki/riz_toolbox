using System.Globalization;
using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum LocalRunHostResultAction { Retry, Return }

/// <summary>Host-owned result UI for the exact cached native capture. No stock
/// result scene, score persistence, Steam call or native receiver is synthesized.
/// The supplied UI view remains owned by the composition root.</summary>
internal sealed class NativeLocalRunResultSink : ILocalRunTerminationResultSink, IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunSessionRegistry _registry;
    private readonly Func<IOwnedUiService> _ui;
    private readonly Func<LocalRunSessionRegistry.SessionHandle, LocalRunHostResultAction, Result> _execute;
    private readonly ILogger? _log;
    private readonly Dictionary<LocalRunSessionRegistry.SessionHandle, Presentation> _presentations = new(32);
    private bool _busy, _disposed;

    internal NativeLocalRunResultSink(IMainThreadDispatcher main, LocalRunSessionRegistry registry,
        IOwnedUiService ui, Func<LocalRunSessionRegistry.SessionHandle, LocalRunHostResultAction, Result> execute, ILogger? log = null)
        : this(main, registry, () => ui, execute, log) => ArgumentNullException.ThrowIfNull(ui);

    private NativeLocalRunResultSink(IMainThreadDispatcher main, LocalRunSessionRegistry registry,
        Func<IOwnedUiService> ui, Func<LocalRunSessionRegistry.SessionHandle, LocalRunHostResultAction, Result> execute, ILogger? log)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(ui); ArgumentNullException.ThrowIfNull(execute);
        (_main, _registry, _ui, _execute, _log) = (main, registry, ui, execute, log);
    }
    internal NativeLocalRunResultSink(IMainThreadDispatcher main, LocalRunSessionRegistry registry,
        Func<IOwnedUiService> ui, NativeLocalRunResultCommands commands, ILogger? log = null)
        : this(main, registry, ui, (session, action) => commands.Execute(session,
            registry.Read(session).Result ?? throw new InvalidOperationException("The exact result cache is absent."), action), log)
        => ArgumentNullException.ThrowIfNull(commands);
    internal bool HasPendingPresentations
    { get { _main.VerifyAccess(); return _presentations.Values.Any(item => !item.ConsumersEnded); } }
    internal bool HasUnknownCleanup
    { get { _main.VerifyAccess(); return _presentations.Values.Any(item => item.Unknown); } }
    internal OperationError? ReadFailure(LocalRunSessionRegistry.SessionHandle session)
    { _main.VerifyAccess(); return _presentations.TryGetValue(session, out var value) ? value.Failure : null; }

    public void Present(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result,
        Action<ILocalRunTerminationResultPresentation> publish)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(result); ArgumentNullException.ThrowIfNull(publish);
        if (_disposed || _busy) throw new InvalidOperationException("The host result sink is unavailable or reentrant.");
        RequireExactResult(session, result);
        if (_presentations.ContainsKey(session)) throw new InvalidOperationException("This exact result presentation has already been attempted.");
        if (_presentations.Count >= LocalRunSessionRegistry.MaximumProcessSessions)
            throw new InvalidOperationException("The host result presentation capacity is exhausted.");
        var owner = new Presentation(this, session, result);
        _presentations.Add(session, owner); _busy = true;
        try
        {
            // The termination queue adopts this owner before a window, control or
            // callback can be created, including a partial-create failure.
            publish(owner);
            owner.Create();
        }
        catch (Exception error)
        {
            owner.MarkUnknown(new(ErrorCode.InternalError, "Result presentation did not complete: " + error.Message));
            throw;
        }
        finally { _busy = false; }
    }
    internal Result Close(LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(session);
        if (_busy) return Result.Fail(new(ErrorCode.NotReady, "The result presentation is being created."));
        if (!_presentations.TryGetValue(session, out var owner))
            return Result.Fail(new(ErrorCode.TargetMissing, "This exact run has no owned result presentation."));
        try { owner.Dispose(); return owner.Unknown ? Result.Fail(owner.Failure!) : Result.Ok(); }
        catch (Exception error) { return Result.Fail(owner.Failure ?? new(ErrorCode.InternalError, error.Message)); }
    }
    internal void Tick()
    {
        _main.VerifyAccess(); if (_busy) return;
        foreach (var owner in _presentations.Values.ToArray())
        {
            owner.ObserveWindow();
            if (owner.ConsumersEnded && _registry.Read(owner.Session).Phase == LocalRunSessionPhase.Closed)
                _presentations.Remove(owner.Session);
        }
    }
    private void RequireExactResult(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result)
    {
        var current = _registry.Read(session);
        if (current.Phase != LocalRunSessionPhase.ResultReady || current.ReturnRequested || current.ProtectionRetired
            || current.ControllerIdentity == 0 || current.Identity != result.Session || !ReferenceEquals(current.Result, result))
            throw new InvalidOperationException("The result view requires this registry's exact live cached native result.");
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_busy) throw new InvalidOperationException("Result presentation creation cannot be disposed reentrantly.");
        _disposed = true;
        var failures = new List<Exception>();
        foreach (var owner in _presentations.Values)
            try { owner.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Host result UI cleanup remains unknown; responsibility is retained.", failures);
    }
    private sealed class Presentation : ILocalRunTerminationResultPresentation
    {
        private readonly NativeLocalRunResultSink _owner;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        private readonly LocalRunCapturedResult _result;
        private readonly IUiControlHandle?[] _controls = new IUiControlHandle?[4];
        private IUiWindowHandle? _window;
        private bool _ready, _accepted, _disposeAttempted, _disposed;
        private int _actions;
        internal bool Unknown { get; private set; }
        internal OperationError? Failure { get; private set; }
        internal Presentation(NativeLocalRunResultSink owner, LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result)
            => (_owner, Session, _result) = (owner, session, result);
        public bool ConsumersEnded
        { get { _owner._main.VerifyAccess(); return _disposed && !Unknown && _actions == 0; } }
        internal void Create()
        {
            var window = _owner._ui().CreateWindow(new("local-result-" + _result.Session.RunGeneration.ToString(CultureInfo.InvariantCulture),
                UiMountIds.Overlay, "LOCAL RESULT", new(new(460, 160, 1000, 720)), Modal: true));
            _window = Require(window);
            var text = Describe(_result);
            _controls[0] = Require(_window.AddLabel(new("result", text, new(28, 62, 940, 390))));
            _controls[1] = Require(_window.AddLabel(new("status", "Captured LOCAL result. Choose Retry or Return.", new(28, 472, 940, 100))));
            _controls[2] = Require(_window.AddButton(new("retry", "Retry", new(28, 610, 420, 64)), () => Invoke(LocalRunHostResultAction.Retry)));
            _controls[3] = Require(_window.AddButton(new("return", "Return", new(488, 610, 420, 64)), () => Invoke(LocalRunHostResultAction.Return)));
            var focused = _window.Focus(); if (!focused.Success) throw new InvalidOperationException(focused.Error!.Message);
            _ready = true;
            if (_owner._log is { } log) ReportDisplayedResult(log, text);
        }
        // Read the label once after focus. A diagnostic read or log failure must
        // leave the player's Return and Retry buttons usable.
        private void ReportDisplayedResult(ILogger log, string expected)
        {
            try
            {
                var text = _controls[0]!.GetText();
                if (!text.Success || !string.Equals(text.Value, expected, StringComparison.Ordinal) || !_window!.IsAlive)
                    throw new InvalidOperationException("The owned result label could not be confirmed.");
                _owner.RequireExactResult(Session, _result);
                var run = _result.Session;
                var copy = _result.Copy;
                log.Log(LogLevel.Info, "LocalResultDisplayed " + JsonSerializer.Serialize(new {
                    windowId = _window.Id, localSessionId = run.SessionId, run.Song, run.SongRevision, run.ChartId, run.RunGeneration,
                    copy.Score, copy.RawCompletion, copy.Perfect, copy.MaxPerfect, copy.Bad, copy.Miss, copy.Crashed, copy.Skipped,
                    endReason = copy.EndReason.ToString()
                }));
            }
            catch (Exception error)
            {
                try { log.Log(LogLevel.Warning, "LOCAL result label readback unavailable.", error); } catch { }
            }
        }
        private static T Require<T>(Result<T> result) where T : class
            => result.Success && result.Value is not null ? result.Value : throw new InvalidOperationException(result.Error?.Message ?? "Owned result UI returned no handle.");
        private static string Describe(LocalRunCapturedResult result)
        {
            var copy = result.Copy;
            // Display the actual copied counters verbatim; never invent a RunId,
            // observed start, normalized completion or replacement best score.
            var body = $"Song: {result.Session.Song}\nSelected chart: {result.Session.ChartId}\n"
                + $"Score: {copy.Score.ToString(CultureInfo.InvariantCulture)}\n"
                + $"Raw completion: {copy.RawCompletion.ToString("R", CultureInfo.InvariantCulture)}\n"
                + $"Perfect: {copy.Perfect.ToString(CultureInfo.InvariantCulture)}    MaxPerfect: {copy.MaxPerfect.ToString(CultureInfo.InvariantCulture)}\n"
                + $"Bad: {copy.Bad.ToString(CultureInfo.InvariantCulture)}    Miss: {copy.Miss.ToString(CultureInfo.InvariantCulture)}\n"
                + $"Crashed: {copy.Crashed}    Skipped: {copy.Skipped}\nEnd reason: {copy.EndReason}";
            return Bounded(body, 3800);
        }
        private void Invoke(LocalRunHostResultAction action)
        {
            _owner._main.VerifyAccess();
            if (!_ready || Unknown || _disposeAttempted || _accepted || _actions != 0 || _owner._disposed || _owner._busy) return;
            ++_actions;
            try
            {
                if (_window?.IsAlive != true) throw new InvalidOperationException("The host result window is no longer live.");
                _owner.RequireExactResult(Session, _result);
                var result = _owner._execute(Session, action) ?? throw new InvalidOperationException("The host command returned no result.");
                if (result.Success)
                {
                    _accepted = true;
                    if (!_disposeAttempted) SetStatus(action + " request accepted; waiting for the host transaction.");
                }
                else
                {
                    Failure = result.Error!;
                    if (!_disposeAttempted) SetStatus(action + " failed [" + result.Error!.Code + "]: " + result.Error.Message);
                }
            }
            catch (Exception error)
            {
                MarkUnknown(new(ErrorCode.InternalError, "The result action outcome is unknown: " + error.Message));
                if (!_disposeAttempted) SetStatus("Action outcome unknown: " + error.Message);
            }
            finally { --_actions; }
        }
        private void SetStatus(string text)
        {
            try
            {
                var result = _controls[1]?.SetText(Bounded(text, 2000));
                if (result?.Success != true) MarkUnknown(result?.Error ?? new(ErrorCode.TargetExpired, "The result status control is unavailable."));
            }
            catch (Exception error) { MarkUnknown(new(ErrorCode.InternalError, "Result status update failed: " + error.Message)); }
        }
        internal void MarkUnknown(OperationError failure) { Unknown = true; _ready = false; Failure ??= failure; }
        internal void ObserveWindow()
        {
            if (_disposeAttempted || _window is null) return;
            try
            {
                if (_window.IsAlive) return;
                // The public UI contract does not expose prior native cleanup
                // errors from an external scene/scope retirement. It cannot be
                // used to certify this presentation's disposal after the fact.
                MarkUnknown(new(ErrorCode.TargetExpired, "The result window expired outside its checked host cleanup."));
                Dispose();
            }
            catch (Exception error) { MarkUnknown(new(ErrorCode.InternalError, "Result window cleanup is unknown: " + error.Message)); }
        }
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            // Publication deliberately precedes the first UI allocation. A
            // synchronous publish/backend callback must not end this owner
            // before Create has adopted all handles returned by that callback.
            if (_owner._busy) throw new InvalidOperationException("A result presentation cannot be disposed while its UI construction is in progress.");
            if (_disposeAttempted)
            {
                if (!_disposed || Unknown) throw new InvalidOperationException("Previous result UI cleanup is unknown and will not be retried.");
                return;
            }
            _disposeAttempted = true; _ready = false;
            var failures = new List<Exception>();
            for (var index = _controls.Length - 1; index >= 0; --index)
                if (_controls[index] is { } control)
                    try { control.Dispose(); _controls[index] = null; } catch (Exception error) { failures.Add(error); }
            if (_window is { } window)
                try { window.Dispose(); _window = null; } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0)
            {
                MarkUnknown(new(ErrorCode.InternalError, "Result UI cleanup failed: " + failures[0].Message));
                throw new AggregateException("Result UI cleanup is unknown and remains owned without retry.", failures);
            }
            _disposed = true;
            if (Unknown) throw new InvalidOperationException("Result presentation responsibility remains unknown after UI cleanup.");
        }
        private static string Bounded(string text, int limit) => text.Length <= limit ? text : text[..limit];
    }
}
