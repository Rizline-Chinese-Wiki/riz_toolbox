using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed record NativeCachedSingleField(IntPtr Field, IntPtr Type, IntPtr DeclaringClass,
    int Offset, string TypeName, IntPtr FieldClass);

/// <summary>An immutable observation of Unity's closed cached float invokable.
/// ArgumentBits preserves every IEEE-754 payload, including NaN and signed zero.
/// ActionInvokeMethod is signature evidence only; this class never invokes it.</summary>
internal sealed record NativeCachedSingleInvokable(IntPtr Object, IntPtr ActualClass,
    IntPtr InvokableParentClass, IntPtr ActionClass, NativeCachedSingleField DelegateField,
    NativeCachedSingleField ArgumentField, IntPtr ArgumentClass, IntPtr Delegate, int ArgumentBits,
    IntPtr ActionInvokeMethod)
{
    internal float Argument => BitConverter.Int32BitsToSingle(ArgumentBits);
}

internal sealed partial class NativeRuntime
{
    private const string CachedSingleType = "UnityEngine.Events.CachedInvokableCall<System.Single>";
    private const string SingleInvokableType = "UnityEngine.Events.InvokableCall<System.Single>";
    private const string SingleActionType = "UnityEngine.Events.UnityAction<System.Single>";

    /// <summary>Projects the already-existing closed Unity cached float invokable
    /// through official metadata and field_get_value. It allocates no generic
    /// reflection object, roots no reference and calls no Unity or managed code.</summary>
    internal NativeCachedSingleInvokable InspectRootedCachedSingleInvokable(RootHandle actualInvokable)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(actualInvokable);
        var value = actualInvokable.Target;
        HookTargetValidation.VerifyReadable(value, checked((nuint)(2 * IntPtr.Size)));
        var actual = _objectClass(value);
        if (actual == IntPtr.Zero || Marshal.ReadIntPtr(value) != actual)
            throw new UnsupportedSignatureException("The rooted cached float invokable has inconsistent actual class metadata.");

