using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ProgressWatchInformation
    {
        internal uint Version, Size, Phase, Active;
        internal ulong Owner, OwnerEpoch, Receipt, Ordinal, Progress, Class, Expected, PlayerWatch, Player, AudioSource;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConfigureProgressWatchNative(IntPtr owner, IntPtr progressClass, IntPtr startClass);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BindHostProgressNative(IntPtr owner, IntPtr progress, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StageProgressNative(IntPtr owner, IntPtr progress, IntPtr playerWatch, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadProgressWatchNative(IntPtr owner, IntPtr receipt, ref ProgressWatchInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RetireProgressWatchNative(IntPtr owner, IntPtr receipt);
    private IntPtr _progressWatchClass;
    private readonly List<ProgressWatch> _progressWatches = new();

    internal void ConfigureProgressWatch(IntPtr progressClass, IntPtr startClass)
    {
        VerifyThread(); _image.Verify();
        if (progressClass == IntPtr.Zero || startClass == IntPtr.Zero || _progressWatchClass != IntPtr.Zero
            || Marshal.SizeOf<ProgressWatchInformation>() != 96 || Export<VersionNative>("RtPlaybackProgressWatchVersion")() != 1
            || Export<ConfigureProgressWatchNative>("RtPlaybackSourceConfigureProgressWatch")(_owner, progressClass, startClass) != 1)
            throw new UnsupportedSignatureException("The exact private Progress-watch contract could not bind.");
        _progressWatchClass = progressClass;
    }
    internal void BindHostProgressWatch(object hostOwner, NativeRuntime api, NativeRuntime.RootHandle progress)
    {
        VerifyThread(); _image.Verify();
        if (_progressWatchClass == IntPtr.Zero) return; // Explicit historical owned-fixture profile.
        RequireProgressRootOwner(hostOwner, api, progress);
        if (Export<BindHostProgressNative>("RtPlaybackSourceBindHostProgressWatch")(_owner, progress.Target, out var receipt) != 1 || receipt == IntPtr.Zero)
            throw new UnsupportedSignatureException("The retained four-root host could not bind its private Progress watch.");
        var watch = new ProgressWatch(this, hostOwner, progress, progress.Target, _progressWatchClass, IntPtr.Zero, receipt);
        _progressWatches.Add(watch); // Own the native receipt before any later read can fail.
        watch.Verify(hostOwner, active: true);
    }
    internal void StageProgressWatch(object sourceOwner, NativeRuntime api, NativeRuntime.RootHandle progress, PlayerWatch player)
    {
        VerifyThread(); _image.Verify();
        if (_progressWatchClass == IntPtr.Zero) return;
        RequireProgressRootOwner(sourceOwner, api, progress);
        player.VerifyPhase(sourceOwner, 1);
        if (Export<StageProgressNative>("RtPlaybackSourceStageProgressWatch")(_owner, progress.Target, player.NativeReceipt, out var receipt) != 1 || receipt == IntPtr.Zero)
            throw new UnsupportedSignatureException("The seven-root Progress watch did not stage with its exact pending player watch.");
        var watch = new ProgressWatch(this, sourceOwner, progress, progress.Target, _progressWatchClass, player.NativeReceipt, receipt);
        _progressWatches.Add(watch);
        watch.Verify(sourceOwner, active: false);
    }
    private void RequireProgressRootOwner(object owner, NativeRuntime api, NativeRuntime.RootHandle progress)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(progress);
        if (_progressWatches.Any(value => ReferenceEquals(value.RootOwner, owner)) || api.ObjectClass(progress.Target) != _progressWatchClass)
            throw new UnsupportedSignatureException("A Progress watch needs one unique retained owner and its exact independent Progress handle.");
    }
    internal void VerifyProgressWatch(object sourceOwner, bool active)
    {
        VerifyThread();
        if (_progressWatchClass == IntPtr.Zero) return;
        var watches = _progressWatches.Where(value => ReferenceEquals(value.RootOwner, sourceOwner)).ToArray();
        if (watches.Length != 1) throw new UnsupportedSignatureException("The exact source has no unique retained Progress watch.");
        watches[0].Verify(sourceOwner, active);
    }
    private void VerifyOperationProgressWatch(object sourceOwner)
    {
        VerifyThread();
        var watches = _progressWatches.Where(value => ReferenceEquals(value.RootOwner, sourceOwner)).ToArray();
        if (_progressWatchClass == IntPtr.Zero || watches.Length != 1)
            throw new UnsupportedSignatureException("The running fresh source has no unique retained Progress watch.");
        watches[0].VerifyOperation(sourceOwner);
    }
    internal bool TryRetireProgressWatch(object rootOwner)
    {
        VerifyThread(); var retired = true;
        foreach (var watch in _progressWatches.Where(value => ReferenceEquals(value.RootOwner, rootOwner)).ToArray())
        {
            if (watch.TryRetire(rootOwner)) _progressWatches.Remove(watch);
            else retired = false;
        }
        return retired;
    }
    private sealed class ProgressWatch
    {
        private readonly NativePlaybackStockSourceHub _owner;
        private readonly NativeRuntime.RootHandle _root;
        private readonly IntPtr _progress, _class, _playerWatch, _receipt;
        private readonly ProgressWatch _registered;
        private ulong _ordinal, _expected, _player, _source;
        private bool _bound;
        internal object RootOwner { get; }
        internal NativeRuntime.RootHandle ProgressRoot => _root;
        internal IntPtr ProgressIdentity => _progress;
        internal IntPtr ClassIdentity => _class;
        internal IntPtr NativeReceipt => _receipt;
        internal ProgressWatch(NativePlaybackStockSourceHub owner, object rootOwner, NativeRuntime.RootHandle root,
            IntPtr progress, IntPtr cls, IntPtr playerWatch, IntPtr receipt)
            => (_owner, RootOwner, _root, _progress, _class, _playerWatch, _receipt, _registered)
                = (owner, rootOwner, root, progress, cls, playerWatch, receipt, this);
        private ProgressWatchInformation Read(object rootOwner)
            => ReadCore(rootOwner, auditImage: true);
        private ProgressWatchInformation ReadCore(object rootOwner, bool auditImage)
        {
            _owner.VerifyThread();
            if (auditImage) _owner._image.Verify();
            if (!ReferenceEquals(_registered, this) || !ReferenceEquals(RootOwner, rootOwner) || !_owner._progressWatches.Contains(this))
                throw new UnsupportedSignatureException("Copied or foreign Progress-watch ownership is unavailable.");
            var value = new ProgressWatchInformation { Version = 1, Size = 96 };
            if (_owner.Export<ReadProgressWatchNative>("RtPlaybackSourceReadProgressWatch")(_owner._owner, _receipt, ref value) != 1
                || value.Version != 1 || value.Size != 96 || value.Owner != unchecked((ulong)_owner._owner.ToInt64()) || value.OwnerEpoch != _owner.Epoch
                || value.Receipt != unchecked((ulong)_receipt.ToInt64()) || value.Progress != unchecked((ulong)_progress.ToInt64())
                || value.Class != unchecked((ulong)_class.ToInt64()) || value.PlayerWatch != unchecked((ulong)_playerWatch.ToInt64())
                || value.Ordinal == 0 || _ordinal != 0 && value.Ordinal != _ordinal || value.Phase is < 1 or > 3
                || value.Active > 1 || (value.Active == 1) != (value.Phase == 2)
                || _bound && (value.Expected != _expected || value.Player != _player || value.AudioSource != _source)
                || _playerWatch == IntPtr.Zero && (value.Player != 0 || value.AudioSource != 0)
                || _playerWatch != IntPtr.Zero && (value.Player == 0 || value.AudioSource == 0))
                throw new UnsupportedSignatureException("The native Progress-watch receipt, graph or lifecycle changed.");
            (_ordinal, _expected, _player, _source, _bound) = (value.Ordinal, value.Expected, value.Player, value.AudioSource, true);
            return value;
        }
        internal void Verify(object rootOwner, bool active)
        {
            var value = Read(rootOwner);
            if (value.Phase == 3 || active && value.Phase != 2 || _root.Target != _progress)
                throw new UnsupportedSignatureException("The exact rooted Progress watch has not reached its required phase.");
        }
        internal void VerifyOperation(object rootOwner)
            => VerifyOperation(rootOwner, active: true);
        internal void VerifyOperation(object rootOwner, bool active)
        {
            var value = ReadCore(rootOwner, auditImage: false);
            if (value.Phase == 3 || active && (value.Phase != 2 || value.Active != 1) || _root.Target != _progress)
                throw new UnsupportedSignatureException("The running Progress watch lost its required phase or retained root.");
        }
        internal bool TryRetire(object rootOwner)
        {
            if (Read(rootOwner).Phase == 3) return true;
            return _owner.Export<RetireProgressWatchNative>("RtPlaybackSourceRetireProgressWatch")(_owner._owner, _receipt) == 1
                && Read(rootOwner).Phase == 3;
        }
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal void BindHostProgressWatch(object owner, NativeRuntime api, NativeRuntime.RootHandle progress)
        {
            VerifyCurrent();
            // Engine initialization captures backing identity at phase four,
            // before its first seven-root source or pending watch can exist.
            if (_targets.HasEnginePitch) _hub!.PrepareUnityBackingMetadata(api);
            _hub!.BindHostProgressWatch(owner, api, progress);
        }
        internal bool TryRetireProgressWatch(object owner) => _hub!.TryRetireProgressWatch(owner);
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal void BindHostProgressWatch(object owner, NativeRuntime api, NativeRuntime.RootHandle progress) => _owner.BindHostProgressWatch(owner, api, progress);
    internal bool TryRetireProgressWatch(object owner) => _owner.TryRetireProgressWatch(owner);
}
