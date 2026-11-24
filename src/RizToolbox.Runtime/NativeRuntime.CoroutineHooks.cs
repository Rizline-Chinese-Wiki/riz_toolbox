using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal VerifiedHookTarget ResolveCoroutineStartHook(IntPtr module, bool automaticAlias = false, bool named = false)
    {
        VerifyAccess(); if (module == IntPtr.Zero) throw new UnsupportedSignatureException("No validated game module.");
        var cls = Class("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour");
        if (automaticAlias && named) throw new UnsupportedSignatureException("The named coroutine overload has no Auto alias.");
        var name = automaticAlias ? "StartCoroutine_Auto" : "StartCoroutine";
        var method = InspectDeclaredMethod(cls, name, new[] { named ? "System.String" : "System.Collections.IEnumerator" }, "UnityEngine.Coroutine", module);
        var input = Describe(_parameterType(method.MethodInfo, 0)); var output = Describe(_returnType(method.MethodInfo));
        var rva = named ? 0x153D490 : 0x153D380;
        if (method.Entry?.Rva != rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod
            || input is not { Kind: NativeValueKind.Reference, Size: 8, ByRef: false }
            || output is not { Kind: NativeValueKind.Reference, Size: 8, ByRef: false } || output.Class == IntPtr.Zero)
            throw new UnsupportedSignatureException("Coroutine start differs from its exact declared one-reference native ABI.");
        var address = IntPtr.Add(module, rva);
        HookTargetValidation.VerifyReadable(method.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        HookTargetValidation.ValidateMetadata(Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.MethodInfo),
            _methodName(method.MethodInfo), cls, _returnType(method.MethodInfo), method.Flags, 1, address);
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.coroutine." + name + (named ? ".named" : ""), address, method.MethodInfo, cls, ExactHookAbi.CoroutineStartReference1, output.Class);
    }
    internal VerifiedHookTarget ResolveCoroutineFactoryHook(IntPtr module, IntPtr declaringClass, string methodName, int rva)
    {
        VerifyAccess(); if (module == IntPtr.Zero || declaringClass == IntPtr.Zero || rva <= 0)
            throw new UnsupportedSignatureException("An exact coroutine factory module, class and RVA are required.");
        var method = InspectDeclaredMethod(declaringClass, methodName, Array.Empty<string>(), "System.Collections.IEnumerator", module);
        var output = Describe(_returnType(method.MethodInfo));
        if (method.Entry?.Rva != rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod
            || output is not { Kind: NativeValueKind.Reference, Size: 8, ByRef: false } || output.Class == IntPtr.Zero)
            throw new UnsupportedSignatureException("Coroutine factory differs from its exact zero-argument reference-return ABI.");
        var address = IntPtr.Add(module, rva); HookTargetValidation.VerifyExecutable(address, module);
        return new("local.coroutine.factory." + methodName, address, method.MethodInfo, declaringClass, ExactHookAbi.CoroutineFactoryReference0, output.Class);
    }
}
