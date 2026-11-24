using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeFirstInstall
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadOperationNative(uint kind, uint slot, ref NativeOperationBootstrapInformation info);

    internal NativeOperationChannelIdentity OperationIdentity(VerifiedHookTarget target, NativeOperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(target); VerifyReady();
        if (target.Abi != ExactHookBroker.OperationAbi(kind) || target.MethodInfo == IntPtr.Zero
            || target.DeclaringClass == IntPtr.Zero || target.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("The late metadata declaration differs from its exact operation ABI.");
        for (uint index = 0; index < NativeOperationPublication.Count; ++index)
        {
            var row = NativeOperationPublication.Row(_operationPublication, index);
            if (row.Target == target.Address && row.Kind == (uint)kind) return row.Identity;
        }
        throw new UnsupportedSignatureException("The pre-init receipt does not own this exact operation target and kind.");
    }

    private static void VerifyOperationMemory(NativeFirstInstallInformationV7 info, IntPtr helper, IntPtr module, ReadOperationNative read)
    {
        for (uint index = 0; index < NativeOperationPublication.Count; ++index)
        {
            var row = NativeOperationPublication.Row(info, index);
            var actual = new NativeOperationBootstrapInformation { Version = 1, Size = 96 };
            if (read(row.Kind, row.Slot, ref actual) != 0
                || !NativeBootstrappedOperationBridge.MatchesInformation(actual, row.Identity, null))
                throw new UnsupportedSignatureException("A native operation lane differs from its permanent pre-init receipt.");
            HookTargetValidation.VerifyExecutable(row.Target, module);
            HookTargetValidation.VerifyExecutable(row.Entry, helper); VerifyExecutable(row.Original);
            HookTargetValidation.VerifyReadable(row.Target, row.BodySize);
            VerifyProtection(row.Target, row.Protection);
            var last = IntPtr.Add(row.Target, checked((int)row.BodySize - 1)); VerifyProtection(last, row.Protection);
            var function = RtlLookupFunctionEntry(unchecked((ulong)row.Target.ToInt64()), out var imageBase, IntPtr.Zero);
            if (function == IntPtr.Zero || imageBase != info.Listener.Common.ModuleBase)
                throw new UnsupportedSignatureException("A published operation lost its actual native function identity.");
            HookTargetValidation.VerifyReadable(function, 12);
            if (unchecked((uint)Marshal.ReadInt32(function)) != row.Rva
                || unchecked((uint)Marshal.ReadInt32(function, 4)) != row.Rva + row.BodySize
                || unchecked((uint)Marshal.ReadInt32(function, 8)) != row.UnwindRva)
                throw new UnsupportedSignatureException("A published operation differs from its retained native function range.");
        }
    }
}
