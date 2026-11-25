using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal enum RendererMetadataFault { Namespace, Width, Rank, Element, StorageOverlap }
    internal RendererMetadataFault? RendererMetadataError;
    internal Action<string, IntPtr>? AfterRendererGetter;
    private void ConfigureRendererExports()
    {
        Add("il2cpp_class_from_name", "ClassLookup", nameof(RendererFindClass));
        Add("il2cpp_class_get_nested_types", "Ptr2", nameof(RendererNested));
        Add("il2cpp_class_get_name", "Ptr1", nameof(RendererClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(RendererNamespace));
        Add("il2cpp_class_get_image", "Ptr1", nameof(RendererClassImage));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(RendererClassParent));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(RendererAssignable));
        Add("il2cpp_class_is_generic", "Predicate", nameof(RendererGeneric));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(RendererInflated));
        Add("il2cpp_class_get_rank", "Flags", nameof(RendererArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(RendererArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(RendererArrayWidth));
        Add("il2cpp_array_length", "ArraySize", nameof(RendererArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(RendererArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(RendererObjectSize));
        Add("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(RendererHeaderSize));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(RendererMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(RendererNamedMethod));
        Add("il2cpp_object_get_class", "Ptr1", nameof(RendererObjectClass));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(RendererInvoke));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(RendererRoot));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(RendererRejectFieldWrite));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(RendererRejectFieldWrite));
    }
    private IntPtr RendererFindClass(IntPtr image, string ns, string name)
        => RendererInverseEnabled && _gameplayImages[image] == "mscorlib.dll" && ns == "System" && name == "Array"
            ? ClassOf(_rendererSystemArrayType) : FlagFindClass(image, ns, name);
    private IntPtr RendererNested(IntPtr parent, IntPtr iterator)
    {
        if (parent != ClassOf(SeekType("InLevel.NoteInformation"))) return SeekNested(parent, iterator);
        if (Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(1)); return ClassOf(_rendererType);
    }
    private IntPtr RendererClassName(IntPtr cls) => cls == ClassOf(_rendererListType) ? GameplayText("List`1") : GameplayClassName(cls);
    private IntPtr RendererNamespace(IntPtr cls) => cls == ClassOf(_rendererListType)
        ? GameplayText(RendererMetadataError == RendererMetadataFault.Namespace ? "Foreign" : "System.Collections.Generic") : GameplayNamespace(cls);
    private IntPtr RendererClassImage(IntPtr cls) => _gameplayImages.Single(row => row.Value ==
        (GameplayTypeName(cls).StartsWith("System.", StringComparison.Ordinal) ? "mscorlib.dll"
            : GameplayTypeName(cls).StartsWith("UnityEngine.", StringComparison.Ordinal) ? "UnityEngine.CoreModule.dll" : "Assembly-CSharp.dll")).Key;
    private IntPtr RendererClassParent(IntPtr cls)
    {
        // The synthetic Hold reader verifies the actual NormalModeInLevelController
        // immediate base. Preserve the canonical controller chain even while the
        // inverse fixture treats those instances as Components.
        if (_gameplayControllerType != IntPtr.Zero && cls == GameplayControllerClass) return ClassOf(_gameplayControllerBaseType);
        if (_gameplayControllerBaseType != IntPtr.Zero && cls == ClassOf(_gameplayControllerBaseType))
            return ClassOf(SeekType("UnityEngine.MonoBehaviour"));
        if (cls == ClassOf(SeekType("UnityEngine.MonoBehaviour"))) return ClassOf(SeekType("UnityEngine.Behaviour"));
        if (cls == ClassOf(SeekType("UnityEngine.Behaviour"))) return ClassOf(SeekType("UnityEngine.Component"));
        if (RendererInverseEnabled && RendererArrayClass(cls)) return ClassOf(_rendererSystemArrayType);
        if (RendererInverseEnabled && cls == ClassOf(_rendererSystemArrayType)) return ClassOf(Object);
        if (RendererInverseEnabled && cls == ClassOf(_rendererGameObjectType)) return ClassOf(SeekType("UnityEngine.Object"));
        if (RendererInverseEnabled && _rendererInverseComponentClasses.Contains(cls)) return ClassOf(_rendererComponentType);
        if (cls == ClassOf(_rendererListType)) return ClassOf(Object);
        if (cls == ClassOf(_rendererTransformType) || cls == ClassOf(_rendererSpriteType)
            || cls == ClassOf(_rendererPoolType) || cls == ClassOf(_rendererChildType)) return ClassOf(_rendererComponentType);
        if (cls == ClassOf(_rendererComponentType)) return ClassOf(SeekType("UnityEngine.Object"));
        return CursorClassParent(cls);
    }
    private bool RendererAssignable(IntPtr target, IntPtr source)
    {
        for (var current = source; current != IntPtr.Zero; current = RendererClassParent(current))
            if (target == current) return true;
        return GameplayAssignable(target, source);
    }
    private bool RendererGeneric(IntPtr cls) => false;
    private bool RendererInflated(IntPtr cls) => cls == ClassOf(_rendererListType);
    private bool RendererArrayClass(IntPtr cls) => cls == ClassOf(_rendererArrayType) || cls == ClassOf(_rendererChildArrayType);
    private uint RendererArrayRank(IntPtr cls) => RendererArrayClass(cls) && RendererMetadataError == RendererMetadataFault.Rank ? 2u : 1u;
    private IntPtr RendererArrayElement(IntPtr cls) => RendererArrayClass(cls)
        ? RendererMetadataError == RendererMetadataFault.Element ? ClassOf(Object)
            : cls == ClassOf(_rendererArrayType) ? ClassOf(_rendererType) : ClassOf(_rendererChildType)
        : cls == ClassOf(_cursorArrayType) ? ClassOf(SeekType("InLevel.NoteInformation")) : ClassOf(Object);
    private int RendererArrayWidth(IntPtr cls) => RendererArrayClass(cls) && RendererMetadataError == RendererMetadataFault.Width ? sizeof(int) : IntPtr.Size;
    private uint RendererArrayLength(IntPtr array)
    {
        var count = _rendererArrays.TryGetValue(array, out var values) ? values.Length : _referenceArrays.TryGetValue(array, out values) ? values.Length : checked((int)ArrayLength(array));
        AfterRendererArrayExport?.Invoke("length", array); return checked((uint)count);
    }
    private uint RendererArrayBytes(IntPtr array)
    {
        var result = _rendererArrays.TryGetValue(array, out var values) ? checked((uint)(values.Length * IntPtr.Size)) : ArrayBytes(array);
        AfterRendererArrayExport?.Invoke("bytes", array); return result;
    }
    private uint RendererObjectSize(IntPtr array)
    {
        var result = _rendererArrays.TryGetValue(array, out var values) ? checked((uint)(ArrayHeaderBytes + values.Length * IntPtr.Size)) : ObjectSize(array);
        AfterRendererArrayExport?.Invoke("size", array); return result;
    }
    private uint RendererHeaderSize() => checked((uint)ArrayHeaderBytes);
    private bool RendererHasMethods(IntPtr cls) => cls == ClassOf(SeekType("InLevel.NoteInformation")) || cls == ClassOf(_rendererComponentType)
        || cls == ClassOf(_rendererTransformType) || cls == ClassOf(_rendererSpriteType) || cls == ClassOf(_rendererPoolType)
        || cls == ClassOf(_rendererChildType) || cls == ClassOf(_rendererType)
        || RendererInverseEnabled && (cls == ClassOf(_rendererGameObjectType) || cls == ClassOf(_rendererSystemArrayType)
            || cls == ClassOf(SeekType("UnityEngine.Object")));
    private IntPtr RendererMethods(IntPtr cls, IntPtr iterator) => RendererHasMethods(cls) ? GameplayMethods(cls, iterator) : FlagMethods(cls, iterator);
    private IntPtr RendererNamedMethod(IntPtr cls, string name, int count) => RendererHasMethods(cls)
        ? _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name && row.Value.Parameters.Length == count).Key
        : FlagNamedMethod(cls, name, count);
    private IntPtr RendererObjectClass(IntPtr value)
    {
        if (_boxedTypes.TryGetValue(value, out var type) && (type == _rendererType || type == _rendererPoolType || type == _rendererChildType
            || type == _rendererTransformType || type == _rendererSpriteType
            || RendererInverseEnabled && type == _rendererGameObjectType)) RendererPublishedBeforeInspection &= HasRootFor(value);
        return NoteViewObjectClass(value);
    }
    private uint RendererRoot(IntPtr target, bool pinned)
    { var handle = NoteViewRoot(target, pinned); AfterRendererRoot?.Invoke(handle, target); return handle; }
    private void RendererRejectFieldWrite(IntPtr owner, IntPtr field, IntPtr value)
    {
        if (!RendererInverseEnabled) throw new InvalidOperationException("Renderer commands must execute native methods, not synthesize effects with field setters.");
        RendererInverseField(owner, field, value);
    }
    internal void RendererChangeMetadata(RendererMetadataFault fault)
    {
        RendererMetadataError = fault;
        if (fault == RendererMetadataFault.StorageOverlap)
        {
            var list = RendererReference(RendererPools[0], "_childList"); var field = RendererStorageField(list, "_version");
            _fieldMetadata[field] = ("_version", Int32, 0x18);
        }
    }
    private IntPtr RendererInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (RendererTryInverseInvoke(method, receiver, arguments, exception, out var inverse)) return inverse;
        if (method == _rendererAppear || method == _rendererDisappear)
        {
            ++Invocations; RendererReceiversRooted &= HasRootFor(receiver); Marshal.WriteIntPtr(exception, IntPtr.Zero);
            RendererExecuteNative(receiver, method == _rendererAppear); return IntPtr.Zero;
        }
        if (method != _rendererComponentTransform && method != _rendererParent && method != _rendererPosition && method != _rendererRotation && method != _rendererScale)
            return FlagInvoke(method, receiver, arguments, exception);
        ++Invocations; RendererReceiversRooted &= HasRootFor(receiver); Marshal.WriteIntPtr(exception, IntPtr.Zero);
        IntPtr result;
        if (method == _rendererComponentTransform) result = _rendererTrs.ContainsKey(receiver) ? receiver : _rendererTransforms[receiver];
        else
        {
            var trs = _rendererTrs[receiver];
            result = method == _rendererParent ? trs.Parent : method == _rendererPosition ? Box(_rendererVectorType, RendererVectorBytes(trs.Position))
                : method == _rendererScale ? Box(_rendererVectorType, RendererVectorBytes(trs.Scale))
                : Box(_rendererQuaternionType, new[] { trs.Rotation.X, trs.Rotation.Y, trs.Rotation.Z, trs.Rotation.W }.SelectMany(BitConverter.GetBytes).ToArray());
        }
        AfterRendererGetter?.Invoke(_gameplayMethods[method].Name, receiver); return result;
    }
    private static byte[] RendererVectorBytes(RizToolbox.Adapter.UiInput.NativeVector3 value)
        => new[] { value.X, value.Y, value.Z }.SelectMany(BitConverter.GetBytes).ToArray();
}
