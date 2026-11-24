using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// Installs synthetic IL2CPP export delegates into the existing unmanaged-buffer
// FakeNative fixture. The candidate runtime executes its actual export path.
sealed partial class FakeNative
{
    private static readonly IntPtr VectorAssembly = new(61001), SingleAssembly = new(61002);
    private static readonly IntPtr VectorImage = new(61003), SingleImage = new(61004);
    private IntPtr _vector3ArrayType, _vector3Type, _vectorAssemblyList;
    internal IntPtr Vector3ArrayElementClass => ClassOf(_vector3Type);
    internal bool WrongVectorRank, WrongVectorElement, WrongVectorFieldOffset, WrongVectorFieldParent, WrongVectorImage;
    internal Action? DuringVectorArrayClass, DuringVectorRootCreation;

    internal NativeRuntime Vector3ArrayCaptureApi()
    {
        _vector3Type = AddStruct("UnityEngine.Vector3", 12,
            new NativeFieldLayout("x", "System.Single", 0),
            new NativeFieldLayout("y", "System.Single", 4),
            new NativeFieldLayout("z", "System.Single", 8));
        _vector3ArrayType = AddType("UnityEngine.Vector3[]", 29, false, IntPtr.Size);
        ArrayElementBytes = 12;
        _vectorAssemblyList = Marshal.AllocHGlobal(2 * IntPtr.Size);
        _allocations.Add(_vectorAssemblyList);
        Marshal.WriteIntPtr(_vectorAssemblyList, 0, VectorAssembly);
        Marshal.WriteIntPtr(_vectorAssemblyList, IntPtr.Size, SingleAssembly);

        Add("il2cpp_domain_get", "Ptr0", nameof(VectorDomain));
        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(VectorAssemblies));
        Add("il2cpp_assembly_get_image", "Ptr1", nameof(VectorAssemblyImage));
        Add("il2cpp_image_get_name", "Ptr1", nameof(VectorImageName));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(VectorFindClass));
        Add("il2cpp_class_get_image", "Ptr1", nameof(VectorClassImage));
        Add("il2cpp_class_get_name", "Ptr1", nameof(VectorClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(VectorClassNamespace));
        Add("il2cpp_class_get_field_from_name", "FieldLookup", nameof(VectorFindField));
        Add("il2cpp_field_get_parent", "Ptr1", nameof(VectorFieldParent));
        Add("il2cpp_field_get_offset", "FieldOffset", nameof(VectorFieldOffset));
        Add("il2cpp_array_class_get", "ArrayClassGet", nameof(VectorArrayClass));
        Add("il2cpp_class_get_rank", "Flags", nameof(VectorArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(VectorArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(VectorArrayElementSize));
        Add("il2cpp_array_new", "ArrayNew", nameof(NewVectorArray));
        Add("il2cpp_array_length", "ArraySize", nameof(VectorArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(VectorArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(VectorArrayObjectSize));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(VectorNewHandle));
        var runtime = new NativeRuntime(ExportPointer); runtime.BindCurrentThread(); return runtime;
    }

    private IntPtr VectorDomain() => new(1);
    private IntPtr VectorAssemblies(IntPtr domain, IntPtr count)
    { Marshal.WriteIntPtr(count, new IntPtr(2)); return _vectorAssemblyList; }
    private IntPtr VectorAssemblyImage(IntPtr assembly)
        => assembly == VectorAssembly ? VectorImage : assembly == SingleAssembly ? SingleImage : IntPtr.Zero;
    private IntPtr VectorImageName(IntPtr image)
        => VectorText(image == VectorImage ? (WrongVectorImage ? "Other.dll" : "UnityEngine.CoreModule.dll")
            : image == SingleImage ? "mscorlib.dll" : "");
    private IntPtr VectorFindClass(IntPtr image, string ns, string name)
        => image == VectorImage && ns == "UnityEngine" && name == "Vector3" ? ClassOf(_vector3Type)
            : image == SingleImage && ns == "System" && name == "Single" ? ClassOf(Single) : IntPtr.Zero;
    private IntPtr VectorClassImage(IntPtr cls)
        => cls == ClassOf(_vector3Type) || cls == ClassOf(_vector3ArrayType) ? VectorImage
            : cls == ClassOf(Single) ? SingleImage : IntPtr.Zero;
    private IntPtr VectorClassName(IntPtr cls)
    { var full = _types[_classes[cls]].Name; return VectorText(full[(full.LastIndexOf('.') + 1)..]); }
    private IntPtr VectorClassNamespace(IntPtr cls)
    { var full = _types[_classes[cls]].Name; var dot = full.LastIndexOf('.'); return VectorText(dot < 0 ? "" : full[..dot]); }
    private IntPtr VectorText(string text)
    { var pointer = Marshal.StringToHGlobalAnsi(text); _allocations.Add(pointer); return pointer; }
    private IntPtr VectorFindField(IntPtr cls, string name)
    {
        if (!_structFields.TryGetValue(cls, out var fields)) return IntPtr.Zero;
        return fields.FirstOrDefault(field => _fieldMetadata[field].Name == name);
    }
    private IntPtr VectorFieldParent(IntPtr field)
        => WrongVectorFieldParent ? ClassOf(Single) : ClassOf(_vector3Type);
    private int VectorFieldOffset(IntPtr field)
        => _fieldMetadata[field].Offset + (WrongVectorFieldOffset && _fieldMetadata[field].Name == "y" ? 4 : 0);
    private IntPtr VectorArrayClass(IntPtr element, uint rank)
    { DuringVectorArrayClass?.Invoke(); return ClassOf(_vector3ArrayType); }
    private uint VectorArrayRank(IntPtr cls) => WrongVectorRank ? 2u : 1u;
    private IntPtr VectorArrayElement(IntPtr cls)
        => WrongVectorElement ? ClassOf(Single) : ClassOf(_vector3Type);
    private int VectorArrayElementSize(IntPtr cls) => ArrayElementBytes;
    private IntPtr NewVectorArray(IntPtr element, ulong count)
    {
        var length = checked((uint)count);
        var storage = checked(ArrayHeaderBytes + checked((int)length * 12) + 8);
        var pointer = Marshal.AllocHGlobal(storage); _allocations.Add(pointer);
        for (var index = 0; index < storage; ++index) Marshal.WriteByte(pointer, index, 0x5a);
        _boxedTypes[pointer] = ReturnWrongArrayClass ? Object : _vector3ArrayType;
        _arrays[pointer] = length; ++ArrayAllocations; LastArray = pointer; return pointer;
    }
    private uint VectorArrayLength(IntPtr array) => _arrays[array];
    private uint VectorArrayBytes(IntPtr array)
        => checked(_arrays[array] * 12 + (CorruptArrayByteLength && _arrays[array] > 0 ? 1u : 0u));
    private uint VectorArrayObjectSize(IntPtr array)
        => checked((uint)ArrayHeaderBytes + _arrays[array] * 12
            + (CorruptArrayObjectSize && _arrays[array] > 0 ? 4u : 0u));
    private uint VectorNewHandle(IntPtr value, bool pinned)
    { var id = NewHandle(value, pinned); DuringVectorRootCreation?.Invoke(); return id; }
}
