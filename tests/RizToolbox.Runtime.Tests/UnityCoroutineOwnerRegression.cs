using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// The actual runtime API runs over readable unmanaged receiver buffers and
// synthetic official-export metadata. No Unity owner is dereferenced; the high
// sentinel owner is intentionally not a usable object/node pointer.
internal static class UnityCoroutineOwnerRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Unity coroutine owner projection reads official cached pointer twice and preserves borrowed root without native owner access", Projection),
        ("Unity coroutine owner projection rejects changed declaring field offset static type storage and byref metadata before field reads", Metadata),
        ("Unity coroutine owner projection requires actual MonoBehaviour and Object ancestry before cached-pointer reads", Ancestry),
        ("Unity coroutine owner projection rejects null or changing cached owner and never calls a fallback native method", OwnerChanges),
        ("Unity coroutine owner projection detects rooted receiver and actual class changes across its bounded reads", ReceiverChanges),
        ("Unity coroutine owner projection rejects disposed null and mismatched roots before metadata or field access", Roots),
        ("Unity coroutine owner projection refuses workers and foreign-thread roots before export reads or root disposal", Threads),
        ("Retained Unity receiver observation copies stable zero twice while the strict live-owner API still rejects it", StableZero),
        ("Retained Unity receiver observation rejects zero-to-live and class or root changes without freeing responsibility", ZeroChanges),
        ("Retained Unity receiver observation validates metadata and thread before reading a zero cached pointer", ObservationGuards)
    };
    private static void Check(bool value, string reason = "Unity coroutine owner regression failed.")
    { if (!value) throw new InvalidOperationException(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Projection()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Receiver);
        var result = f.Api.ReadUnityCoroutineOwner(root, f.Receiver.ToInt64());
        Check(result.ManagedReceiver == f.Receiver.ToInt64() && result.NativeOwner == Fixture.Owner.ToInt64()
            && result.ManagedClass == f.ControllerClass && result.NativeOwner != result.ManagedReceiver
            && root.Target == f.Receiver && f.Reads == 2 && f.LastReadReceiver == f.Receiver
            && f.LastReadField == f.Field && f.LastLookupClass == f.ObjectClass && f.LastLookupName == "m_CachedPtr");
        Check(f.Base.ActiveRoots == 1 && f.Base.NewRoots == 1 && f.Base.FreedRoots == 0 && f.Base.Invocations == 0);
        Check(Marshal.ReadIntPtr(f.Receiver, 0x10) == Fixture.Owner && f.ExportFailure is null);
    }
    private static void Metadata()
    {
        foreach (var mode in new[] { "declaring", "offset", "static", "type", "name", "size", "reference", "byref" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Receiver);
            switch (mode)
            {
                case "declaring": f.FieldParent = f.ControllerClass; break;
                case "offset": f.Offset = 0x18; break;
                case "static": f.FieldFlags = 0x10; break;
                case "type": f.FieldType = f.ObjectType; break;
                case "name": f.PointerName = "System.UIntPtr"; break;
                case "size": f.PointerSize = 4; break;
                case "reference": f.PointerIsValue = false; break;
                case "byref": f.PointerByRef = true; break;
            }
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityCoroutineOwner(root, f.Receiver.ToInt64()));
            Check(f.Reads == 0 && f.Base.Invocations == 0 && f.Base.ActiveRoots == 1 && f.Base.FreedRoots == 0 && f.ExportFailure is null);
        }
    }
    private static void Ancestry()
    {
        foreach (var mode in new[] { "receiver", "monobehaviour" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Receiver);
            if (mode == "receiver") Marshal.WriteIntPtr(f.Receiver, f.ForeignClass);
            else f.MonoParent = IntPtr.Zero;
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityCoroutineOwner(root, f.Receiver.ToInt64()));
            Check(f.Reads == 0 && f.Lookups == 0 && f.Base.Invocations == 0 && f.ExportFailure is null);
        }
    }
    private static void OwnerChanges()
    {
        foreach (var changeAt in new[] { 0, 2 })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Receiver);
            if (changeAt == 0) Marshal.WriteIntPtr(f.Receiver, 0x10, IntPtr.Zero);
            else f.ChangeOwnerAtRead = changeAt;
            Throws<TargetExpiredException>(() => f.Api.ReadUnityCoroutineOwner(root, f.Receiver.ToInt64()));
            Check(f.Reads == (changeAt == 0 ? 1 : 2) && root.Target == f.Receiver
                && f.Base.Invocations == 0 && f.Base.ActiveRoots == 1 && f.Base.FreedRoots == 0 && f.ExportFailure is null);
        }
    }
    private static void ReceiverChanges()
    {
        foreach (var mode in new[] { "root-after-first", "root-after-second", "class" })
        {
            using var f = new Fixture();
            var targetReads = 0; var frees = 0;
            using var root = new NativeRuntime.RootHandle(1, _ => ++frees, _ => {
                ++targetReads;
                return mode == "root-after-first" && targetReads >= 2 || mode == "root-after-second" && targetReads >= 3
                    ? f.OtherReceiver : f.Receiver;
            });
            if (mode == "class") f.ChangeClassAtRead = 1;
            Throws<TargetExpiredException>(() => f.Api.ReadUnityCoroutineOwner(root, f.Receiver.ToInt64()));
            Check(f.Reads == (mode == "root-after-second" ? 2 : 1) && frees == 0 && f.Base.Invocations == 0 && f.ExportFailure is null);
        }
    }
    private static void Roots()
    {
        using var f = new Fixture(); var disposed = f.Api.Root(f.Receiver); disposed.Dispose();
        Throws<TargetExpiredException>(() => f.Api.ReadUnityCoroutineOwner(disposed, f.Receiver.ToInt64()));
        using var live = f.Api.Root(f.Receiver);
        Throws<TargetExpiredException>(() => f.Api.ReadUnityCoroutineOwner(live, f.OtherReceiver.ToInt64()));
        Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityCoroutineOwner(live, 0));
        Throws<ArgumentNullException>(() => f.Api.ReadUnityCoroutineOwner(null!, f.Receiver.ToInt64()));
        Check(f.Reads == 0 && f.AssemblyReads == 0 && f.Base.ActiveRoots == 1 && f.Base.FreedRoots == 1 && f.Base.Invocations == 0);
    }
    private static void Threads()
    {
        using var f = new Fixture(); using var live = f.Api.Root(f.Receiver);
        Task.Run(() => Throws<InvalidOperationException>(() => f.Api.ReadUnityCoroutineOwner(live, f.Receiver.ToInt64()))).GetAwaiter().GetResult();
        var access = new NativeThreadAccess(); Task.Run(access.BindCurrentThread).GetAwaiter().GetResult();
        var frees = 0; var targets = 0;
        var foreign = new NativeRuntime.RootHandle(2, _ => ++frees, _ => { ++targets; return f.Receiver; }, access.Verify);
        Throws<InvalidOperationException>(() => f.Api.ReadUnityCoroutineOwner(foreign, f.Receiver.ToInt64()));
        Check(f.Reads == 0 && f.AssemblyReads == 0 && targets == 0 && frees == 0
            && f.Base.ActiveRoots == 1 && f.Base.FreedRoots == 0 && f.ExportFailure is null);
        // The foreign synthetic root has no native/GC resource. It remains an
        // unconsumed thread-owned responsibility; this test never disposes it
        // on the wrong thread or uses a worker to operate the runtime.
        GC.KeepAlive(foreign);
    }

    private static void StableZero()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Receiver);
        Marshal.WriteIntPtr(f.Receiver, 0x10, IntPtr.Zero);
        var value = f.Api.ObserveUnityCoroutineOwner(root, f.Receiver.ToInt64());
        Check(value.ManagedReceiver == f.Receiver.ToInt64() && value.ManagedClass == f.ControllerClass
            && value.CachedNativeOwner == 0 && f.Reads == 2 && root.Target == f.Receiver
            && f.Base.ActiveRoots == 1 && f.Base.FreedRoots == 0 && f.Base.Invocations == 0);
        Throws<TargetExpiredException>(() => f.Api.ReadUnityCoroutineOwner(root, f.Receiver.ToInt64()));
        Check(f.Reads == 3 && f.Base.ActiveRoots == 1 && f.Base.FreedRoots == 0 && f.ExportFailure is null);
    }
    private static void ZeroChanges()
    {
        foreach (var mode in new[] { "owner", "class", "class-after-second", "root" })
        {
            using var f = new Fixture(); Marshal.WriteIntPtr(f.Receiver, 0x10, IntPtr.Zero);
            var rootReads = 0; var frees = 0;
            using var root = new NativeRuntime.RootHandle(11, _ => ++frees, _ => {
                ++rootReads; return mode == "root" && rootReads >= 2 ? f.OtherReceiver : f.Receiver;
            });
            if (mode == "owner") f.ChangeOwnerAtRead = 2;
            if (mode == "class") f.ChangeClassAtRead = 1;
            if (mode == "class-after-second") f.ChangeClassAtRead = 2;
            Throws<TargetExpiredException>(() => f.Api.ObserveUnityCoroutineOwner(root, f.Receiver.ToInt64()));
            Check(f.Reads == (mode is "owner" or "class-after-second" ? 2 : 1)
                && frees == 0 && f.Base.Invocations == 0 && f.ExportFailure is null);
        }
    }
    private static void ObservationGuards()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Receiver);
        Marshal.WriteIntPtr(f.Receiver, 0x10, IntPtr.Zero); f.Offset = 0x18;
        Task.Run(() => Throws<InvalidOperationException>(() => f.Api.ObserveUnityCoroutineOwner(root, f.Receiver.ToInt64()))).GetAwaiter().GetResult();
        Check(f.Reads == 0 && f.AssemblyReads == 0);
        Throws<UnsupportedSignatureException>(() => f.Api.ObserveUnityCoroutineOwner(root, f.Receiver.ToInt64()));
        Check(f.Reads == 0 && f.Base.FreedRoots == 0 && f.Base.Invocations == 0 && f.ExportFailure is null);
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly IntPtr Owner = new(unchecked((long)0x8123456789abcdefUL));
        internal readonly FakeNative Base = new();
        private readonly List<IntPtr> _allocations = new();
        private readonly Dictionary<string, Delegate> _exports = new();
        internal readonly IntPtr Receiver, OtherReceiver, ObjectClass, MonoClass, ControllerClass, ForeignClass, PointerClass, ObjectType, PointerType, Field;
        private readonly IntPtr _assemblies, _assembly, _imageName;
        internal readonly NativeRuntime Api;
        internal IntPtr FieldParent, FieldType, MonoParent;
        internal int Offset = 0x10, PointerSize = 8;
        internal uint FieldFlags;
        internal bool PointerByRef, PointerIsValue = true;
        internal string PointerName = "System.IntPtr";
        internal int Reads, Lookups, AssemblyReads, ChangeOwnerAtRead, ChangeClassAtRead;
        internal IntPtr LastLookupClass, LastReadReceiver, LastReadField;
        internal string? LastLookupName, ExportFailure;
        internal Fixture()
        {
            Receiver = Allocate(0x40); OtherReceiver = Allocate(0x40);
            ObjectClass = Allocate(0x40); MonoClass = Allocate(0x40); ControllerClass = Allocate(0x40); ForeignClass = Allocate(0x40);
            PointerClass = Allocate(0x40); ObjectType = Allocate(0x20); PointerType = Allocate(0x20); Field = Allocate(0x20);
            _assemblies = Allocate(0x10); _assembly = Allocate(0x10); _imageName = Name("UnityEngine.CoreModule.dll");
            Marshal.WriteIntPtr(_assemblies, _assembly); Marshal.WriteIntPtr(Receiver, ControllerClass); Marshal.WriteIntPtr(OtherReceiver, ControllerClass);
            Marshal.WriteIntPtr(Receiver, 0x10, Owner); Marshal.WriteIntPtr(OtherReceiver, 0x10, Owner);
            FieldParent = ObjectClass; FieldType = PointerType; MonoParent = ObjectClass;
            Bind("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies));
            Bind("il2cpp_assembly_get_image", "Ptr1", nameof(Identity)); Bind("il2cpp_image_get_name", "Ptr1", nameof(ImageName));
            Bind("il2cpp_class_from_name", "ClassLookup", nameof(FindClass)); Bind("il2cpp_object_get_class", "Ptr1", nameof(ReadClass));
            Bind("il2cpp_class_get_parent", "Ptr1", nameof(Parent)); Bind("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable));
            Bind("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField)); Bind("il2cpp_field_get_parent", "Ptr1", nameof(Declaring));
            Bind("il2cpp_field_get_type", "Ptr1", nameof(Type)); Bind("il2cpp_field_get_flags", "Flags", nameof(Flags));
            Bind("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset)); Bind("il2cpp_field_get_value", "FieldGet", nameof(ReadField));
            Bind("il2cpp_type_is_byref", "Predicate", nameof(ByRef)); Bind("il2cpp_type_get_name", "Ptr1", nameof(TypeName));
            Bind("il2cpp_class_from_type", "Ptr1", nameof(TypeClass)); Bind("il2cpp_class_is_valuetype", "Predicate", nameof(ValueType));
            Bind("il2cpp_class_is_enum", "Predicate", nameof(IsEnum));
            Bind("il2cpp_class_value_size", "ValueSize", nameof(ValueSize)); Bind("il2cpp_type_get_type", "TypeCode", nameof(Code));
            Api = new NativeRuntime(name => _exports.TryGetValue(name, out var value) ? Marshal.GetFunctionPointerForDelegate(value) : Base.ExportForListenerTests(name));
            Api.BindCurrentThread();
        }
        private IntPtr Allocate(int bytes)
        { var pointer = Marshal.AllocHGlobal(bytes); _allocations.Add(pointer); Marshal.Copy(new byte[bytes], 0, pointer, bytes); return pointer; }
        private IntPtr Name(string text)
        { var pointer = Marshal.StringToHGlobalAnsi(text); _allocations.Add(pointer); return pointer; }
        private void Bind(string export, string type, string method)
            => _exports[export] = Delegate.CreateDelegate(typeof(NativeRuntime).GetNestedType(type, BindingFlags.NonPublic)!, this,
                GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!);
        // Every reverse-P/Invoke export is total over its actual fixture inputs;
        // faults are recorded as scalars rather than thrown across native code.
        private IntPtr Assemblies(IntPtr domain, IntPtr size) { ++AssemblyReads; Marshal.WriteIntPtr(size, new IntPtr(1)); return _assemblies; }
        private IntPtr Identity(IntPtr value) => value;
        private IntPtr ImageName(IntPtr image) => _imageName;
        private IntPtr FindClass(IntPtr image, string ns, string name)
            => image != _assembly || ns != "UnityEngine" ? IntPtr.Zero : name == "Object" ? ObjectClass : name == "MonoBehaviour" ? MonoClass : IntPtr.Zero;
        private IntPtr ReadClass(IntPtr obj) => obj == Receiver || obj == OtherReceiver ? Marshal.ReadIntPtr(obj) : IntPtr.Zero;
        private IntPtr Parent(IntPtr cls) => cls == ControllerClass ? MonoClass : cls == MonoClass ? MonoParent : IntPtr.Zero;
        private bool Assignable(IntPtr expected, IntPtr actual)
        { for (var count = 0; actual != IntPtr.Zero && count < 8; ++count, actual = Parent(actual)) if (expected == actual) return true; return false; }
        private IntPtr FindField(IntPtr cls, string name)
        { ++Lookups; LastLookupClass = cls; LastLookupName = name; return cls == ObjectClass && name == "m_CachedPtr" ? Field : IntPtr.Zero; }
        private IntPtr Declaring(IntPtr field) => FieldParent;
        private IntPtr Type(IntPtr field) => FieldType;
        private uint Flags(IntPtr field) => FieldFlags;
        private int FieldOffset(IntPtr field) => Offset;
        private bool ByRef(IntPtr type) => type == PointerType && PointerByRef;
        private IntPtr TypeName(IntPtr type) => Marshal.StringToCoTaskMemUTF8(type == PointerType ? PointerName : "UnityEngine.Object");
        private IntPtr TypeClass(IntPtr type) => type == PointerType ? PointerClass : ObjectClass;
        private bool ValueType(IntPtr cls) => cls == PointerClass && PointerIsValue;
        private bool IsEnum(IntPtr cls) => false;
        private int ValueSize(IntPtr cls, out uint alignment) { alignment = 8; return cls == PointerClass ? PointerSize : 0; }
        private int Code(IntPtr type) => type == PointerType ? 24 : 18;
        private void ReadField(IntPtr receiver, IntPtr field, IntPtr output)
        {
            ++Reads; LastReadReceiver = receiver; LastReadField = field;
            if (receiver != Receiver || field != Field) { ExportFailure = "Unexpected actual field read identity."; Marshal.WriteIntPtr(output, IntPtr.Zero); return; }
            if (Reads == ChangeOwnerAtRead) Marshal.WriteIntPtr(Receiver, 0x10, new IntPtr(unchecked((long)0x923456789abcdef0UL)));
            if (Reads == ChangeClassAtRead) Marshal.WriteIntPtr(Receiver, ForeignClass);
            Marshal.WriteIntPtr(output, Marshal.ReadIntPtr(Receiver, 0x10));
        }
        public void Dispose()
        { foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer); Base.Dispose(); GC.KeepAlive(_exports); }
    }
}
