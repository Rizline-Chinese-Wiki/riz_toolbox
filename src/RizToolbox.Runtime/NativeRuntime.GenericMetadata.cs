namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Requests only a closed reflection type. Allocates BCL Type/Array objects, never a resource wrapper or Unity object.</summary>
    internal NativeMetadataClass ReflectSingleReferenceGenericClass(IntPtr definitionClass, IntPtr argumentClass,
        string expectedClosedType, IntPtr expectedContract = default)
    {
        VerifyAccess();
        if (definitionClass == IntPtr.Zero || argumentClass == IntPtr.Zero || string.IsNullOrWhiteSpace(expectedClosedType)
            || _isValueType(definitionClass) || _isValueType(argumentClass))
            throw new UnsupportedSignatureException("Closed reference metadata requires non-null reference classes and an exact expected type.");
        // Resolve the optional official conversion before allocating any reflection objects.
        var fromReflection = Api<Ptr1>("il2cpp_class_from_system_type");
        var systemType = Class("mscorlib.dll", "System", "Type");
        var systemArray = Class("mscorlib.dll", "System", "Array");
        using var definition = Root(Type(definitionClass));
        using var argument = Root(Type(argumentClass));
        void RequireTypeObject(RootHandle value)
        {
            if (!_assignable(systemType, _objectClass(value.Target)))
                throw new UnsupportedSignatureException("Reflection metadata did not produce a System.Type object.");
        }
        RequireTypeObject(definition); RequireTypeObject(argument);
        if (fromReflection(definition.Target) != definitionClass || fromReflection(argument.Target) != argumentClass)
            throw new UnsupportedSignatureException("Input reflection types do not round-trip to their exact metadata classes.");
        IntPtr Resolve(IntPtr cls, string name, bool isStatic, string returns, params string[] parameters)
        {
            var method = Method(cls, name, parameters.Length, isStatic, parameters);
            if (DescribeReturnType(method) != returns)
                throw new UnsupportedSignatureException("Reflection method return changed: " + name);
            return method;
        }
        var reflectionClass = _objectClass(definition.Target);
        var isDefinition = Resolve(reflectionClass, "get_IsGenericTypeDefinition", false, "System.Boolean");
        var containsParameters = Resolve(reflectionClass, "get_ContainsGenericParameters", false, "System.Boolean");
        var genericArguments = Resolve(reflectionClass, "GetGenericArguments", false, "System.Type[]");
        var genericDefinition = Resolve(reflectionClass, "GetGenericTypeDefinition", false, "System.Type");
        var makeGeneric = Resolve(reflectionClass, "MakeGenericType", false, "System.Type", "System.Type[]");
        var createArray = Resolve(systemArray, "CreateInstance", true, "System.Array", "System.Type", "System.Int32");
        if (!Unbox<bool>(Invoke(isDefinition, definition.Target)))
            throw new UnsupportedSignatureException("The requested class is not a generic type definition.");
        using var openArguments = Root(Invoke(genericArguments, definition.Target));
        if (ValidatedArrayLength(openArguments.Target) != 1)
            throw new UnsupportedSignatureException("Only a single generic type argument is supported by this metadata path.");
        using var arrayElementType = Root(Type(systemType));
        using var arguments = Root(Invoke(createArray, IntPtr.Zero, Arg.Ref(arrayElementType.Target), Arg.Value(1)));
        if (InspectMetadataClass(_objectClass(arguments.Target)).TypeName != "System.Type[]" || ValidatedArrayLength(arguments.Target) != 1)
            throw new UnsupportedSignatureException("Reflection argument storage differs from an exact one-element System.Type array.");
        var setValue = Resolve(_objectClass(arguments.Target), "SetValue", false, "System.Void", "System.Object", "System.Int32");
        Invoke(setValue, arguments.Target, Arg.Ref(argument.Target), Arg.Value(0));
        using var closed = Root(Invoke(makeGeneric, definition.Target, Arg.Ref(arguments.Target)));
        RequireTypeObject(closed);
        if (Unbox<bool>(Invoke(containsParameters, closed.Target)) || Unbox<bool>(Invoke(isDefinition, closed.Target)))
            throw new UnsupportedSignatureException("The reflected type still contains unbound generic parameters.");
        using var actualDefinition = Root(Invoke(genericDefinition, closed.Target));
        RequireTypeObject(actualDefinition);
        if (fromReflection(actualDefinition.Target) != definitionClass)
            throw new UnsupportedSignatureException("The closed type belongs to another generic definition.");
        using var actualArguments = Root(Invoke(genericArguments, closed.Target));
        if (ValidatedArrayLength(actualArguments.Target) != 1)
            throw new UnsupportedSignatureException("The closed type's argument count changed.");
        using var actualArgument = Root(CallTyped(actualArguments.Target, "GetValue", new[] { "System.Int32" }, Arg.Value(0)));
        RequireTypeObject(actualArgument);
        if (fromReflection(actualArgument.Target) != argumentClass)
            throw new UnsupportedSignatureException("The closed type has a different exact generic argument.");
        var result = InspectMetadataClass(fromReflection(closed.Target));
        if (result.TypeName != expectedClosedType || _isValueType(result.Class))
            throw new UnsupportedSignatureException("The closed class differs from the required exact reference type.");
        if (expectedContract != IntPtr.Zero && !_assignable(expectedContract, result.Class))
            throw new UnsupportedSignatureException("The closed class does not implement the exact required reference contract.");
        using var roundTrip = Root(Type(result.Class)); RequireTypeObject(roundTrip);
        if (fromReflection(roundTrip.Target) != result.Class)
            throw new UnsupportedSignatureException("Closed class/reflection metadata round trip changed its identity.");
        return result;
    }
}
