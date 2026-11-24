using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

sealed partial class FakeNative
{
    private static readonly IntPtr SetLineAssembly = new(63001), SetLineImage = new(63002);
    private IntPtr _setLineType, _setMethod, _setMethodName, _setAssemblyList;
    internal IntPtr SetLineObject, SetArrayObject;
    internal NativeRuntime.RootHandle SetLineRoot = null!, SetArrayRoot = null!;
    internal bool SetExceptionAfterFields;
    internal bool SetAllArgumentsRooted = true;
    internal IntPtr SetParameterOverride;
    internal bool SetUnreadableMethod;
    internal int SetMethodNameReads;

    internal NativeRuntime SetPositionsApi(IntPtr module)
    {
        var capture = CaptureVector3Buffer(new NativeVector3BufferBits(0, 0, 0),
            new NativeVector3BufferBits(0, 0, 0), new NativeVector3BufferBits(0, 0, 0));
        SetArrayRoot = capture.Original; SetArrayObject = capture.Original.Target;
        _setLineType = AddType("InLevel.LineInformation", 18, false, IntPtr.Size);
        SetLineObject = Marshal.AllocHGlobal(0x98); _allocations.Add(SetLineObject);
        for (var i = 0; i < 0x98; ++i) Marshal.WriteByte(SetLineObject, i, 0);
        Marshal.WriteIntPtr(SetLineObject, ClassOf(_setLineType));
        _boxedTypes[SetLineObject] = _setLineType;
        SetLineRoot = new NativeRuntime.RootHandle(NewHandle(SetLineObject, true), FreeHandle, HandleTarget);
        _setMethodName = VectorText("SetPositions");
        _setMethod = Marshal.AllocHGlobal(Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        _allocations.Add(_setMethod);
        Marshal.StructureToPtr(new HookTargetValidation.MethodInfo2022 {
            MethodPointer = IntPtr.Add(module, NativeRuntime.ExactLineSetPositionsRva),
            VirtualMethodPointer = IntPtr.Add(module, NativeRuntime.ExactLineSetPositionsRva),
            Name = _setMethodName, Class = ClassOf(_setLineType), ReturnType = Void,
            ParameterCount = 2, Flags = 0, ImplementationFlags = 0
        }, _setMethod, false);
        _setAssemblyList = Marshal.AllocHGlobal(3 * IntPtr.Size); _allocations.Add(_setAssemblyList);
        Marshal.WriteIntPtr(_setAssemblyList, VectorAssembly);
        Marshal.WriteIntPtr(_setAssemblyList, IntPtr.Size, SingleAssembly);
        Marshal.WriteIntPtr(_setAssemblyList, IntPtr.Size * 2, SetLineAssembly);
        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(SetAssemblies));
        Add("il2cpp_assembly_get_image", "Ptr1", nameof(SetAssemblyImage));
        Add("il2cpp_image_get_name", "Ptr1", nameof(SetImageName));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(SetFindClass));
        Add("il2cpp_class_get_image", "Ptr1", nameof(SetClassImage));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(SetMethods));
        Add("il2cpp_method_get_name", "Ptr1", nameof(SetMethodName));
        Add("il2cpp_method_get_class", "Ptr1", nameof(SetMethodClass));
        Add("il2cpp_method_get_param_count", "Flags", nameof(SetParameterCount));
        Add("il2cpp_method_get_param", "ParameterType", nameof(SetParameter));
        Add("il2cpp_object_get_size", "ArraySize", nameof(SetObjectSize));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(SetInvoke));
        var api = new NativeRuntime(ExportPointer); api.BindCurrentThread(); return api;
    }

    internal IntPtr SetLineClass => ClassOf(_setLineType);
    internal IntPtr SetSavedArray => Marshal.ReadIntPtr(SetLineObject, 0x88);
    internal int SetSavedCount => Marshal.ReadInt32(SetLineObject, 0x90);
    internal uint SetArrayTemporaryPin => _handles.Where(pair => pair.Value == SetArrayObject).Max(pair => pair.Key);
    internal void SetLineForeignClass() => _boxedTypes[SetLineObject] = Object;
    internal void SetMethodWrongRva(IntPtr module)
    {
        var value = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(_setMethod);
        value.MethodPointer = IntPtr.Add(module, NativeRuntime.ExactLineSetPositionsRva + 0x120);
        Marshal.StructureToPtr(value, _setMethod, false);
    }

    private IntPtr SetAssemblies(IntPtr domain, IntPtr count)
    { Marshal.WriteIntPtr(count, new IntPtr(3)); return _setAssemblyList; }
    private IntPtr SetAssemblyImage(IntPtr assembly)
        => assembly == SetLineAssembly ? SetLineImage : VectorAssemblyImage(assembly);
    private IntPtr SetImageName(IntPtr image)
        => image == SetLineImage ? VectorText("Assembly-CSharp.dll") : VectorImageName(image);
    private IntPtr SetClassImage(IntPtr cls)
        => cls == SetLineClass ? SetLineImage : cls == ClassOf(Int32) || cls == ClassOf(Void)
            ? SingleImage : VectorClassImage(cls);
    private IntPtr SetFindClass(IntPtr image, string ns, string name)
        => image == SetLineImage && ns == "InLevel" && name == "LineInformation" ? SetLineClass
            : image == SingleImage && ns == "System" && name == "Int32" ? ClassOf(Int32)
            : image == SingleImage && ns == "System" && name == "Void" ? ClassOf(Void)
            : VectorFindClass(image, ns, name);
    private IntPtr SetMethods(IntPtr cls, IntPtr iterator)
    {
        if (cls != SetLineClass || Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(1)); return SetUnreadableMethod ? new IntPtr(1) : _setMethod;
    }
    private IntPtr SetMethodName(IntPtr method) { ++SetMethodNameReads; return _setMethodName; }
    private IntPtr SetMethodClass(IntPtr method) => SetLineClass;
    private uint SetParameterCount(IntPtr method) => 2;
    private IntPtr SetParameter(IntPtr method, uint index)
        => index == 0 ? _vector3ArrayType : SetParameterOverride == IntPtr.Zero ? Int32 : SetParameterOverride;
    private uint SetObjectSize(IntPtr value) => value == SetLineObject ? 0x98u : VectorArrayObjectSize(value);
    private IntPtr SetInvoke(IntPtr method, IntPtr self, IntPtr arguments, IntPtr exception)
    {
        if (method != _setMethod || self != SetLineObject) throw new InvalidOperationException("Foreign synthetic SetPositions dispatch.");
        ++Invocations;
        var array = Marshal.ReadIntPtr(arguments);
        var count = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, IntPtr.Size));
        SetAllArgumentsRooted &= _handles.Values.Contains(self) && _handles.Values.Contains(array);
        Marshal.WriteIntPtr(self, 0x88, array);
        Marshal.WriteInt32(self, 0x90, count);
        // Simulates the generated method storing both plan fields before a
        // later IL2CPP exception. No mapped image machine code is executed.
        Marshal.WriteIntPtr(exception, SetExceptionAfterFields ? new IntPtr(1) : IntPtr.Zero);
        return IntPtr.Zero;
    }
}
