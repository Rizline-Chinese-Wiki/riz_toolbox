using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct PlayerWatchInformation
    {
        internal uint Version, Size, Phase, Active;
        internal ulong Owner, OwnerEpoch, Receipt, Ordinal, Player, Class, Expected, Cut, Writer, Source, Scheduling, AudioSource, AudioClass;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConfigureWatchNative(IntPtr owner, IntPtr cls, IntPtr audioClass);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StageWatchNative(IntPtr owner, IntPtr player, IntPtr audioSource, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadWatchNative(IntPtr owner, IntPtr receipt, ref PlayerWatchInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RetireWatchNative(IntPtr owner, IntPtr receipt);
    private IntPtr _playerWatchClass, _audioWatchClass;
    private readonly List<PlayerWatch> _playerWatches = new();
    // A constructor-stage failure can leave a pending native receipt whose
    // retirement cannot yet be proved. Retain its exact owner and owner-thread
    // drain continuation even when construction never returned it to the Router.
    private readonly Dictionary<object, Func<bool>> _unresolvedPlayerRootOwners = new(ReferenceEqualityComparer.Instance);
    private bool _pumpingPlayerRootOwners;
    internal int UnresolvedPlayerRootOwnerCount => _unresolvedPlayerRootOwners.Count;
    internal void RetainUnresolvedPlayerRootOwner(object sourceOwner, Func<bool> tryRelease)
    {
        VerifyThread();
        ArgumentNullException.ThrowIfNull(sourceOwner); ArgumentNullException.ThrowIfNull(tryRelease);
        _unresolvedPlayerRootOwners.TryAdd(sourceOwner, tryRelease);
    }
    internal void PumpUnresolvedPlayerRootOwners()
    {
        // Transport journal pumping is also used from worker cleanup paths.
        // Retained NativeRuntime roots must wait for their actual owner thread.
        if (_thread != Environment.CurrentManagedThreadId) return;
        VerifyThread();
        // A source drain pumps this same group's native journals. Do not enter
        // its continuation recursively, or pump any other group's retained roots.
        if (_pumpingPlayerRootOwners) return;
        _pumpingPlayerRootOwners = true;
        try
        {
            foreach (var retained in _unresolvedPlayerRootOwners.ToArray())
                if (retained.Value()) _unresolvedPlayerRootOwners.Remove(retained.Key);
        }
        finally { _pumpingPlayerRootOwners = false; }
    }
    internal void ConfigurePlayerWatch(IntPtr cls, IntPtr audioClass)
    {
        VerifyThread(); _image.Verify();
        if (cls == IntPtr.Zero || audioClass == IntPtr.Zero || _playerWatchClass != IntPtr.Zero || _audioWatchClass != IntPtr.Zero
            || Marshal.SizeOf<PlayerWatchInformation>() != 120
            || Export<VersionNative>("RtPlaybackPlayerWatchVersion")() != 2
            || Export<ConfigureWatchNative>("RtPlaybackSourceConfigurePlayerWatch")(_owner, cls, audioClass) != 1)
            throw new UnsupportedSignatureException("The exact shared constructor player-watch contract could not bind.");
        (_playerWatchClass, _audioWatchClass) = (cls, audioClass);
    }
    internal PlayerWatch? StagePlayerWatch(object sourceOwner, NativeRuntime api, NativeRuntime.RootHandle player, NativeRuntime.RootHandle audioSource)
    {
        VerifyThread(); _image.Verify();
        if (_playerWatchClass == IntPtr.Zero) return null; // explicit historical owned-fixture scope
        if (_playerWatches.Any(value => ReferenceEquals(value.SourceOwner, sourceOwner)) || api.ObjectClass(player.Target) != _playerWatchClass || api.ObjectClass(audioSource.Target) != _audioWatchClass
            || ReferenceEquals(player, audioSource))
            throw new UnsupportedSignatureException("A player watch needs one exact source owner and its still-rooted player.");
        if (Export<StageWatchNative>("RtPlaybackSourceStagePlayerWatch")(_owner, player.Target, audioSource.Target, out var receipt) != 1 || receipt == IntPtr.Zero)
            throw new UnsupportedSignatureException("The private rooted player watch was not staged at its source-open boundary.");
        var watch = new PlayerWatch(this, sourceOwner, player, audioSource, player.Target, _playerWatchClass, audioSource.Target, _audioWatchClass, receipt);
        _playerWatches.Add(watch); // retain before any later read can fail
        watch.VerifyPhase(sourceOwner, 1); return watch;
    }
    internal void VerifyPlayerWatch(object sourceOwner)
    {
        VerifyThread();
        VerifyUnityBackingWatch(sourceOwner, active: false);
        VerifyProgressWatch(sourceOwner, active: false);
        var watches = _playerWatches.Where(value => ReferenceEquals(value.SourceOwner, sourceOwner)).ToArray();
        if (_playerWatchClass != IntPtr.Zero && watches.Length != 1)
            throw new UnsupportedSignatureException("The exact rooted source has no unique retained player watch.");
        foreach (var watch in watches) watch.Verify(sourceOwner);
    }
    internal void VerifyActivePlayerWatch(object sourceOwner)
    {
        VerifyThread();
        VerifyUnityBackingWatch(sourceOwner, active: true);
        VerifyProgressWatch(sourceOwner, active: true);
        var watches = _playerWatches.Where(value => ReferenceEquals(value.SourceOwner, sourceOwner)).ToArray();
        if (_playerWatchClass != IntPtr.Zero && watches.Length != 1)
            throw new UnsupportedSignatureException("Source-open did not retain one exact player watch.");
        foreach (var watch in watches) watch.VerifyPhase(sourceOwner, 2);
    }
    internal void VerifyOperationActivePlayerWatch(object sourceOwner)
    {
        VerifyThread();
        // Historical owned fixtures deliberately omit these native watches.
        // Formal stock source scopes require their configured metadata first.
        if (_unityBackingObjectClass != IntPtr.Zero) VerifyOperationUnityBackingWatch(sourceOwner);
        if (_progressWatchClass != IntPtr.Zero) VerifyOperationProgressWatch(sourceOwner);
        var watches = _playerWatches.Where(value => ReferenceEquals(value.SourceOwner, sourceOwner)).ToArray();
        if (_playerWatchClass != IntPtr.Zero && watches.Length != 1)
            throw new UnsupportedSignatureException("A running fresh source requires its unique active native player watch.");
        foreach (var watch in watches) watch.VerifyOperationPhase(sourceOwner);
    }
    internal bool TryRetirePlayerWatch(object sourceOwner)
    {
        if (!TryRetireUnityBackingWatch(sourceOwner)) return false;
        if (!TryRetireProgressWatch(sourceOwner)) return false;
        VerifyThread(); var retired = true;
        foreach (var watch in _playerWatches.Where(value => ReferenceEquals(value.SourceOwner, sourceOwner)).ToArray())
        {
            if (watch.TryRetire(sourceOwner)) _playerWatches.Remove(watch);
            else retired = false;
        }
        return retired;
    }
    internal sealed class PlayerWatch
    {
        private readonly NativePlaybackStockSourceHub _owner;
        private readonly NativeRuntime.RootHandle _root, _audioRoot;
        private readonly IntPtr _player, _class, _audioSource, _audioClass, _receipt;
        private readonly PlayerWatch _registered;
        private ulong _ordinal;
        private ulong _expected, _cut, _writer, _source, _scheduling;
        private bool _bound;
        internal object SourceOwner { get; }
        internal IntPtr NativeReceipt => _receipt;
        // Borrow both independently owned handles from the same seven-root graph.
        // The wire Source remains a fence; AudioSource is a separate identity.
        internal PlayerWatch(NativePlaybackStockSourceHub owner, object sourceOwner, NativeRuntime.RootHandle root, NativeRuntime.RootHandle audioRoot,
            IntPtr player, IntPtr cls, IntPtr audioSource, IntPtr audioClass, IntPtr receipt)
            => (_owner, SourceOwner, _root, _audioRoot, _player, _class, _audioSource, _audioClass, _receipt, _registered)
                = (owner, sourceOwner, root, audioRoot, player, cls, audioSource, audioClass, receipt, this);
        private PlayerWatchInformation Read(object sourceOwner)
            => ReadCore(sourceOwner, auditImage: true);
        private PlayerWatchInformation ReadCore(object sourceOwner, bool auditImage)
        {
            _owner.VerifyThread();
            if (auditImage) _owner._image.Verify();
            if (!ReferenceEquals(_registered, this) || !_owner._playerWatches.Contains(this) || !ReferenceEquals(SourceOwner, sourceOwner))
                throw new UnsupportedSignatureException("Copied or foreign player-watch ownership is unavailable.");
            var value = new PlayerWatchInformation { Version = 2, Size = 120 };
            if (_owner.Export<ReadWatchNative>("RtPlaybackSourceReadPlayerWatch")(_owner._owner, _receipt, ref value) != 1
                || value.Version != 2 || value.Size != 120 || value.Receipt != unchecked((ulong)_receipt.ToInt64())
                || value.Owner != unchecked((ulong)_owner._owner.ToInt64()) || value.OwnerEpoch != _owner.Epoch
                || value.Player != unchecked((ulong)_player.ToInt64()) || value.Class != unchecked((ulong)_class.ToInt64())
                || value.AudioSource != unchecked((ulong)_audioSource.ToInt64()) || value.AudioClass != unchecked((ulong)_audioClass.ToInt64())
                || value.Ordinal == 0 || _ordinal != 0 && value.Ordinal != _ordinal || value.Phase is < 1 or > 3
                || value.Active > 1 || (value.Active == 1) != (value.Phase == 2))
                throw new UnsupportedSignatureException("The native player-watch identity or lifecycle changed.");
            if (_bound && (value.Expected != _expected || value.Cut != _cut || value.Writer != _writer
                || value.Source != _source || value.Scheduling != _scheduling))
                throw new UnsupportedSignatureException("The staged player-watch cut or source-fence identity changed.");
            (_expected, _cut, _writer, _source, _scheduling, _bound)
                = (value.Expected, value.Cut, value.Writer, value.Source, value.Scheduling, true);
            _ordinal = value.Ordinal; return value;
        }
        internal void Verify(object sourceOwner)
        {
            var value = Read(sourceOwner);
            if (value.Phase == 3 || _root.Target != _player || _audioRoot.Target != _audioSource)
                throw new UnsupportedSignatureException("A retired player watch cannot validate its previous graph.");
        }
        internal void VerifyPhase(object sourceOwner, uint phase)
        {
            var value = Read(sourceOwner);
            if (value.Phase != phase || value.Active != (phase == 2 ? 1u : 0u) || _root.Target != _player || _audioRoot.Target != _audioSource)
                throw new UnsupportedSignatureException("The player watch did not reach its required source-open phase.");
        }
        internal void VerifyOperationPhase(object sourceOwner)
        {
            // Every call reads the real native receipt and its fences. The
            // retained ordinal is identity, never a successful audit cache.
            var value = ReadCore(sourceOwner, auditImage: false);
            if (value.Phase != 2 || value.Active != 1 || _root.Target != _player || _audioRoot.Target != _audioSource)
                throw new UnsupportedSignatureException("The running player watch is no longer active on its retained roots.");
        }
        internal bool TryRetire(object sourceOwner)
        {
            if (Read(sourceOwner).Phase == 3) return true;
            if (_owner.Export<RetireWatchNative>("RtPlaybackSourceRetirePlayerWatch")(_owner._owner, _receipt) != 1) return false;
            return Read(sourceOwner).Phase == 3;
        }
    }
}

internal sealed partial class NativePlaybackSingleReturnBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ActivateInitialWatchNative(uint slot, IntPtr owner, IntPtr receipt);
    internal void ActivateInitialPlayerWatch(NativePlaybackStockSourceHub source, NativePlaybackStockSourceHub.PlayerWatch watch)
    {
        lock (_gate)
        {
            var result = Export<ActivateInitialWatchNative>("RizSingleReturnPrototype_ActivateInitialPlayerWatch")(_slot, source.NativeOwner, watch.NativeReceipt);
            if (result is not (0 or 2))
                throw new UnsupportedSignatureException($"Initial player-watch native source-open rejected: {result}.");
            // Result2 leaves the watch pending under a registered transfer.
            // Actual source-open action4/10 performs its only activation cut.
            watch.VerifyPhase(watch.SourceOwner, result == 0 ? 2u : 1u);
        }
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal void ActivateInitialPlayerWatch(NativePlaybackStockSourceHub hub, NativePlaybackStockSourceHub.PlayerWatch watch)
        { lock (_gate) _bridge!.ActivateInitialPlayerWatch(hub, watch); }
    }
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal bool StagePlayerWatch(object sourceOwner, NativeRuntime api, NativeRuntime.RootHandle player, NativeRuntime.RootHandle audioSource, NativeRuntime.RootHandle progress, NativeRuntime.RootHandle clip)
        {
            VerifyCurrent();
            if (_targets.HasUnityBacking) _hub!.PrepareUnityBackingWatch(api, audioSource, clip);
            var watch = _hub!.StagePlayerWatch(sourceOwner, api, player, audioSource);
            if (watch is null) return false;
            _hub.StageProgressWatch(sourceOwner, api, progress, watch);
            if (_targets.HasUnityBacking) _hub.StageUnityBackingWatch(sourceOwner, api, audioSource, clip, watch);
            _getTime!.ActivateInitialPlayerWatch(_hub, watch);
            return true;
        }
        internal void VerifyPlayerWatch(object sourceOwner) => _hub!.VerifyPlayerWatch(sourceOwner);
        internal void VerifyActivePlayerWatch(object sourceOwner) => _hub!.VerifyActivePlayerWatch(sourceOwner);
        internal void VerifyOperationActivePlayerWatch(object sourceOwner) => _hub!.VerifyOperationActivePlayerWatch(sourceOwner);
        internal bool TryRetirePlayerWatch(object sourceOwner) => _hub!.TryRetirePlayerWatch(sourceOwner);
        internal void RetainUnresolvedPlayerRootOwner(object sourceOwner, Func<bool> tryRelease) => _hub!.RetainUnresolvedPlayerRootOwner(sourceOwner, tryRelease);
        internal int UnresolvedPlayerRootOwnerCount => _hub!.UnresolvedPlayerRootOwnerCount;
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal bool StagePlayerWatch(object sourceOwner, NativeRuntime api, NativeRuntime.RootHandle player, NativeRuntime.RootHandle audioSource, NativeRuntime.RootHandle progress, NativeRuntime.RootHandle clip) => _owner.StagePlayerWatch(sourceOwner, api, player, audioSource, progress, clip);
    internal void VerifyPlayerWatch(object sourceOwner) => _owner.VerifyPlayerWatch(sourceOwner);
    internal void VerifyActivePlayerWatch(object sourceOwner) => _owner.VerifyActivePlayerWatch(sourceOwner);
    internal void VerifyOperationActivePlayerWatch(object sourceOwner)
    {
        VerifyOperationLease();
        _owner.VerifyOperationActivePlayerWatch(sourceOwner);
    }
    internal bool TryRetirePlayerWatch(object sourceOwner) => _owner.TryRetirePlayerWatch(sourceOwner);
    internal void RetainUnresolvedPlayerRootOwner(object sourceOwner, Func<bool> tryRelease) => _owner.RetainUnresolvedPlayerRootOwner(sourceOwner, tryRelease);
    internal int UnresolvedPlayerRootOwnerCount => _owner.UnresolvedPlayerRootOwnerCount;
}
