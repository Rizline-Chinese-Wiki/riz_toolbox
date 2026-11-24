using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal VerifiedHookTarget ResolveTweenOperation(IntPtr module, NativeTweenOperation operation)
    {
        VerifyAccess();
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("A pinned module is required for Tween operations.");
        var row = TweenOperationProfile.Get(operation);
        var cls = Class("Assembly-CSharp.dll", "", row.Class);
        var method = InspectDeclaredMethod(cls, row.Method, row.Parameters.ToArray(), "System.Void", module);
        var returns = Describe(_returnType(method.MethodInfo));
        if (method.DeclaringClass.Class != cls || method.DeclaringClass.TypeName != row.Class
            || method.Name != row.Method || !method.ParameterTypes.SequenceEqual(row.Parameters)
            || method.Entry?.Rva != row.Rva || (method.Flags & (0x10 | 0x40 | 0x400)) != 0
            || method.GenericMethod || method.InflatedMethod || returns.Kind != NativeValueKind.Void || returns.ByRef
            || _parameterCount(method.MethodInfo) != row.Parameters.Count)
            throw new UnsupportedSignatureException("The actual Tween method differs from its fixed instance void declaration.");
        for (uint index = 0; index < row.Parameters.Count; ++index)
        {
            var argument = Describe(_parameterType(method.MethodInfo, index));
            var expectedClass = Class("Assembly-CSharp.dll", "", row.Parameters[(int)index]);
            if (argument.Name != row.Parameters[(int)index] || argument.Kind != NativeValueKind.Reference
                || argument.ByRef || argument.Class != expectedClass)
                throw new UnsupportedSignatureException("Tween arguments require the exact official non-byref TweenInfo class.");
        }
        var address = IntPtr.Add(module, row.Rva);
        HookTargetValidation.VerifyReadable(method.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.MethodInfo);
        HookTargetValidation.ValidateMetadata(layout, _methodName(method.MethodInfo), cls, _returnType(method.MethodInfo),
            method.Flags, (uint)row.Parameters.Count, address);
        if (layout.ImplementationFlags != (ushort)method.ImplementationFlags)
            throw new UnsupportedSignatureException("Tween implementation flags changed after official metadata resolution.");
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.tween.operation." + operation, address, method.MethodInfo, cls, ExactHookBroker.OperationAbi(row.Kind));
    }
}
