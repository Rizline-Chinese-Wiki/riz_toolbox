using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class ListenerMetadataRegression
{
    // A property avoids depending on static-field initialization order across
    // partial files. All cases use the existing real SEC_IMAGE fixture; none of
    // its executable pages or game methods is invoked.
    private static (string Name, Action Run)[] TweenResolverCases => new (string Name, Action Run)[] {
        ("Tween resolver binds all five fixed actual instance declarations with their exact native ABI", TweenResolverAdmission),
        ("Tween resolver rejects static virtual abstract generic and inflated production methods", TweenResolverFlags),
        ("Tween resolver requires the exact official non-byref TweenInfo reference identity for both reference arguments", TweenResolverArguments),
        ("Tween resolver rejects shared placeholder foreign and changed method entries", TweenResolverEntries),
        ("Tween resolver rechecks actual MethodInfo layout against official exports for every operation", TweenResolverLayout),
        ("Tween resolver rejects inherited foreign declaring metadata and renamed actual methods", TweenResolverOwners),
        ("Tween resolver rejects worker missing-module and unknown-operation access before metadata resolution", TweenResolverThread),
        ("Tween admission resolves actual official non-generic class chain and cached pointer metadata without roots or calls", TweenAdmissionMetadata),
        ("Tween admission rejects changed official class ancestry image field ownership type and offset", TweenAdmissionMetadataRejection),
        ("Tween admission metadata rejects worker access before runtime queries", TweenAdmissionMetadataThread),
        ("Tween source metadata validates actual rooted manager queue array and event roles without journal pointer adoption", TweenSourceMetadata),
        ("Tween source metadata rejects wrong role changed official fields and worker access", TweenSourceMetadataRejection),
        ("Single event resolver binds only the actual closed UnityEvent<float>.Invoke at 0xBC0FF0", SingleEventAdmission),
        ("Single event resolver rejects open wrong-parent wrong-argument changed-entry and foreign metadata", SingleEventRejection),
        ("Single event resolver rejects worker access before metadata enumeration", SingleEventThread),
        ("Rooted Single event observation preserves exact float bits across all five Tween caller returns", SingleEventFrames),
        ("Rooted Single event observation rejects forged targets changed roots wrong callers and invalid frames", SingleEventFrameRejection)
    };

    private static void TweenResolverAdmission()
    {
        using var f = new Fixture(tween: true);
        foreach (var pair in f.TweenMethods)
        {
            var target = f.Api.ResolveTweenOperation(f.Module, pair.Key); var method = pair.Value;
            var expectedAbi = pair.Key is NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween
                ? ExactHookAbi.OperationReference1Void : ExactHookAbi.OperationVoid0;
            Check(target.Id == "local.tween.operation." + pair.Key && target.Address == method.Entry
                && target.MethodInfo == method.Pointer && target.DeclaringClass == method.Class
                && target.Abi == expectedAbi && target.ReturnClass == IntPtr.Zero);
        }
        Check(f.TweenMethods.Count == 5 && f.ClassLookups == 2 && f.MethodExports > 0 && f.Enumerations > 0);
        NoTweenInvocation(f);
    }

    private static void TweenResolverFlags()
    {
        foreach (var operation in Enum.GetValues<NativeTweenOperation>())
            foreach (var mode in new[] { "static", "virtual", "abstract", "generic", "inflated" })
            {
                using var f = new Fixture(tween: true); var method = f.TweenMethods[operation];
                if (mode == "static") method.Flags = 0x10;
                if (mode == "virtual") method.Flags = 0x40;
                if (mode == "abstract") method.Flags = 0x400;
                if (mode == "generic") method.Generic = true;
                if (mode == "inflated") method.Inflated = true;
                f.Sync(method); RefuseTween(f, operation);
            }
    }

    private static void TweenResolverArguments()
    {
        foreach (var operation in new[] { NativeTweenOperation.AddTweenToWaitingList, NativeTweenOperation.ExecuteTween })
            foreach (var mode in new[] { "missing", "extra", "wrong-name", "byref", "value", "same-name-foreign-class", "nonvoid", "byref-void" })
            {
                using var f = new Fixture(tween: true); var method = f.TweenMethods[operation];
                if (mode == "missing") method.Parameters = Array.Empty<IntPtr>();
                if (mode == "extra") method.Parameters = new[] { f.TweenType, f.TweenType };
                if (mode == "wrong-name") method.Parameters = new[] { f.ObjectType };
                if (mode == "byref") f.Types[f.TweenType].ByRef = true;
                if (mode == "value") { f.Types[f.TweenType].Value = true; f.Types[f.TweenType].Code = 17; f.Types[f.TweenType].Size = 16; }
                if (mode == "same-name-foreign-class") method.Parameters = new[] { f.ForeignNamedTweenType() };
                if (mode == "nonvoid") method.Return = f.ObjectType;
                if (mode == "byref-void") f.Types[f.VoidType].ByRef = true;
                f.Sync(method); RefuseTween(f, operation);
            }
    }

    private static void TweenResolverEntries()
    {
        foreach (var operation in Enum.GetValues<NativeTweenOperation>())
            foreach (var mode in new[] { "other-operation", "shared-placeholder", "foreign-memory" })
            {
                using var f = new Fixture(tween: true); var method = f.TweenMethods[operation];
                if (mode == "other-operation") method.Entry = f.TweenMethods.First(pair => pair.Key != operation).Value.Entry;
                if (mode == "shared-placeholder") method.Entry = IntPtr.Add(f.Module, 0x1AFAD0);
                if (mode == "foreign-memory") method.Entry = f.Allocate(16);
                f.Sync(method); RefuseTween(f, operation);
            }
    }

    private static void TweenResolverLayout()
    {
        foreach (var operation in Enum.GetValues<NativeTweenOperation>())
            foreach (var mode in new[] { "name", "class", "return", "flags", "implementation", "count", "pointer" })
            {
                using var f = new Fixture(tween: true); var method = f.TweenMethods[operation];
                var actual = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.Pointer);
                if (mode == "name") actual.Name = f.AllocName("OtherMethod");
                if (mode == "class") actual.Class = f.TargetClass;
                if (mode == "return") actual.ReturnType = f.ObjectType;
                if (mode == "flags") actual.Flags = 0x10;
                if (mode == "implementation") actual.ImplementationFlags = 8;
                if (mode == "count") actual.ParameterCount = checked((byte)(method.Parameters.Length + 1));
                if (mode == "pointer") actual.MethodPointer = f.EventInvoke.Entry;
                Marshal.StructureToPtr(actual, method.Pointer, false); RefuseTween(f, operation);
            }
    }

    private static void TweenResolverOwners()
    {
        foreach (var operation in Enum.GetValues<NativeTweenOperation>())
            foreach (var mode in new[] { "inherited", "declaring-name", "method-name" })
            {
                using var f = new Fixture(tween: true); var method = f.TweenMethods[operation];
                if (mode == "inherited") f.MoveTweenMethodToParent(method);
                if (mode == "declaring-name") f.ChangeTweenDeclaringTypeName(method);
                if (mode == "method-name") method.NamePointer = f.AllocName("DifferentMethod");
                f.Sync(method); RefuseTween(f, operation);
            }
    }

    private static void TweenResolverThread()
    {
        using var f = new Fixture(tween: true);
        Task.Run(() => {
            foreach (var operation in Enum.GetValues<NativeTweenOperation>())
                Throws<InvalidOperationException>(() => f.Api.ResolveTweenOperation(f.Module, operation));
        }).GetAwaiter().GetResult();
        Throws<UnsupportedSignatureException>(() => f.Api.ResolveTweenOperation(IntPtr.Zero, NativeTweenOperation.Play));
        Throws<ArgumentOutOfRangeException>(() => f.Api.ResolveTweenOperation(f.Module, (NativeTweenOperation)99));
        Check(f.ClassLookups == 0 && f.Enumerations == 0 && f.MethodExports == 0); NoTweenInvocation(f);
    }

    private static void RefuseTween(Fixture f, NativeTweenOperation operation)
    {
        try { f.Api.ResolveTweenOperation(f.Module, operation); }
        catch (Exception error) when (error is UnsupportedSignatureException or MissingMethodException)
        { NoTweenInvocation(f); return; }
        throw new InvalidOperationException("Changed production Tween metadata was admitted: " + operation);
    }

    private static void NoTweenInvocation(Fixture f)
        => Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0 && f.Base.FreedRoots == 0 && f.FieldReads == 0);

    private static void TweenAdmissionMetadata()
    {
        using var f = new Fixture(tween: true); f.InitializeAdmissionMetadata();
        var result = f.AdmissionApi.InspectTweenAdmissionMetadata(f.Module);
        Check(result.TweenClass == f.AdmissionApi.Class("Assembly-CSharp.dll", "", "TweenInfo")
            && result.CachedPointerField.Field == f.CachedPointer.Pointer
            && result.CachedPointerField.TypeName == "System.IntPtr" && result.CachedPointerField.Size == 8
            && result.CachedPointerField.Offset == 16 && !result.CachedPointerField.IsStatic);
        NoTweenInvocation(f);
    }
    private static void TweenAdmissionMetadataRejection()
    {
        foreach (var mode in new[] { "generic", "inflated", "parent", "component-parent", "image", "offset", "static", "type", "byref", "shadow" })
        {
            using var f = new Fixture(tween: true); f.InitializeAdmissionMetadata(); f.ChangeAdmissionMetadata(mode);
            Throws<UnsupportedSignatureException>(() => f.AdmissionApi.InspectTweenAdmissionMetadata(f.Module)); NoTweenInvocation(f);
        }
    }
    private static void TweenAdmissionMetadataThread()
    {
        using var f = new Fixture(tween: true); f.InitializeAdmissionMetadata();
        Task.Run(() => Throws<InvalidOperationException>(() => f.AdmissionApi.InspectTweenAdmissionMetadata(f.Module))).GetAwaiter().GetResult();
        Check(f.ClassLookups == 0 && f.MethodExports == 0 && f.Enumerations == 0); NoTweenInvocation(f);
    }
    private static void TweenSourceMetadata()
    {
        using var f = new Fixture(tween: true); f.InitializeSourceMetadata();
        var expected = f.AdmissionApi.InspectTweenSourcesMetadata(f.Module);
        Check(expected.TweenClass == f.SourceTweenClass && expected.ManagerClass == f.SourceManagerClass
            && expected.Playing.Offset == 0x20 && expected.OnStart.Offset == 0x88 && expected.OnTween.Offset == 0x98);
        var value = f.Allocate(256); Marshal.WriteIntPtr(value, expected.ManagerClass);
        using var root = f.AdmissionApi.Root(value); var attempts = f.Base.NewRoots;
        Check(f.AdmissionApi.ValidateRootedTweenSourceObject(root, NativeTweenSourceObjectKind.Manager, expected, f.Module) == value);
        Check(f.Base.NewRoots == attempts && f.Base.ActiveRoots == 1 && f.Base.Invocations == 0 && f.FieldReads == 0);
    }
    private static void TweenSourceMetadataRejection()
    {
        using var f = new Fixture(tween: true); f.InitializeSourceMetadata();
        var expected = f.AdmissionApi.InspectTweenSourcesMetadata(f.Module); var value = f.Allocate(256);
        Marshal.WriteIntPtr(value, expected.ManagerClass); using var root = f.AdmissionApi.Root(value);
        Throws<UnsupportedSignatureException>(() => f.AdmissionApi.ValidateRootedTweenSourceObject(root, NativeTweenSourceObjectKind.Tween, expected, f.Module));
        var reads = f.MethodExports;
        Task.Run(() => Throws<InvalidOperationException>(() => f.AdmissionApi.ValidateRootedTweenSourceObject(root, NativeTweenSourceObjectKind.Manager, expected, f.Module))).GetAwaiter().GetResult();
        Check(f.MethodExports == reads);
        f.NamedField(expected.ManagerClass, "playingTweens").Offset = 0x28;
        Throws<UnsupportedSignatureException>(() => f.AdmissionApi.ValidateRootedTweenSourceObject(root, NativeTweenSourceObjectKind.Manager, expected, f.Module));
        Check(f.Base.NewRoots == 1 && f.Base.Invocations == 0 && f.FieldReads == 0);
    }
    private static void SingleEventAdmission()
    {
        using var f = new Fixture(tween: true); f.InitializeSingleEventMetadata();
        var target = f.AdmissionApi.ResolveObservedInstanceSingle1(f.Module, f.SingleEventClass, 0xbc0ff0);
        Check(target.Id == "local.unityevent.single.invoke" && target.Address == f.SingleEventInvoke.Entry
            && target.MethodInfo == f.SingleEventInvoke.Pointer && target.DeclaringClass == f.SingleEventClass
            && target.Abi == ExactHookAbi.OperationSingle1Void && target.ReturnClass == IntPtr.Zero);
        Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0 && f.FieldReads == 0);
    }
    private static void SingleEventRejection()
    {
        foreach (var mode in new[] { "zero-module", "wrong-rva", "open-generic", "not-inflated", "parent", "image",
            "name", "namespace", "type-name", "single-code", "single-size", "static", "abstract", "generic-method",
            "count", "parameter", "return", "entry", "layout" })
        {
            using var f = new Fixture(tween: true); f.InitializeSingleEventMetadata();
            var module = f.Module; var cls = f.SingleEventClass; var rva = 0xbc0ff0;
            if (mode == "zero-module") module = IntPtr.Zero;
            if (mode == "wrong-rva") rva = 0x15642e0;
            if (mode == "open-generic") f.Types[f.SingleEventType].GenericClass = true;
            if (mode == "not-inflated") f.Types[f.SingleEventType].InflatedClass = false;
            if (mode == "parent") f.SingleClassMetadata.Parent = f.ObjectTypeClass;
            if (mode == "image") f.SingleClassMetadata.ImageOverride = new IntPtr(0x12345678);
            if (mode == "name") f.SingleClassMetadata.Name = "OtherEvent`1";
            if (mode == "namespace") f.SingleClassMetadata.Name = "Fixture.UnityEvent<System.Single>";
            if (mode == "type-name") f.Types[f.SingleEventType].Name = "UnityEngine.Events.UnityEvent<System.Double>";
            if (mode == "single-code") f.Types[f.SingleType].Code = 2;
            if (mode == "single-size") f.Types[f.SingleType].Size = 8;
            if (mode == "static") f.SingleEventInvoke.Flags = 0x10;
            if (mode == "abstract") f.SingleEventInvoke.Flags = 0x400;
            if (mode == "generic-method") f.SingleEventInvoke.Generic = true;
            if (mode == "count") f.SingleEventInvoke.Parameters = Array.Empty<IntPtr>();
            if (mode == "parameter") f.SingleEventInvoke.Parameters = new[] { f.IntType };
            if (mode == "return") f.SingleEventInvoke.Return = f.ObjectType;
            if (mode == "entry") f.SingleEventInvoke.Entry = f.EventInvoke.Entry;
            if (mode == "layout")
            {
                var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(f.SingleEventInvoke.Pointer);
                layout.Class = f.EventClass; Marshal.StructureToPtr(layout, f.SingleEventInvoke.Pointer, false);
            }
            else f.Sync(f.SingleEventInvoke);
            try { f.AdmissionApi.ResolveObservedInstanceSingle1(module, cls, rva); }
            catch (Exception error) when (error is UnsupportedSignatureException or MissingMethodException)
            { Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0); continue; }
            throw new InvalidOperationException("Changed Single event metadata was admitted: " + mode);
        }
    }
    private static void SingleEventThread()
    {
        using var f = new Fixture(tween: true); f.InitializeSingleEventMetadata();
        Task.Run(() => Throws<InvalidOperationException>(() =>
            f.AdmissionApi.ResolveObservedInstanceSingle1(f.Module, f.SingleEventClass, 0xbc0ff0))).GetAwaiter().GetResult();
        Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0);
    }
    private static void SingleEventFrames()
    {
        using var f = new Fixture(tween: true); f.InitializeSingleEventMetadata();
        var value = f.Allocate(0x80); Marshal.WriteIntPtr(value, f.SingleEventClass);
        using var root = f.AdmissionApi.Root(value);
        var target = f.AdmissionApi.ResolveObservedInstanceSingle1(f.Module, f.SingleEventClass, 0xbc0ff0);
        foreach (var returnRva in new[] { 0x395c07, 0x3962ab, 0x397f8a, 0x397fbf, 0x398026 })
        {
            var caller = IntPtr.Add(f.Module, returnRva);
            var operation = returnRva < 0x396be0 ? NativeTweenOperation.Play : NativeTweenOperation.ExecuteTween;
            var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1,
                Self = new(value.ToInt64()), Arg0 = new(unchecked((long)(uint)0x7fc12345)),
                Method = target.MethodInfo, Caller = caller, Sequence = checked((ulong)returnRva) };
            var observation = f.AdmissionApi.ObserveRootedSingleEventInvocation(f.Module, root, target, frame, operation);
            Check(observation.Event == value && observation.ValueBits == 0x7fc12345u
                && observation.Method == target.MethodInfo && observation.Caller == caller
                && observation.Sequence == checked((ulong)returnRva) && float.IsNaN(observation.Value));
        }
        Check(f.Base.Invocations == 0 && root.Target == value && f.Base.ActiveRoots == 1);
    }
    private static void SingleEventFrameRejection()
    {
        foreach (var mode in new[] { "kind", "arg1", "self", "method", "caller", "bare-return-rva", "absolute-call-pc",
            "bare-call-rva", "wrong-parent", "call-opcode", "call-target", "sequence", "class", "target", "event-target", "event-class", "worker" })
        {
            using var f = new Fixture(tween: true); f.InitializeSingleEventMetadata();
            var value = f.Allocate(0x80); Marshal.WriteIntPtr(value, f.SingleEventClass);
            using var root = f.AdmissionApi.Root(value);
            var target = f.AdmissionApi.ResolveObservedInstanceSingle1(f.Module, f.SingleEventClass, 0xbc0ff0);
            var operation = NativeTweenOperation.Play;
            var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1,
                Self = new(value.ToInt64()), Arg0 = new(1), Method = target.MethodInfo,
                Caller = IntPtr.Add(f.Module, 0x395c07), Sequence = 1 };
            if (mode == "kind") frame.Kind = NativeOperationKind.Reference1;
            if (mode == "arg1") frame.Arg1 = new(2);
            if (mode == "self") frame.Self = new(value.ToInt64() + 8);
            if (mode == "method") frame.Method = IntPtr.Zero;
            if (mode == "caller") frame.Caller = new(0x1234);
            if (mode == "bare-return-rva") frame.Caller = new(0x395c07);
            if (mode == "absolute-call-pc") frame.Caller = IntPtr.Add(f.Module, 0x395c02);
            if (mode == "bare-call-rva") frame.Caller = new(0x395c02);
            if (mode == "wrong-parent") operation = NativeTweenOperation.ExecuteTween;
            if (mode == "call-opcode") f.WriteSingleEventCall(0x395c02, opcode: 0x90);
            if (mode == "call-target") f.WriteSingleEventCall(0x395c02, targetRva: 0xbc0ff1);
            if (mode == "sequence") frame.Sequence = 0;
            if (mode == "class") Marshal.WriteIntPtr(value, f.EventClass);
            if (mode == "target") target = target with { MethodInfo = IntPtr.Add(target.MethodInfo, 8) };
            if (mode == "event-target") target = target with { DeclaringClass = f.EventClass };
            if (mode == "event-class") Marshal.WriteIntPtr(value, f.EventClass);
            if (mode == "worker")
            {
                Task.Run(() => Throws<InvalidOperationException>(() =>
                    f.AdmissionApi.ObserveRootedSingleEventInvocation(f.Module, root, target, frame, operation))).GetAwaiter().GetResult();
            }
            else Throws<UnsupportedSignatureException>(() =>
                f.AdmissionApi.ObserveRootedSingleEventInvocation(f.Module, root, target, frame, operation));
            Check(f.Base.Invocations == 0);
        }
    }

    private sealed partial class Fixture
    {
        internal readonly Dictionary<NativeTweenOperation, MethodRow> TweenMethods = new();
        internal IntPtr TweenType;
        internal FieldRow CachedPointer = null!;
        internal IntPtr SourceTweenClass, SourceManagerClass;
        internal IntPtr SingleType, SingleEventType, SingleEventClass;
        internal MethodRow SingleEventInvoke = null!;
        internal TypeRow SingleClassMetadata = null!;
        internal IntPtr ObjectTypeClass => ClassOf(ObjectType);
        private IntPtr _sourceArrayClass;

        private void InitializeTweenMetadata()
        {
            TweenType = AddType("TweenInfo", 18, false, 8);
            var tween = ClassOf(TweenType); var manager = ClassOf(AddType("TweenManager", 18, false, 8));
            // Independent fixed declarations exercise production resolution; a
            // profile DTO compared with itself would miss a resolver admission bug.
            TweenMethods.Add(NativeTweenOperation.Play, AddMethod(tween, "Play", _void, 0x3956F0));
            TweenMethods.Add(NativeTweenOperation.AddTweenToWaitingList, AddMethod(manager, "AddTweenToWaitingList", _void, 0x396BE0, TweenType));
            TweenMethods.Add(NativeTweenOperation.ExecuteTween, AddMethod(manager, "ExecuteTween", _void, 0x396E40, TweenType));
            TweenMethods.Add(NativeTweenOperation.Update, AddMethod(manager, "Update", _void, 0x398D60));
            TweenMethods.Add(NativeTweenOperation.Awake, AddMethod(manager, "Awake", _void, 0x396CF0));
        }

        internal void InitializeSourceMetadata()
        {
            InitializeAdmissionMetadata(); SourceTweenClass = ClassOf(TweenType);
            SourceManagerClass = _classes.Values.Single(row => row.Name == "TweenManager").Class;
            _classes[SourceManagerClass].Parent = Types[TweenType].Parent;
            var array = AddType("TweenInfo[]", 29, false, 8); _sourceArrayClass = ClassOf(array);
            var list = AddType("System.Collections.Generic.List<TweenInfo>", 21, false, 8, ClassOf(ObjectType));
            var stack = AddType("System.Collections.Generic.Stack<TweenInfo>", 21, false, 8, ClassOf(ObjectType));
            Types[list].InflatedClass = Types[stack].InflatedClass = true;
            AddField(SourceManagerClass, "playingTweens", array, 0x20);
            AddField(SourceManagerClass, "scheduledTweens", list, 0x28); AddField(SourceManagerClass, "readyTweens", stack, 0x30);
            AddField(ClassOf(list), "_items", array, 0x10); AddField(ClassOf(list), "_size", IntType, 0x18); AddField(ClassOf(list), "_version", IntType, 0x1c);
            AddField(ClassOf(stack), "_array", array, 0x10); AddField(ClassOf(stack), "_size", IntType, 0x18); AddField(ClassOf(stack), "_version", IntType, 0x1c);
            AddField(SourceTweenClass, "onStart", _classTypeFor(EventClass), 0x88);
            AddField(SourceTweenClass, "onComplete", _classTypeFor(EventClass), 0x90);
            var progress = AddType("UnityEngine.Events.UnityEvent<System.Single>", 21, false, 8, EventBaseClass);
            Types[progress].InflatedClass = true; AddField(SourceTweenClass, "onTween", progress, 0x98);
            Bind("il2cpp_class_get_name", "Ptr1", nameof(SourceClassName));
            Bind("il2cpp_class_get_namespace", "Ptr1", nameof(SourceClassNamespace));
            Bind("il2cpp_class_get_rank", "Flags", nameof(SourceRank));
            Bind("il2cpp_class_get_element_class", "Ptr1", nameof(SourceElement));
            Bind("il2cpp_array_element_size", "ArrayElementSize", nameof(SourceElementSize));
            AdmissionApi = new NativeRuntime(name => _exports.TryGetValue(name, out var export) ? Marshal.GetFunctionPointerForDelegate(export) : Base.ExportForListenerTests(name));
            AdmissionApi.BindCurrentThread();
        }
        internal void InitializeSingleEventMetadata()
        {
            if (SingleEventClass != IntPtr.Zero)
            {
                AdmissionApi = new NativeRuntime(name => _exports.TryGetValue(name, out var export)
                    ? Marshal.GetFunctionPointerForDelegate(export) : Base.ExportForListenerTests(name));
                AdmissionApi.BindCurrentThread(); return;
            }
            InitializeAdmissionMetadata();
            SingleType = AddType("System.Single", 12, true, 4);
            SingleEventType = AddType("UnityEngine.Events.UnityEvent<System.Single>", 21, false, 8, EventBaseClass);
            // IL2CPP distinguishes an open generic definition from an inflated
            // closed instance: only the latter represents UnityEvent<Single>.
            Types[SingleEventType].GenericClass = false; Types[SingleEventType].InflatedClass = true;
            SingleEventClass = ClassOf(SingleEventType); SingleClassMetadata = Types[SingleEventType];
            SingleEventInvoke = AddMethod(SingleEventClass, "Invoke", _void, 0xbc0ff0, SingleType);
            SingleEventInvoke.Inflated = true; Sync(SingleEventInvoke);
            foreach (var callRva in new[] { 0x395c02, 0x3962a6, 0x397f85, 0x397fba, 0x398021 })
                WriteSingleEventCall(callRva);
            Bind("il2cpp_class_get_name", "Ptr1", nameof(SingleClassName));
            Bind("il2cpp_class_get_namespace", "Ptr1", nameof(SingleClassNamespace));
            AdmissionApi = new NativeRuntime(name => _exports.TryGetValue(name, out var export)
                ? Marshal.GetFunctionPointerForDelegate(export) : Base.ExportForListenerTests(name));
            AdmissionApi.BindCurrentThread();
        }
        internal void WriteSingleEventCall(int callRva, byte opcode = 0xe8, int targetRva = 0xbc0ff0)
        {
            // This is the fixture's synthetic SEC_IMAGE only; these bytes are
            // inspected by admission and are never invoked as executable code.
            var address = IntPtr.Add(Module, callRva);
            if (!VirtualProtectSingleFixture(address, 5, 0x40, out var previous))
                throw new InvalidOperationException("Synthetic Single callsite could not be made writable.");
            try { Marshal.WriteByte(address, opcode); Marshal.WriteInt32(address, 1, checked(targetRva - callRva - 5)); }
            finally
            {
                if (!VirtualProtectSingleFixture(address, 5, previous, out _))
                    throw new InvalidOperationException("Synthetic Single callsite protection could not be restored.");
            }
        }
        [DllImport("kernel32", EntryPoint = "VirtualProtect", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualProtectSingleFixture(IntPtr address, nuint size, uint protection, out uint previous);
        private IntPtr SingleClassName(IntPtr cls) => cls == SingleEventClass
            ? AllocName("UnityEvent`1") : AdmissionClassName(cls);
        private IntPtr SingleClassNamespace(IntPtr cls)
        {
            if (cls != SingleEventClass) return AdmissionClassNamespace(cls);
            var name = SingleClassMetadata.Name;
            var generic = name.IndexOf('<');
            if (generic >= 0) name = name[..generic];
            var separator = name.LastIndexOf('.');
            return AllocName(separator < 0 ? "" : name[..separator]);
        }
        private IntPtr _classTypeFor(IntPtr cls) => _classes[cls].Type;
        private IntPtr SourceClassName(IntPtr cls)
        {
            var name = _classes[cls].Name;
            return AllocName(name.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) ? "List`1"
                : name.StartsWith("System.Collections.Generic.Stack<", StringComparison.Ordinal) ? "Stack`1"
                : name.StartsWith("UnityEngine.Events.UnityEvent<", StringComparison.Ordinal) ? "UnityEvent`1" : name.Split('.').Last());
        }
        private IntPtr SourceClassNamespace(IntPtr cls) => _classes[cls].Name.StartsWith("UnityEngine.Events.UnityEvent<", StringComparison.Ordinal)
            ? AllocName("UnityEngine.Events") : AdmissionClassNamespace(cls);
        private uint SourceRank(IntPtr cls) => cls == _sourceArrayClass ? 1u : 0u;
        private IntPtr SourceElement(IntPtr cls) => cls == _sourceArrayClass ? SourceTweenClass : IntPtr.Zero;
        private int SourceElementSize(IntPtr cls) => cls == _sourceArrayClass ? 8 : 0;

        internal IntPtr ForeignNamedTweenType()
        {
            var alias = AddType("TweenInfo", 18, false, 8);
            Types[alias].LookupName = "Fixture.UnresolvableAlias";
            return alias;
        }
        internal void MoveTweenMethodToParent(MethodRow method)
        { _classes[method.Class].Parent = TargetClass; method.Class = TargetClass; }
        internal void ChangeTweenDeclaringTypeName(MethodRow method)
        { _classes[method.Class].Name = "Fixture.ForeignTweenType"; }

        internal void InitializeAdmissionMetadata()
        {
            var unityObject = ClassOf(AddType("UnityEngine.Object", 18, false, 8, ClassOf(ObjectType)));
            var component = ClassOf(AddType("UnityEngine.Component", 18, false, 8, unityObject));
            var behaviour = ClassOf(AddType("UnityEngine.Behaviour", 18, false, 8, component));
            var mono = ClassOf(AddType("UnityEngine.MonoBehaviour", 18, false, 8, behaviour));
            Types[TweenType].Parent = mono;
            CachedPointer = AddField(unityObject, "m_CachedPtr", PointerType, 16);
            Bind("il2cpp_class_get_image", "Ptr1", nameof(AdmissionClassImage));
            Bind("il2cpp_class_get_name", "Ptr1", nameof(AdmissionClassName)); Bind("il2cpp_class_get_namespace", "Ptr1", nameof(AdmissionClassNamespace));
            Bind("il2cpp_class_is_generic", "Predicate", nameof(AdmissionGenericClass)); Bind("il2cpp_class_is_inflated", "Predicate", nameof(AdmissionInflatedClass));
            Bind("il2cpp_class_instance_size", "ArraySize", nameof(AdmissionInstanceSize));
            Bind("il2cpp_class_get_fields", "Ptr2", nameof(AdmissionFields)); Bind("il2cpp_field_get_parent", "Ptr1", nameof(AdmissionFieldParent));
            Bind("il2cpp_field_get_name", "Ptr1", nameof(AdmissionFieldName));
            // NativeRuntime resolves delegates in its constructor, so replace
            // the tested API with a fresh bound instance after adding exports.
            AdmissionApi = new NativeRuntime(name => _exports.TryGetValue(name, out var export) ? Marshal.GetFunctionPointerForDelegate(export) : Base.ExportForListenerTests(name));
            AdmissionApi.BindCurrentThread();
        }
        internal NativeRuntime AdmissionApi = null!;
        internal void ChangeAdmissionMetadata(string mode)
        {
            if (mode == "generic") Types[TweenType].GenericClass = true;
            if (mode == "inflated") Types[TweenType].InflatedClass = true;
            if (mode == "parent") Types[TweenType].Parent = TargetClass;
            if (mode == "component-parent") _classes[Types[TweenType].Parent].Parent = ClassOf(ObjectType);
            if (mode == "image") Types[TweenType].ImageOverride = _unity;
            if (mode == "offset") CachedPointer.Offset = 24;
            if (mode == "static") CachedPointer.Flags = 0x10;
            if (mode == "type") CachedPointer.Type = ObjectType;
            if (mode == "byref") Types[PointerType].ByRef = true;
            if (mode == "shadow") AddField(ClassOf(TweenType), "m_CachedPtr", PointerType, 16);
        }
        private IntPtr AdmissionClassImage(IntPtr cls)
        { var row = _classes[cls]; return row.ImageOverride != IntPtr.Zero ? row.ImageOverride : row.Name.StartsWith("UnityEngine.", StringComparison.Ordinal) ? _unity : row.Name.StartsWith("System.", StringComparison.Ordinal) ? _mscorlib : _game; }
        private IntPtr AdmissionClassName(IntPtr cls) => AllocName(_classes[cls].Name.Split('.').Last());
        private IntPtr AdmissionClassNamespace(IntPtr cls) { var name = _classes[cls].Name; var index = name.LastIndexOf('.'); return AllocName(index < 0 ? "" : name[..index]); }
        private bool AdmissionGenericClass(IntPtr cls) => _classes[cls].GenericClass;
        private bool AdmissionInflatedClass(IntPtr cls) => _classes[cls].InflatedClass;
        private uint AdmissionInstanceSize(IntPtr cls) => 256;
        private IntPtr AdmissionFields(IntPtr cls, IntPtr iterator)
        { var rows = _fields.Where(field => field.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32(); if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer; }
        private IntPtr AdmissionFieldParent(IntPtr field) => Field(field).Class;
        private IntPtr AdmissionFieldName(IntPtr field) => AllocName(Field(field).Name);
    }
}
