using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Availability bookkeeping, never a source of native authority. The
/// fixed native profile still owns construction, attachment and every live
/// verification. A late transport can be retried without replacing a profile
/// that already remembers its original hook owner.</summary>
internal sealed class GameplayClockProfileCache<TProfile, THook>
    where TProfile : class where THook : class
{
    private readonly Func<THook?> _captureHook;
    private readonly Func<THook?, TProfile> _create;
    private readonly Action<TProfile, THook> _attach;
    private readonly Action<TProfile> _verify;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private TProfile? _profile;
    private THook? _hook;
    private string? _ownerFailure;
    internal TProfile? Current { get; private set; }

    internal GameplayClockProfileCache(Func<THook?> captureHook, Func<THook?, TProfile> create,
        Action<TProfile, THook> attach, Action<TProfile> verify)
        => (_captureHook, _create, _attach, _verify) = (captureHook, create, attach, verify);

    internal string? Refresh()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Clock profile availability belongs to its owner thread.");
        Current = null;
        if (_ownerFailure is not null) return _ownerFailure;
        try
        {
            var hook = _captureHook();
            if (_profile is null)
            {
                // Assign only after the complete fixed profile constructor
                // succeeds. NotReady must not become a permanent empty cache.
                _profile = _create(hook) ?? throw new InvalidOperationException("No clock profile was constructed.");
                _hook = hook;
            }
            else if (_hook is null && hook is not null)
            {
                // Existing unhooked metadata can adopt the exact broker lease
                // once. Attach verifies the original body and all consumers.
                _attach(_profile, hook);
                _hook = hook;
            }
            else if (_hook is not null && hook is null)
            {
                throw new UnsupportedSignatureException("The clock provider no longer supplies its registered GetTime hook.");
            }
            else if (!ReferenceEquals(_hook, hook))
            {
                // Never rebuild an unhooked profile to forget an old owner, or
                // replace that owner's sticky native fault with another lease.
                _ownerFailure = "The clock provider replaced its registered GetTime hook owner.";
                throw new UnsupportedSignatureException(_ownerFailure);
            }
            _verify(_profile);
            Current = _profile;
            return null;
        }
        catch (Exception error) { return error.Message; }
    }
}
