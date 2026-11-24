using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    // RtPlaybackAudioInterruptionInfoV1, not the separate 112-byte pitch wire.
    // This diagnostic projection grants no retry, writer or source authority.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct AudioInterruptionInformation
    {
        internal uint Version, Size, Invalidated, Reason, FenceKind, AttemptRole, ActualFlags, Reserved;
        internal ulong Owner, OwnerEpoch, Initializer, InitializerOrdinal, Enumerator, Progress, YieldSequence;
        internal ulong AttemptSequence, AttemptToken, Writer, Source, Scheduling, ActualStamp, PlayerWatchReceipt;
    }

    private static void VerifyAudioInterruptionLayout()
    {
        if (Marshal.SizeOf<AudioInterruptionInformation>() != 144
            || Marshal.OffsetOf<AudioInterruptionInformation>(nameof(AudioInterruptionInformation.Reserved)).ToInt64() != 28
            || Marshal.OffsetOf<AudioInterruptionInformation>(nameof(AudioInterruptionInformation.Owner)).ToInt64() != 32
            || Marshal.OffsetOf<AudioInterruptionInformation>(nameof(AudioInterruptionInformation.PlayerWatchReceipt)).ToInt64() != 136)
            throw new UnsupportedSignatureException("The private audio-interruption V1/Pack8/144-byte layout differs.");
    }

    internal AudioInterruptionInformation ReadAudioInterruption(NativeRuntime.RootHandle enumeratorRoot,
        NativePlaybackSourceObservation actualYield)
        => ReadAudioInterruptionCore(enumeratorRoot, actualYield, auditImage: true);

    internal AudioInterruptionInformation ReadOperationAudioInterruption(NativeRuntime.RootHandle enumeratorRoot,
        NativePlaybackSourceObservation actualYield)
        => ReadAudioInterruptionCore(enumeratorRoot, actualYield, auditImage: false);

    private AudioInterruptionInformation ReadAudioInterruptionCore(NativeRuntime.RootHandle enumeratorRoot,
        NativePlaybackSourceObservation actualYield, bool auditImage)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(enumeratorRoot);
        if (auditImage) _image.Verify(); else _image.VerifyOperationModule();
        var enumerator = enumeratorRoot.Target;
        var actual = WriterOrigin(actualYield);
        if (!_lowerTimeConfigured || actualYield.Role != NativePlaybackSourceRole.ProgressStartMoveNext
            || actualYield.ParentSequence != 0 || actualYield.Depth != 1 || actualYield.Self != enumerator
            || actual.Sequence == 0)
            throw new UnsupportedSignatureException("Audio-interruption read requires this group's actual retained initializer and yield.");
        var info = new AudioInterruptionInformation { Version = 1, Size = 144 };
        if (_lowerTime.Read(_owner, enumerator, actual.Sequence, ref info) != 1
            || info.Version != 1 || info.Size != 144 || info.Reserved != 0 || info.Invalidated > 1
            || info.Owner != unchecked((ulong)_owner.ToInt64()) || info.OwnerEpoch != Epoch
            || info.Initializer == 0 || info.InitializerOrdinal == 0 || info.Progress == 0
            || info.Enumerator != unchecked((ulong)enumerator.ToInt64()) || info.YieldSequence != actual.Sequence
            || info.Invalidated == 0 && (info.Reason != 0 || info.FenceKind != 0 || info.AttemptRole != 0
                || info.ActualFlags != 0 || info.AttemptSequence != 0 || info.AttemptToken != 0 || info.Writer != 0
                || info.Source != 0 || info.Scheduling != 0 || info.ActualStamp != 0 || info.PlayerWatchReceipt != 0)
            || info.Invalidated == 1 && (info.FenceKind != 2
                || !((info.Reason == 1 && info.AttemptRole == (uint)NativePlaybackSourceRole.SetTime)
                    || (info.Reason == 2 && info.AttemptRole == (uint)NativePlaybackSourceRole.LowerSetTime))
                || info.AttemptSequence == 0 || info.AttemptToken == 0 || info.Writer == 0 || info.Source == 0
                || info.Scheduling == 0 || info.ActualStamp != info.Scheduling
                || (info.ActualFlags & (uint)(NativePlaybackSourceFlags.LowerPitchChild
                    | NativePlaybackSourceFlags.EnginePitchChild | NativePlaybackSourceFlags.LowerTimeChild)) != 0)
            || enumeratorRoot.Target != enumerator)
            throw new UnsupportedSignatureException("The native audio-interruption projection lost its actual initializer or Scheduling identity.");
        // PlayerWatchReceipt may be zero during phase six before any pending
        // watch exists. Native retry independently proves the exact attempt,
        // pre/post fences, normal PhysicalExit, fresh initializer and joint drain.
        return info;
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal NativePlaybackStockSourceHub.AudioInterruptionInformation ReadOperationAudioInterruption(
            NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        {
            VerifySourceOperationRole(NativePlaybackSourceRole.ProgressStartMoveNext);
            var value = _hub!.ReadOperationAudioInterruption(enumeratorRoot, actualYield);
            VerifySourceOperationRole(NativePlaybackSourceRole.ProgressStartMoveNext);
            return value;
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal NativePlaybackStockSourceHub.AudioInterruptionInformation ReadOperationAudioInterruption(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
    {
        VerifyOperationLease();
        return _owner.ReadOperationAudioInterruption(enumeratorRoot, actualYield);
    }
}
