using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed record NativeCachedBooleanField(IntPtr Field, IntPtr Type, IntPtr DeclaringClass,
    int Offset, string TypeName, IntPtr FieldClass);

/// <summary>An immutable observation, not a callable listener or an owned delegate.
/// The caller retains Object's root and independently adopts Delegate before retaining
/// that reference. ActionInvokeMethod is signature evidence only; no entry is resolved.</summary>
internal sealed record NativeCachedBooleanInvokable(IntPtr Object, IntPtr ActualClass,
    IntPtr InvokableParentClass, IntPtr ActionClass, NativeCachedBooleanField DelegateField,
    NativeCachedBooleanField ArgumentField, IntPtr ArgumentClass, IntPtr Delegate, bool Argument,
    IntPtr ActionInvokeMethod);

internal sealed partial class NativeRuntime
{
    private const string CachedBooleanType = "UnityEngine.Events.CachedInvokableCall<System.Boolean>";
    private const string BooleanInvokableType = "UnityEngine.Events.InvokableCall<System.Boolean>";
    private const string BooleanActionType = "UnityEngine.Events.UnityAction<System.Boolean>";

    /// <summary>Reads the existing closed Boolean cached invokable through official
    /// metadata and field_get_value. It constructs no generic type/delegate, invokes no
    /// managed method and creates/frees no GC handle. Generic dump offsets and shared
    /// placeholder RVAs are deliberately not part of this observation path.</summary>
    internal NativeCachedBooleanInvokable InspectRootedCachedBooleanInvokable(RootHandle actualInvokable)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(actualInvokable);
        var value = actualInvokable.Target;
        HookTargetValidation.VerifyReadable(value, checked((nuint)(2 * IntPtr.Size)));
        var actual = _objectClass(value);
        if (actual == IntPtr.Zero || Marshal.ReadIntPtr(value) != actual)
            throw new UnsupportedSignatureException("The rooted cached invokable has inconsistent actual class metadata.");

