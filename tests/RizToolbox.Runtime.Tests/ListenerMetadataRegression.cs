using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class ListenerMetadataRegression
{
    internal static readonly (string Name, Action Run)[] Cases = new (string Name, Action Run)[] {
        ("Rooted listener projection preserves actual target full metadata and caller ownership without invocation", Projection),
        ("Rooted listener projection rejects disposed roots exact-class mismatches and non-delegates", Roots),
        ("Rooted listener projection refuses multicast parents before interpreting their method token", Multicast),
        ("Rooted listener projection validates official field type offset staticness and shadowing", Fields),
        ("Rooted listener projection rejects unreadable method and nested metadata before method exports", Pointers),
        ("Rooted listener projection requires official bounded declaring-method membership", Membership),
        ("Rooted listener projection cross-checks layout flags name return and parameter count", Layout),
        ("Rooted listener projection reports static virtual generic and inflated metadata without granting invocation", Flags),
        ("Rooted listener projection preserves opaque unverified executable evidence", OpaqueEntry),
        ("Rooted listener projection detects a delegate field changing during the metadata read", Mutation),
        ("Observed ref0 resolver accepts only the two exact declared PrepareInvoke reference returns", Prepare),
        ("Observed ref0 resolver rejects foreign class name RVA and fabricated return class", PrepareAdmission),
        ("Observed ref0 resolver rejects static abstract generic inflated arguments and wrong reference layouts", PrepareShape),
        ("Listener metadata APIs reject worker calls before metadata reads or invocation", Thread),
        ("Observed native void0 resolver admits only the actual pinned UnityEvent Invoke without invoking it", NativeVoidAdmission),
        ("Observed native void0 resolver rejects unsupported method shape entry and worker access", NativeVoidShape),
        ("Delegate writer resolver binds both exact non-generic InvokableCall UnityAction methods without invocation", DelegateWriterAdmission),
        ("Delegate writer resolver refuses shared generic inflated and changed native metadata", DelegateWriterShape),
        ("Delegate writer resolver rejects worker access before metadata or native state reads", DelegateWriterThread)
    }.Concat(CachedBooleanInvokableRegression.Cases).Concat(CachedSingleInvokableRegression.Cases)
        .Concat(TweenGraphReadRegression.Cases).Concat(TweenResolverCases)
        .Concat(TweenVector4AbiRegression.Cases).Concat(NativeTweenSourceRegression.Cases)
        .Concat(NativeTweenSourceReferencesRegression.Cases).Concat(NativeTweenSourceDynamicReferencesRegression.Cases)
        .Concat(NativeTweenSourceLifetimeRegression.Cases)
        .Concat(TweenDeathCases).ToArray();
    private static void Projection()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
        var result = f.Project(root);
        Check(result.Target == f.Target && result.ActualClass.Class == f.ActionClass && !result.Multicast && !result.Virtual);
        Check(result.Method.MethodInfo == f.Listener.Pointer && result.Method.Name == "Listen" && result.Method.ReturnType == "System.Void"
            && result.Method.ParameterTypes.SequenceEqual(new[] { "System.Int32" }) && result.Method.Entry?.Rva == 0x154E1D0);
        Check(result.MethodPointer == f.Listener.Entry && result.InvokeImplementation == f.Listener.Entry && result.MethodCode == f.Target);
        Check(root.Target == f.Delegate && f.Base.ActiveRoots == 1 && f.Base.NewRoots == 1 && f.Base.FreedRoots == 0 && f.Base.Invocations == 0);
    }
    private static void Roots()
    {
        using var f = new Fixture(); var root = f.Api.Root(f.Delegate); root.Dispose();
        Throws<TargetExpiredException>(() => f.Project(root)); Equal(0, f.FieldReads);
        using var live = f.Api.Root(f.Delegate);
        Throws<UnsupportedSignatureException>(() => f.Api.InspectRootedDelegateMethod(live, f.TargetClass, f.Module));
        using var foreign = f.Api.Root(f.Target);
        Throws<UnsupportedSignatureException>(() => f.Api.InspectRootedDelegateMethod(foreign, f.TargetClass, f.Module));
        Throws<UnsupportedSignatureException>(() => f.Api.InspectRootedDelegateMethod(live, new IntPtr(1), f.Module));
        Equal(0, f.MethodExports); Equal(0, f.Base.Invocations);
    }
    private static void Multicast()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
        Marshal.WriteIntPtr(f.Delegate, 0x78, f.Target);
        Throws<UnsupportedSignatureException>(() => f.Project(root)); Equal(0, f.MethodExports);
        Check(root.Target == f.Delegate && f.Base.ActiveRoots == 1);
    }
    private static void Fields()
    {
        foreach (var mode in new[] { "offset", "type", "static", "shadow" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
            var field = f.NamedField(f.DelegateClass, "method");
            if (mode == "offset") field.Offset++;
            if (mode == "type") field.Type = f.ObjectType;
            if (mode == "static") field.Flags = 0x10;
            if (mode == "shadow") f.AddField(f.ActionClass, "method", f.PointerType, 0x28);
            Throws<UnsupportedSignatureException>(() => f.Project(root)); Equal(0, f.FieldReads); Equal(0, f.MethodExports);
        }
    }
    private static void Pointers()
    {
        foreach (var mode in new[] { "method", "name", "class", "return", "parameters" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
            var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(f.Listener.Pointer);
            if (mode == "method") Marshal.WriteIntPtr(f.Delegate, 0x28, new IntPtr(1));
            if (mode == "name") layout.Name = new IntPtr(1);
            if (mode == "class") layout.Class = new IntPtr(1);
            if (mode == "return") layout.ReturnType = new IntPtr(1);
            if (mode == "parameters") layout.Parameters = new IntPtr(1);
            Marshal.StructureToPtr(layout, f.Listener.Pointer, false);
            Throws<UnsupportedSignatureException>(() => f.Project(root)); Equal(0, f.MethodExports); Equal(0, f.Base.Invocations);
        }
    }
    private static void Membership()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Delegate); f.Listener.Listed = false;
        Throws<UnsupportedSignatureException>(() => f.Project(root)); Equal(0, f.MethodExports);
        Check(f.Enumerations > 0 && f.Base.Invocations == 0);
        var before = f.Enumerations; f.EndlessEnumeration = true;
        Throws<UnsupportedSignatureException>(() => f.Project(root)); Equal(4096, f.Enumerations - before);
        Equal(0, f.MethodExports); Check(root.Target == f.Delegate && f.Base.Invocations == 0);
    }
    private static void Layout()
    {
        foreach (var mode in new[] { "name", "class", "return", "flags", "implementation", "count" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
            var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(f.Listener.Pointer);
            if (mode == "name") layout.Name = f.AllocName("Other");
            if (mode == "class") layout.Class = f.EventBaseClass;
            if (mode == "return") layout.ReturnType = f.ObjectType;
            if (mode == "flags") layout.Flags = 0x10;
            if (mode == "implementation") layout.ImplementationFlags = 8;
            if (mode == "count") layout.ParameterCount = 0;
            Marshal.StructureToPtr(layout, f.Listener.Pointer, false);
            Throws<UnsupportedSignatureException>(() => f.Project(root)); Equal(0, f.Base.Invocations);
        }
    }
    private static void Flags()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
        f.Listener.Flags = 0x50; f.Listener.Implementation = 8; f.Listener.Generic = f.Listener.Inflated = true; f.Sync(f.Listener);
        Marshal.WriteByte(f.Delegate, 0x70, 1);
        var result = f.Project(root);
        Check(result.Virtual && result.Method.GenericMethod && result.Method.InflatedMethod && result.Method.Flags == 0x50
            && result.Method.ImplementationFlags == 8 && f.Base.Invocations == 0);
        // A null target is an observed open/static shape, not evidence that work
        // ended or permission to call its otherwise executable method pointer.
        Marshal.WriteIntPtr(f.Delegate, 0x20, IntPtr.Zero);
        result = f.Project(root);
        Check(result.Target == IntPtr.Zero && result.Method.Flags == 0x50 && root.Target == f.Delegate && f.Base.Invocations == 0);
    }
    private static void OpaqueEntry()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
        f.Listener.Entry = f.Allocate(8); f.Sync(f.Listener);
        var result = f.Project(root); Check(result.Method.Entry is null && result.Method.EntryStatus.StartsWith("Unverified"));
        Equal(0, f.Base.Invocations);
    }
    private static void Mutation()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Delegate); var reads = 0;
        f.OnRead = field => { if (field.Name == "method" && ++reads == 2) Marshal.WriteIntPtr(f.Delegate, 0x20, IntPtr.Zero); };
        Throws<UnsupportedSignatureException>(() => f.Project(root)); Check(root.Target == f.Delegate && f.Base.Invocations == 0);
    }
    private static void Prepare()
    {
        using var f = new Fixture();
        foreach (var method in new[] { f.EventPrepare, f.ListPrepare })
        {
            var target = f.Resolve(method);
            Check(target.Address == method.Entry && target.MethodInfo == method.Pointer && target.DeclaringClass == method.Class
                && target.Abi == ExactHookAbi.CoroutineFactoryReference0 && target.ReturnClass == f.ListClass);
        }
        Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots);
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveExactHook("ordinary", f.Module, f.EventBaseClass,
            "PrepareInvoke", Array.Empty<string>(), 0x1563A60, ExactHookAbi.CoroutineFactoryReference0));
    }
    private static void PrepareAdmission()
    {
        using var f = new Fixture();
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedReferenceReturn0(f.Module, f.TargetClass, "PrepareInvoke", f.ListClass, 0x1563A60));
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedReferenceReturn0(f.Module, f.EventBaseClass, "Other", f.ListClass, 0x1563A60));
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedReferenceReturn0(f.Module, f.EventBaseClass, "PrepareInvoke", f.ListClass, 0x154E1D0));
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedReferenceReturn0(f.Module, f.EventBaseClass, "PrepareInvoke", f.TargetClass, 0x1563A60));
        Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots);
    }
    private static void PrepareShape()
    {
        foreach (var mode in new[] { "static", "abstract", "generic", "inflated", "argument", "byref", "value", "foreign-return", "entry", "layout" })
        {
            using var f = new Fixture(); var method = f.EventPrepare;
            if (mode == "static") method.Flags = 0x10;
            if (mode == "abstract") method.Flags = 0x400;
            if (mode == "generic") method.Generic = true;
            if (mode == "inflated") method.Inflated = true;
            if (mode == "argument") method.Parameters = new[] { f.IntType };
            if (mode == "byref") f.Types[f.ListType].ByRef = true;
            if (mode == "value") f.Types[f.ListType].Value = true;
            if (mode == "foreign-return") method.Return = f.ObjectType;
            if (mode == "entry") method.Entry = IntPtr.Add(f.Module, 0x154E1D0);
            f.Sync(method);
            if (mode == "layout") { var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.Pointer); layout.Flags = 0x10; Marshal.StructureToPtr(layout, method.Pointer, false); }
            if (mode is "static" or "argument") Throws<MissingMethodException>(() => f.Resolve(method));
            else Throws<UnsupportedSignatureException>(() => f.Resolve(method));
            Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots);
        }
    }
    private static void Thread()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Delegate);
        Task.Run(() => { Throws<InvalidOperationException>(() => f.Project(root)); Throws<InvalidOperationException>(() => f.Resolve(f.EventPrepare)); }).GetAwaiter().GetResult();
        Equal(0, f.FieldReads); Equal(0, f.MethodExports); Equal(0, f.Base.Invocations); f.Project(root);
    }
    private static void NativeVoidAdmission()
    {
        using var f = new Fixture();
        var target = f.Api.ResolveObservedInstanceVoid0(f.Module, f.EventClass, "Invoke", 0x15642E0);
        Check(target.Address == f.EventInvoke.Entry && target.MethodInfo == f.EventInvoke.Pointer
            && target.DeclaringClass == f.EventClass && target.ReturnClass == IntPtr.Zero && target.Abi == ExactHookAbi.NativeVoid0);
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedInstanceVoid0(IntPtr.Zero, f.EventClass, "Invoke", 0x15642E0));
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedInstanceVoid0(f.Module, f.EventBaseClass, "Invoke", 0x15642E0));
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedInstanceVoid0(f.Module, f.EventClass, "PrepareInvoke", 0x15642E0));
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveObservedInstanceVoid0(f.Module, f.EventClass, "Invoke", 0x154E5B0));
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveExactHook("ordinary", f.Module, f.EventClass,
            "Invoke", Array.Empty<string>(), 0x15642E0, ExactHookAbi.NativeVoid0));
        Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots);
    }
    private static void NativeVoidShape()
    {
        foreach (var mode in new[] { "static", "abstract", "generic", "inflated", "argument", "reference-return", "byref", "entry", "layout" })
        {
            using var f = new Fixture(); var method = f.EventInvoke;
            if (mode == "static") method.Flags = 0x10;
            if (mode == "abstract") method.Flags = 0x400;
            if (mode == "generic") method.Generic = true;
            if (mode == "inflated") method.Inflated = true;
            if (mode == "argument") method.Parameters = new[] { f.IntType };
            if (mode == "reference-return") method.Return = f.ObjectType;
            if (mode == "byref") f.Types[f.VoidType].ByRef = true;
            if (mode == "entry") method.Entry = IntPtr.Add(f.Module, 0x154E5B0);
            f.Sync(method);
            if (mode == "layout") { var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.Pointer); layout.Flags = 0x10; Marshal.StructureToPtr(layout, method.Pointer, false); }
            try { f.Api.ResolveObservedInstanceVoid0(f.Module, f.EventClass, "Invoke", 0x15642E0); throw new InvalidOperationException("Unsupported native void0 method admitted: " + mode); }
            catch (MissingMethodException) { }
            catch (UnsupportedSignatureException) { }
            Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots);
        }
        using var thread = new Fixture();
        Task.Run(() => Throws<InvalidOperationException>(() => thread.Api.ResolveObservedInstanceVoid0(
            thread.Module, thread.EventClass, "Invoke", 0x15642E0))).GetAwaiter().GetResult();
        Equal(0, thread.MethodExports); Equal(0, thread.Base.Invocations);
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Listener metadata assertion failed."); }
    private static void Equal<T>(T expected, T actual) where T : notnull => Check(EqualityComparer<T>.Default.Equals(expected, actual));
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    // Synthetic IL2CPP exports over real readable buffers. The SEC_IMAGE mapping
    // supplies only executable-range validation evidence; no mapped code executes.
    private static void DelegateWriterAdmission()
    {
        using var f = new Fixture();
        foreach (var (operation, method) in new[] {
            (UnityEventOperation.AddDelegate, f.AddDelegate), (UnityEventOperation.RemoveDelegate, f.RemoveDelegate) })
        {
            var target = f.Api.ResolveUnityEventOperation(f.Module, operation);
            Check(target.Address == method.Entry && target.MethodInfo == method.Pointer && target.DeclaringClass == f.InvokableClass
                && target.Abi == ExactHookAbi.OperationReference1Void && target.ReturnClass == IntPtr.Zero);
        }
        Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots);
    }
    private static void DelegateWriterShape()
    {
        foreach (var mode in new[] { "static", "abstract", "generic", "inflated", "argument", "byref", "return", "entry", "class", "layout" })
        {
            using var f = new Fixture(); var method = f.AddDelegate;
            if (mode == "static") method.Flags = 0x10;
            if (mode == "abstract") method.Flags = 0x400;
            if (mode == "generic") method.Generic = true;
            if (mode == "inflated") method.Inflated = true;
            if (mode == "argument") method.Parameters = new[] { f.ObjectType };
            if (mode == "byref") f.Types[f.ActionType].ByRef = true;
            if (mode == "return") method.Return = f.ObjectType;
            if (mode == "entry") method.Entry = f.RemoveDelegate.Entry;
            if (mode == "class") method.Class = f.TargetClass;
            f.Sync(method);
            if (mode == "layout")
            {
                var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.Pointer);
                layout.Class = f.EventClass; Marshal.StructureToPtr(layout, method.Pointer, false);
            }
            try { f.Api.ResolveUnityEventOperation(f.Module, UnityEventOperation.AddDelegate); }
            catch (Exception error) when (error is UnsupportedSignatureException or MissingMethodException)
            { Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots); continue; }
            throw new InvalidOperationException("Changed delegate writer metadata was admitted: " + mode);
        }
    }
    private static void DelegateWriterThread()
    {
        using var f = new Fixture();
        Task.Run(() => Throws<InvalidOperationException>(() => f.Api.ResolveUnityEventOperation(f.Module, UnityEventOperation.AddDelegate)))
            .GetAwaiter().GetResult();
        Equal(0, f.MethodExports); Equal(0, f.Base.Invocations); Equal(0, f.Base.NewRoots);
    }
    private sealed partial class Fixture : IDisposable
    {
        internal sealed class TypeRow
        { internal string Name = "", LookupName = ""; internal IntPtr Class, Type, Parent, ImageOverride; internal int Code, Size; internal bool Value, ByRef, GenericClass, InflatedClass; }
        internal sealed class FieldRow
        { internal string Name = ""; internal IntPtr Pointer, Class, Type; internal int Offset; internal uint Flags; }
        internal sealed class MethodRow
        { internal string Name = ""; internal IntPtr Pointer, Class, Return, Entry, NamePointer; internal IntPtr[] Parameters = Array.Empty<IntPtr>(); internal uint Flags, Implementation; internal bool Generic, Inflated, Listed = true; }
        internal readonly FakeNative Base = new();
        internal readonly Dictionary<IntPtr, TypeRow> Types = new();
        private readonly Dictionary<IntPtr, TypeRow> _classes = new();
        private readonly List<FieldRow> _fields = new(); private readonly List<MethodRow> _methods = new();
        private readonly Dictionary<string, System.Delegate> _exports = new(); private readonly List<IntPtr> _allocations = new();
        private readonly Image _image = new(); private readonly IntPtr _assemblies, _mscorlib, _unity, _game, _mscorlibName, _unityName, _gameName;
        internal readonly IntPtr Module, ObjectType, PointerType, IntType, ListType, DelegateClass, ActionClass, TargetClass, EventBaseClass, CallsClass, ListClass, Delegate, Target, EventClass;
        internal readonly IntPtr InvokableClass, ActionType;
        internal IntPtr VoidType => _void;
        private readonly IntPtr _void, _boolean, _multicast, _delegateArray;
        internal readonly MethodRow Listener, EventPrepare, ListPrepare, EventInvoke, AddDelegate, RemoveDelegate;
        internal readonly NativeRuntime Api;
        internal int FieldReads, MethodExports, Enumerations, ClassLookups;
        internal bool EndlessEnumeration;
        internal Action<FieldRow>? OnRead;
        internal Fixture(bool tween = false)
        {
            Module = _image.Address; _assemblies = Allocate(24); _mscorlib = Allocate(16); _unity = Allocate(16); _game = Allocate(16);
            Marshal.WriteIntPtr(_assemblies, 0, _mscorlib); Marshal.WriteIntPtr(_assemblies, 8, _unity); Marshal.WriteIntPtr(_assemblies, 16, _game);
            _mscorlibName = AllocName("mscorlib.dll"); _unityName = AllocName("UnityEngine.CoreModule.dll"); _gameName = AllocName("Assembly-CSharp.dll");
            _void = AddType("System.Void", 1, true, 0); _boolean = AddType("System.Boolean", 2, true, 1);
            IntType = AddType("System.Int32", 8, true, 4); PointerType = AddType("System.IntPtr", 24, true, 8);
            ObjectType = AddType("System.Object", 28, false, 8); DelegateClass = ClassOf(AddType("System.Delegate", 18, false, 8));
            _multicast = ClassOf(AddType("System.MulticastDelegate", 18, false, 8, DelegateClass));
            ActionType = AddType("UnityEngine.Events.UnityAction", 18, false, 8, _multicast); ActionClass = ClassOf(ActionType);
            InvokableClass = ClassOf(AddType("UnityEngine.Events.InvokableCall", 18, false, 8));
            TargetClass = ClassOf(AddType("Fixture.ListenerTarget", 18, false, 8));
            EventBaseClass = ClassOf(AddType("UnityEngine.Events.UnityEventBase", 18, false, 8));
            EventClass = ClassOf(AddType("UnityEngine.Events.UnityEvent", 18, false, 8, EventBaseClass));
            CallsClass = ClassOf(AddType("UnityEngine.Events.InvokableCallList", 18, false, 8));
            ListType = AddType("System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>", 18, false, 8); ListClass = ClassOf(ListType);
            _delegateArray = AddType("System.Delegate[]", 29, false, 8);
            AddField(DelegateClass, "method", PointerType, 0x28); AddField(DelegateClass, "m_target", ObjectType, 0x20);
            AddField(DelegateClass, "method_ptr", PointerType, 0x10); AddField(DelegateClass, "invoke_impl", PointerType, 0x18);
            AddField(DelegateClass, "method_code", PointerType, 0x40); AddField(DelegateClass, "method_is_virtual", _boolean, 0x70);
            AddField(_multicast, "delegates", _delegateArray, 0x78); AddField(CallsClass, "m_ExecutingCalls", ListType, 0x20);
            Listener = AddMethod(TargetClass, "Listen", _void, 0x154E1D0, IntType);
            EventPrepare = AddMethod(EventBaseClass, "PrepareInvoke", ListType, 0x1563A60);
            ListPrepare = AddMethod(CallsClass, "PrepareInvoke", ListType, 0x154E1D0);
            EventInvoke = AddMethod(EventClass, "Invoke", _void, 0x15642E0);
            AddDelegate = AddMethod(InvokableClass, "add_Delegate", _void, 0x154E890, ActionType);
            RemoveDelegate = AddMethod(InvokableClass, "remove_Delegate", _void, 0x154E930, ActionType);
            Target = Allocate(0x80); Marshal.WriteIntPtr(Target, TargetClass);
            Delegate = Allocate(0x80); Marshal.WriteIntPtr(Delegate, ActionClass);
            Marshal.WriteIntPtr(Delegate, 0x28, Listener.Pointer); Marshal.WriteIntPtr(Delegate, 0x20, Target);
            Marshal.WriteIntPtr(Delegate, 0x10, Listener.Entry); Marshal.WriteIntPtr(Delegate, 0x18, Listener.Entry); Marshal.WriteIntPtr(Delegate, 0x40, Target);
            Bind("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies)); Bind("il2cpp_assembly_get_image", "Ptr1", nameof(Identity));
            Bind("il2cpp_image_get_name", "Ptr1", nameof(ImageName)); Bind("il2cpp_class_from_name", "ClassLookup", nameof(FindClass));
            Bind("il2cpp_class_get_parent", "Ptr1", nameof(Parent)); Bind("il2cpp_class_get_type", "Ptr1", nameof(ClassType));
            Bind("il2cpp_class_from_type", "Ptr1", nameof(ClassOf)); Bind("il2cpp_object_get_class", "Ptr1", nameof(ObjectClass));
            Bind("il2cpp_type_get_name", "Ptr1", nameof(TypeName)); Bind("il2cpp_type_get_type", "TypeCode", nameof(TypeCode));
            Bind("il2cpp_type_is_byref", "Predicate", nameof(ByRef)); Bind("il2cpp_class_is_valuetype", "Predicate", nameof(ValueType));
            Bind("il2cpp_class_is_enum", "Predicate", nameof(IsEnum)); Bind("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable));
            Bind("il2cpp_class_value_size", "ValueSize", nameof(ValueSize));
            Bind("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField)); Bind("il2cpp_field_get_type", "Ptr1", nameof(FieldType));
            Bind("il2cpp_field_get_flags", "Flags", nameof(FieldFlags)); Bind("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset));
            Bind("il2cpp_field_get_value", "FieldGet", nameof(ReadField)); Bind("il2cpp_class_get_methods", "Ptr2", nameof(Methods));
            Bind("il2cpp_method_get_name", "Ptr1", nameof(MethodName)); Bind("il2cpp_method_get_class", "Ptr1", nameof(MethodClass));
            Bind("il2cpp_method_get_return_type", "Ptr1", nameof(MethodReturn)); Bind("il2cpp_method_get_flags", "MethodFlags", nameof(MethodFlags));
            Bind("il2cpp_method_get_param_count", "Flags", nameof(ParameterCount)); Bind("il2cpp_method_get_param", "ParameterType", nameof(ParameterType));
            Bind("il2cpp_method_is_generic", "Predicate", nameof(Generic)); Bind("il2cpp_method_is_inflated", "Predicate", nameof(Inflated));
            Api = new NativeRuntime(name => _exports.TryGetValue(name, out var export) ? Marshal.GetFunctionPointerForDelegate(export) : Base.ExportForListenerTests(name));
            Api.BindCurrentThread();
            if (tween) InitializeTweenMetadata();
        }
        internal NativeRootedDelegateMethod Project(NativeRuntime.RootHandle root) => Api.InspectRootedDelegateMethod(root, ActionClass, Module);
        internal VerifiedHookTarget Resolve(MethodRow row) => Api.ResolveObservedReferenceReturn0(Module, row.Class, row.Name, ListClass, row == EventPrepare ? 0x1563A60 : 0x154E1D0);
        internal IntPtr Allocate(int size) { var p = Marshal.AllocHGlobal(Math.Max(16, size)); _allocations.Add(p); Marshal.Copy(new byte[Math.Max(16, size)], 0, p, Math.Max(16, size)); return p; }
        internal IntPtr AllocName(string value) { var p = Marshal.StringToHGlobalAnsi(value); _allocations.Add(p); return p; }
        private IntPtr AddType(string name, int code, bool value, int size, IntPtr parent = default)
        { var row = new TypeRow { Name = name, LookupName = name, Type = Allocate(16), Class = Allocate(0x200), Code = code, Value = value, Size = size, Parent = parent }; Types.Add(row.Type, row); _classes.Add(row.Class, row); return row.Type; }
        internal FieldRow AddField(IntPtr cls, string name, IntPtr type, int offset)
        { var row = new FieldRow { Class = cls, Name = name, Type = type, Offset = offset, Pointer = Allocate(16) }; _fields.Add(row); return row; }
        internal FieldRow NamedField(IntPtr cls, string name) => _fields.Single(row => row.Class == cls && row.Name == name);
        private MethodRow AddMethod(IntPtr cls, string name, IntPtr returns, int rva, params IntPtr[] parameters)
        { var row = new MethodRow { Class = cls, Name = name, Return = returns, Entry = IntPtr.Add(Module, rva), Parameters = parameters, Pointer = Allocate(Marshal.SizeOf<HookTargetValidation.MethodInfo2022>()), NamePointer = AllocName(name) }; _methods.Add(row); Sync(row); return row; }
        internal void Sync(MethodRow row)
        {
            var parameters = row.Parameters.Length == 0 ? IntPtr.Zero : Allocate(row.Parameters.Length * 8);
            for (var i = 0; i < row.Parameters.Length; ++i) Marshal.WriteIntPtr(parameters, i * 8, row.Parameters[i]);
            Marshal.StructureToPtr(new HookTargetValidation.MethodInfo2022 { MethodPointer = row.Entry, VirtualMethodPointer = row.Entry,
                Name = row.NamePointer, Class = row.Class, ReturnType = row.Return, Parameters = parameters, Flags = (ushort)row.Flags,
                ImplementationFlags = (ushort)row.Implementation, ParameterCount = checked((byte)row.Parameters.Length) }, row.Pointer, false);
        }
        private void Bind(string name, string type, string method) => _exports[name] = DelegateFor(type, method);
        private System.Delegate DelegateFor(string type, string method) => System.Delegate.CreateDelegate(typeof(NativeRuntime).GetNestedType(type, BindingFlags.NonPublic)!, this, GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!);
        private IntPtr Assemblies(IntPtr domain, IntPtr size) { Marshal.WriteIntPtr(size, new IntPtr(3)); return _assemblies; }
        private IntPtr Identity(IntPtr value) => value;
        private IntPtr ImageName(IntPtr image) => image == _mscorlib ? _mscorlibName : image == _game ? _gameName : _unityName;
        private IntPtr FindClass(IntPtr image, string ns, string name)
        { ++ClassLookups; return _classes.Values.Single(row => row.LookupName == (ns.Length == 0 ? name : ns + "." + name)).Class; }
        private IntPtr Parent(IntPtr cls) => _classes[cls].Parent;
        private IntPtr ClassType(IntPtr cls) => _classes[cls].Type;
        private IntPtr ClassOf(IntPtr type) => Types[type].Class;
        private IntPtr ObjectClass(IntPtr obj) => Marshal.ReadIntPtr(obj);
        private IntPtr TypeName(IntPtr type) => Marshal.StringToCoTaskMemUTF8(Types[type].Name);
        private int TypeCode(IntPtr type) => Types[type].Code;
        private bool ByRef(IntPtr type) => Types[type].ByRef;
        private bool ValueType(IntPtr cls) => _classes[cls].Value;
        private bool IsEnum(IntPtr cls) => false;
        private bool Assignable(IntPtr expected, IntPtr actual) { for (var cls = actual; cls != IntPtr.Zero; cls = Parent(cls)) if (cls == expected) return true; return expected == ClassOf(ObjectType); }
        private int ValueSize(IntPtr cls, out uint alignment) { alignment = 8; return _classes[cls].Size; }
        private IntPtr FindField(IntPtr cls, string name) { for (; cls != IntPtr.Zero; cls = Parent(cls)) { var row = _fields.FirstOrDefault(field => field.Class == cls && field.Name == name); if (row is not null) return row.Pointer; } return IntPtr.Zero; }
        private FieldRow Field(IntPtr field) => _fields.Single(row => row.Pointer == field);
        private IntPtr FieldType(IntPtr field) => Field(field).Type;
        private uint FieldFlags(IntPtr field) => Field(field).Flags;
        private int FieldOffset(IntPtr field) => Field(field).Offset;
        private void ReadField(IntPtr obj, IntPtr field, IntPtr output) { ++FieldReads; var row = Field(field); OnRead?.Invoke(row); var data = new byte[Types[row.Type].Size]; Marshal.Copy(IntPtr.Add(obj, row.Offset), data, 0, data.Length); Marshal.Copy(data, 0, output, data.Length); }
        private MethodRow Method(IntPtr pointer) { ++MethodExports; return _methods.Single(row => row.Pointer == pointer); }
        private IntPtr Methods(IntPtr cls, IntPtr iterator) { ++Enumerations; if (EndlessEnumeration) return EventPrepare.Pointer; var rows = _methods.Where(row => row.Class == cls && row.Listed).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32(); if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer; }
        private IntPtr MethodName(IntPtr method) => Method(method).NamePointer;
        private IntPtr MethodClass(IntPtr method) => Method(method).Class;
        private IntPtr MethodReturn(IntPtr method) => Method(method).Return;
        private uint MethodFlags(IntPtr method, out uint implementation) { var row = Method(method); implementation = row.Implementation; return row.Flags; }
        private uint ParameterCount(IntPtr method) => (uint)Method(method).Parameters.Length;
        private IntPtr ParameterType(IntPtr method, uint index) => Method(method).Parameters[index];
        private bool Generic(IntPtr method) => Method(method).Generic;
        private bool Inflated(IntPtr method) => Method(method).Inflated;
        public void Dispose() { _image.Dispose(); foreach (var p in _allocations) Marshal.FreeHGlobal(p); Base.Dispose(); GC.KeepAlive(_exports); }
    }
    private sealed class Image : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "RizToolbox.ListenerMetadata." + Guid.NewGuid().ToString("N") + ".dll");
        private readonly FileStream _file; private readonly IntPtr _mapping;
        internal readonly IntPtr Address;
        internal Image()
        {
            var bytes = new byte[0x400]; using (var writer = new BinaryWriter(new MemoryStream(bytes)))
            {
                writer.Write((ushort)0x5A4D); writer.BaseStream.Position = 0x3C; writer.Write(0x80); writer.BaseStream.Position = 0x80;
                writer.Write(0x4550); writer.Write((ushort)0x8664); writer.Write((ushort)1); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write((ushort)0xF0); writer.Write((ushort)0x2022);
                var optional = writer.BaseStream.Position; writer.Write((ushort)0x20B); writer.BaseStream.Position = optional + 24; writer.Write(0x180000000UL);
                writer.Write(0x1000); writer.Write(0x200); writer.BaseStream.Position = optional + 40; writer.Write((ushort)6); writer.Write((ushort)0);
                writer.BaseStream.Position = optional + 48; writer.Write((ushort)6); writer.Write((ushort)0);
                writer.BaseStream.Position = optional + 56; writer.Write(0x1570000); writer.Write(0x200); writer.BaseStream.Position = optional + 68; writer.Write((ushort)3);
                writer.BaseStream.Position = optional + 108; writer.Write(16); writer.BaseStream.Position = optional + 0xF0;
                // The first image section starts at SectionAlignment, as required
                // by the Windows image mapper. Its zero-filled virtual tail covers
                // listener and Tween pinned RVAs without code execution or DllMain.
                writer.Write(new byte[] { 46, 116, 101, 120, 116, 0, 0, 0 }); writer.Write(0x156E000); writer.Write(0x1000); writer.Write(0x200); writer.Write(0x200);
                writer.Write(0); writer.Write(0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0x60000020);
            }
            File.WriteAllBytes(_path, bytes); _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _mapping = CreateFileMapping(_file.SafeFileHandle.DangerousGetHandle(), IntPtr.Zero, 0x1000002, 0, 0, null);
            if (_mapping == IntPtr.Zero) throw new InvalidOperationException("Synthetic metadata SEC_IMAGE mapping failed: " + Marshal.GetLastWin32Error());
            Address = MapViewOfFile(_mapping, 4, 0, 0, 0);
            if (Address == IntPtr.Zero) throw new InvalidOperationException("Synthetic metadata image view failed: " + Marshal.GetLastWin32Error());
        }
        public void Dispose() { UnmapViewOfFile(Address); CloseHandle(_mapping); _file.Dispose(); File.Delete(_path); }
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attributes, uint protect, uint high, uint low, string? name);
        [DllImport("kernel32", SetLastError = true)] private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint high, uint low, nuint size);
        [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnmapViewOfFile(IntPtr view);
        [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
}

sealed partial class FakeNative
{
    internal IntPtr ExportForListenerTests(string name) => ExportPointer(name);
}
