using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed record NativeSingleExecutingList(long Event, long List, long Array, int Count, int Version,
    IReadOnlyList<long> Items);
internal sealed record NativeSingleEventGraph(long Event, long Calls, long PersistentGroup, bool CallsDirty,
    bool NeedsUpdate, NativeSingleExecutingList PersistentGroupCalls, NativeSingleExecutingList PersistentCalls,
    NativeSingleExecutingList RuntimeCalls, NativeSingleExecutingList ExecutingCalls);
internal sealed record NativeSingleDispatchTarget(uint Site, long Invokable, long Target, long Method,
    uint OriginalValueBits, uint? EffectiveSingleBits, bool CachedSingle, long ArgumentArray,
    IReadOnlyList<long> ArgumentObjects, string Evidence, long Delegate = 0);

internal sealed partial class NativeRuntime
{
    // All input roots are supplied before entering this method. New backing/element references
    // are synchronously published and rooted before class/field/metadata inspection.
    internal NativeSingleExecutingList ReadSingleExecutingList(RootHandle eventRoot, RootHandle listRoot,
        VerifiedHookTarget target, Func<IntPtr, RootHandle> rootPublished)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(eventRoot); ArgumentNullException.ThrowIfNull(listRoot);
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(rootPublished);
        var receiver = eventRoot.Target; var list = listRoot.Target;
        HookTargetValidation.VerifyReadable(receiver, checked((nuint)(2 * IntPtr.Size)));
        HookTargetValidation.VerifyReadable(list, checked((nuint)(2 * IntPtr.Size)));
        if (target.Abi != ExactHookAbi.OperationSingle1Void || target.DeclaringClass == IntPtr.Zero
            || eventRoot.Target != receiver || Marshal.ReadIntPtr(receiver) != target.DeclaringClass
            || _objectClass(receiver) != target.DeclaringClass)
            throw new UnsupportedSignatureException("The rooted Single event differs from its admitted closed invocation class.");
        var invokableList = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "InvokableCallList");
        var declared = InspectMetadataFieldClass(invokableList, "m_ExecutingCalls", "System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>");
        var listClass = declared.FieldClass.Class;
        if (listRoot.Target != list || Marshal.ReadIntPtr(list) != listClass || _objectClass(list) != listClass)
            throw new UnsupportedSignatureException("The actual Single PrepareInvoke result has a foreign executing-list class.");
        var elementClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "BaseInvokableCall");
        var result = ReadSingleListCore(receiver, listRoot, listClass, "UnityEngine.Events.BaseInvokableCall[]", elementClass, rootPublished);
        if (eventRoot.Target != receiver)
            throw new UnsupportedSignatureException("Single event identity changed during capture.");
        return result;
    }

    internal NativeSingleEventGraph ReadSingleEventGraph(RootHandle eventRoot, VerifiedHookTarget target,
        Func<IntPtr, RootHandle> rootPublished)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(eventRoot);
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(rootPublished);
        var receiver = eventRoot.Target;
        HookTargetValidation.VerifyReadable(receiver, checked((nuint)(2 * IntPtr.Size)));
        if (target.Abi != ExactHookAbi.OperationSingle1Void || target.DeclaringClass == IntPtr.Zero
            || eventRoot.Target != receiver || Marshal.ReadIntPtr(receiver) != target.DeclaringClass
            || _objectClass(receiver) != target.DeclaringClass)
            throw new UnsupportedSignatureException("The rooted Single event graph differs from its admitted closed invocation class.");

        var eventBase = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEventBase");
        var callsClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "InvokableCallList");
        var groupClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "PersistentCallGroup");
        var baseCallClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "BaseInvokableCall");
        var persistentCallClass = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "PersistentCall");
        if (!_assignable(eventBase, target.DeclaringClass))
            throw new UnsupportedSignatureException("Single event class does not derive from UnityEventBase.");

        var callsField = InspectExactDeclaredReadField(eventBase, "m_Calls", "UnityEngine.Events.InvokableCallList", false, 0x10);
        var groupField = InspectExactDeclaredReadField(eventBase, "m_PersistentCalls", "UnityEngine.Events.PersistentCallGroup", false, 0x18);
        var dirtyField = InspectExactDeclaredReadField(eventBase, "m_CallsDirty", "System.Boolean", false, 0x20);
        if (callsField.FieldClass != callsClass || groupField.FieldClass != groupClass)
            throw new UnsupportedSignatureException("UnityEventBase m_Calls or m_PersistentCalls class mismatch.");

        var persistentCallsField = InspectExactDeclaredReadField(callsClass, "m_PersistentCalls", "System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>", false, 0x10);
        var runtimeCallsField = InspectExactDeclaredReadField(callsClass, "m_RuntimeCalls", "System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>", false, 0x18);
        var executingCallsField = InspectExactDeclaredReadField(callsClass, "m_ExecutingCalls", "System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>", false, 0x20);
        var needsUpdateField = InspectExactDeclaredReadField(callsClass, "m_NeedsUpdate", "System.Boolean", false, 0x28);
        var baseListClass = executingCallsField.FieldClass;
        if (persistentCallsField.FieldClass != baseListClass || runtimeCallsField.FieldClass != baseListClass)
            throw new UnsupportedSignatureException("InvokableCallList list fields differ from the exact BaseInvokableCall list class.");

        var groupCallsField = InspectExactDeclaredReadField(groupClass, "m_Calls", "System.Collections.Generic.List<UnityEngine.Events.PersistentCall>", false, 0x10);
        var persistentListClass = groupCallsField.FieldClass;

        RootHandle? callsRoot = null, groupRoot = null;
        PublishExactReferenceField(eventRoot, callsField, pointer => { if (pointer != IntPtr.Zero) callsRoot = rootPublished(pointer); });
        PublishExactReferenceField(eventRoot, groupField, pointer => { if (pointer != IntPtr.Zero) groupRoot = rootPublished(pointer); });
        if (callsRoot is null || groupRoot is null)
            throw new UnsupportedSignatureException("Single event graph is missing m_Calls or m_PersistentCalls.");
        var callsPointer = callsRoot.Target; var groupPointer = groupRoot.Target;
        HookTargetValidation.VerifyReadable(callsPointer, checked((nuint)(2 * IntPtr.Size)));
        HookTargetValidation.VerifyReadable(groupPointer, checked((nuint)(2 * IntPtr.Size)));
        if (Marshal.ReadIntPtr(callsPointer) != callsClass || _objectClass(callsPointer) != callsClass
            || Marshal.ReadIntPtr(groupPointer) != groupClass || _objectClass(groupPointer) != groupClass)
            throw new UnsupportedSignatureException("Single event m_Calls or m_PersistentCalls has a foreign runtime class.");

        var callsDirty = ReadExactField<bool>(eventRoot, dirtyField);
        var needsUpdate = ReadExactField<bool>(callsRoot, needsUpdateField);

        RootHandle ReadChildListRoot(RootHandle ownerRoot, NativeExactReadField listField, IntPtr expectedListClass)
        {
            RootHandle? listRoot = null;
            PublishExactReferenceField(ownerRoot, listField, pointer => { if (pointer != IntPtr.Zero) listRoot = rootPublished(pointer); });
            if (listRoot is null)
                throw new UnsupportedSignatureException($"Single event list field '{listField.Name}' is null.");
            var listPointer = listRoot.Target;
            HookTargetValidation.VerifyReadable(listPointer, checked((nuint)(2 * IntPtr.Size)));
            if (Marshal.ReadIntPtr(listPointer) != expectedListClass || _objectClass(listPointer) != expectedListClass)
                throw new UnsupportedSignatureException($"Single event list field '{listField.Name}' has a foreign class.");
            return listRoot;
        }

        var persistentGroupCallsRoot = ReadChildListRoot(groupRoot, groupCallsField, persistentListClass);
        var persistentCallsRoot = ReadChildListRoot(callsRoot, persistentCallsField, baseListClass);
        var runtimeCallsRoot = ReadChildListRoot(callsRoot, runtimeCallsField, baseListClass);
        var executingCallsRoot = ReadChildListRoot(callsRoot, executingCallsField, baseListClass);

        var persistentGroupCalls = ReadSingleListCore(receiver, persistentGroupCallsRoot, persistentListClass,
            "UnityEngine.Events.PersistentCall[]", persistentCallClass, rootPublished);
        var persistentCalls = ReadSingleListCore(receiver, persistentCallsRoot, baseListClass,
            "UnityEngine.Events.BaseInvokableCall[]", baseCallClass, rootPublished);
        var runtimeCalls = ReadSingleListCore(receiver, runtimeCallsRoot, baseListClass,
            "UnityEngine.Events.BaseInvokableCall[]", baseCallClass, rootPublished);
        var executingCalls = ReadSingleListCore(receiver, executingCallsRoot, baseListClass,
            "UnityEngine.Events.BaseInvokableCall[]", baseCallClass, rootPublished);

        void VerifyChildListUnchanged(RootHandle ownerRoot, NativeExactReadField listField, IntPtr expectedListClass,
            RootHandle listRoot, NativeSingleExecutingList captured, string arrayTypeName)
        {
            IntPtr recheckList = IntPtr.Zero;
            PublishExactReferenceField(ownerRoot, listField, pointer => { recheckList = pointer; if (pointer != IntPtr.Zero) rootPublished(pointer); });
            if (recheckList != listRoot.Target || recheckList.ToInt64() != captured.List)
                throw new UnsupportedSignatureException($"Single event list field '{listField.Name}' changed during rooted capture.");
            var backing = InspectExactDeclaredReadField(expectedListClass, "_items", arrayTypeName, false, 0x10);
            var size = InspectExactDeclaredReadField(expectedListClass, "_size", "System.Int32", false, 0x18);
            var version = InspectExactDeclaredReadField(expectedListClass, "_version", "System.Int32", false, 0x1c);
            IntPtr recheckArray = IntPtr.Zero;
            PublishExactReferenceField(listRoot, backing, pointer => { recheckArray = pointer; if (pointer != IntPtr.Zero) rootPublished(pointer); });
            if (listRoot.Target.ToInt64() != captured.List
                || recheckArray.ToInt64() != captured.Array
                || ReadExactField<int>(listRoot, size) != captured.Count
                || ReadExactField<int>(listRoot, version) != captured.Version)
                throw new UnsupportedSignatureException($"Single event list '{listField.Name}' contents changed during rooted capture.");
        }

        VerifyChildListUnchanged(groupRoot, groupCallsField, persistentListClass, persistentGroupCallsRoot, persistentGroupCalls, "UnityEngine.Events.PersistentCall[]");
        VerifyChildListUnchanged(callsRoot, persistentCallsField, baseListClass, persistentCallsRoot, persistentCalls, "UnityEngine.Events.BaseInvokableCall[]");
        VerifyChildListUnchanged(callsRoot, runtimeCallsField, baseListClass, runtimeCallsRoot, runtimeCalls, "UnityEngine.Events.BaseInvokableCall[]");
        VerifyChildListUnchanged(callsRoot, executingCallsField, baseListClass, executingCallsRoot, executingCalls, "UnityEngine.Events.BaseInvokableCall[]");

        IntPtr recheckCalls = IntPtr.Zero, recheckGroup = IntPtr.Zero;
        PublishExactReferenceField(eventRoot, callsField, pointer => { recheckCalls = pointer; if (pointer != IntPtr.Zero) rootPublished(pointer); });
        PublishExactReferenceField(eventRoot, groupField, pointer => { recheckGroup = pointer; if (pointer != IntPtr.Zero) rootPublished(pointer); });
        if (eventRoot.Target != receiver || callsRoot.Target != callsPointer || groupRoot.Target != groupPointer
            || recheckCalls != callsPointer || recheckGroup != groupPointer
            || ReadExactField<bool>(eventRoot, dirtyField) != callsDirty
            || ReadExactField<bool>(callsRoot, needsUpdateField) != needsUpdate)
            throw new UnsupportedSignatureException("Single event graph changed during rooted capture.");

        return new(receiver.ToInt64(), callsPointer.ToInt64(), groupPointer.ToInt64(), callsDirty, needsUpdate,
            persistentGroupCalls, persistentCalls, runtimeCalls, executingCalls);
    }

    private NativeSingleExecutingList ReadSingleListCore(IntPtr eventPointer, RootHandle listRoot, IntPtr listClass,
        string arrayTypeName, IntPtr elementClass, Func<IntPtr, RootHandle> rootPublished)
    {
        var list = listRoot.Target;
        var backing = InspectExactDeclaredReadField(listClass, "_items", arrayTypeName, false, 0x10);
        var size = InspectExactDeclaredReadField(listClass, "_size", "System.Int32", false, 0x18);
        var version = InspectExactDeclaredReadField(listClass, "_version", "System.Int32", false, 0x1c);
        var count = ReadExactField<int>(listRoot, size); var stamp = ReadExactField<int>(listRoot, version);
        RootHandle? array = null;
        PublishExactReferenceField(listRoot, backing, pointer => { if (pointer != IntPtr.Zero) array = rootPublished(pointer); });
        if (array is null || count < 0 || count > 4096)
            throw new UnsupportedSignatureException("Single executing-list count/backing is not completely bounded.");
        var vector = ReadRootedReferenceArrayWithPublication(array, backing.FieldClass, elementClass, 4096,
            (_, pointer) => { if (pointer != IntPtr.Zero) rootPublished(pointer); });
        if (count > vector.Length || ReadExactField<int>(listRoot, size) != count || ReadExactField<int>(listRoot, version) != stamp)
            throw new UnsupportedSignatureException("Single executing-list changed during its rooted full-capacity capture.");
        IntPtr currentArray = IntPtr.Zero;
        PublishExactReferenceField(listRoot, backing, pointer => { currentArray = pointer; if (pointer != IntPtr.Zero) rootPublished(pointer); });
        if (currentArray != array.Target || list != listRoot.Target)
            throw new UnsupportedSignatureException("Single event/list/backing identity changed during capture.");
        // Root every element first, then inspect actual element classes. Duplicate objects remain distinct indices.
        for (var index = 0; index < count; index++)
        {
            var item = vector.Items[index];
            if (item == IntPtr.Zero)
                throw new UnsupportedSignatureException("An active Single executing-list element is null or not a BaseInvokableCall.");
            var itemRoot = rootPublished(item);
            var itemPointer = itemRoot.Target;
            if (itemPointer != item)
                throw new UnsupportedSignatureException("A rooted Single executing-list element changed identity.");
            HookTargetValidation.VerifyReadable(itemPointer, checked((nuint)(2 * IntPtr.Size)));
            var itemClass = _objectClass(itemPointer);
            if (Marshal.ReadIntPtr(itemPointer) != itemClass || !_assignable(elementClass, itemClass))
                throw new UnsupportedSignatureException("An active Single executing-list element is null or not a BaseInvokableCall.");
        }
        IntPtr finalArray = IntPtr.Zero;
        PublishExactReferenceField(listRoot, backing, pointer => { finalArray = pointer; if (pointer != IntPtr.Zero) rootPublished(pointer); });
        if (listRoot.Target != list || finalArray != vector.Array
            || ReadExactField<int>(listRoot, size) != count || ReadExactField<int>(listRoot, version) != stamp)
            throw new UnsupportedSignatureException("Single executing-list changed during element verification.");
        return new(eventPointer.ToInt64(), list.ToInt64(), vector.Array.ToInt64(), count, stamp,
            Array.AsReadOnly(vector.Items.Select(p => p.ToInt64()).ToArray()));
    }

    internal NativeSingleDispatchTarget ReadSingleDispatch(NativeTweenSingleEventObservation observation,
        RootHandle invokableRoot, RootHandle? argumentArrayRoot, Func<IntPtr, RootHandle> rootPublished, IntPtr module)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(invokableRoot); ArgumentNullException.ThrowIfNull(rootPublished);
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("GameAssembly module is absent.");
        var frame = observation.Frame;
        if (frame.Stage != NativeTweenSingleEventStage.Before || frame.Invokable != invokableRoot.Target || observation.Machine is not { } machine)
            throw new UnsupportedSignatureException("Single dispatch inspection requires its actual rooted Before and machine copy.");
        var value = invokableRoot.Target;
        HookTargetValidation.VerifyReadable(value, checked((nuint)(2 * IntPtr.Size)));
        var headerClass = Marshal.ReadIntPtr(value);
        var cls = _objectClass(value);
        if (cls == IntPtr.Zero || headerClass != cls)
            throw new UnsupportedSignatureException("The rooted Single dispatch receiver has inconsistent class metadata.");
        var baseInvokable = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "BaseInvokableCall");
        var plain = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "InvokableCall");
        if (!_assignable(baseInvokable, cls))
            throw new UnsupportedSignatureException("The actual Single dispatch receiver is not a BaseInvokableCall.");
        uint? effective = null; var cached = false; var method = IntPtr.Zero; long delegatePointer = 0;
        var arguments = Array.Empty<long>();
        if (frame.Site == 1)
        {
            var declaration = InspectDeclaredMethod(plain, "Invoke", Array.Empty<string>(), "System.Void", module);
            if (cls != plain || declaration.Entry?.Rva != 0x154e5b0 || frame.ActualTarget != IntPtr.Add(module, 0x154e5b0) || frame.ArgRdx != IntPtr.Zero
                || (declaration.Flags & (0x10u | 0x40u | 0x400u | 0x2000u)) != 0
                || declaration.GenericMethod || declaration.InflatedMethod
                || declaration.DeclaringClass.Class != plain)
                throw new UnsupportedSignatureException("Single site 1 is the exact direct zero-argument InvokableCall.Invoke path; it has no float ABI.");
            HookTargetValidation.VerifyExecutable(frame.ActualTarget, module);
            method = declaration.MethodInfo;
        }
        else
        {
            if (frame.Site > 2 || unchecked((long)machine.Rax) != cls.ToInt64())
                throw new UnsupportedSignatureException("Single virtual dispatch saved class differs from its already rooted receiver.");
            var offset = frame.Site == 0 ? 0x198 : 0x178;
            HookTargetValidation.VerifyReadable(cls, checked((nuint)offset + 16));
            var actualTarget = Marshal.ReadIntPtr(cls, offset); var actualMethod = Marshal.ReadIntPtr(cls, offset + 8);
            if (actualTarget != frame.ActualTarget || actualMethod != frame.ArgR8 || actualMethod == IntPtr.Zero)
                throw new UnsupportedSignatureException("Single dispatch differs from its exact saved vtable target and MethodInfo pair.");
            HookTargetValidation.VerifyExecutable(actualTarget, module);

            var classImage = ReadGraphExport<Ptr1>("il2cpp_class_get_image");
            var generic = ReadGraphExport<Predicate>("il2cpp_class_is_generic");
            var inflated = ReadGraphExport<Predicate>("il2cpp_class_is_inflated");
            var unityImage = classImage(baseInvokable);
            if (unityImage == IntPtr.Zero)
                throw new UnsupportedSignatureException("UnityEngine.CoreModule image is absent.");

            bool IsClosedUnityEventGeneric(IntPtr candidate, string shortName, string metadataName)
            {
                if (candidate == IntPtr.Zero) return false;
                HookTargetValidation.VerifyReadable(candidate, 16);
                var type = _classType(candidate);
                return type != IntPtr.Zero && _typeClass(type) == candidate && !_byRef(type) && _typeCode(type) == 21
                    && !_isValueType(candidate) && !generic(candidate) && inflated(candidate)
                    && classImage(candidate) == unityImage
                    && Utf8(_classNamespace(candidate)) == "UnityEngine.Events"
                    && Utf8(_className(candidate)) == shortName
                    && MetadataTypeName(type) == metadataName;
            }

            IntPtr declaredClass;
            if (frame.Site == 0)
            {
                var parent = _classParent(cls);
                if (IsClosedUnityEventGeneric(cls, "CachedInvokableCall`1", CachedSingleType)
                    && IsClosedUnityEventGeneric(parent, "InvokableCall`1", SingleInvokableType)
                    && _classParent(parent) == baseInvokable)
                {
                    cached = true;
                    declaredClass = parent;
                }
                else if (IsClosedUnityEventGeneric(cls, "InvokableCall`1", SingleInvokableType)
                    && parent == baseInvokable)
                {
                    cached = false;
                    declaredClass = cls;
                }
                else
                {
                    throw new UnsupportedSignatureException("Single site 0 has no exact closed typed/cached Single receiver metadata.");
                }
            }
            else
            {
                var actualType = _classType(cls);
                if (actualType == IntPtr.Zero || _typeClass(actualType) != cls || _byRef(actualType) || _isValueType(cls)
                    || generic(cls))
                    throw new UnsupportedSignatureException("Single site 2 object[] dispatch requires a concrete closed reference class.");
                var typeCode = _typeCode(actualType);
                if (!(typeCode == 18 && !inflated(cls) || typeCode == 21 && inflated(cls)))
                    throw new UnsupportedSignatureException("Single site 2 object[] dispatch requires a closed non-generic or inflated generic reference class.");
                if (_assignable(plain, cls))
                    throw new UnsupportedSignatureException("Single site 2 object[] dispatch cannot accept InvokableCall or its subclasses.");
                var sawBase = false;
                var cursor = cls;
                for (var depth = 0; cursor != IntPtr.Zero && depth < 32; ++depth, cursor = _classParent(cursor))
                {
                    HookTargetValidation.VerifyReadable(cursor, 16);
                    var cursorType = _classType(cursor);
                    var cursorTypeName = cursorType != IntPtr.Zero ? MetadataTypeName(cursorType) : string.Empty;
                    if (cursor == plain
                        || cursorTypeName is SingleInvokableType or CachedSingleType
                        || IsClosedUnityEventGeneric(cursor, "InvokableCall`1", SingleInvokableType)
                        || IsClosedUnityEventGeneric(cursor, "CachedInvokableCall`1", CachedSingleType)
                        || generic(cursor)
                        || (cursorType != IntPtr.Zero && _typeCode(cursorType) == 21 && !inflated(cursor)))
                        throw new UnsupportedSignatureException("Single site 2 object[] dispatch cannot accept InvokableCall, closed Single invokable classes, or open generic ancestors.");
                    if (cursor == baseInvokable) { sawBase = true; break; }
                }
                if (!sawBase)
                    throw new UnsupportedSignatureException("Single site 2 receiver hierarchy exceeds bounded BaseInvokableCall depth.");
                declaredClass = baseInvokable;
            }

            // Require official object_get_virtual_method resolution in addition to the pinned pair.
            var parameterType = frame.Site == 0 ? "System.Single" : "System.Object[]";
            var declared = Method(declaredClass, "Invoke", 1, false, new[] { parameterType });
            if (_methodClass(declared) != declaredClass)
                throw new UnsupportedSignatureException("Single declared virtual Invoke has a foreign declaring class.");
            var declaredFlags = _methodFlags(declared, out _);
            if ((declaredFlags & 0x10u) != 0 || (declaredFlags & 0x40u) == 0 || _isGenericMethod(declared)
                || _isInflatedMethod(declared) || frame.Site == 0 && (declaredFlags & 0x400u) != 0)
                throw new UnsupportedSignatureException("Single declared virtual Invoke differs from its official virtual metadata flags.");
            HookTargetValidation.VerifyReadable(declared, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var declaredLayout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(declared);
            if (declaredLayout.Slot == ushort.MaxValue || checked(0x138 + declaredLayout.Slot * 2 * IntPtr.Size) != offset)
                throw new UnsupportedSignatureException("Single declared Invoke vtable slot disagrees with its site offset.");

            var resolved = ReadGraphExport<Ptr2>("il2cpp_object_get_virtual_method")(value, declared);
            var projection = InspectMetadataMethod(resolved, module);
            if (resolved != actualMethod || projection.Entry is not { MethodInfoSlot: var resolvedSlot }
                || projection.Entry.Address != actualTarget || projection.Name != "Invoke"
                || (frame.Site == 0
                    ? projection.DeclaringClass.Class != cls
                    : !(projection.DeclaringClass.Class == declaredClass || _assignable(declaredClass, projection.DeclaringClass.Class)))
                || !_assignable(projection.DeclaringClass.Class, cls)
                || projection.ReturnType != "System.Void"
                || projection.ParameterTypes.Count != 1 || projection.ParameterTypes[0] != parameterType
                || (projection.Flags & 0x10) != 0 || (projection.Flags & 0x40) == 0 || (projection.Flags & 0x400) != 0
                || projection.GenericMethod || projection.InflatedMethod)
                throw new UnsupportedSignatureException("Single virtual CALL does not match its official exact instance Invoke ABI.");
            if (resolvedSlot == ushort.MaxValue || checked(0x138 + resolvedSlot * 2 * IntPtr.Size) != offset
                || resolvedSlot != declaredLayout.Slot)
                throw new UnsupportedSignatureException("Single resolved Invoke vtable slot disagrees with its site offset.");
            method = actualMethod;

            if (frame.Site == 0)
            {
                if (cached)
                {
                    var fixedArgument = InspectRootedCachedSingleInvokable(invokableRoot);
                    effective = unchecked((uint)fixedArgument.ArgumentBits);
                    RootHandle? delegateRoot = null;
                    var publishedDelegate = PublishCachedSingleDelegateOnce(invokableRoot, fixedArgument, pointer =>
                    {
                        if (pointer != IntPtr.Zero) delegateRoot = rootPublished(pointer);
                    });
                    if (publishedDelegate != IntPtr.Zero)
                    {
                        if (delegateRoot is null || delegateRoot.Target != publishedDelegate)
                            throw new UnsupportedSignatureException("Cached Single delegate root changed identity.");
                        HookTargetValidation.VerifyReadable(publishedDelegate, checked((nuint)(2 * IntPtr.Size)));
                        if (Marshal.ReadIntPtr(publishedDelegate) != fixedArgument.ActionClass
                            || _objectClass(publishedDelegate) != fixedArgument.ActionClass)
                            throw new UnsupportedSignatureException("Cached Single delegate object differs from UnityAction<System.Single>.");
                    }
                    delegatePointer = publishedDelegate.ToInt64();
                }
                else
                {
                    var multicast = Class("mscorlib.dll", "System", "MulticastDelegate");
                    var delegateClass = Class("mscorlib.dll", "System", "Delegate");
                    var systemImage = classImage(delegateClass);
                    if (systemImage == IntPtr.Zero || systemImage == unityImage || classImage(multicast) != systemImage)
                        throw new UnsupportedSignatureException("Single typed invokable requires distinct official Unity and System images.");
                    var delegateField = InspectExactDeclaredReadField(cls, "Delegate", SingleActionType, false, 0x10);
                    var actionClass = delegateField.FieldClass;
                    if (!IsClosedUnityEventGeneric(actionClass, "UnityAction`1", SingleActionType)
                        || _classParent(actionClass) != multicast || _classParent(multicast) != delegateClass)
                        throw new UnsupportedSignatureException("Single typed invokable Delegate field differs from official UnityAction<System.Single>.");
                    RootHandle? delegateRoot = null;
                    IntPtr rawDelegate = IntPtr.Zero;
                    PublishExactReferenceField(invokableRoot, delegateField, pointer =>
                    {
                        rawDelegate = pointer;
                        if (pointer != IntPtr.Zero) delegateRoot = rootPublished(pointer);
                    });
                    if (rawDelegate != IntPtr.Zero)
                    {
                        if (delegateRoot is null || delegateRoot.Target != rawDelegate)
                            throw new UnsupportedSignatureException("Single typed invokable Delegate root changed identity.");
                        HookTargetValidation.VerifyReadable(rawDelegate, checked((nuint)(2 * IntPtr.Size)));
                        if (Marshal.ReadIntPtr(rawDelegate) != actionClass || _objectClass(rawDelegate) != actionClass)
                            throw new UnsupportedSignatureException("Single typed invokable Delegate object differs from UnityAction<System.Single>.");
                    }
                    delegatePointer = rawDelegate.ToInt64();
                    effective = frame.ValueBits;
                }
            }
            else
            {
                if (argumentArrayRoot is null || argumentArrayRoot.Target != frame.ArgRdx)
                    throw new UnsupportedSignatureException("Single site 2 must root the actual RDX object array before reading metadata.");
                var objectClass = Class("mscorlib.dll", "System", "Object");
                var arrayClass = _typeClass(_parameterType(resolved, 0));
                var vector = ReadRootedReferenceArrayWithPublication(argumentArrayRoot, arrayClass, objectClass, 4096,
                    (_, pointer) => { if (pointer != IntPtr.Zero) rootPublished(pointer); });
                if (vector.Length != 1 || vector.Items[0] == IntPtr.Zero)
                    throw new UnsupportedSignatureException("Single object-array dispatch requires its actual one-element boxed payload.");
                var box = rootPublished(vector.Items[0]);
                var boxPointer = box.Target;
                if (boxPointer != vector.Items[0])
                    throw new UnsupportedSignatureException("The rooted boxed Single identity changed before metadata inspection.");
                var single = Class("mscorlib.dll", "System", "Single");
                var singleType = _classType(single);
                if (singleType == IntPtr.Zero || _typeClass(singleType) != single || _byRef(singleType)
                    || _typeCode(singleType) != 12 || !_isValueType(single) || _isEnum(single)
                    || _valueSize(single, out _) != sizeof(float)
                    || Describe(singleType) is not { Kind: NativeValueKind.Single, Size: 4, ByRef: false })
                    throw new UnsupportedSignatureException("Single object-array dispatch requires the official four-byte System.Single class.");
                HookTargetValidation.VerifyReadable(boxPointer, checked((nuint)(2 * IntPtr.Size)));
                if (Marshal.ReadIntPtr(boxPointer) != single || _objectClass(boxPointer) != single)
                    throw new UnsupportedSignatureException("The actual rooted Site 2 element is not a boxed System.Single.");
                var unboxed = _unbox(boxPointer);
                HookTargetValidation.VerifyReadable(unboxed, sizeof(int));
                var boxedBits = unchecked((uint)Marshal.ReadInt32(unboxed));
                if (box.Target != boxPointer || Marshal.ReadIntPtr(boxPointer) != single || boxedBits != frame.ValueBits)
                    throw new UnsupportedSignatureException("The actual rooted boxed Single raw bits differ from the parent payload.");
                arguments = vector.Items.Select(p => p.ToInt64()).ToArray();
            }
            if (invokableRoot.Target != value || Marshal.ReadIntPtr(value) != cls
                || Marshal.ReadIntPtr(cls, offset) != actualTarget || Marshal.ReadIntPtr(cls, offset + 8) != actualMethod)
                throw new UnsupportedSignatureException("Single virtual pair changed during rooted observation.");
        }
        return new(frame.Site, value.ToInt64(), frame.ActualTarget.ToInt64(), method.ToInt64(), frame.ValueBits,
            effective, cached, frame.Site == 2 ? frame.ArgRdx.ToInt64() : 0, Array.AsReadOnly(arguments),
            frame.Site == 0 ? "Exact Single virtual CALL; cached argument bits are distinct from dynamic Invoke bits."
                : frame.Site == 1 ? "Exact direct zero-argument CALL; no float was delivered by this ABI."
                : "Exact object[] virtual CALL; actual array and boxed payload rooted before inspection.",
            delegatePointer);
    }
}
