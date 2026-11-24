using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Selected inverse of one saved field through official GC-aware
    /// field exports. It does not replay SetPositions or undo a Unity consumer.</summary>
    internal void WriteLineRendererSavedField(RootHandle owner, NativeExactReadField field,
        long expected, long target, RootHandle? expectedReference, RootHandle? targetReference,
        Action guard, Action verifySource, Action beforeDispatch)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(guard); ArgumentNullException.ThrowIfNull(verifySource);
        ArgumentNullException.ThrowIfNull(beforeDispatch);
        var reference = field.Name == "_positionsPlan";
        if (!reference && field.Name != "_positionCountPlan")
            throw new UnsupportedSignatureException("Only the two exact LineInformation saved fields permit selected restoration.");
        if (!reference && (expected < int.MinValue || expected > int.MaxValue
            || target < int.MinValue || target > int.MaxValue || expectedReference is not null || targetReference is not null))
            throw new UnsupportedSignatureException("The saved count inverse requires exact Int32 values.");
        var entered = false;
        var argument = reference ? Arg.Ref(new IntPtr(target)) : Arg.Value((int)target);
        NativePublicationCall call;
        try { call = new NativePublicationCall(VerifyAccess, new[] { argument }); }
        catch { argument.Dispose(); throw; }
        try
        {
            try
            {
                void G()
                {
                    VerifyAccess(); owner.VerifyOwnerThread(); expectedReference?.VerifyOwnerThread();
                    targetReference?.VerifyOwnerThread(); guard();
                }
                T O<T>(Func<T> read) { G(); var value = read(); G(); return value; }
                var receiver = O(() => owner.Target);
                RootHandle Pin(IntPtr value) => call.Root(() =>
                {
                    G(); VerifyLineSetPositionsReadable(value, 16);
                    var id = _newHandle(value, true);
                    return new RootHandle(id, _freeHandle, _handleTarget, VerifyAccess);
                });
                var receiverPin = Pin(receiver); G();
                RootHandle? oldPin = null, nextPin = null;
                if (reference)
                {
                    if ((expected == 0) != (expectedReference is null) || (target == 0) != (targetReference is null))
                        throw new UnsupportedSignatureException("A saved array inverse requires the exact original retained roots, including null.");
                    if (expectedReference is not null)
                    {
                        if (O(() => expectedReference.Target).ToInt64() != expected)
                            throw new UnsupportedSignatureException("The retained before-array root names another saved array.");
                        oldPin = Pin(new IntPtr(expected)); G();
                    }
                    if (targetReference is not null)
                    {
                        if (O(() => targetReference.Target).ToInt64() != target)
                            throw new UnsupportedSignatureException("The retained target-array root names another original saved array.");
                        nextPin = Pin(new IntPtr(target)); G();
                    }
                }
                var classes = InspectLineRendererClasses(G);
                if (field.DeclaringClass != classes.Line || (field.Flags & 0x20u) != 0)
                    throw new UnsupportedSignatureException("The saved-field inverse is not a mutable exact LineInformation declaration.");
                var setObject = reference ? O(() => ReadGraphExport<FieldSetObject>("il2cpp_field_set_value_object")) : null;
                var setValue = reference ? null : O(() => ReadGraphExport<FieldSetValue>("il2cpp_field_set_value"));
                using var output = new Arg(field.Size);
                void Shape()
                {
                    if (O(() => receiverPin.Target) != receiver || O(() => owner.Target) != receiver
                        || LineRendererFieldOwner(owner, field, G) != receiver)
                        throw new UnsupportedSignatureException("The original saved-field receiver changed.");
                    void ArrayRoot(RootHandle? source, RootHandle? pin, long address)
                    {
                        if (source is null) return;
                        if (O(() => source.Target).ToInt64() != address || O(() => pin!.Target).ToInt64() != address
                            || O(() => { VerifyLineSetPositionsReadable(new IntPtr(address), 16); return _objectClass(new IntPtr(address)); }) != field.FieldClass)
                            throw new UnsupportedSignatureException("The original saved-array root or exact class changed.");
                    }
                    ArrayRoot(expectedReference, oldPin, expected);
                    ArrayRoot(targetReference, nextPin, target);
                }
                long Read()
                {
                    G(); VerifyLineSetPositionsReadable(receiver, checked((nuint)(field.Offset + field.Size)));
                    _fieldGet(receiver, field.Field, output.Pointer); G();
                    return reference ? Marshal.ReadIntPtr(output.Pointer).ToInt64() : Marshal.ReadInt32(output.Pointer);
                }
                void RequireExpected()
                {
                    Shape(); verifySource(); G();
                    if (Read() != expected || Read() != expected)
                        throw new InvalidOperationException("The saved field differs from its selected expected value; no inverse is admitted.");
                }
                RequireExpected();
                beforeDispatch(); G();
                RequireExpected();
                VerifyLineSetPositionsWritable(IntPtr.Add(receiver, field.Offset), checked((nuint)field.Size));
                entered = true;
                if (reference) setObject!(receiver, field.Field, new IntPtr(target));
                else setValue!(receiver, field.Field, argument.Pointer);
                G();
            }
            catch (Exception error) { call.OperationFailed(error); throw; }
            finally { call.Dispose(); }
        }
        catch (Exception error)
        {
            error.Data["LineRendererSavedFieldStoreEntered"] = entered;
            error.Data["LineRendererSavedFieldOwner"] = owner;
            error.Data["LineRendererSavedFieldName"] = field.Name;
            error.Data["LineRendererSavedFieldExpected"] = expected;
            error.Data["LineRendererSavedFieldTarget"] = target;
            if (targetReference is not null) error.Data["LineRendererSavedFieldTargetRoot"] = targetReference;
            throw;
        }
    }
}
