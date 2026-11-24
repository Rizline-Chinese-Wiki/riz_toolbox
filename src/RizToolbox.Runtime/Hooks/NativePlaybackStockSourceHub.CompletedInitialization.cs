using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CaptureCompletedInitializerNative(
        IntPtr owner, IntPtr progress, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VerifyCompletedInitializerNative(
        IntPtr owner, IntPtr progress, IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadCompletedInitializerNative(
        IntPtr owner, IntPtr progress, IntPtr receipt, uint phase, ref InitializationInformation information);
    private readonly Dictionary<IntPtr, CompletedInitializationReceipt> _completedInitializers = new();

    internal CompletedInitializationReceipt CaptureCompletedInitializer(NativeRuntime.RootHandle progress)
        => TryCaptureCompletedInitializer(progress)
            ?? throw new UnsupportedSignatureException("The rooted Progress has no completed actual initializer yet.");

    internal CompletedInitializationReceipt? TryCaptureCompletedInitializer(NativeRuntime.RootHandle progress)
    {
        VerifyThread(); _image.Verify(); ArgumentNullException.ThrowIfNull(progress);
        if (Export<VersionNative>("RtPlaybackCompletedInitializerVersion")() != 1)
            throw new UnsupportedSignatureException("The native completed-initializer capture protocol is unavailable.");
        var target = progress.Target;
        var result = Export<CaptureCompletedInitializerNative>("RtPlaybackSourceCaptureCompletedInitializer")(_owner, target, out var pointer);
        if (result == 8 && pointer == IntPtr.Zero && progress.Target == target) return null;
        if (result != 0 || pointer == IntPtr.Zero || progress.Target != target)
            throw new UnsupportedSignatureException($"The actual completed initializer did not bind this rooted Progress: {result}.");
        if (_completedInitializers.TryGetValue(pointer, out var existing))
        {
            existing.VerifyRoot(progress); return existing;
        }
        var receipt = new CompletedInitializationReceipt(this, progress, target, pointer);
        _completedInitializers.Add(pointer, receipt); receipt.VerifyCurrent(); return receipt;
    }

    // Selects the existing native private record after it completed before the
    // managed run observer could attach. The host still supplies its real run
    // token and original rooted Progress; this receipt does not invent a run.
    internal sealed class CompletedInitializationReceipt
    {
        private readonly NativePlaybackStockSourceHub _owner;
        private readonly NativeRuntime.RootHandle _root;
        private readonly IntPtr _progress, _native;
        private NativePlaybackSingleReturnBridge.LogicalHandoff? _handoff;
        internal CompletedInitializationReceipt(NativePlaybackStockSourceHub owner, NativeRuntime.RootHandle root,
            IntPtr progress, IntPtr native) => (_owner, _root, _progress, _native) = (owner, root, progress, native);
        internal void VerifyCurrent()
        {
            Registered();
            if (_handoff is not null || _owner.Export<VerifyCompletedInitializerNative>(
                    "RtPlaybackSourceVerifyCompletedInitializer")(_owner._owner, _progress, _native) != 0)
                throw new UnsupportedSignatureException("The rooted completed initializer is copied, superseded, consumed or no longer current.");
        }
        private void Registered()
        {
            _owner.VerifyThread(); _owner._image.Verify();
            if (!_owner._completedInitializers.TryGetValue(_native, out var actual) || !ReferenceEquals(actual, this) || _root.Target != _progress)
                throw new UnsupportedSignatureException("The completed initializer lost its original registered Progress root.");
        }
        private InitializationInformation ReadPhase(uint phase)
        {
            Registered(); var info = new InitializationInformation { Version = 2, Size = 160 };
            var result = _owner.Export<ReadCompletedInitializerNative>("RtPlaybackSourceReadCompletedInitializer")(
                _owner._owner, _progress, _native, phase, ref info);
            if (result != 0 || info.Version != 2 || info.Size != 160 || info.Phase != phase
                || info.Owner != unchecked((ulong)_owner._owner.ToInt64()) || info.OwnerEpoch != _owner.Epoch
                || info.Progress != unchecked((ulong)_progress.ToInt64()) || info.Receipt != unchecked((ulong)_native.ToInt64())
                || info.Player == 0 || info.Source == 0 || info.Clip == 0)
                throw new UnsupportedSignatureException($"The actual rooted initializer record no longer matches phase {phase}: {result}.");
            return info;
        }
        internal InitializationInformation Read() { VerifyCurrent(); return ReadPhase(4); }
        internal void BindHandoff(NativePlaybackSingleReturnBridge.LogicalHandoff handoff)
        {
            Registered();
            if (_handoff is not null) throw new UnsupportedSignatureException("A completed initializer cannot transfer twice.");
            handoff.VerifyCompletedInitializerOwner(this); _handoff = handoff;
        }
        private void VerifyHandoff(NativePlaybackSingleReturnBridge.LogicalHandoff handoff)
        {
            Registered();
            if (!ReferenceEquals(_handoff, handoff)) throw new UnsupportedSignatureException("This initializer belongs to another actual logical transfer.");
            handoff.VerifyCompletedInitializerOwner(this);
        }
        internal void VerifyTransferred(NativePlaybackSingleReturnBridge.LogicalHandoff handoff)
        {
            VerifyHandoff(handoff); handoff.Read(2); var info = ReadPhase(6);
            handoff.VerifyInitializationCapture(_owner, checked((long)info.Player), checked((long)info.Source), checked((long)info.Clip));
        }
        internal void VerifyOpened(NativePlaybackSingleReturnBridge.LogicalHandoff handoff)
        {
            VerifyHandoff(handoff);
            if (handoff.Read().Phase is not (3 or 4 or 5)) throw new UnsupportedSignatureException("The actual initializer source has not opened.");
            _ = ReadPhase(7);
        }
        internal void VerifyRoot(NativeRuntime.RootHandle root)
        {
            if (!ReferenceEquals(_root, root)) throw new UnsupportedSignatureException("The initializer belongs to its original rooted Progress handle.");
            VerifyCurrent();
        }
        internal IntPtr NativeIdentityFor(NativePlaybackStockSourceHub owner)
        {
            if (!ReferenceEquals(_owner, owner)) throw new UnsupportedSignatureException("The completed initializer belongs to another physical source group.");
            VerifyCurrent(); return _native;
        }
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal NativePlaybackStockSourceHub.CompletedInitializationReceipt CaptureCompletedInitializer(
            NativeRuntime api, NativeRuntime.RootHandle progress)
            => TryCaptureCompletedInitializer(api, progress)
                ?? throw new UnsupportedSignatureException("The rooted Progress has no completed actual initializer yet.");
        internal NativePlaybackStockSourceHub.CompletedInitializationReceipt? TryCaptureCompletedInitializer(
            NativeRuntime api, NativeRuntime.RootHandle progress)
        {
            ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(progress); VerifyCurrent();
            if (api.ObjectClass(progress.Target) != _targets.ProgressPlay.DeclaringClass)
                throw new UnsupportedSignatureException("The actual retained Progress does not match this group's fixed metadata class.");
            return _hub!.TryCaptureCompletedInitializer(progress);
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal NativePlaybackStockSourceHub.CompletedInitializationReceipt CaptureCompletedInitializer(
        NativeRuntime api, NativeRuntime.RootHandle progress) => _owner.CaptureCompletedInitializer(api, progress);
    internal NativePlaybackStockSourceHub.CompletedInitializationReceipt? TryCaptureCompletedInitializer(
        NativeRuntime api, NativeRuntime.RootHandle progress) => _owner.TryCaptureCompletedInitializer(api, progress);
}
