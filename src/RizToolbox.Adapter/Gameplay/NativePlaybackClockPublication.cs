using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Controlled prototype publication of the exact same immutable clock
/// snapshot consumed by NativePlaybackClock. This installs no hook, admits no
/// game build, and cannot turn stock fallback into synchronized rate recovery.</summary>
internal sealed partial class NativePlaybackClockPublication : INativePlaybackClockPublication
{
    private readonly object _gate = new();
    private readonly NativePlaybackSingleReturnBridge? _bridge;
    private readonly NativePlaybackGetTimeHookLease? _hook;
    private readonly IntPtr _permittedNullCaller;
    private readonly Action _verifySource;
    private readonly PlaybackSourceGroupLease? _operationGroup;
    private readonly Action? _verifyOperationSource;
    private readonly NativePlaybackClockPublication _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private NativePlaybackClockState _current;
    private NativePlaybackSingleReturnAuthority? _owned, _attempted;
    private bool _withdrawn;
    private NativePlaybackSingleReturnBridge.DormantBootstrap? _bootstrap;
    private bool _armed = true;
    internal bool ProductionAdmission => false;

    internal NativePlaybackClockPublication(NativePlaybackSingleReturnBridge bridge, NativePlaybackClockState initial,
        IntPtr permittedNullCaller, Action verifySource)
        : this(bridge ?? throw new ArgumentNullException(nameof(bridge)), null, initial, permittedNullCaller, verifySource) { }

    internal NativePlaybackClockPublication(NativePlaybackGetTimeHookLease hook, NativePlaybackClockState initial,
        IntPtr permittedNullCaller, Action verifySource)
        : this(null, hook ?? throw new ArgumentNullException(nameof(hook)), initial, permittedNullCaller, verifySource) { }

    private NativePlaybackClockPublication(NativePlaybackSingleReturnBridge? bridge, NativePlaybackGetTimeHookLease? hook,
        NativePlaybackClockState initial, IntPtr permittedNullCaller, Action verifySource, bool dormant = false,
        PlaybackSourceGroupLease? group = null, Action? verifyOperationSource = null)
    {
        ArgumentNullException.ThrowIfNull(initial); ArgumentNullException.ThrowIfNull(verifySource);
        _bridge = bridge; _hook = hook; _current = initial; _permittedNullCaller = permittedNullCaller; _verifySource = verifySource;
        _registered = this; _operationGroup = group; _verifyOperationSource = verifyOperationSource;
        VerifyOperationConfiguration();
        // This rejection is outside our cleanup region: an occupied bridge
        // belongs to its existing publisher, even if all policy data is equal.
        if (ReadCurrent().Authority is not null)
            throw new InvalidOperationException("This Single bridge already has a publication owner.");
        try
        {
            _verifySource(); _attempted = Authority(initial);
            if (dormant)
            {
                _armed = false;
                _bootstrap = _hook!.PrepareDormant(_attempted);
            }
            else PublishNative(0, _attempted);
            _verifySource();
            if (dormant) _bootstrap!.Read(1);
            else if (!NativeMatches(initial)) throw new InvalidOperationException("Initial native playback clock publication did not read back completely.");
            _owned = _attempted; _attempted = null;
        }
        catch { WithdrawOwned(); throw; }
    }

    internal static NativePlaybackClockPublication PrepareDormant(NativePlaybackGetTimeHookLease hook,
        NativePlaybackClockState initial, IntPtr permittedNullCaller, Action verifySource,
        PlaybackSourceGroupLease? group = null, Action? verifyOperationSource = null)
        => new(null, hook ?? throw new ArgumentNullException(nameof(hook)), initial, permittedNullCaller, verifySource, true, group, verifyOperationSource);

