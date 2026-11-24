using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

/// <summary>A read-only leaf projection. Neither a callable listener capability nor
/// ownership of Target is transferred; the caller keeps the actual delegate rooted
/// and adopts a separate Target root before retaining that pointer independently.
/// Static/open, virtual, generic and inflated methods remain diagnostic observations.</summary>
internal sealed record NativeRootedDelegateMethod(IntPtr Target, NativeMetadataClass ActualClass,
    NativeMetadataMethod Method, bool Multicast, bool Virtual, IntPtr MethodPointer,
    IntPtr InvokeImplementation, IntPtr MethodCode);

internal sealed partial class NativeRuntime
{
    internal NativeRootedDelegateMethod InspectRootedDelegateMethod(RootHandle actualDelegate,
        IntPtr exactDelegateClass, IntPtr module)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(actualDelegate);
        if (module == IntPtr.Zero || exactDelegateClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("A rooted exact delegate and validated module are required.");
        var value = actualDelegate.Target;
        HookTargetValidation.VerifyReadable(value, 0x80);
        HookTargetValidation.VerifyReadable(exactDelegateClass, 16);
        if (Marshal.ReadIntPtr(value) != exactDelegateClass || _objectClass(value) != exactDelegateClass)
            throw new UnsupportedSignatureException("The actual rooted delegate changed its exact class.");
        var delegateClass = Class("mscorlib.dll", "System", "Delegate");
        var multicastClass = Class("mscorlib.dll", "System", "MulticastDelegate");
        if (!_assignable(delegateClass, exactDelegateClass))
            throw new UnsupportedSignatureException("The rooted object is not a System.Delegate.");
        IntPtr OfficialField(IntPtr ownerClass, string name, string type, int offset)
        {
            RequireInstanceFieldLayout(ownerClass, name, type, offset);
            var official = Field(ownerClass, name);
            if (Field(exactDelegateClass, name) != official)
                throw new UnsupportedSignatureException("A delegate subclass shadows an official field: " + name);
            return official;
        }
        var methodField = OfficialField(delegateClass, "method", "System.IntPtr", 0x28);
        var targetField = OfficialField(delegateClass, "m_target", "System.Object", 0x20);
        var pointerField = OfficialField(delegateClass, "method_ptr", "System.IntPtr", 0x10);
        var invokeField = OfficialField(delegateClass, "invoke_impl", "System.IntPtr", 0x18);
        var codeField = OfficialField(delegateClass, "method_code", "System.IntPtr", 0x40);
        var virtualField = OfficialField(delegateClass, "method_is_virtual", "System.Boolean", 0x70);
        IntPtr multicastField = IntPtr.Zero;
        if (_assignable(multicastClass, exactDelegateClass))
        {
            multicastField = OfficialField(multicastClass, "delegates", "System.Delegate[]", 0x78);
            if (ReadField<IntPtr>(value, multicastField, false) != IntPtr.Zero)
                throw new UnsupportedSignatureException("Multicast arrays require bounded leaf expansion before method projection.");
        }
        // actualDelegate already independently roots the receiver and its fields.
        // ReadField uses only bounded local value buffers. There is no additional
        // native GC-handle allocation/free whose unknown outcome could escape here.
        // These field/metadata reads do not invoke or create a managed delegate.
        var method = ReadField<IntPtr>(value, methodField, false);
        var target = ReadField<IntPtr>(value, targetField, false);
        var pointer = ReadField<IntPtr>(value, pointerField, false);
        var invoke = ReadField<IntPtr>(value, invokeField, false);
        var code = ReadField<IntPtr>(value, codeField, false);
        var isVirtual = ReadField<bool>(value, virtualField, false);
        var bytes = (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>();
        HookTargetValidation.VerifyReadable(method, bytes);
        var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method);
        if (layout.ParameterCount > 128 || layout.MethodPointer == IntPtr.Zero)
            throw new UnsupportedSignatureException("The actual delegate method has an unsupported native metadata shape.");
        VerifyListenerMetadataName(layout.Name);
        HookTargetValidation.VerifyReadable(layout.Class, 16);
        HookTargetValidation.VerifyReadable(layout.ReturnType, 16);
        if (layout.ParameterCount != 0)
            HookTargetValidation.VerifyReadable(layout.Parameters, checked((nuint)layout.ParameterCount * (nuint)IntPtr.Size));
        // A readable lookalike MethodInfo is insufficient: require the exact token
        // in the declaring class's official, bounded method enumeration first.
        var found = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var index = 0; index < 4096; ++index)
            {
                var candidate = _methods(layout.Class, iterator.Pointer);
                if (candidate == IntPtr.Zero) break;
                if (candidate == method) { found = true; break; }
            }
        if (!found) throw new UnsupportedSignatureException("The delegate method is not an officially declared metadata member.");
        var flags = _methodFlags(method, out var implementation);
        HookTargetValidation.ValidateMetadata(layout, _methodName(method), _methodClass(method),
            _returnType(method), flags, _parameterCount(method), layout.MethodPointer);
        if (layout.ImplementationFlags != (ushort)implementation)
            throw new UnsupportedSignatureException("Delegate MethodInfo implementation flags changed.");
        var projection = InspectMetadataMethod(method, module);
        if (actualDelegate.Target != value || _objectClass(value) != exactDelegateClass
            || ReadField<IntPtr>(value, methodField, false) != method || ReadField<IntPtr>(value, targetField, false) != target
            || ReadField<IntPtr>(value, pointerField, false) != pointer || ReadField<IntPtr>(value, invokeField, false) != invoke
            || ReadField<IntPtr>(value, codeField, false) != code || ReadField<bool>(value, virtualField, false) != isVirtual
            || multicastField != IntPtr.Zero && ReadField<IntPtr>(value, multicastField, false) != IntPtr.Zero)
            throw new UnsupportedSignatureException("The rooted delegate changed while its method was projected.");
        return new(target, InspectMetadataClass(exactDelegateClass), projection, false, isVirtual, pointer, invoke, code);
    }

    private static void VerifyListenerMetadataName(IntPtr name)
    {
        for (var index = 0; index < 1024; ++index)
        {
            var current = IntPtr.Add(name, index); HookTargetValidation.VerifyReadable(current, 1);
            if (Marshal.ReadByte(current) == 0)
            {
                if (index == 0) throw new UnsupportedSignatureException("Delegate method metadata has an empty name.");
                return;
            }
        }
        throw new UnsupportedSignatureException("Delegate method metadata name exceeds the bounded projection limit.");
    }

    /// <summary>Only the two actual UnityEvent preparation methods may use the
    /// native ref0 observation ABI. This does not invoke PrepareInvoke or listeners.</summary>
    internal VerifiedHookTarget ResolveObservedReferenceReturn0(IntPtr module, IntPtr declaringClass,
        string name, IntPtr returnClass, int rva)
    {
        VerifyAccess();
        if (module == IntPtr.Zero || declaringClass == IntPtr.Zero || returnClass == IntPtr.Zero || name != "PrepareInvoke")
            throw new UnsupportedSignatureException("An exact UnityEvent PrepareInvoke observation is required.");
        var eventBase = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEventBase");
        var list = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "InvokableCallList");
        if (!(declaringClass == eventBase && rva == 0x1563A60 || declaringClass == list && rva == 0x154E1D0))
            throw new UnsupportedSignatureException("Only the two pinned PrepareInvoke declarations may use ref0 observation.");
        const string resultType = "System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>";
        RequireInstanceFieldLayout(list, "m_ExecutingCalls", resultType, 0x20);
        var actualReturnClass = InspectMetadataFieldClass(list, "m_ExecutingCalls", resultType).FieldClass.Class;
        if (returnClass != actualReturnClass)
            throw new UnsupportedSignatureException("PrepareInvoke return class must be the actual closed executing-list field class.");
        var method = InspectDeclaredMethod(declaringClass, name, Array.Empty<string>(), resultType, module);
        var output = Describe(_returnType(method.MethodInfo));
        if (method.Entry?.Rva != rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod
            || output is not { Kind: NativeValueKind.Reference, Size: 8, ByRef: false } || output.Class != actualReturnClass)
            throw new UnsupportedSignatureException("PrepareInvoke differs from its exact declared instance reference8 no-argument ABI.");
        var address = IntPtr.Add(module, rva);
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.unityevent.prepare." + (declaringClass == eventBase ? "event" : "list"), address,
            method.MethodInfo, declaringClass, ExactHookAbi.CoroutineFactoryReference0, actualReturnClass);
    }
}
