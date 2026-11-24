using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    // Orthogonal status only. Possession of this copied invalidation badge
    // cannot retry, stage a watch, or mint a source/Clock capability.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct BackingInitializationInformation
    {
        internal uint Version, Size, Invalidated, Reason;
        internal ulong Owner, OwnerEpoch, Initializer, InitializerOrdinal, Enumerator, Progress, YieldSequence,
            BackingWatchReceipt, BackingWatchOrdinal, InvalidationSourceFence;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadBackingInitializationNative(IntPtr owner, IntPtr enumerator, ulong yieldSequence,
        ref BackingInitializationInformation info);

    internal BackingInitializationInformation ReadBackingInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        => ReadBackingInitializationInvalidationCore(enumeratorRoot, actualYield, auditImage: true);

    internal BackingInitializationInformation ReadOperationBackingInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        => ReadBackingInitializationInvalidationCore(enumeratorRoot, actualYield, auditImage: false);

    private BackingInitializationInformation ReadBackingInitializationInvalidationCore(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield, bool auditImage)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(enumeratorRoot);
        if (auditImage) _image.Verify(); else _image.VerifyOperationModule();
        var enumerator = enumeratorRoot.Target;
        var actual = WriterOrigin(actualYield);
        if (actualYield.Role != NativePlaybackSourceRole.ProgressStartMoveNext || actualYield.ParentSequence != 0
            || actualYield.Depth != 1 || actualYield.Self != enumerator || actual.Sequence == 0
            || Marshal.SizeOf<BackingInitializationInformation>() != 96)
            throw new UnsupportedSignatureException("Backing invalidation requires this group's retained actual initializer and yield.");
        var info = new BackingInitializationInformation { Version = 1, Size = 96 };
        if (Export<ReadBackingInitializationNative>("RtPlaybackSourceReadBackingInitializationInvalidation")(
                _owner, enumerator, actual.Sequence, ref info) != 1
            || info.Version != 1 || info.Size != 96 || info.Owner != unchecked((ulong)_owner.ToInt64()) || info.OwnerEpoch != Epoch
            || info.Initializer == 0 || info.InitializerOrdinal == 0 || info.Enumerator != unchecked((ulong)enumerator.ToInt64())
            || info.Progress == 0 || info.YieldSequence != actual.Sequence || info.Invalidated > 1 || info.Reason > 5
            || info.Invalidated == 0 && (info.Reason != 0 || info.BackingWatchReceipt != 0
                || info.BackingWatchOrdinal != 0 || info.InvalidationSourceFence != 0)
            || info.Invalidated == 1 && (info.Reason == 0 || info.BackingWatchReceipt == 0
                || info.BackingWatchOrdinal == 0 || info.InvalidationSourceFence == 0)
            || enumeratorRoot.Target != enumerator)
            throw new UnsupportedSignatureException("The exact native initializer backing-invalidation status changed its registered identity.");
        return info;
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal NativePlaybackStockSourceHub.BackingInitializationInformation ReadBackingInitializationInvalidation(
            NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        { VerifyCurrent(); return _hub!.ReadBackingInitializationInvalidation(enumeratorRoot, actualYield); }

        internal NativePlaybackStockSourceHub.BackingInitializationInformation ReadOperationBackingInitializationInvalidation(
            NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        {
            VerifySourceOperationRole(NativePlaybackSourceRole.ProgressStartMoveNext);
            var value = _hub!.ReadOperationBackingInitializationInvalidation(enumeratorRoot, actualYield);
            VerifySourceOperationRole(NativePlaybackSourceRole.ProgressStartMoveNext);
            return value;
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal NativePlaybackStockSourceHub.BackingInitializationInformation ReadBackingInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        => _owner.ReadBackingInitializationInvalidation(enumeratorRoot, actualYield);

    internal NativePlaybackStockSourceHub.BackingInitializationInformation ReadOperationBackingInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
    {
        VerifyOperationLease();
        return _owner.ReadOperationBackingInitializationInvalidation(enumeratorRoot, actualYield);
    }
}