        var classImage = CachedBooleanExport<Ptr1>("il2cpp_class_get_image");
        var fieldParent = CachedBooleanExport<Ptr1>("il2cpp_field_get_parent");
        var generic = CachedBooleanExport<Predicate>("il2cpp_class_is_generic");
        var inflated = CachedBooleanExport<Predicate>("il2cpp_class_is_inflated");
        var objectSize = CachedBooleanExport<ArraySize>("il2cpp_object_get_size");
        var baseClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "BaseInvokableCall");
        var boolean = Class("mscorlib.dll", "System", "Boolean");
        var multicast = Class("mscorlib.dll", "System", "MulticastDelegate");
        var delegateClass = Class("mscorlib.dll", "System", "Delegate");
        var voidClass = Class("mscorlib.dll", "System", "Void");
        var unityImage = classImage(baseClass); var systemImage = classImage(boolean);
        if (unityImage == IntPtr.Zero || systemImage == IntPtr.Zero || unityImage == systemImage)
            throw new UnsupportedSignatureException("The cached listener requires distinct official Unity and System images.");

        void ClosedClass(IntPtr cls, string expectedName, string metadataName)
        {
            if (cls == IntPtr.Zero) throw new UnsupportedSignatureException("The cached Boolean closed class is absent.");
            HookTargetValidation.VerifyReadable(cls, 16);
            var type = _classType(cls);
            if (type == IntPtr.Zero || _typeClass(type) != cls || _byRef(type) || _typeCode(type) != 21
                || _isValueType(cls) || generic(cls) || !inflated(cls) || classImage(cls) != unityImage
                || Utf8(_classNamespace(cls)) != "UnityEngine.Events" || Utf8(_className(cls)) != metadataName
                || MetadataTypeName(type) != expectedName)
                throw new UnsupportedSignatureException("The actual listener differs from the required existing closed Boolean class.");
        }
        void BooleanClass()
        {
            var type = _classType(boolean);
            if (type == IntPtr.Zero || _typeClass(type) != boolean || _byRef(type) || _typeCode(type) != 2
                || !_isValueType(boolean) || _isEnum(boolean) || _valueSize(boolean, out _) != 1
                || MetadataTypeName(type) != "System.Boolean" || classImage(boolean) != systemImage)
                throw new UnsupportedSignatureException("The cached argument requires the official one-byte System.Boolean class.");
        }

        ClosedClass(actual, CachedBooleanType, "CachedInvokableCall`1");
        var parent = _classParent(actual); ClosedClass(parent, BooleanInvokableType, "InvokableCall`1");
        if (_classParent(parent) != baseClass || !_assignable(baseClass, actual))
            throw new UnsupportedSignatureException("The cached Boolean invokable has a different direct parent chain.");
        BooleanClass();
        var argument = CachedBooleanDeclaredField(actual, actual, "m_Arg1", "System.Boolean", fieldParent);
        var actionField = CachedBooleanDeclaredField(parent, actual, "Delegate", BooleanActionType, fieldParent);
        var action = _typeClass(actionField.Type); ClosedClass(action, BooleanActionType, "UnityAction`1");
        if (_classParent(action) != multicast || _classParent(multicast) != delegateClass
            || classImage(multicast) != systemImage || classImage(delegateClass) != systemImage)
            throw new UnsupportedSignatureException("The cached Boolean action has a different official delegate parent chain.");
        if (_typeClass(argument.Type) != boolean || _typeCode(argument.Type) != 2
            || Describe(argument.Type) is not { Kind: NativeValueKind.Boolean, Size: 1, ByRef: false }
            || Describe(actionField.Type) is not { Kind: NativeValueKind.Reference, Size: 8, ByRef: false })
            throw new UnsupportedSignatureException("Cached Boolean argument/delegate fields differ from their exact native storage.");
        var invoke = CachedBooleanActionInvoke(action, boolean, voidClass);
        var bytes = objectSize(value);
        if (bytes < 2 * IntPtr.Size || bytes > 4096 || actionField.Offset < 2 * IntPtr.Size
            || (long)actionField.Offset + IntPtr.Size > bytes || argument.Offset < actionField.Offset + IntPtr.Size
            || (long)argument.Offset + 1 > bytes)
            throw new UnsupportedSignatureException("The exported cached Boolean fields exceed their bounded actual object storage.");

        // FieldInfo, rather than a generic dump's zero offset, controls these reads.
        // ReadField validates the exact one-byte Boolean output buffer before entry.
        var fixedArgument = ReadField<bool>(value, argument.Field, false);
        var actualDelegate = ReadField<IntPtr>(value, actionField.Field, false);

        // These immutable values are only a snapshot. The caller separately roots
        // the delegate and rechecks its complete event graph before accepting it.
        ClosedClass(actual, CachedBooleanType, "CachedInvokableCall`1");
        ClosedClass(parent, BooleanInvokableType, "InvokableCall`1");
        ClosedClass(action, BooleanActionType, "UnityAction`1"); BooleanClass();
        if (actualInvokable.Target != value || _objectClass(value) != actual || Marshal.ReadIntPtr(value) != actual
            || _classParent(actual) != parent || _classParent(parent) != baseClass
            || _classParent(action) != multicast || _classParent(multicast) != delegateClass
            || classImage(baseClass) != unityImage || classImage(multicast) != systemImage || classImage(delegateClass) != systemImage
            || objectSize(value) != bytes
            || CachedBooleanDeclaredField(actual, actual, "m_Arg1", "System.Boolean", fieldParent) != argument
            || CachedBooleanDeclaredField(parent, actual, "Delegate", BooleanActionType, fieldParent) != actionField
            || CachedBooleanActionInvoke(action, boolean, voidClass) != invoke
            || ReadField<bool>(value, argument.Field, false) != fixedArgument
            || ReadField<IntPtr>(value, actionField.Field, false) != actualDelegate)
            throw new UnsupportedSignatureException("The rooted cached Boolean listener changed during its observation.");
        return new(value, actual, parent, action, actionField, argument, boolean, actualDelegate, fixedArgument, invoke);
    }

    /// <summary>Publishes the actual officially verified Delegate field before any
    /// post-read inspection. The publisher immediately records its independent root;
    /// a failed publication retains responsibility just like reference invocation.
    /// An earlier projection is descriptive data, so revalidate it before reading.</summary>
    internal IntPtr ReadCachedBooleanDelegateWithPublication(RootHandle actualInvokable,
        NativeCachedBooleanInvokable expected, Action<IntPtr> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(publish);
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            if (InspectRootedCachedBooleanInvokable(actualInvokable) != expected)
                throw new UnsupportedSignatureException("The cached Boolean field publication differs from its actual rooted observation.");
            var value = ReadField<IntPtr>(expected.Object, expected.DelegateField.Field, false);
            call.BeginPublication(value);
            try { publish(value); call.PublicationAccepted(); }
            catch { call.PublicationFailed(); throw; }
            // The independently rooted actual result remains caller-owned even
            // when a writer changed this reference after the pre-read snapshot.
            if (value != expected.Delegate)
                throw new UnsupportedSignatureException("The actual published cached Delegate differs from its earlier rooted observation.");
            return value;
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private NativeCachedBooleanField CachedBooleanDeclaredField(IntPtr declaringClass, IntPtr actualClass,
        string name, string expectedType, Ptr1 fieldParent)
    {
        IntPtr found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var count = 0; count < 256; ++count)
            {
                var field = _fields(declaringClass, iterator.Pointer);
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (fieldParent(field) != declaringClass)
                    throw new UnsupportedSignatureException("A cached listener field has a different official declaring class.");
                if (Utf8(_fieldName(field)) != name) continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("Cached listener metadata contains duplicate named fields.");
                found = field;
            }
        if (!terminated || found == IntPtr.Zero || Field(actualClass, name) != found)
            throw new UnsupportedSignatureException("The cached listener field is absent, shadowed or exceeds its declared metadata bound.");
        var type = _fieldType(found);
        if (type == IntPtr.Zero || _byRef(type) || (_fieldFlags(found) & (0x10u | 0x40u)) != 0
            || MetadataTypeName(type) != expectedType)
            throw new UnsupportedSignatureException("The cached listener field differs from its exact non-byref instance type.");
        var cls = _typeClass(type);
        if (cls == IntPtr.Zero || _typeClass(_classType(cls)) != cls || MetadataTypeName(_classType(cls)) != expectedType)
            throw new UnsupportedSignatureException("The cached listener field-to-class metadata round trip changed its exact type.");
        return new(found, type, declaringClass, _fieldOffset(found), expectedType, cls);
    }

    private IntPtr CachedBooleanActionInvoke(IntPtr actionClass, IntPtr booleanClass, IntPtr voidClass)
    {
        IntPtr found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var count = 0; count < 256; ++count)
            {
                var method = _methods(actionClass, iterator.Pointer);
                if (method == IntPtr.Zero) { terminated = true; break; }
                if (_methodClass(method) != actionClass)
                    throw new UnsupportedSignatureException("A Boolean action method has a different official declaring class.");
                if (Utf8(_methodName(method)) != "Invoke") continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("The Boolean action has ambiguous declared Invoke metadata.");
                var returns = _returnType(method);
                if ((_methodFlags(method, out _) & (0x10u | 0x400u)) != 0 || _isGenericMethod(method)
                    || _parameterCount(method) != 1 || returns == IntPtr.Zero || _byRef(returns)
                    || _typeCode(returns) != 1 || _typeClass(returns) != voidClass || MetadataTypeName(returns) != "System.Void")
                    throw new UnsupportedSignatureException("The closed Boolean action has an unsupported declared Invoke signature.");
                var parameter = _parameterType(method, 0);
                if (parameter == IntPtr.Zero || _byRef(parameter) || _typeCode(parameter) != 2
                    || _typeClass(parameter) != booleanClass || MetadataTypeName(parameter) != "System.Boolean")
                    throw new UnsupportedSignatureException("The closed Boolean action Invoke parameter is not the official System.Boolean.");
                // An inflated method on this closed delegate class is expected.
                // Its native entry can be shared/placeholder: never resolve or use it.
                found = method;
            }
        if (!terminated || found == IntPtr.Zero)
            throw new UnsupportedSignatureException("The closed Boolean action has no bounded official declared Invoke method.");
        return found;
    }

    private T CachedBooleanExport<T>(string name) where T : Delegate
    {
        try { return Api<T>(name); }
        catch (EntryPointNotFoundException error)
        { throw new UnsupportedSignatureException("Cached Boolean observation requires official " + name + ": " + error.Message); }
    }
}
