using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Only the pinned UnityEvent.Invoke entry owns this native caller-aware
    /// void0 observation capability. It cannot be requested through SDK hook ABIs.</summary>
    internal VerifiedHookTarget ResolveObservedInstanceVoid0(IntPtr module, IntPtr declaringClass, string name, int rva)
    {
        VerifyAccess();
        if (module == IntPtr.Zero || declaringClass == IntPtr.Zero || name != "Invoke" || rva != 0x15642E0)
            throw new UnsupportedSignatureException("Native void0 observation requires the pinned UnityEvent.Invoke declaration.");
        var actualClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEvent");
        if (declaringClass != actualClass)
            throw new UnsupportedSignatureException("Native void0 observation cannot use a different declared class.");
        var method = InspectDeclaredMethod(actualClass, name, Array.Empty<string>(), "System.Void", module);
        var output = Describe(_returnType(method.MethodInfo));
        if (method.Entry?.Rva != rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod
            || output.Kind != NativeValueKind.Void || output.ByRef || _parameterCount(method.MethodInfo) != 0)
            throw new UnsupportedSignatureException("UnityEvent.Invoke differs from its exact declared instance void0 ABI.");
        var address = IntPtr.Add(module, rva);
        HookTargetValidation.VerifyReadable(method.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.MethodInfo);
        HookTargetValidation.ValidateMetadata(layout, _methodName(method.MethodInfo), actualClass, _returnType(method.MethodInfo),
            method.Flags, 0, address);
        if (layout.ImplementationFlags != (ushort)method.ImplementationFlags)
            throw new UnsupportedSignatureException("UnityEvent.Invoke implementation flags changed.");
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.unityevent.invoke", address, method.MethodInfo, actualClass, ExactHookAbi.NativeVoid0);
    }
}
