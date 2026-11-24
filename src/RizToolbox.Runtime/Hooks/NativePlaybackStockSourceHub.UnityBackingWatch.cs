using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct UnityBackingInformation
    {
        internal uint Version, Size, Phase, Current;
        internal ulong Owner, OwnerEpoch, Receipt, Ordinal, PlayerWatchReceipt,
            Source, SourceClass, SourceNative, Clip, ClipClass, ClipNative, SourceFence;
        internal int InstanceIdOffset, SourceId, ClipId;
        internal uint Reason;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConfigureUnityBackingNative(IntPtr owner, IntPtr objectClass, IntPtr clipClass, int offset);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StageUnityBackingNative(IntPtr owner, IntPtr playerWatch, IntPtr clip, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadUnityBackingNative(IntPtr owner, IntPtr receipt, ref UnityBackingInformation info);

    private IntPtr _unityBackingObjectClass, _unityBackingClipClass;
    private int _unityBackingInstanceIdOffset;
    private readonly List<UnityBackingWatch> _unityBackingWatches = new();

    // Only the official layout is installed here. Native still needs the actual
    // completed Start/Progress graph and two native samples before it can retain
    // an initializer identity; no player/backing watch is staged by this method.
    internal void PrepareUnityBackingMetadata(NativeRuntime api)
    {
        VerifyThread(); _image.Verify();
        var layout = api.ReadUnityObjectBackingLayout();
        if (layout.AudioSourceClass != _audioWatchClass || _playerWatchClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("The Unity backing layout differs from the physical group's exact AudioSource class.");
        if (_unityBackingObjectClass != IntPtr.Zero)
        {
            if (_unityBackingObjectClass != layout.UnityObjectClass || _unityBackingClipClass != layout.AudioClipClass
                || _unityBackingInstanceIdOffset != layout.NativeInstanceIdOffset)
                throw new UnsupportedSignatureException("The installed Unity backing layout cannot be rebound by metadata bootstrap.");
            return;
        }
        if (Marshal.SizeOf<UnityBackingInformation>() != 128 || Export<VersionNative>("RtPlaybackUnityBackingWatchVersion")() != 1
            || Export<ConfigureUnityBackingNative>("RtPlaybackSourceConfigureUnityBackingWatch")(_owner,
                layout.UnityObjectClass, layout.AudioClipClass, layout.NativeInstanceIdOffset) != 1)
            throw new UnsupportedSignatureException("The exact Unity backing metadata could not configure before the first Start.");
        (_unityBackingObjectClass, _unityBackingClipClass, _unityBackingInstanceIdOffset)
            = (layout.UnityObjectClass, layout.AudioClipClass, layout.NativeInstanceIdOffset);
    }

    private (NativeUnityObjectBackingObservation Source, NativeUnityObjectBackingObservation Clip) ObserveUnityBacking(
        NativeRuntime api, NativeRuntime.RootHandle source, NativeRuntime.RootHandle clip)
    {
        if (ReferenceEquals(source, clip))
            throw new UnsupportedSignatureException("AudioSource and Clip must borrow their distinct existing roots.");
        var sourceClass = api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
        var clipClass = api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
        var sourceObservation = api.ReadUnityObjectBackingObservation(source, source.Target.ToInt64(), sourceClass);
        var clipObservation = api.ReadUnityObjectBackingObservation(clip, clip.Target.ToInt64(), clipClass);
        if (sourceObservation.UnityObjectClass != clipObservation.UnityObjectClass
            || sourceObservation.NativeInstanceIdOffset != clipObservation.NativeInstanceIdOffset
            || sourceObservation.InstanceIdOffsetFieldOffset != 0 || clipObservation.InstanceIdOffsetFieldOffset != 0)
            throw new UnsupportedSignatureException("The independently rooted Unity objects disagree on the official Object layout.");
        return (sourceObservation, clipObservation);
    }

    internal void PrepareUnityBackingWatch(NativeRuntime api, NativeRuntime.RootHandle source, NativeRuntime.RootHandle clip)
    {
        VerifyThread(); _image.Verify();
        var observation = ObserveUnityBacking(api, source, clip);
        if (_unityBackingObjectClass != IntPtr.Zero)
        {
            if (_unityBackingObjectClass != observation.Source.UnityObjectClass || _unityBackingClipClass != observation.Clip.ExactClass
                || _unityBackingInstanceIdOffset != observation.Source.NativeInstanceIdOffset)
                throw new UnsupportedSignatureException("The installed Unity backing layout cannot be rebound by a successor source.");
            return;
        }
        if (Marshal.SizeOf<UnityBackingInformation>() != 128 || Export<VersionNative>("RtPlaybackUnityBackingWatchVersion")() != 1
            || Export<ConfigureUnityBackingNative>("RtPlaybackSourceConfigureUnityBackingWatch")(_owner,
                observation.Source.UnityObjectClass, observation.Clip.ExactClass, observation.Source.NativeInstanceIdOffset) != 1)
            throw new UnsupportedSignatureException("The exact Unity backing watch could not configure before source staging.");
        (_unityBackingObjectClass, _unityBackingClipClass, _unityBackingInstanceIdOffset)
            = (observation.Source.UnityObjectClass, observation.Clip.ExactClass, observation.Source.NativeInstanceIdOffset);
    }

    internal void StageUnityBackingWatch(object sourceOwner, NativeRuntime api, NativeRuntime.RootHandle source,
        NativeRuntime.RootHandle clip, PlayerWatch player)
    {
        VerifyThread(); _image.Verify();
        if (_unityBackingObjectClass == IntPtr.Zero || !ReferenceEquals(player.SourceOwner, sourceOwner)
            || !_playerWatches.Contains(player) || _unityBackingWatches.Any(value => ReferenceEquals(value.SourceOwner, sourceOwner)))
            throw new UnsupportedSignatureException("Unity backing staging requires this group's unique pending rooted player watch.");
        var before = ObserveUnityBacking(api, source, clip);
        if (Export<StageUnityBackingNative>("RtPlaybackSourceStageUnityBackingWatch")(_owner, player.NativeReceipt,
            clip.Target, out var receipt) != 1 || receipt == IntPtr.Zero)
            throw new UnsupportedSignatureException("The native Unity source/clip identity was not staged at the actual source-open boundary.");
        var watch = new UnityBackingWatch(this, sourceOwner, api, source, clip, player, receipt, before);
        _unityBackingWatches.Add(watch); // retain roots' owner before any later verification can fail
        watch.Verify(active: false);
    }

    private void VerifyUnityBackingWatch(object sourceOwner, bool active)
    {
        VerifyThread();
        if (_unityBackingObjectClass == IntPtr.Zero) return; // explicitly non-Unity owned fixture
        var watches = _unityBackingWatches.Where(value => ReferenceEquals(value.SourceOwner, sourceOwner)).ToArray();
        if (watches.Length != 1) throw new UnsupportedSignatureException("No unique independently rooted Unity backing watch belongs to this source.");
        watches[0].Verify(active);
    }
    private void VerifyOperationUnityBackingWatch(object sourceOwner)
    {
        VerifyThread();
        var watches = _unityBackingWatches.Where(value => ReferenceEquals(value.SourceOwner, sourceOwner)).ToArray();
        if (_unityBackingObjectClass == IntPtr.Zero || watches.Length != 1)
            throw new UnsupportedSignatureException("The running fresh source has no unique retained Unity backing watch.");
        watches[0].VerifyOperation();
    }
    private bool TryRetireUnityBackingWatch(object sourceOwner)
    {
        VerifyThread(); var retired = true;
        foreach (var watch in _unityBackingWatches.Where(value => ReferenceEquals(value.SourceOwner, sourceOwner)).ToArray())
        {
            if (watch.TryRetire()) _unityBackingWatches.Remove(watch);
            else retired = false;
        }
        return retired;
    }

    // Receipt creation is private to the physical group. The copied runtime
    // observation only checks metadata; native reads and retains the actual ID.
    private sealed class UnityBackingWatch
    {
        private readonly NativePlaybackStockSourceHub _owner;
        private readonly NativeRuntime _api;
        private readonly NativeRuntime.RootHandle _source, _clip;
        private readonly PlayerWatch _player;
        private readonly IntPtr _receipt;
        private readonly (NativeUnityObjectBackingObservation Source, NativeUnityObjectBackingObservation Clip) _observation;
        private UnityBackingInformation? _initial;
        internal object SourceOwner { get; }
        internal UnityBackingWatch(NativePlaybackStockSourceHub owner, object sourceOwner, NativeRuntime api,
            NativeRuntime.RootHandle source, NativeRuntime.RootHandle clip, PlayerWatch player, IntPtr receipt,
            (NativeUnityObjectBackingObservation Source, NativeUnityObjectBackingObservation Clip) observation)
            => (_owner, SourceOwner, _api, _source, _clip, _player, _receipt, _observation)
                = (owner, sourceOwner, api, source, clip, player, receipt, observation);
        private UnityBackingInformation Read()
            => ReadCore(auditImage: true);
        private UnityBackingInformation ReadCore(bool auditImage)
        {
            _owner.VerifyThread();
            if (auditImage) _owner._image.Verify();
            if (!_owner._unityBackingWatches.Contains(this))
                throw new UnsupportedSignatureException("A copied or retired Unity backing view is not registered by its physical group.");
            var info = new UnityBackingInformation { Version = 1, Size = 128 };
            if (_owner.Export<ReadUnityBackingNative>("RtPlaybackSourceReadUnityBackingWatch")(_owner._owner, _receipt, ref info) != 1
                || info.Owner != unchecked((ulong)_owner._owner.ToInt64()) || info.Receipt != unchecked((ulong)_receipt.ToInt64())
                || info.PlayerWatchReceipt != unchecked((ulong)_player.NativeReceipt.ToInt64()) || info.OwnerEpoch == 0 || info.Ordinal == 0
                || info.Source != unchecked((ulong)_observation.Source.ManagedObject) || info.Clip != unchecked((ulong)_observation.Clip.ManagedObject)
                || info.SourceClass != unchecked((ulong)_observation.Source.ExactClass.ToInt64()) || info.ClipClass != unchecked((ulong)_observation.Clip.ExactClass.ToInt64())
                || info.SourceNative != unchecked((ulong)_observation.Source.CachedNativePointer) || info.ClipNative != unchecked((ulong)_observation.Clip.CachedNativePointer)
                || info.InstanceIdOffset != _observation.Source.NativeInstanceIdOffset || info.SourceId == 0 || info.ClipId == 0
                || info.Phase is < 1 or > 4 || info.Current > 1 || info.Current != (info.Phase == 2 ? 1u : 0u))
                throw new UnsupportedSignatureException("The native Unity backing receipt or immutable source/clip identity changed.");
            if (_initial is { } initial && (initial.OwnerEpoch != info.OwnerEpoch || initial.Ordinal != info.Ordinal
                || initial.SourceId != info.SourceId || initial.ClipId != info.ClipId || initial.SourceFence != info.SourceFence))
                throw new UnsupportedSignatureException("A native Unity backing receipt was rebound to another lifetime.");
            _initial ??= info; return info;
        }
        internal void Verify(bool active)
        {
            var info = Read();
            if (info.Phase >= 3 || active && info.Phase != 2 || _owner.ObserveUnityBacking(_api, _source, _clip) != _observation)
                throw new TargetExpiredException("The rooted source/clip backing lifetime is no longer current.");
        }
        internal void VerifyOperation()
        {
            var info = ReadCore(auditImage: false);
            if (info.Phase != 2 || info.Current != 1 || _owner.ObserveUnityBacking(_api, _source, _clip) != _observation)
                throw new TargetExpiredException("The running rooted source/clip backing lifetime is no longer current.");
        }
        internal bool TryRetire()
        {
            if (Read().Phase == 4) return true;
            if (_owner.Export<RetireWatchNative>("RtPlaybackSourceRetireUnityBackingWatch")(_owner._owner, _receipt) != 1) return false;
            return Read().Phase == 4;
        }
    }
}
