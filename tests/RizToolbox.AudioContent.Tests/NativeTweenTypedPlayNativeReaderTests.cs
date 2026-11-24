using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

// The bounded unmanaged metadata/export and SEC_IMAGE fixture is adapted from
// Runtime.Tests/ListenerMetadataRegression. No game or synthetic image code runs.
internal static partial class NativeTweenTypedPlayNativeReaderTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Single production Play reader roots the exact direct delegate and validates native MethodInfo entry and full signature", NativeReader),
        ("Single production Play reader keeps foreign multicast virtual adapted and changed metadata consumers unclassified", NativeReaderRefusals),
        ("Single production Play reader rejects a delegate chain that mutates during rooted native reads", NativeReaderMutation)
    };
    private static void NativeReader()
    {
        using var f = new Fixture(); f.InitializePlayConsumer(); var result = f.Read();
        Check(result is not null && result.Invokable == f.Invokable.ToInt64() && result.Action == f.Delegate.ToInt64()
            && result.Tween == f.PlayObject.ToInt64() && result.Method == f.Play.Pointer.ToInt64()
            && result.Entry == f.Play.Entry.ToInt64() && f.Roots.ContainsKey(result.Tween));
        Check(f.Base.Invocations == 0 && f.Base.FreedRoots == 0 && f.Base.ActiveRoots == 3);
    }
    private static void NativeReaderRefusals()
    {
        foreach (var mode in new[] { "foreign", "multicast", "virtual", "invoke", "code", "methodptr", "method", "return", "argument", "static", "generic", "inflated", "declaring", "entry" })
        {
            using var f = new Fixture(); f.InitializePlayConsumer();
            switch (mode)
            {
                case "foreign": f.Owned = false; break;
                case "multicast": Marshal.WriteIntPtr(f.Delegate, 0x78, f.Target); break;
                case "virtual": Marshal.WriteByte(f.Delegate, 0x70, 1); break;
                case "invoke": Marshal.WriteIntPtr(f.Delegate, 0x18, f.Listener.Entry); break;
                case "code": Marshal.WriteIntPtr(f.Delegate, 0x40, f.Target); break;
                case "methodptr": Marshal.WriteIntPtr(f.Delegate, 0x10, f.Listener.Entry); break;
                case "method": Marshal.WriteIntPtr(f.Delegate, 0x28, f.Listener.Pointer); break;
                case "return": f.Play.Return = f.ObjectType; f.Sync(f.Play); break;
                case "argument": f.Play.Parameters = new[] { f.IntType }; f.Sync(f.Play); break;
                case "static": f.Play.Flags = 0x10; f.Sync(f.Play); break;
                case "generic": f.Play.Generic = true; break;
                case "inflated": f.Play.Inflated = true; break;
                case "declaring": f.Play.Class = f.TargetClass; f.Sync(f.Play); break;
                case "entry": f.Play.Entry = f.Listener.Entry; f.Sync(f.Play); break;
            }
            try { Check(f.Read() is null); }
            catch (Exception error) when (error is UnsupportedSignatureException or MissingMethodException) { }
            Check(f.Base.Invocations == 0);
        }
    }
    private static void NativeReaderMutation()
    {
        using var f = new Fixture(); f.InitializePlayConsumer();
        f.OnRead = field => { if (field.Name == "method_code") { f.OnRead = null; Marshal.WriteIntPtr(f.Invokable, 0x10, IntPtr.Zero); } };
        Throws<UnsupportedSignatureException>(() => f.Read());
        Check(f.Base.Invocations == 0 && f.Roots.ContainsKey(f.PlayObject.ToInt64()) && f.Base.FreedRoots == 0);
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
        private readonly Image _image; private readonly IntPtr _assemblies, _mscorlib, _unity, _game, _mscorlibName, _unityName, _gameName;
        internal readonly IntPtr Module, ObjectType, PointerType, IntType, ListType, DelegateClass, ActionClass, TargetClass, EventBaseClass, CallsClass, ListClass, Delegate, Target, EventClass;
        internal readonly IntPtr InvokableClass, ActionType;
        internal IntPtr VoidType => _void;
        private readonly IntPtr _void, _boolean, _multicast, _delegateArray;
        internal readonly MethodRow Listener, EventPrepare, ListPrepare, EventInvoke, AddDelegate, RemoveDelegate;
        internal readonly NativeRuntime Api;
        internal IntPtr TweenClass, PlayObject, Invokable;
        internal MethodRow Play = null!, InvokableInvoke = null!;
        internal readonly Dictionary<long, NativeRuntime.RootHandle> Roots = new();
        internal bool Owned = true;
        internal NativeExactReadField DelegatesField = null!, TargetField = null!;
        internal void InitializePlayConsumer()
        {
            TweenClass = ClassOf(AddType("TweenInfo", 18, false, 8));
            Play = AddMethod(TweenClass, "Play", _void, 0x3956f0);
            InvokableInvoke = AddMethod(InvokableClass, "Invoke", _void, 0x154e5b0);
            AddField(InvokableClass, "Delegate", ActionType, 0x10);
            PlayObject = Allocate(0x80); Marshal.WriteIntPtr(PlayObject, TweenClass);
            Invokable = Allocate(0x80); Marshal.WriteIntPtr(Invokable, InvokableClass); Marshal.WriteIntPtr(Invokable, 0x10, Delegate);
            Marshal.WriteIntPtr(Delegate, 0x20, PlayObject); Marshal.WriteIntPtr(Delegate, 0x28, Play.Pointer);
            Marshal.WriteIntPtr(Delegate, 0x10, Play.Entry); Marshal.WriteIntPtr(Delegate, 0x18, Play.Entry); Marshal.WriteIntPtr(Delegate, 0x40, PlayObject);
            DelegatesField = Api.InspectExactDeclaredReadField(_multicast, "delegates", "System.Delegate[]", false, 0x78);
            TargetField = Api.InspectExactDeclaredReadField(DelegateClass, "m_target", "System.Object", false, 0x20);
            Root(Invokable.ToInt64());
        }
        internal NativeRuntime.RootHandle Root(long identity)
        {
            if (!Roots.TryGetValue(identity, out var root)) Roots.Add(identity, root = Api.Root(new(identity)));
            return root;
        }
        internal long Reference(long identity, NativeExactReadField field, bool nullable)
        {
            long result = 0;
            Api.PublishExactReferenceField(Root(identity), field, value => { result = value.ToInt64(); if (value != IntPtr.Zero) Root(result); });
            if (!nullable && result == 0) throw new InvalidOperationException("Missing native test reference.");
            return result;
        }
        internal NativeTweenTypedPlayConsumer? Read()
        {
            var frame = new NativeTweenSingleEventFrame { Stage = NativeTweenSingleEventStage.Before, Site = 1,
                Invokable = Invokable, ActualTarget = InvokableInvoke.Entry, ValueBits = 0x80000000 };
            var target = new NativeSingleDispatchTarget(1, Invokable.ToInt64(), InvokableInvoke.Entry.ToInt64(), InvokableInvoke.Pointer.ToInt64(),
                frame.ValueBits, null, false, 0, Array.Empty<long>(), "native fixture");
            return NativeTweenTypedPlayConsumerReader.Read(Api, Module, frame, target,
                new("typed.play", Play.Entry, Play.Pointer, TweenClass, ExactHookAbi.OperationVoid0), TweenClass,
                DelegatesField, TargetField, Root, Reference, value => Owned && value == PlayObject.ToInt64(), () => { });
        }

        internal int FieldReads, MethodExports, Enumerations, ClassLookups;
        internal bool EndlessEnumeration;
        internal Action<FieldRow>? OnRead;
        internal Fixture(bool tween = false, IntPtr nativeInvoke = default, IntPtr nativePlay = default)
        {
            _image = new(nativeInvoke, nativePlay);
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
            Bind("il2cpp_class_get_fields", "Ptr2", nameof(Fields));
            Bind("il2cpp_field_get_name", "Ptr1", nameof(FieldName));
            Bind("il2cpp_field_get_parent", "Ptr1", nameof(FieldParent));
            Bind("il2cpp_class_instance_size", "ArraySize", nameof(InstanceSize));
            Api = new NativeRuntime(name => _exports.TryGetValue(name, out var export) ? Marshal.GetFunctionPointerForDelegate(export) : Base.ExportForTypedPlayReaderTests(name));
            Api.BindCurrentThread();
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
        private IntPtr Fields(IntPtr cls, IntPtr iterator)
        { var fields = _fields.Where(row => row.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32();
          if (index >= fields.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new(index + 1)); return fields[index].Pointer; }
        private IntPtr FieldName(IntPtr field) => AllocName(Field(field).Name);
        private IntPtr FieldParent(IntPtr field) => Field(field).Class;
        private uint InstanceSize(IntPtr cls) => 0x80;
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
        public void Dispose() { foreach (var root in Roots.Values) root.Dispose(); _image.Dispose(); foreach (var p in _allocations) Marshal.FreeHGlobal(p); Base.Dispose(); GC.KeepAlive(_exports); }
    }
    private sealed class Image : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "RizToolbox.ListenerMetadata." + Guid.NewGuid().ToString("N") + ".dll");
        private readonly FileStream _file; private readonly IntPtr _mapping;
        internal readonly IntPtr Address;
        internal Image(IntPtr nativeInvoke = default, IntPtr nativePlay = default)
        {
            var executableFixture = nativeInvoke != IntPtr.Zero && nativePlay != IntPtr.Zero;
            var bytes = new byte[executableFixture ? 0x156f000 : 0x400]; using (var writer = new BinaryWriter(new MemoryStream(bytes)))
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
                writer.Write(new byte[] { 46, 116, 101, 120, 116, 0, 0, 0 }); writer.Write(0x156E000); writer.Write(0x1000); writer.Write(bytes.Length - 0x200); writer.Write(0x200);
                writer.Write(0); writer.Write(0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0x60000020);
                if (executableFixture)
                    foreach (var entry in new[] { (Rva: 0x154e5b0, Target: nativeInvoke), (Rva: 0x3956f0, Target: nativePlay) })
                    {
                        writer.BaseStream.Position = entry.Rva - 0x1000 + 0x200;
                        writer.Write((byte)0x48); writer.Write((byte)0xb8); writer.Write(entry.Target.ToInt64());
                        writer.Write((byte)0xff); writer.Write((byte)0xe0);
                    }
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
    internal IntPtr ExportForTypedPlayReaderTests(string name) => ExportPointer(name);
}
