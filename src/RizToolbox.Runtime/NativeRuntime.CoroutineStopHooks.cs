using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal VerifiedHookTarget ResolveCoroutineStopHook(IntPtr module, CoroutineStopOperation operation)
    {
        VerifyAccess();
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("An exact game module is required for coroutine stop observation.");
        var row = CoroutineStopProfile.Get(operation);
        var cls = Class("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour");
        var parameters = row.Parameter is null ? Array.Empty<string>() : new[] { row.Parameter };
        var method = InspectDeclaredMethod(cls, row.Method, parameters, "System.Void", module);
        var returns = Describe(_returnType(method.MethodInfo));
        if (method.Entry?.Rva != row.Rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod
            || returns.Kind != NativeValueKind.Void || returns.ByRef || _parameterCount(method.MethodInfo) != parameters.Length)
            throw new UnsupportedSignatureException("Coroutine stop differs from its exact declared instance void ABI.");
        if (parameters.Length != 0)
        {
            var input = Describe(_parameterType(method.MethodInfo, 0));
            if (input.Name != row.Parameter || input is not { Kind: NativeValueKind.Reference, Size: 8, ByRef: false } || input.Class == IntPtr.Zero)
                throw new UnsupportedSignatureException("Coroutine stop requires its exact reference parameter type.");
        }
        var address = IntPtr.Add(module, row.Rva);
        HookTargetValidation.VerifyReadable(method.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.MethodInfo);
        HookTargetValidation.ValidateMetadata(layout, _methodName(method.MethodInfo), cls, _returnType(method.MethodInfo), method.Flags, (uint)parameters.Length, address);
        if (layout.ImplementationFlags != (ushort)method.ImplementationFlags)
            throw new UnsupportedSignatureException("Coroutine stop implementation flags differ from inspected metadata.");
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.coroutine.stop." + operation, address, method.MethodInfo, cls, ExactHookBroker.OperationAbi(row.Kind));
    }
}
