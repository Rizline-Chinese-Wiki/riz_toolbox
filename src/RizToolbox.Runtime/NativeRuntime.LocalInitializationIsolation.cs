using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal VerifiedHookTarget ResolveLocalInitializationHook(IntPtr module, bool getter)
    {
        VerifyAccess();
        var cls = Class("Assembly-CSharp.dll", getter ? "" : "InLevel", getter ? "NormalModeInLevelController" : "ProgressControl");
        var name = getter ? "GetInsideLevelParams" : "Play";
        var parameter = getter ? "InLevel.LevelInformation" : "System.Boolean";
        var returns = getter ? "InsideLevelParams" : "System.Void";
        var rva = getter ? 0x3BEDF0 : 0x400700;
        var method = InspectDeclaredMethod(cls, name, new[] { parameter }, returns, module);
        if (method.Entry?.Rva != rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod)
            throw new UnsupportedSignatureException("LOCAL initialization requires its exact declared non-generic instance entry.");
        var input = Describe(_parameterType(method.MethodInfo, 0)); var output = Describe(_returnType(method.MethodInfo));
        if (input.ByRef || output.ByRef || (getter
            ? input is not { Kind: NativeValueKind.Reference, Size: 8 } || output is not { Kind: NativeValueKind.Reference, Size: 8 } || output.Class == IntPtr.Zero
            : input is not { Kind: NativeValueKind.Boolean, Size: 1 } || output.Kind != NativeValueKind.Void))
            throw new UnsupportedSignatureException("LOCAL initialization reference/Boolean ABI differs from the pinned signature.");
        var address = IntPtr.Add(module, rva);
        HookTargetValidation.VerifyReadable(method.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        HookTargetValidation.ValidateMetadata(Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.MethodInfo),
            _methodName(method.MethodInfo), cls, _returnType(method.MethodInfo), method.Flags, 1, address);
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.initialization." + name, address, method.MethodInfo, cls,
            getter ? ExactHookAbi.LocalInitializationReference1 : ExactHookAbi.LocalProgressPlayBooleanVoid,
            getter ? output.Class : IntPtr.Zero);
    }
}
