using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeFirstInstall
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AttachTweenSourceReferencesNative(ref NativeTweenSourceReferencesObserver observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadTweenSourceReferencesNative(ref NativeTweenSourceReferencesInformation information);

    internal sealed partial class TweenSourceReady
    {
        internal ulong GetSourceEpoch()
        {
            VerifyReady();
            return (_producer._tweenSourceIdentity
                ?? throw new UnsupportedSignatureException("The actual source producer has no immutable publication identity.")).Epoch;
        }

        /// <summary>The actual native FirstInstall gate checks complete Ready and
        /// official metadata before calling its private same-helper attachment.
        /// A copied source packet has no way to reach this owner method.</summary>
        internal void AttachReferences(ref NativeTweenSourceReferencesObserver observer)
        {
            VerifyReady(); NativeTweenSourceReferences.ValidateLayout(); var epoch = GetSourceEpoch();
            if (observer.Version != 1 || observer.Size != 24 || observer.Notify == IntPtr.Zero)
                throw new UnsupportedSignatureException("The actual source observer has no complete callback shape.");
            var address = NativeLibrary.GetExport(HelperModule, "RizFirstInstall_AttachTweenSourceReferencesV1");
            var readAddress = NativeLibrary.GetExport(HelperModule, "RizTweenSourceReferences_GetInfo");
            HookTargetValidation.VerifyExecutable(address, HelperModule);
            HookTargetValidation.VerifyExecutable(readAddress, HelperModule);
            var read = Marshal.GetDelegateForFunctionPointer<ReadTweenSourceReferencesNative>(readAddress);
            var before = NativeTweenSourceReferences.NewInformation();
            if (read(ref before) != 0 || before.Version != 1 || before.Size != 104 || before.Attached != 0 || before.Unknown != 0
                || before.ScopeCapacity != NativeTweenSourceReferences.MaximumScopes || before.SourceEpoch != 0 || before.ObserverEpoch != 0
                || before.ActiveScopes != 0 || before.RetainedScopes != 0 || before.CallbackInFlight != 0
                || before.ScopesBegun != 0 || before.ScopesEnded != 0 || before.ScopesCompleted != 0
                || before.DeliveryAttempts != 0 || before.DeliveriesCompleted != 0 || before.CallbackFaults != 0)
                throw new UnsupportedSignatureException("The actual source reference observer is already claimed or its pre-attach state is unknown.");
            var expected = observer;
            var result = Marshal.GetDelegateForFunctionPointer<AttachTweenSourceReferencesNative>(address)(ref observer);
            if (result != 0 || !observer.Equals(expected))
                throw new UnsupportedSignatureException("The actual FirstInstall source observer gate rejected or changed this once-only observer: " + result);
            VerifyReady();
            var after = NativeTweenSourceReferences.NewInformation();
            if (read(ref after) != 0 || !NativeTweenSourceReferences.InformationShape(after, epoch) || after.Unknown != 0)
                throw new UnsupportedSignatureException("The actual FirstInstall gate has no matching known attached source observer receipt.");
        }

        internal void VerifyReferenceOrigin(NativeTweenSourceReferenceOrigin origin)
        {
            VerifyReady();
            var packet = _producer._tweenSourceIdentity
                ?? throw new UnsupportedSignatureException("The actual source producer has no retained publication identity.");
            var row = packet.Row((uint)origin.Site);
            // One scope keeps the real initial Before/Decision CALL return as its
            // immutable anchor even when a complete replay later emits After.
            if (origin.SourceEpoch != packet.Epoch || origin.SourcePc != row.SourcePc
                || origin.RelayReturnPc != row.PreReturnPc || row.PreReturnPc == IntPtr.Zero)
                throw new UnsupportedSignatureException("A source reference origin crossed its actual published source or initial relay return PC.");
        }
    }
}
