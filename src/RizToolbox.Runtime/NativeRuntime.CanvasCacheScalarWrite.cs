using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Conditional official field store for the two exact cache
    /// scalars. Resolve all metadata, roots and exports before fresh raw-bit
    /// observations; no native getter follows the final attempt recorder.</summary>
    internal void WriteCanvasCacheScalar(RootHandle owner, NativeExactReadField field,
        int expectedBits, int bits, Action guard, Action beforeDispatch, Action verifySource)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard); guard();
        ArgumentNullException.ThrowIfNull(beforeDispatch); ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(verifySource);
        if (field.Name is not ("nowTime" or "speed"))
            throw new UnsupportedSignatureException("Only the two exact Canvas cache scalar fields may be conditionally stored.");
        T Observe<T>(Func<T> operation) { guard(); var value = operation(); guard(); return value; }
        using var argument = Arg.Value(BitConverter.Int32BitsToSingle(bits));
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var receiver = Observe(() => owner.Target);
            var root = Observe(() => call.Root(() => Root(receiver)));
            if (Observe(() => root.Target) != receiver || Observe(() => _objectClass(receiver)) != field.DeclaringClass)
                throw new UnsupportedSignatureException("The actual Canvas cache scalar owner changed its exact class or identity.");
            var setter = _writeFieldValue ??= OptionalFieldWriteExport<FieldSetValue>("il2cpp_field_set_value");
            verifySource(); guard();
            var current = Observe(() => InspectCanvasCacheField(field.DeclaringClass, field.Name));
            if (current != field || current.TypeName != "System.Single" || current.Size != sizeof(float))
                throw new UnsupportedSignatureException("The saved/current actual Canvas scalar descriptor changed before storage.");
            using var output = new Arg(sizeof(int));
            int Read()
            {
                guard(); _fieldGet(receiver, current.Field, output.Pointer); guard();
                return Marshal.ReadInt32(output.Pointer);
            }
            for (var i = 0; i < 2; ++i)
            {
                var observed = Read();
                if (observed != expectedBits)
                    throw NativeCanvasCacheConflict.Error(receiver.ToInt64(), -1, current.Name, expectedBits, observed);
            }
            guard(); beforeDispatch(); guard();
            setter(receiver, current.Field, argument.Pointer);
            guard();
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
}
