using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal enum NativeLineProducerField { StartPosition, EndPosition, NewLinePosition, EaseType }

internal sealed record NativeLineProducerControlSnapshot(IntPtr Control,
    NativeVector3BufferBits StartPosition, NativeVector3BufferBits EndPosition,
    NativeVector3BufferBits NewLinePosition, int EaseType)
{
    internal bool AtomicSnapshot => false;
}

// A word group from the frozen producer: start/end XY then Z; per sample Z,
// X, Y. EaseType uses component zero/X as an Int32, not a float conversion.
internal readonly record struct NativeLineProducerPatch(NativeLineProducerField Field,
    int Component, int Components, NativeVector3BufferBits Value);

internal sealed record NativeLineProducerControlWriteObservation(NativeLineProducerControlSnapshot Before,
    NativeLineProducerControlSnapshot After, NativeLineProducerPatch Patch)
{
    internal bool StoreEntered => true;
    internal bool AtomicConditionalWrite => false;
}

internal sealed partial class NativeRuntime
{
    private static string LineProducerFieldName(NativeLineProducerField field) => field switch
    {
        NativeLineProducerField.StartPosition => "startPosition",
        NativeLineProducerField.EndPosition => "endPosition",
        NativeLineProducerField.NewLinePosition => "newLinePosition",
        NativeLineProducerField.EaseType => "easeType",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private NativeExactReadField InspectLineProducerControlField(IntPtr owner,
        NativeLineProducerField selected, Action guard)
    {
        VerifyAccess();
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        _ = LineEndpointClassType(owner, "Assembly-CSharp.dll", "InLevel", "LineControl", 18, false, guard);
        var name = LineProducerFieldName(selected);
        var scalar = selected == NativeLineProducerField.EaseType;
        var offset = selected switch
        {
            NativeLineProducerField.StartPosition => 0x50,
            NativeLineProducerField.EndPosition => 0x5c,
            NativeLineProducerField.NewLinePosition => 0x68,
            _ => 0x80
        };
        var fieldClass = LineEndpointClass(scalar ? "mscorlib.dll" : "UnityEngine.CoreModule.dll",
            scalar ? "System" : "UnityEngine", scalar ? "Int32" : "Vector3", guard);
        var fieldType = LineEndpointClassType(fieldClass, scalar ? "mscorlib.dll" : "UnityEngine.CoreModule.dll",
            scalar ? "System" : "UnityEngine", scalar ? "Int32" : "Vector3", scalar ? 8 : 17, true, guard);
        if (!scalar) VerifyLineEndpointVector(fieldType, guard);
        else if (O(() => _valueSize(fieldClass, out _)) != 4)
            throw new UnsupportedSignatureException("The producer easeType is not a canonical four-byte Int32.");
        var getParent = O(() => ReadGraphExport<Ptr1>("il2cpp_field_get_parent"));
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 256; ++i)
            {
                var field = O(() => _fields(owner, iterator.Pointer));
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (O(() => getParent(field)) != owner)
                    throw new UnsupportedSignatureException("A producer field has another declaring class.");
                if (Utf8(O(() => _fieldName(field))) != name) continue;
                if (found != IntPtr.Zero) throw new UnsupportedSignatureException("Ambiguous producer field.");
                found = field;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _findField(owner, name)) != found)
            throw new UnsupportedSignatureException("The exact producer field is absent.");
        var type = O(() => _fieldType(found)); var flags = O(() => _fieldFlags(found));
        var typeName = scalar ? "System.Int32" : "UnityEngine.Vector3";
        if ((flags & (0x10u | 0x20u | 0x40u)) != 0 || type == IntPtr.Zero
            || O(() => _typeClass(type)) != fieldClass || O(() => _byRef(type))
            || O(() => _typeCode(type)) != (scalar ? 8 : 17)
            || LineRingTypeName(type, guard) != typeName || O(() => _fieldOffset(found)) != offset)
            throw new UnsupportedSignatureException("The producer field has another exact mutable ABI/offset.");
        if (!scalar) VerifyLineEndpointVector(type, guard);
        var instanceSize = O(() => ReadGraphExport<ArraySize>("il2cpp_class_instance_size"));
        var size = O(() => instanceSize(owner)); var width = scalar ? 4 : 12;
        if (size < 0x88 || size > 4096 || (uint)(offset + width) > size)
            throw new UnsupportedSignatureException("The producer field exceeds its bounded LineControl instance.");
        return new(found, type, owner, fieldClass, name, typeName, false, offset, width, flags);
    }

    private static void ValidateLineProducerPatch(NativeLineProducerPatch patch)
    {
        _ = LineProducerFieldName(patch.Field);
        if (patch.Field == NativeLineProducerField.EaseType)
        {
            if (patch.Component != 0 || patch.Components != 1)
                throw new ArgumentOutOfRangeException(nameof(patch), "easeType has one exact Int32 word.");
        }
        else if (patch.Component < 0 || patch.Components < 1 || patch.Components > 2
            || patch.Component + patch.Components > 3 || patch.Components == 2 && patch.Component != 0)
            throw new ArgumentOutOfRangeException(nameof(patch), "Producer stores are one component or the original XY word group.");
    }

    private static int ProducerComponent(NativeVector3BufferBits bits, int index)
        => index == 0 ? bits.X : index == 1 ? bits.Y : bits.Z;

    internal static NativeLineProducerControlSnapshot PatchLineProducerControl(
        NativeLineProducerControlSnapshot before, NativeLineProducerPatch patch)
    {
        ArgumentNullException.ThrowIfNull(before); ValidateLineProducerPatch(patch);
        if (patch.Field == NativeLineProducerField.EaseType) return before with { EaseType = patch.Value.X };
        var bits = patch.Field == NativeLineProducerField.StartPosition ? before.StartPosition
            : patch.Field == NativeLineProducerField.EndPosition ? before.EndPosition : before.NewLinePosition;
        var words = new[] { bits.X, bits.Y, bits.Z };
        for (var i = patch.Component; i < patch.Component + patch.Components; ++i)
            words[i] = ProducerComponent(patch.Value, i);
        var next = new NativeVector3BufferBits(words[0], words[1], words[2]);
        return patch.Field switch
        {
            NativeLineProducerField.StartPosition => before with { StartPosition = next },
            NativeLineProducerField.EndPosition => before with { EndPosition = next },
            _ => before with { NewLinePosition = next }
        };
    }

    private T WithLineProducerControl<T>(RootHandle original, Action guard,
        Func<IntPtr, NativeExactReadField[], Action, Func<NativeLineProducerControlSnapshot>, T> action)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(guard);
        void G() { VerifyAccess(); original.VerifyOwnerThread(); guard(); }
        TRead O<TRead>(Func<TRead> read) { G(); var value = read(); G(); return value; }
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var receiver = O(() => original.Target);
            var pin = call.Root(() =>
            {
                G(); VerifyLineSetPositionsReadable(receiver, 16);
                var id = _newHandle(receiver, true);
                return new RootHandle(id, _freeHandle, _handleTarget, VerifyAccess);
            });
            G();
            var cls = LineEndpointClass("Assembly-CSharp.dll", "InLevel", "LineControl", G);
            var fields = Enum.GetValues<NativeLineProducerField>().Select(field => InspectLineProducerControlField(cls, field, G)).ToArray();
            var objectSize = O(() => ReadGraphExport<ArraySize>("il2cpp_object_get_size"));
            void Shape()
            {
                if (O(() => original.Target) != receiver || O(() => pin.Target) != receiver)
                    throw new UnsupportedSignatureException("The original producer control root changed.");
                var actualClass = O(() => { VerifyLineSetPositionsReadable(receiver, 16); return _objectClass(receiver); });
                var bytes = O(() => { VerifyLineSetPositionsReadable(receiver, 16); return objectSize(receiver); });
                if (actualClass != cls || bytes < 0x88 || bytes > 4096)
                    throw new UnsupportedSignatureException("The original producer receiver has another exact class or storage extent.");
                foreach (var selected in Enum.GetValues<NativeLineProducerField>())
                    if (InspectLineProducerControlField(cls, selected, G) != fields[(int)selected])
                        throw new UnsupportedSignatureException("A retained producer field descriptor changed.");
                G(); VerifyLineSetPositionsReadable(receiver, 0x88); G();
            }
            NativeLineProducerControlSnapshot ReadOnce()
            {
                var values = new NativeVector3BufferBits[3]; var ease = 0;
                for (var i = 0; i < fields.Length; ++i)
                {
                    var field = fields[i]; using var output = new Arg(field.Size);
                    G(); VerifyLineSetPositionsReadable(receiver, checked((nuint)(field.Offset + field.Size)));
                    _fieldGet(receiver, field.Field, output.Pointer); G();
                    if (i == 3) ease = Marshal.ReadInt32(output.Pointer);
                    else values[i] = new(Marshal.ReadInt32(output.Pointer), Marshal.ReadInt32(output.Pointer, 4), Marshal.ReadInt32(output.Pointer, 8));
                }
                return new(receiver, values[0], values[1], values[2], ease);
            }
            NativeLineProducerControlSnapshot Read()
            {
                Shape(); var first = ReadOnce(); var second = ReadOnce(); Shape();
                if (first != second) throw new InvalidOperationException("The complete producer control words changed between observations.");
                return second;
            }
            Shape();
            return action(receiver, fields, Shape, Read);
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    internal NativeLineProducerControlSnapshot ReadRootedLineProducerControl(RootHandle original, Action guard)
        => WithLineProducerControl(original, guard, (_, _, _, read) => read());

    internal NativeLineProducerControlWriteObservation WriteRootedLineProducerControlSlice(RootHandle original,
        NativeLineProducerControlSnapshot expected, NativeLineProducerPatch patch,
        Action guard, Action verifySource, Action beforeStore)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(verifySource);
        ArgumentNullException.ThrowIfNull(beforeStore); ValidateLineProducerPatch(patch);
        var entered = false; NativeLineProducerControlWriteObservation? completed = null;
        var target = PatchLineProducerControl(expected, patch);
        try
        {
            return WithLineProducerControl(original, guard, (receiver, fields, shape, read) =>
            {
                if (expected.Control != receiver) throw new InvalidOperationException("The expected producer state names another original control.");
                void RequireExpected()
                {
                    shape(); verifySource(); guard();
                    if (read() != expected) throw new InvalidOperationException("The complete producer control differs before selected storage.");
                }
                RequireExpected();
                var field = fields[(int)patch.Field]; var width = checked(patch.Components * 4);
                var bytes = new byte[width];
                for (var i = 0; i < patch.Components; ++i)
                    BitConverter.TryWriteBytes(bytes.AsSpan(i * 4, 4), ProducerComponent(patch.Value, patch.Component + i));
                var destination = IntPtr.Add(receiver, checked(field.Offset + patch.Component * 4));
                beforeStore(); guard(); RequireExpected();
                VerifyLineSetPositionsWritable(destination, checked((nuint)width));
                entered = true;
                Marshal.Copy(bytes, 0, destination, width);
                guard();
                var after = read();
                if (after != target) throw new InvalidOperationException("Producer storage returned another complete raw-word state.");
                return completed = new(expected, after, patch);
            });
        }
        catch (Exception error)
        {
            error.Data["LineProducerControlOriginalRoot"] = original;
            error.Data["LineProducerControlStoreEntered"] = entered;
            error.Data["LineProducerControlBefore"] = expected;
            error.Data["LineProducerControlTarget"] = target;
            error.Data["LineProducerControlPatch"] = patch;
            if (completed is not null) error.Data["LineProducerControlWriteObservation"] = completed;
            throw;
        }
    }
}