    internal void VerifyDormant(NativePlaybackClockState initial)
    {
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_withdrawn || _armed || (_bootstrap is null && _transfer is null && !_freshPrepared) || !ReferenceEquals(_current, initial)
                || _owned is null || !OwnsPublication(_owned))
                throw new InvalidOperationException("This exact initial publication is not the registered dormant owner.");
            _verifySource();
            if (_fresh is { } fresh) fresh.Read(1); else if (_transfer is { } transfer) transfer.Read(4); else _bootstrap!.Read(1);
            _verifySource();
        }
    }

    internal bool TryArm(NativePlaybackStockClockRouter.Session session)
    {
        lock (_gate)
        {
            session.VerifyFinalClock(this, _current);
            VerifyDormant(_current);
            // Keep the same dormant ticket available while the observed paused
            // values differ. No native activation has been attempted yet.
            session.VerifyActivationBaseline(afterCommit: false);
            try
            {
                // Native Arm can commit before its managed readback throws.
                // Keep the activation inside the exact-ticket cleanup region.
                if (!(_fresh is { } fresh ? fresh.TryArm() : _transfer is { } transfer ? transfer.TryArm() : _bootstrap!.TryArm())) return false;
                _armed = true;
                session.VerifyActivationBaseline(afterCommit: true);
                if (!NativeMatches(_current)) throw new InvalidOperationException("Armed initial native policy did not read back exactly.");
                return true;
            }
            catch { WithdrawOwned(); throw; }
        }
    }

    public void Publish(NativePlaybackClockState expected, NativePlaybackClockState next)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(next);
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_withdrawn || !_armed || !OwnsCurrentState(expected) || next.Binding != expected.Binding || next.Revision <= expected.Revision
                || next.Phase == NativePlaybackPhase.Withdrawn || next.WithdrawalReason is not null)
                throw new InvalidOperationException("The exact immutable playback policy baseline changed or was withdrawn.");
            try
            {
                VerifyRunningSource();
                if (!NativeMatchesOperation(expected)) throw new InvalidOperationException("The actual native playback policy differs from the expected complete snapshot.");
                _attempted = Authority(next);
                PublishOperationNative(expected.Revision, _attempted);
                VerifyRunningSource();
                if (!NativeMatchesOperation(next)) throw new InvalidOperationException("The actual native playback policy did not read back at the requested complete snapshot.");
                _current = next; _owned = _attempted; _attempted = null;
            }
            catch { WithdrawOwned(); throw; }
        }
    }
    public bool IsCurrent(NativePlaybackClockState state)
    {
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_withdrawn || !_armed || _current != state) return false;
            _verifySource(); return NativeMatches(state);
        }
    }
    // An independent consumer checks its current source. A read/write already
    // bracketed by the registered roots can use the policy-only check below.
    internal bool IsCurrentOperation(NativePlaybackClockState state)
    {
        lock (_gate)
        {
            VerifyOperationPublicationOwner();
            if (_withdrawn || !_armed || !OwnsCurrentState(state)) return false;
            VerifyRunningSource();
            return IsCurrentOperationPolicy(state);
        }
    }
    internal bool IsCurrentOperationPolicy(NativePlaybackClockState state)
    {
        lock (_gate)
        {
            VerifyOperationPublicationOwner();
            return !_withdrawn && _armed && OwnsCurrentState(state) && NativeMatchesOperation(state);
        }
    }
    public void Withdraw(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Playback withdrawal requires an actual reason.", nameof(reason));
        lock (_gate) WithdrawOwned();
    }
    private void WithdrawOwned()
    {
        VerifyFreshPublicationOwner();
        _withdrawn = true;
        // Source verification is intentionally absent. Only our exact private
        // receipt may authorize cleanup. attempted wins after CAS success but
        // failed readback; a foreign successor can never be inferred as ours
        // merely by reading its actual revision from health.
        var ticket = _attempted is { } attempted && OwnsPublication(attempted) ? attempted
            : _owned is { } owned && OwnsPublication(owned) ? owned : null;
        if (_fresh is { } fresh)
        {
            if (fresh.IsClosed) return;
            if (ticket is not null) fresh.Withdraw(ticket.Revision);
            else fresh.Cancel();
        }
        else if (ticket is not null) WithdrawNative(ticket.Revision); // false means a successor won the final native CAS
    }
    private bool NativeMatches(NativePlaybackClockState state)
    {
        _fresh?.Read(2);
        var health = _hook is { } hook ? hook.ReadHealth() : _bridge!.ReadHealth();
        if (!health.Known || !health.AuthorityActive || health.Revision != state.Revision) return false;
        var native = ReadCurrent();
        return native.Active && native.Authority == Authority(state);
    }
    private bool NativeMatchesOperation(NativePlaybackClockState state)
    {
        VerifyOperationPublicationOwner();
        if (_operationGroup is not { } group) return NativeMatches(state);
        _fresh?.Read(2);
        var native = group.ReadOperationClockPolicy(_hook!);
        return native.Active && native.Authority == Authority(state);
    }
    private void VerifyOperationConfiguration()
    {
        if ((_operationGroup is null) != (_verifyOperationSource is null)
            || _operationGroup is { } group && (_hook is null || !ReferenceEquals(group.GetTimeLease, _hook)))
            throw new InvalidOperationException("Running Clock checks require this exact group, hook and rooted source verifier.");
    }
    private bool OwnsCurrentState(NativePlaybackClockState state)
        => _operationGroup is null ? _current == state : ReferenceEquals(_current, state);
    private void VerifyOperationPublicationOwner()
    {
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_registered, this))
            throw new InvalidOperationException("A copied or foreign-thread publication owns no current Clock policy.");
        VerifyFreshPublicationOwner();
    }
    private void VerifyRunningSource()
    {
        VerifyOperationPublicationOwner();
        (_verifyOperationSource ?? _verifySource)();
        VerifyOperationPublicationOwner();
    }
    private (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadCurrent()
        => _hook is { } hook ? hook.ReadCurrent() : _bridge!.ReadCurrent();
    private bool OwnsPublication(NativePlaybackSingleReturnAuthority authority)
        => _hook is { } hook ? hook.OwnsPublication(authority) : _bridge!.OwnsPublication(authority);
    private void PublishNative(long expected, NativePlaybackSingleReturnAuthority authority)
    { if (_fresh is { } fresh) fresh.Publish(expected, authority); else if (_hook is { } hook) hook.Publish(expected, authority); else _bridge!.Publish(expected, authority); }
    private void PublishOperationNative(long expected, NativePlaybackSingleReturnAuthority authority)
    {
        if (_fresh is { } fresh) fresh.Publish(expected, authority);
        else if (_operationGroup is { } group) group.PublishOperationClockPolicy(_hook!, expected, authority);
        else PublishNative(expected, authority);
    }
    private void WithdrawNative(long expected)
    { if (_hook is { } hook) hook.Withdraw(expected); else _bridge!.Withdraw(expected); }
    private NativePlaybackSingleReturnAuthority Authority(NativePlaybackClockState state)
        => new(Identity(state.Binding), state.Revision, Phase(state), state.DspAnchorSeconds, state.MediaAnchorSeconds,
            state.Rate, state.ClipLengthSeconds, _permittedNullCaller);

    internal static NativePlaybackSingleReturnIdentity Identity(NativePlaybackBinding binding)
    {
        if (!binding.Valid) throw new ArgumentException("The native clock publisher requires the entire exact run/source graph.", nameof(binding));
        return new(binding.RunId, binding.SceneGeneration, binding.ControllerGeneration, binding.SourceGeneration,
            new(binding.Controller), new(binding.Information), new(binding.Progress), new(binding.Player), new(binding.AudioSource),
            new(binding.Clip), new(binding.GetTimeMethod));
    }
    internal static NativePlaybackSingleReturnPhase Phase(NativePlaybackClockState state) => state.Phase switch
    {
        NativePlaybackPhase.Paused => NativePlaybackSingleReturnPhase.Paused,
        NativePlaybackPhase.Playing => NativePlaybackSingleReturnPhase.Playing,
        NativePlaybackPhase.Scheduled => NativePlaybackSingleReturnPhase.Scheduled,
        _ => throw new InvalidOperationException("A withdrawn playback clock has no native return authority.")
    };
}
