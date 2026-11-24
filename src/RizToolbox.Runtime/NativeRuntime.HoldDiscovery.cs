namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Publish the actual reflection Type produced from the canonical
    /// controller class before checking withdrawal after type_get_object.</summary>
    internal void PublishHoldControllerType(IntPtr controllerClass, Func<IntPtr, RootHandle, bool> publishRoot, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(publishRoot); ArgumentNullException.ThrowIfNull(guard); guard();
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var canonical = LineEndpointClass("Assembly-CSharp.dll", "", "NormalModeInLevelController", guard);
        if (controllerClass == IntPtr.Zero || controllerClass != canonical)
            throw new UnsupportedSignatureException("Hold discovery requires the canonical controller Type.");
        var type = O(() => _classType(canonical));
        if (type == IntPtr.Zero || O(() => _typeClass(type)) != canonical || O(() => _byRef(type))
            || O(() => _typeCode(type)) != 18 || O(() => _isValueType(canonical)))
            throw new UnsupportedSignatureException("The Hold controller declaration is not an exact reference type.");
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        var publishing = false; var accepted = false;
        try
        {
            guard(); var token = _typeObject(type);
            if (token == IntPtr.Zero) throw new InvalidDataException("The Hold controller Type object is absent.");
            call.BeginPublication(token); publishing = true;
            var temporary = call.Root(() => Root(token));
            if (publishRoot(token, temporary)) call.TransferRoot(temporary);
            call.PublicationAccepted(); accepted = true; guard();
        }
        catch (Exception error)
        {
            if (publishing && !accepted) call.PublicationFailed();
            call.OperationFailed(error); throw;
        }
        finally { call.Dispose(); }
    }

    /// <summary>Fresh declared canonical Resources.FindObjectsOfTypeAll(Type)
    /// returning an actual one-dimensional UnityEngine.Object[] reference.</summary>
    internal IntPtr InspectHoldDiscoveryMethod(IntPtr resourcesClass, IntPtr unityObjectClass, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard); guard();
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var resources = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Resources", guard);
        var unityObject = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object", guard);
        var systemType = LineEndpointClass("mscorlib.dll", "System", "Type", guard);
        if (resourcesClass == IntPtr.Zero || resourcesClass != resources || unityObjectClass != unityObject)
            throw new UnsupportedSignatureException("Hold discovery requires exact canonical Resources/Object declarations.");
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var index = 0; index < 4096; ++index)
            {
                var method = O(() => _methods(resources, iterator.Pointer));
                if (method == IntPtr.Zero) { terminated = true; break; }
                if (Utf8(O(() => _methodName(method))) != "FindObjectsOfTypeAll" || O(() => _parameterCount(method)) != 1) continue;
                uint ignored = 0; var flags = O(() => _methodFlags(method, out ignored));
                if ((flags & (0x10u | 0x400u)) != 0x10u) continue;
                var parameter = O(() => _parameterType(method, 0));
                if (parameter == IntPtr.Zero || O(() => _typeClass(parameter)) != systemType
                    || O(() => _byRef(parameter)) || O(() => _typeCode(parameter)) != 18
                    || O(() => _isValueType(systemType)) || LineRingTypeName(parameter, guard) != "System.Type") continue;
                if (found != IntPtr.Zero) throw new UnsupportedSignatureException("The exact Hold discovery method is ambiguous.");
                found = method;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _methodClass(found)) != resources)
            throw new UnsupportedSignatureException("The exact declared Hold discovery method is absent.");
        var generic = O(() => _isGenericMethod(found)); var inflated = O(() => _isInflatedMethod(found));
        if (generic || inflated) throw new UnsupportedSignatureException("Hold discovery does not invoke a generic overload.");
        var returns = O(() => _returnType(found));
        if (returns == IntPtr.Zero || O(() => _byRef(returns)) || O(() => _typeCode(returns)) != 29)
            throw new UnsupportedSignatureException("The Hold discovery method does not return a complete reference array.");
        var arrayClass = O(() => _typeClass(returns));
        VerifyReferenceArrayClass(arrayClass, unityObject, guard);
        guard(); return found;
    }

    /// <summary>Successful reference results are rooted and published before
    /// the post-native source guard. Exceptional native returns still check
    /// withdrawal before reading any exception-method metadata.</summary>
    internal void PublishHoldControllerCandidates(RootHandle controllerType, IntPtr resourcesClass,
        IntPtr unityObjectClass, Func<IntPtr, RootHandle, bool> publishRoot, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(controllerType);
        ArgumentNullException.ThrowIfNull(publishRoot); ArgumentNullException.ThrowIfNull(guard); guard();
        var method = InspectHoldDiscoveryMethod(resourcesClass, unityObjectClass, guard);
        var target = LineRingObserve(guard, () => controllerType.Target);
        var arguments = new[] { Arg.Ref(target) };
        var call = new NativePublicationCall(VerifyAccess, arguments);
        try
        {
            InvokeCore(method, IntPtr.Zero, token =>
            {
                if (token == IntPtr.Zero) throw new InvalidDataException("Hold discovery returned no actual candidate array.");
                var temporary = call.Root(() => Root(token));
                if (publishRoot(token, temporary)) call.TransferRoot(temporary);
            }, call, false, arguments, guard, guard, guard, publishBeforePostGuard: true);
            guard();
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    /// <summary>Copy the complete bounded array, including null/duplicate slots.
    /// Each non-null slot acquires a real temporary root before publication or
    /// a withdrawal check; the original array and adopted roots stay owned.</summary>
    internal NativeRootedReferenceArray ReadHoldControllerCandidates(RootHandle array, IntPtr arrayClass,
        IntPtr unityObjectClass, Func<int, IntPtr, RootHandle, bool> publishRoot, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(array); ArgumentNullException.ThrowIfNull(publishRoot);
        ArgumentNullException.ThrowIfNull(guard); guard();
        var canonical = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object", guard);
        if (unityObjectClass != canonical) throw new UnsupportedSignatureException("Hold discovery requires the canonical Unity element class.");
        var value = LineRingObserve(guard, () => array.Target);
        if (arrayClass == IntPtr.Zero || LineRingObserve(guard, () => _objectClass(value)) != arrayClass)
            throw new UnsupportedSignatureException("The Hold discovery result differs from the rooted array class.");
        // The declared return remains Object[]. A vector allocated for the
        // exact Type passed to FindObjectsOfTypeAll is also a valid result.
        var element = LineRingObserve(guard, () => ReadGraphExport<Ptr1>("il2cpp_class_get_element_class")(arrayClass));
        if (element != canonical && element != LineEndpointClass("Assembly-CSharp.dll", "", "NormalModeInLevelController", guard))
        {
            var type = element == IntPtr.Zero ? IntPtr.Zero : LineRingObserve(guard, () => _classType(element));
            var name = type == IntPtr.Zero ? "<unavailable>" : LineRingTypeName(type, guard);
            throw new UnsupportedSignatureException($"The Hold discovery array has element type {name}. Expected UnityEngine.Object or NormalModeInLevelController.");
        }
        return ReadRootedReferenceArrayCore(array, arrayClass, element, 1024,
            MaximumReadOnlyReferenceArrayLength, (index, token) =>
            {
                if (token == IntPtr.Zero) return;
                // A complete1024-slot scan must not consume the separate
                // publication call's129-root record capacity. Each slot owns
                // its one acquisition/transfer/cleanup to terminal first.
                var slot = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
                var accepted = false;
                try
                {
                    slot.BeginPublication(token);
                    var temporary = slot.Root(() => Root(token));
                    if (publishRoot(index, token, temporary)) slot.TransferRoot(temporary);
                    slot.PublicationAccepted(); accepted = true;
                }
                catch (Exception error)
                {
                    if (!accepted) slot.PublicationFailed();
                    slot.OperationFailed(error); throw;
                }
                finally { slot.Dispose(); }
            }, guard);
    }
}
