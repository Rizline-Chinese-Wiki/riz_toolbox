using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal VerifiedHookTarget ResolveUnityEventOperation(IntPtr module, UnityEventOperation operation)
    {
        VerifyAccess();
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("A pinned module is required for UnityEvent operations.");
        var row = UnityEventOperationProfile.Get(operation);
        // Native pre-initialization has verified fixed code and ABI facts.
        // Once IL2CPP metadata exists, require the actual declaration as well
        // before adopting that same lane. A matching name or shared generic
        // address does not establish the receiver/argument contract.
        var cls = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", row.Class);
        var method = InspectDeclaredMethod(cls, row.Method, row.Parameters.ToArray(), "System.Void", module);
        var returns = Describe(_returnType(method.MethodInfo));
        if (method.DeclaringClass.Class != cls || method.DeclaringClass.TypeName != "UnityEngine.Events." + row.Class
            || method.Name != row.Method || !method.ParameterTypes.SequenceEqual(row.Parameters)
            || method.Entry?.Rva != row.Rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod
            || returns.Kind != NativeValueKind.Void || returns.ByRef || _parameterCount(method.MethodInfo) != row.Parameters.Count)
            throw new UnsupportedSignatureException("Allowlisted UnityEvent operation differs from its declared instance void ABI.");
        for (uint index = 0; index < row.Parameters.Count; ++index)
        {
            var arg = Describe(_parameterType(method.MethodInfo, index));
            if (arg.Name != row.Parameters[(int)index] || arg.Kind != NativeValueKind.Reference || arg.ByRef || arg.Class == IntPtr.Zero)
                throw new UnsupportedSignatureException("UnityEvent operation arguments must be exact non-byref object references.");
        }
        var address = IntPtr.Add(module, row.Rva);
        HookTargetValidation.VerifyReadable(method.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.MethodInfo);
        HookTargetValidation.ValidateMetadata(layout, _methodName(method.MethodInfo), cls, _returnType(method.MethodInfo), method.Flags,
            (uint)row.Parameters.Count, address);
        if (layout.ImplementationFlags != (ushort)method.ImplementationFlags)
            throw new UnsupportedSignatureException("UnityEvent operation implementation flags changed.");
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.unityevent.operation." + operation, address, method.MethodInfo, cls, ExactHookBroker.OperationAbi(row.Kind));
    }
}
