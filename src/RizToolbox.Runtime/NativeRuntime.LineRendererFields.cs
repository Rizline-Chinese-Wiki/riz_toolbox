using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal sealed record NativeLineRendererClasses(IntPtr Line, IntPtr Plan, IntPtr Pool,
    IntPtr Child, IntPtr Component, IntPtr Transform, IntPtr Renderer);

internal sealed partial class NativeRuntime
{
    private string LineRendererTypeName(IntPtr type, Action guard)
        => LineRingTypeName(type, guard).Replace('+', '.').Replace('/', '.');

    /// <summary>The plan is the canonical nested class of this exact LineInfo,
    /// never the unrelated UnityEngine.LineRenderer short name.</summary>
    internal NativeLineRendererClasses InspectLineRendererClasses(Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        IntPtr Game(string name) => LineEndpointClass("Assembly-CSharp.dll", "InLevel", name, guard);
        IntPtr Unity(string name) => LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", name, guard);
        var line = Game("LineInformation");
        var nested = O(() => ReadGraphExport<Ptr2>("il2cpp_class_get_nested_types"));
        var plan = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 4096; ++i)
            {
                var value = O(() => nested(line, iterator.Pointer));
                if (value == IntPtr.Zero) { terminated = true; break; }
                if (Utf8(O(() => _className(value))) != "LineRenderer") continue;
                if (plan != IntPtr.Zero) throw new UnsupportedSignatureException("Ambiguous nested LineInformation.LineRenderer.");
                plan = value;
            }
        if (!terminated || plan == IntPtr.Zero)
            throw new UnsupportedSignatureException("The canonical nested LineInformation.LineRenderer is absent.");
        var type = O(() => _classType(plan));
        var image = O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_image"));
        if (type == IntPtr.Zero || O(() => _typeClass(type)) != plan || O(() => _byRef(type))
            || O(() => _typeCode(type)) != 18 || O(() => _isValueType(plan)) || O(() => _isEnum(plan))
            || LineRendererTypeName(type, guard) != "InLevel.LineInformation.LineRenderer"
            || O(() => image(plan)) != O(() => image(line)))
            throw new UnsupportedSignatureException("The nested renderer plan class/type/image differs.");
        return new(line, plan, Game("EasyPool"), Game("EasyPoolChild"),
            Unity("Component"), Unity("Transform"), Unity("LineRenderer"));
    }

    private (string Name, string Type, int Offset, int Size, IntPtr FieldClass)
        LineRendererFieldShape(NativeLineRendererClasses classes, IntPtr owner, string name, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        if (owner == classes.Line)
        {
            if (name == "_positionsPlan")
            {
                var vector = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
                var array = O(() => ReadGraphExport<ArrayClassGet>("il2cpp_array_class_get"));
                var cls = O(() => array(vector, 1));
                return (name, "UnityEngine.Vector3[]", 0x88, IntPtr.Size, cls);
            }
            if (name == "_positionCountPlan") return (name, "System.Int32", 0x90, 4,
                LineEndpointClass("mscorlib.dll", "System", "Int32", guard));
            if (name == "lineRendererPlan") return (name, "InLevel.LineInformation.LineRenderer", 0x48, 8, classes.Plan);
        }
        if (owner == classes.Plan)
        {
            if (name == "pool") return (name, "InLevel.EasyPool", 0x10, 8, classes.Pool);
            if (name == "poolChild") return (name, "InLevel.EasyPoolChild", 0x18, 8, classes.Child);
            if (name == "poolChildTransformPlan") return (name, "UnityEngine.Transform", 0x20, 8, classes.Transform);
            if (name == "active") return (name, "System.Boolean", 0x28, 1,
                LineEndpointClass("mscorlib.dll", "System", "Boolean", guard));
        }
        if (owner == classes.Child)
        {
            if (name == "targetComponent") return (name, "UnityEngine.Component", 0x20, 8, classes.Component);
            if (name == "parentPool") return (name, "InLevel.EasyPool", 0x28, 8, classes.Pool);
        }
        throw new UnsupportedSignatureException("The field is outside the exact saved line renderer contract.");
    }

    internal NativeExactReadField InspectLineRendererField(IntPtr owner, string name, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var classes = InspectLineRendererClasses(guard);
        var shape = LineRendererFieldShape(classes, owner, name, guard);
        var getParent = O(() => ReadGraphExport<Ptr1>("il2cpp_field_get_parent"));
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 256; ++i)
            {
                var field = O(() => _fields(owner, iterator.Pointer));
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (O(() => getParent(field)) != owner)
                    throw new UnsupportedSignatureException("A renderer field belongs to a foreign declaring class.");
                if (Utf8(O(() => _fieldName(field))) != name) continue;
                if (found != IntPtr.Zero) throw new UnsupportedSignatureException("Ambiguous renderer saved field.");
                found = field;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _findField(owner, name)) != found)
            throw new UnsupportedSignatureException("The exact saved renderer field is absent.");
        var flags = O(() => _fieldFlags(found)); var type = O(() => _fieldType(found));
        if ((flags & (0x10u | 0x40u)) != 0 || type == IntPtr.Zero || O(() => _byRef(type))
            || O(() => _typeClass(type)) != shape.FieldClass
            || LineRendererTypeName(type, guard) != shape.Type
            || O(() => _fieldOffset(found)) != shape.Offset)
            throw new UnsupportedSignatureException("The exact saved renderer field flags/type/offset changed.");
        var canonicalType = O(() => _classType(shape.FieldClass));
        var code = shape.Type == "System.Int32" ? 8 : shape.Type == "System.Boolean" ? 2
            : shape.Type == "UnityEngine.Vector3[]" ? 29 : 18;
        var value = code is 8 or 2;
        if (canonicalType == IntPtr.Zero || O(() => _typeClass(canonicalType)) != shape.FieldClass
            || O(() => _byRef(canonicalType)) || O(() => _typeCode(type)) != code
            || O(() => _typeCode(canonicalType)) != code
            || LineRendererTypeName(canonicalType, guard) != shape.Type
            || O(() => _isValueType(shape.FieldClass)) != value || O(() => _isEnum(shape.FieldClass))
            || value && O(() => _valueSize(shape.FieldClass, out _)) != shape.Size)
            throw new UnsupportedSignatureException("The renderer saved field has another canonical native ABI.");
        if (code == 29) VerifyLineRingReferenceClass(shape.Type, shape.FieldClass, code, guard);
        var instanceSize = O(() => ReadGraphExport<ArraySize>("il2cpp_class_instance_size"));
        var size = O(() => instanceSize(owner));
        if (shape.Offset < 16 || (ulong)shape.Offset + (uint)shape.Size > size || size > 4096)
            throw new UnsupportedSignatureException("The renderer field exceeds its bounded native instance storage.");
        return new(found, type, owner, shape.FieldClass, name, shape.Type, false, shape.Offset, shape.Size, flags);
    }

    private IntPtr LineRendererFieldOwner(RootHandle root, NativeExactReadField field, Action guard)
    {
        VerifyAccess(); root.VerifyOwnerThread();
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        if (InspectLineRendererField(field.DeclaringClass, field.Name, guard) != field)
            throw new UnsupportedSignatureException("The saved/current line renderer descriptor differs.");
        var value = O(() => root.Target);
        var cls = O(() => {
            VerifyLineSetPositionsReadable(value, 16);
            return _objectClass(value);
        });
        if (cls != field.DeclaringClass) throw new UnsupportedSignatureException("The actual renderer field receiver changed class.");
        var size = O(() => ReadGraphExport<ArraySize>("il2cpp_object_get_size"));
        var bytes = O(() => {
            VerifyLineSetPositionsReadable(value, 16);
            return size(value);
        });
        if ((ulong)field.Offset + (uint)field.Size > bytes || bytes > 4096)
            throw new UnsupportedSignatureException("The actual renderer field receiver has inconsistent bounds.");
        return value;
    }

    internal T ReadLineRendererField<T>(RootHandle root, NativeExactReadField field, Action guard) where T : struct
    {
        VerifyAccess();
        var size = field.TypeName == "System.Boolean" ? 1 : field.TypeName == "System.Int32" ? 4 : 0;
        if (size == 0 || typeof(T) != (size == 1 ? typeof(bool) : typeof(int)))
            throw new UnsupportedSignatureException("Renderer scalar reads require exact Boolean or Int32.");
        var receiver = LineRendererFieldOwner(root, field, guard);
        using var output = new Arg(size);
        guard(); VerifyLineSetPositionsReadable(receiver, checked((nuint)(field.Offset + field.Size)));
        _fieldGet(receiver, field.Field, output.Pointer); guard();
        return ReadValue<T>(output.Pointer);
    }

    internal void PublishLineRendererReference(RootHandle root, NativeExactReadField field,
        Action<IntPtr> publish, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(publish);
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var receiver = LineRendererFieldOwner(root, field, guard);
            if (field.Size != IntPtr.Size || field.TypeName is "System.Boolean" or "System.Int32")
                throw new UnsupportedSignatureException("The renderer field is not a reference.");
            using var output = new Arg(IntPtr.Size);
            guard(); VerifyLineSetPositionsReadable(receiver, checked((nuint)(field.Offset + field.Size)));
            _fieldGet(receiver, field.Field, output.Pointer);
            var result = Marshal.ReadIntPtr(output.Pointer);
            call.BeginPublication(result);
            try { publish(result); call.PublicationAccepted(); }
            catch { call.PublicationFailed(); throw; }
            guard();
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
}
