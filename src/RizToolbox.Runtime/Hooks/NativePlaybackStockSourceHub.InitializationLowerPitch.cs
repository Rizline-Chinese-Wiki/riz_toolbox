using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    // Diagnostic projection only. Native authenticates the registered phase-six
    // direct-lower interruption again when minting a distinct retry receipt.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct LowerPitchInitializationInformation
    {
        internal uint Version, Size, Invalidated, Reason;
        internal ulong Owner, OwnerEpoch, Initializer, InitializerOrdinal, Enumerator, Progress, YieldSequence,
            AttemptSequence, AttemptToken, InvalidationWriter, InvalidationSourceFence, PlayerWatchReceipt;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadLowerPitchInitializationNative(IntPtr owner, IntPtr enumerator, ulong yieldSequence,
        ref LowerPitchInitializationInformation info);

    internal LowerPitchInitializationInformation ReadLowerPitchInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        => ReadLowerPitchInitializationInvalidationCore(enumeratorRoot, actualYield, auditImage: true);

    internal LowerPitchInitializationInformation ReadOperationLowerPitchInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        => ReadLowerPitchInitializationInvalidationCore(enumeratorRoot, actualYield, auditImage: false);

    private LowerPitchInitializationInformation ReadLowerPitchInitializationInvalidationCore(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield, bool auditImage)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(enumeratorRoot);
        if (auditImage) _image.Verify(); else _image.VerifyOperationModule();
        var enumerator = enumeratorRoot.Target;
        var actual = WriterOrigin(actualYield);
        if (!_lowerPitchConfigured || actualYield.Role != NativePlaybackSourceRole.ProgressStartMoveNext
            || actualYield.ParentSequence != 0 || actualYield.Depth != 1 || actualYield.Self != enumerator
            || actual.Sequence == 0 || Marshal.SizeOf<LowerPitchInitializationInformation>() != 112)
            throw new UnsupportedSignatureException("Lower-pitch invalidation requires this exact group's retained initializer and actual yield.");
        // Phase six may wait for a real GetTime borrower before TryAdmit stages
        // any pending PlayerWatch. Its diagnostic badge may therefore carry zero;
        // this projection grants no retry authority, which native authenticates.
        var info = new LowerPitchInitializationInformation { Version = 1, Size = 112 };
        if (Export<ReadLowerPitchInitializationNative>("RtPlaybackSourceReadLowerPitchInitializationInvalidation")(
                _owner, enumerator, actual.Sequence, ref info) != 1
            || info.Version != 1 || info.Size != 112 || info.Owner != unchecked((ulong)_owner.ToInt64()) || info.OwnerEpoch != Epoch
            || info.Initializer == 0 || info.InitializerOrdinal == 0 || info.Enumerator != unchecked((ulong)enumerator.ToInt64())
            || info.Progress == 0 || info.YieldSequence != actual.Sequence || info.Invalidated > 1 || info.Reason > 2
            || info.Invalidated == 0 && (info.Reason != 0 || info.AttemptSequence != 0 || info.AttemptToken != 0
                || info.InvalidationWriter != 0 || info.InvalidationSourceFence != 0 || info.PlayerWatchReceipt != 0)
            || info.Invalidated == 1 && (info.Reason is not (1 or 2) || info.Reason == 2 && !_enginePitchConfigured || info.AttemptSequence == 0 || info.AttemptToken == 0
                || info.InvalidationWriter == 0 || info.InvalidationSourceFence == 0)
            || enumeratorRoot.Target != enumerator)
            throw new UnsupportedSignatureException("The native direct-lower initialization badge lost its actual registered identity.");
        return info;
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal NativePlaybackStockSourceHub.LowerPitchInitializationInformation ReadLowerPitchInitializationInvalidation(
            NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        { VerifyCurrent(); return _hub!.ReadLowerPitchInitializationInvalidation(enumeratorRoot, actualYield); }

        internal NativePlaybackStockSourceHub.LowerPitchInitializationInformation ReadOperationLowerPitchInitializationInvalidation(
            NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        {
            VerifySourceOperationRole(NativePlaybackSourceRole.ProgressStartMoveNext);
            var value = _hub!.ReadOperationLowerPitchInitializationInvalidation(enumeratorRoot, actualYield);
            VerifySourceOperationRole(NativePlaybackSourceRole.ProgressStartMoveNext);
            return value;
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal NativePlaybackStockSourceHub.LowerPitchInitializationInformation ReadLowerPitchInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        => _owner.ReadLowerPitchInitializationInvalidation(enumeratorRoot, actualYield);

    internal NativePlaybackStockSourceHub.LowerPitchInitializationInformation ReadOperationLowerPitchInitializationInvalidation(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
    {
        VerifyOperationLease();
        return _owner.ReadOperationLowerPitchInitializationInvalidation(enumeratorRoot, actualYield);
    }
}
