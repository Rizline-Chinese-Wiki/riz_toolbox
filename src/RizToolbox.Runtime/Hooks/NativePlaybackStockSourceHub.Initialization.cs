using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    // Read-only native diagnostics. Receipt is not an initialization capability;
    // no public constructor or boolean can reopen fresh capture/publication.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct InitializationInformation
    {
        internal uint Version, Size, Phase, Blocked;
        internal ulong Owner, OwnerEpoch, Ordinal, Enumerator, Progress, Source, Clip, Player;
        internal ulong YieldSequence, YieldToken, FinalSequence, FinalToken, ChildSequence, ChildToken;
        internal ulong Writer, SourceFence, Scheduling, Receipt;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConfigureInitializationNative(IntPtr owner, IntPtr caller);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadInitializationNative(IntPtr owner, ref InitializationInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnterFreshNative(IntPtr owner);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void LeaveFreshNative(IntPtr owner);

    internal void ConfigureInitialization(IntPtr exactCaller)
    {
        VerifyThread(); _image.Verify();
        if (Export<VersionNative>("RtPlaybackInitializationVersion")() != 2
            || Marshal.SizeOf<InitializationInformation>() != 160
            || Export<ConfigureInitializationNative>("RtPlaybackSourceConfigureInitialization")(_owner, exactCaller) != 1)
            throw new UnsupportedSignatureException("The closed initialization CALL could not bind its actual source owner.");
    }
    internal InitializationInformation ReadInitialization()
    {
        VerifyThread(); _image.Verify();
        var value = new InitializationInformation { Version = 2, Size = 160 };
        if (Export<ReadInitializationNative>("RtPlaybackSourceReadInitialization")(_owner, ref value) != 1
            || value.Version != 2 || value.Size != 160 || value.Owner != unchecked((ulong)_owner.ToInt64())
            || value.OwnerEpoch != Epoch || value.Phase > 7 || value.Blocked > 1
            || value.Receipt != 0 && (value.Phase != 4 || value.Blocked != 1))
            throw new UnsupportedSignatureException("The private initialization projection changed its owner or protocol.");
        return value;
    }
    internal bool MatchesInitializationCall(NativePlaybackSourceObservation actual, ulong sequence, ulong token)
    {
        VerifyThread(); _image.Verify();
        var retained = WriterOrigin(actual); // exact registered object, not caller-provided frame
        return retained.Sequence == sequence && retained.Token == token;
    }
    internal void RequireFreshAdmission()
    {
        VerifyThread(); _image.Verify();
        if (Export<EnterFreshNative>("RtPlaybackSourceEnterFresh")(_owner) != 1)
            throw new UnsupportedSignatureException("Initialization is pending; fresh source capture, writer or Clock publication is unavailable.");
        Export<LeaveFreshNative>("RtPlaybackSourceLeaveFresh")(_owner);
    }
}

internal sealed partial class NativePlaybackSingleReturnBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BindFreshSourceNative(uint slot, IntPtr library, IntPtr owner);
    internal void BindFreshSource(NativePlaybackStockSourceHub source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            if (Export<BindFreshSourceNative>("RizSingleReturnPrototype_BindFreshSource")(_slot, source.NativeLibraryHandle, source.NativeOwner) != 0)
                throw new UnsupportedSignatureException("The exact GetTime/source owners could not bind their native fresh-admission gates.");
        }
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal void BindFreshSource(NativePlaybackStockSourceHub hub)
        { lock (_gate) { _bridge!.BindFreshSource(hub); } }
    }
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal NativeRuntime.RootHandle RetainInitializationEnumerator(NativeRuntime api, NativePlaybackSourceObservation observation)
        {
            VerifyIssuedBefore(observation);
            if (observation.Role != NativePlaybackSourceRole.ProgressStartMoveNext)
                throw new UnsupportedSignatureException("Only this actual Start parent can retain its exact initializer receiver.");
            var cls = _targets.ProgressStartMoveNext.DeclaringClass;
            api.RequireInstanceFieldLayout(cls, "<>1__state", "System.Int32", 0x10);
            api.RequireInstanceFieldLayout(cls, "<>4__this", "InLevel.ProgressControl", 0x20);
            return api.RetainExactObject(observation.Self, cls);
        }
        internal NativeRuntime.RootHandle RetainInitializationProgress(NativeRuntime api, IntPtr progress)
        { VerifyCurrent(); return api.RetainExactObject(progress, _targets.ProgressPlay.DeclaringClass); }
        internal NativeRuntime.RootHandle RetainOperationInitializationEnumerator(NativeRuntime api, NativePlaybackSourceObservation observation)
        {
            VerifyIssuedOperationBefore(observation);
            if (observation.Role != NativePlaybackSourceRole.ProgressStartMoveNext)
                throw new UnsupportedSignatureException("Only this actual Start parent can retain its exact initializer receiver.");
            var cls = _targets.ProgressStartMoveNext.DeclaringClass;
            api.RequireInstanceFieldLayout(cls, "<>1__state", "System.Int32", 0x10);
            api.RequireInstanceFieldLayout(cls, "<>4__this", "InLevel.ProgressControl", 0x20);
            return api.RetainExactObject(observation.Self, cls);
        }
        internal NativeRuntime.RootHandle RetainOperationInitializationProgress(NativeRuntime api, IntPtr progress)
        {
            VerifySourceOperationOwner();
            return api.RetainExactObject(progress, _targets.ProgressPlay.DeclaringClass);
        }
        internal bool MatchesInitializationCall(NativePlaybackSourceObservation actual, ulong sequence, ulong token)
        { VerifyCurrent(); return _hub!.MatchesInitializationCall(actual, sequence, token); }
        internal void RequireFreshAdmission() { VerifyCurrent(); _hub!.RequireFreshAdmission(); }
        internal NativePlaybackStockSourceHub.InitializationInformation ReadInitialization()
        { VerifyCurrent(); return _hub!.ReadInitialization(); }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal NativeRuntime.RootHandle RetainOperationInitializationEnumerator(NativeRuntime api, NativePlaybackSourceObservation observation)
    {
        VerifyOperationLease();
        return _owner.RetainOperationInitializationEnumerator(api, observation);
    }
    internal NativeRuntime.RootHandle RetainOperationInitializationProgress(NativeRuntime api, IntPtr progress)
    {
        VerifyOperationLease();
        return _owner.RetainOperationInitializationProgress(api, progress);
    }
}
