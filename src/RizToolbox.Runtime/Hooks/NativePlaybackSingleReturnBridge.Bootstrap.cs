using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackSingleReturnBridge
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BootstrapInformation
    {
        internal uint Version, Size, Slot, Phase;
        internal ulong Epoch, InitialGeneration, CurrentGeneration, InFlight;
        internal IntPtr Receipt;
        internal uint Unknown, Reserved;
        internal NativePlaybackSingleReturnNativePolicy Policy;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint PrepareBootstrapNative(uint slot, ulong expected, ref NativePlaybackSingleReturnNativePolicy policy, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BootstrapActionNative(IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadBootstrapNative(IntPtr receipt, ref BootstrapInformation information);
    private DormantBootstrap? _bootstrap;

    /// <summary>Initial publication is retained and readable, but the actual native
    /// entry captures no policy or clock-reader responsibility until Arm commits.
    /// The four exports are resolved only for this new protocol, not old helpers.</summary>
    internal DormantBootstrap PrepareDormant(NativePlaybackSingleReturnAuthority initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        lock (_gate)
        {
            if (_fault is not null || _freshSourceCapture is not null || _published is not null || _bootstrap is not null || !initial.IsValid)
                throw new InvalidOperationException("A dormant bootstrap requires a fresh known physical slot and complete initial policy.");
            if (Marshal.SizeOf<BootstrapInformation>() != 192)
                throw new UnsupportedSignatureException("The native dormant bootstrap layout differs.");
            var prepare = Export<PrepareBootstrapNative>("RizSingleReturnPrototype_PrepareDormantMusicClock");
            _ = Export<BootstrapActionNative>("RizSingleReturnPrototype_ArmDormantMusicClock");
            _ = Export<BootstrapActionNative>("RizSingleReturnPrototype_CancelDormantMusicClock");
            _ = Export<ReadBootstrapNative>("RizSingleReturnPrototype_ReadDormantMusicClock");
            var policy = initial.ToNative(_readerPointer, _owner);
            var result = prepare(_slot, 0, ref policy, out var receipt);
            if (result != 0 || receipt == IntPtr.Zero)
                throw new InvalidOperationException($"Native dormant preparation rejected without issuing authority: {result}.");
            // Store the exact object before readback so failed readback still has
            // its own cleanup receipt; public current-generation reads grant none.
            _published = initial;
            _bootstrap = DormantBootstrap.Issue(this, receipt, initial);
            try { _bootstrap.Read(1); return _bootstrap; }
            catch { try { _bootstrap.Cancel(); } catch { } throw; }
        }
    }

    internal sealed class DormantBootstrap
    {
        private readonly NativePlaybackSingleReturnBridge _owner;
        private readonly IntPtr _receipt;
        private readonly NativePlaybackSingleReturnAuthority _initial;
        private readonly BootstrapActionNative _arm, _cancel;
        private readonly ReadBootstrapNative _read;
        private ulong _epoch;
        private DormantBootstrap(NativePlaybackSingleReturnBridge owner, IntPtr receipt,
            NativePlaybackSingleReturnAuthority initial, BootstrapActionNative arm,
            BootstrapActionNative cancel, ReadBootstrapNative read)
            => (_owner, _receipt, _initial, _arm, _cancel, _read) = (owner, receipt, initial, arm, cancel, read);

        internal static DormantBootstrap Issue(NativePlaybackSingleReturnBridge owner, IntPtr receipt, NativePlaybackSingleReturnAuthority initial)
            => new(owner, receipt, initial,
                owner.Export<BootstrapActionNative>("RizSingleReturnPrototype_ArmDormantMusicClock"),
                owner.Export<BootstrapActionNative>("RizSingleReturnPrototype_CancelDormantMusicClock"),
                owner.Export<ReadBootstrapNative>("RizSingleReturnPrototype_ReadDormantMusicClock"));

        private void Registered()
        {
            if (!ReferenceEquals(_owner._bootstrap, this))
                throw new UnsupportedSignatureException("A copied dormant receipt is not the actual bridge's registered object.");
        }
        internal BootstrapInformation Read(uint phase)
        {
            lock (_owner._gate)
            {
                Registered();
                var info = new BootstrapInformation { Version = 1, Size = 192 };
                if (_read(_receipt, ref info) != 0 || info.Version != 1 || info.Size != 192 || info.Slot != _owner._slot
                    || info.Phase != phase || info.Epoch == 0 || (_epoch != 0 && info.Epoch != _epoch)
                    || info.InitialGeneration != (ulong)_initial.Revision || info.Receipt != _receipt
                    || info.Unknown != 0 || info.Reserved != 0 || _owner._fault is not null
                    || _owner._published is not { } current || info.CurrentGeneration != (ulong)current.Revision
                    || !current.MatchesNative(info.Policy, _owner._readerPointer, _owner._owner))
                    throw new UnsupportedSignatureException("The actual native bootstrap receipt or complete immutable policy changed.");
                _epoch = info.Epoch;
                var actual = _owner.ReadCurrentCore();
                if (actual.Active != (phase == 2) || !ReferenceEquals(actual.Authority, _owner._published))
                    throw new UnsupportedSignatureException("Dormant/armed state differs from actual native policy activation.");
                return info;
            }
        }
        internal bool TryArm()
        {
            lock (_owner._gate)
            {
                Registered(); Read(1);
                var result = _arm(_receipt);
                if (result == 8) { Read(1); return false; } // Actual frame still owes original/finally.
                if (result != 0) throw new UnsupportedSignatureException($"Native conditional Arm rejected: {result}.");
                Read(2); return true;
            }
        }
        internal void Cancel()
        {
            lock (_owner._gate)
            {
                Registered();
                // Never reinterpret an armed or later revision as our dormant
                // cleanup. Its exact publisher must use conditional Withdraw.
                if (!ReferenceEquals(_owner._published, _initial))
                    throw new UnsupportedSignatureException("A successor publication cannot be cancelled by dormant cleanup.");
                var result = _cancel(_receipt);
                if (result != 0) throw new UnsupportedSignatureException($"Native dormant Cancel rejected: {result}.");
            }
        }
    }
}