        var classImage = CachedSingleExport<Ptr1>("il2cpp_class_get_image");
        var fieldParent = CachedSingleExport<Ptr1>("il2cpp_field_get_parent");
        var generic = CachedSingleExport<Predicate>("il2cpp_class_is_generic");
        var inflated = CachedSingleExport<Predicate>("il2cpp_class_is_inflated");
        var objectSize = CachedSingleExport<ArraySize>("il2cpp_object_get_size");
        var baseClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "BaseInvokableCall");
        var single = Class("mscorlib.dll", "System", "Single");
        var multicast = Class("mscorlib.dll", "System", "MulticastDelegate");
        var delegateClass = Class("mscorlib.dll", "System", "Delegate");
        var voidClass = Class("mscorlib.dll", "System", "Void");
        var unityImage = classImage(baseClass); var systemImage = classImage(single);
        if (unityImage == IntPtr.Zero || systemImage == IntPtr.Zero || unityImage == systemImage)
            throw new UnsupportedSignatureException("The cached float listener requires distinct official Unity and System images.");

        void ClosedClass(IntPtr cls, string expectedName, string metadataName)
        {
            if (cls == IntPtr.Zero) throw new UnsupportedSignatureException("The cached Single closed class is absent.");
            HookTargetValidation.VerifyReadable(cls, 16);
            var type = _classType(cls);
            if (type == IntPtr.Zero || _typeClass(type) != cls || _byRef(type) || _typeCode(type) != 21
                || _isValueType(cls) || generic(cls) || !inflated(cls) || classImage(cls) != unityImage
                || Utf8(_classNamespace(cls)) != "UnityEngine.Events" || Utf8(_className(cls)) != metadataName
                || MetadataTypeName(type) != expectedName)
                throw new UnsupportedSignatureException("The actual listener differs from the required existing closed Single class.");
        }
        void SingleClass()
        {
            var type = _classType(single);
            if (type == IntPtr.Zero || _typeClass(type) != single || _byRef(type) || _typeCode(type) != 12
                || !_isValueType(single) || _isEnum(single) || _valueSize(single, out _) != sizeof(float)
                || MetadataTypeName(type) != "System.Single" || classImage(single) != systemImage)
                throw new UnsupportedSignatureException("The cached argument requires the official four-byte System.Single class.");
        }

        ClosedClass(actual, CachedSingleType, "CachedInvokableCall`1");
        var parent = _classParent(actual); ClosedClass(parent, SingleInvokableType, "InvokableCall`1");
        if (_classParent(parent) != baseClass || !_assignable(baseClass, actual))
            throw new UnsupportedSignatureException("The cached Single invokable has a different direct parent chain.");
        SingleClass();
        var argument = CachedSingleDeclaredField(actual, actual, "m_Arg1", "System.Single", fieldParent);
        var actionField = CachedSingleDeclaredField(parent, actual, "Delegate", SingleActionType, fieldParent);
        var action = _typeClass(actionField.Type); ClosedClass(action, SingleActionType, "UnityAction`1");
        if (_classParent(action) != multicast || _classParent(multicast) != delegateClass
            || classImage(multicast) != systemImage || classImage(delegateClass) != systemImage)
            throw new UnsupportedSignatureException("The cached Single action has a different official delegate parent chain.");
        if (_typeClass(argument.Type) != single
            || Describe(argument.Type) is not { Kind: NativeValueKind.Single, Size: 4, ByRef: false }
            || Describe(actionField.Type) is not { Kind: NativeValueKind.Reference, Size: 8, ByRef: false })
            throw new UnsupportedSignatureException("Cached Single argument/delegate fields differ from their exact native storage.");
        var invoke = CachedSingleActionInvoke(action, single, voidClass);
        var bytes = objectSize(value);
        if (bytes < 2 * IntPtr.Size || bytes > 4096 || actionField.Offset < 2 * IntPtr.Size
            || (long)actionField.Offset + IntPtr.Size > bytes || argument.Offset < actionField.Offset + IntPtr.Size
            || (long)argument.Offset + sizeof(float) > bytes)
            throw new UnsupportedSignatureException("The exported cached Single fields exceed their bounded actual object storage.");

        var fixedArgumentBits = ReadSingleBitsField(value, argument.Field);
        var actualDelegate = ReadField<IntPtr>(value, actionField.Field, false);

        ClosedClass(actual, CachedSingleType, "CachedInvokableCall`1");
        ClosedClass(parent, SingleInvokableType, "InvokableCall`1");
        ClosedClass(action, SingleActionType, "UnityAction`1"); SingleClass();
        if (actualInvokable.Target != value || _objectClass(value) != actual || Marshal.ReadIntPtr(value) != actual
            || _classParent(actual) != parent || _classParent(parent) != baseClass
            || _classParent(action) != multicast || _classParent(multicast) != delegateClass
            || classImage(baseClass) != unityImage || classImage(multicast) != systemImage || classImage(delegateClass) != systemImage
            || objectSize(value) != bytes
            || CachedSingleDeclaredField(actual, actual, "m_Arg1", "System.Single", fieldParent) != argument
            || CachedSingleDeclaredField(parent, actual, "Delegate", SingleActionType, fieldParent) != actionField
            || CachedSingleActionInvoke(action, single, voidClass) != invoke
            || ReadSingleBitsField(value, argument.Field) != fixedArgumentBits
            || ReadField<IntPtr>(value, actionField.Field, false) != actualDelegate)
            throw new UnsupportedSignatureException("The rooted cached Single listener changed during its observation.");
        return new(value, actual, parent, action, actionField, argument, single, actualDelegate,
            fixedArgumentBits, invoke);
    }

    private int ReadSingleBitsField(IntPtr owner, IntPtr field)
    {
        VerifyAccess();
        RequireObject(owner);
        if ((_fieldFlags(field) & 0x10u) != 0)
            throw new UnsupportedSignatureException("Cached Single argument field must be an instance field.");
        if (Describe(_fieldType(field)) is not { Kind: NativeValueKind.Single, Size: 4, ByRef: false })
            throw new UnsupportedSignatureException("Cached Single argument field must be four-byte non-byref System.Single.");
        using var output = new Arg(sizeof(int));
        _fieldGet(owner, field, output.Pointer);
        return Marshal.ReadInt32(output.Pointer);
    }

    internal IntPtr PublishCachedSingleDelegateOnce(RootHandle actualInvokable,
        NativeCachedSingleInvokable expected, Action<IntPtr> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(actualInvokable);
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(publish);
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = actualInvokable.Target;
            if (value != expected.Object)
                throw new UnsupportedSignatureException("The rooted cached Single object identity changed.");
            HookTargetValidation.VerifyReadable(value, checked((nuint)(2 * IntPtr.Size)));
            if (Marshal.ReadIntPtr(value) != expected.ActualClass || _objectClass(value) != expected.ActualClass)
                throw new UnsupportedSignatureException("The rooted cached Single object class changed.");
            var delegatePointer = ReadField<IntPtr>(expected.Object, expected.DelegateField.Field, false);
            call.BeginPublication(delegatePointer);
            try { publish(delegatePointer); call.PublicationAccepted(); }
            catch { call.PublicationFailed(); throw; }
            if (delegatePointer != expected.Delegate || actualInvokable.Target != value)
                throw new UnsupportedSignatureException("The published cached Single Delegate differs from its earlier rooted observation.");
            return delegatePointer;
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    /// <summary>Publishes the official Delegate field before any post-result
    /// inspection. The callback must retain the returned pointer immediately.</summary>
    internal IntPtr ReadCachedSingleDelegateWithPublication(RootHandle actualInvokable,
        NativeCachedSingleInvokable expected, Action<IntPtr> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(publish);
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            if (InspectRootedCachedSingleInvokable(actualInvokable) != expected)
                throw new UnsupportedSignatureException("The cached Single field publication differs from its rooted observation.");
            var value = ReadField<IntPtr>(expected.Object, expected.DelegateField.Field, false);
            call.BeginPublication(value);
            try { publish(value); call.PublicationAccepted(); }
            catch { call.PublicationFailed(); throw; }
            if (value != expected.Delegate)
                throw new UnsupportedSignatureException("The published cached Single Delegate differs from its earlier rooted observation.");
            return value;
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private NativeCachedSingleField CachedSingleDeclaredField(IntPtr declaringClass, IntPtr actualClass,
        string name, string expectedType, Ptr1 fieldParent)
    {
        IntPtr found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var count = 0; count < 256; ++count)
            {
                var field = _fields(declaringClass, iterator.Pointer);
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (fieldParent(field) != declaringClass)
                    throw new UnsupportedSignatureException("A cached Single field has a different official declaring class.");
                if (Utf8(_fieldName(field)) != name) continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("Cached Single metadata contains duplicate named fields.");
                found = field;
            }
        if (!terminated || found == IntPtr.Zero || Field(actualClass, name) != found)
            throw new UnsupportedSignatureException("The cached Single field is absent, shadowed or exceeds its declared metadata bound.");
        var type = _fieldType(found);
        if (type == IntPtr.Zero || _byRef(type) || (_fieldFlags(found) & (0x10u | 0x40u)) != 0
            || MetadataTypeName(type) != expectedType)
            throw new UnsupportedSignatureException("The cached Single field differs from its exact non-byref instance type.");
        var cls = _typeClass(type);
        if (cls == IntPtr.Zero || _typeClass(_classType(cls)) != cls || MetadataTypeName(_classType(cls)) != expectedType)
            throw new UnsupportedSignatureException("The cached Single field-to-class metadata round trip changed its exact type.");
        return new(found, type, declaringClass, _fieldOffset(found), expectedType, cls);
    }

    private IntPtr CachedSingleActionInvoke(IntPtr actionClass, IntPtr singleClass, IntPtr voidClass)
    {
        IntPtr found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var count = 0; count < 256; ++count)
            {
                var method = _methods(actionClass, iterator.Pointer);
                if (method == IntPtr.Zero) { terminated = true; break; }
                if (_methodClass(method) != actionClass)
                    throw new UnsupportedSignatureException("A Single action method has a different official declaring class.");
                if (Utf8(_methodName(method)) != "Invoke") continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("The Single action has ambiguous declared Invoke metadata.");
                var returns = _returnType(method);
                if ((_methodFlags(method, out _) & (0x10u | 0x400u)) != 0 || _isGenericMethod(method)
                    || _parameterCount(method) != 1 || returns == IntPtr.Zero || _byRef(returns)
                    || _typeCode(returns) != 1 || _typeClass(returns) != voidClass || MetadataTypeName(returns) != "System.Void")
                    throw new UnsupportedSignatureException("The closed Single action has an unsupported declared Invoke signature.");
                var parameter = _parameterType(method, 0);
                if (parameter == IntPtr.Zero || _byRef(parameter) || _typeCode(parameter) != 12
                    || _typeClass(parameter) != singleClass || MetadataTypeName(parameter) != "System.Single")
                    throw new UnsupportedSignatureException("The closed Single action Invoke parameter is not the official System.Single.");
                found = method;
            }
        if (!terminated || found == IntPtr.Zero)
            throw new UnsupportedSignatureException("The closed Single action has no bounded official declared Invoke method.");
        return found;
    }

    private T CachedSingleExport<T>(string name) where T : Delegate
    {
        try { return Api<T>(name); }
        catch (EntryPointNotFoundException error)
        { throw new UnsupportedSignatureException("Cached Single observation requires official " + name + ": " + error.Message); }
    }
}
