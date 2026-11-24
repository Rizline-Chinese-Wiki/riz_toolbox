using System.Runtime.ExceptionServices;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>A fresh selection belongs to one explicit consumer, including a
/// failed selection. Nested consumers start empty and restore their caller's
/// scope on exit. Without a scope every read selects afresh.</summary>
internal sealed class GameplayClockProfileReads<TProfile> where TProfile : class
{
    private readonly Func<(TProfile? Profile, string? Error)> _refresh;
    private Scope? _scope;
    internal GameplayClockProfileReads(Func<(TProfile? Profile, string? Error)> refresh) => _refresh = refresh;
    internal IDisposable Begin()
    {
        var scope = new Scope(this, _scope); _scope = scope; return scope;
    }
    internal TProfile? Read(out string? error)
    {
        if (_scope is { } scope) return scope.Read(out error);
        var fresh = _refresh(); error = fresh.Error; return fresh.Profile;
    }
    private sealed class Scope : IDisposable
    {
        private readonly GameplayClockProfileReads<TProfile> _owner;
        private readonly Scope? _previous;
        private TProfile? _profile;
        private string? _error;
        private ExceptionDispatchInfo? _failure;
        private bool _selected, _disposed;
        internal Scope(GameplayClockProfileReads<TProfile> owner, Scope? previous) => (_owner, _previous) = (owner, previous);
        internal TProfile? Read(out string? error)
        {
            if (!_selected)
            {
                _selected = true;
                try { (_profile, _error) = _owner._refresh(); }
                catch (Exception failure) { _failure = ExceptionDispatchInfo.Capture(failure); }
            }
            _failure?.Throw();
            error = _error; return _profile;
        }
        public void Dispose()
        {
            if (_disposed) return;
            if (!ReferenceEquals(_owner._scope, this))
                throw new InvalidOperationException("Clock read scopes must end in consumer order.");
            _owner._scope = _previous; _disposed = true;
            _profile = null; _error = null; _failure = null;
        }
    }
}
