using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

// Controlled official IL2CPP exports and actual unmanaged storage, adapted from TweenGraphReadRegression.
// NativeRuntime.Root performs the real production root calls; no game is loaded.
internal sealed class SingleListFixture : IDisposable
    {
        internal sealed class TypeRow
        {
            internal string Name = "", Namespace = "", ShortName = "";
            internal IntPtr Type, Class, Parent, Image, Element, EnumUnderlying;
            internal int Code, Size; internal uint InstanceSize = 64, Rank = 1;
            internal bool Value, ByRef, Generic, Inflated;
        }
        internal sealed class FieldRow
        { internal string Name = ""; internal IntPtr Pointer, Class, Type, OwnerOverride, StaticValue; internal int Offset; internal uint Flags; }
        internal readonly FakeNative Base = new();
        private readonly System.Collections.Generic.List<IntPtr> _allocations = new(); private readonly Dictionary<string, System.Delegate> _exports = new();
        private readonly Dictionary<IntPtr, TypeRow> _types = new(), _classes = new(); private readonly System.Collections.Generic.List<FieldRow> _fields = new();
        private readonly Dictionary<(IntPtr, string, string), IntPtr> _definitions = new();
        private readonly IntPtr _assemblies;
        internal readonly IntPtr SystemImage, UnityImage, GameImage, Manager, Child, Tween, OtherTween, Scheduled, Ready, PlayingArray, StaticAction;
        internal readonly TypeRow ObjectClass, Boolean, Int32, TweenClass, ManagerClass, ChildClass, List, Stack, ArrayClass;
        internal readonly FieldRow Working, BooleanField, Playing, AddTween, ListBacking, ListSize;
        internal readonly NativeRuntime Api;
        internal uint ArrayHeader = 40;
        internal int ElementBytes = 8, ArrayByteAdjustment, ArrayObjectAdjustment, FieldReads, MetadataReads;
        internal string? MissingExport;
        internal Action? AfterStorageRead, OnMetadata;
        internal readonly List<IntPtr> UnrootedObjectReads = new();
        internal readonly Dictionary<IntPtr,NativeRuntime.RootHandle> Retained = new();
        internal Action<IntPtr>? BeforePublishedRoot;

        internal SingleListFixture()
        {
            SystemImage = Allocate(16); UnityImage = Allocate(16); GameImage = Allocate(16); _assemblies = Allocate(24);
            Marshal.WriteIntPtr(_assemblies, 0, SystemImage); Marshal.WriteIntPtr(_assemblies, 8, UnityImage); Marshal.WriteIntPtr(_assemblies, 16, GameImage);
            ObjectClass = AddType("System.Object", "System", "Object", 28, false, 8, SystemImage);
            Boolean = AddType("System.Boolean", "System", "Boolean", 2, true, 1, SystemImage);
            Int32 = AddType("System.Int32", "System", "Int32", 8, true, 4, SystemImage);
            TweenClass = AddType("UnityEngine.Events.BaseInvokableCall", "UnityEngine.Events", "BaseInvokableCall", 18, false, 8, UnityImage, ObjectClass.Class);
            ManagerClass = AddType("TweenManager", "", "TweenManager", 18, false, 8, GameImage, ObjectClass.Class);
            ChildClass = AddType("Fixture.ManagerChild", "Fixture", "ManagerChild", 18, false, 8, GameImage, ManagerClass.Class);
            ArrayClass = AddType("UnityEngine.Events.BaseInvokableCall[]", "UnityEngine.Events", "BaseInvokableCall[]", 29, false, 8, GameImage, ObjectClass.Class); ArrayClass.Element = TweenClass.Class;
            List = AddType("System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>", "System.Collections.Generic", "List`1", 21, false, 8, SystemImage, ObjectClass.Class);
            Stack = AddType("System.Collections.Generic.Stack<TweenInfo>", "System.Collections.Generic", "Stack`1", 21, false, 8, SystemImage, ObjectClass.Class);
            var action = AddType("System.Action<TweenInfo>", "System", "Action`1", 21, false, 8, SystemImage, ObjectClass.Class);
            Manager = Instance(ManagerClass.Class); Child = Instance(ChildClass.Class); Tween = Instance(TweenClass.Class); OtherTween = Instance(TweenClass.Class);
            Scheduled = Instance(List.Class); Ready = Instance(Stack.Class); StaticAction = Instance(action.Class);
            PlayingArray = Allocate(72); Marshal.WriteIntPtr(PlayingArray, ArrayClass.Class);
            Marshal.WriteIntPtr(PlayingArray, 40, Tween); Marshal.WriteIntPtr(PlayingArray, 48, IntPtr.Zero);
            Marshal.WriteIntPtr(PlayingArray, 56, Tween); Marshal.WriteIntPtr(PlayingArray, 64, OtherTween);
            Marshal.WriteInt32(Manager, 0x3C, 2); Marshal.WriteInt32(Child, 0x3C, 2); Marshal.WriteIntPtr(Manager, 0x20, PlayingArray);
            Working = AddField(ManagerClass.Class, "_workingTweenTrackSize", Int32.Type, 0x3C);
            BooleanField = AddField(ManagerClass.Class, "flag", Boolean.Type, 0x30); Playing = AddField(ManagerClass.Class, "playingTweens", ArrayClass.Type, 0x20);
            AddTween = AddField(ManagerClass.Class, "AddTween", action.Type, 0); AddTween.Flags = 0x10; AddTween.StaticValue = StaticAction;
            ListBacking = AddField(List.Class, "_items", ArrayClass.Type, 0x10); ListSize = AddField(List.Class, "_size", Int32.Type, 0x18);
            AddField(List.Class, "_version", Int32.Type, 0x1C); AddField(Stack.Class, "_array", ArrayClass.Type, 0x10);
            AddField(Stack.Class, "_size", Int32.Type, 0x18); AddField(Stack.Class, "_version", Int32.Type, 0x1C);
            foreach (var value in new[] { Scheduled, Ready }) { Marshal.WriteIntPtr(value, 0x10, PlayingArray); Marshal.WriteInt32(value, 0x18, 2); Marshal.WriteInt32(value, 0x1C, 7); }
            Bind("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies)); Bind("il2cpp_assembly_get_image", "Ptr1", nameof(Identity));
            Bind("il2cpp_image_get_name", "Ptr1", nameof(ImageName)); Bind("il2cpp_class_from_name", "ClassLookup", nameof(FindClass));
            Bind("il2cpp_object_get_class", "Ptr1", nameof(GetObjectClass)); Bind("il2cpp_class_get_parent", "Ptr1", nameof(Parent));
            Bind("il2cpp_class_get_type", "Ptr1", nameof(ClassType)); Bind("il2cpp_class_from_type", "Ptr1", nameof(TypeClass));
            Bind("il2cpp_class_get_name", "Ptr1", nameof(ClassName)); Bind("il2cpp_class_get_namespace", "Ptr1", nameof(ClassNamespace));
            Bind("il2cpp_class_get_image", "Ptr1", nameof(ClassImage)); Bind("il2cpp_class_instance_size", "ArraySize", nameof(InstanceSize));
            Bind("il2cpp_class_is_generic", "Predicate", nameof(Generic)); Bind("il2cpp_class_is_inflated", "Predicate", nameof(Inflated));
            Bind("il2cpp_type_get_name", "Ptr1", nameof(TypeName)); Bind("il2cpp_type_get_type", "TypeCode", nameof(TypeCode));
            Bind("il2cpp_type_is_byref", "Predicate", nameof(ByRef)); Bind("il2cpp_class_is_valuetype", "Predicate", nameof(ValueType));
            Bind("il2cpp_class_is_enum", "Predicate", nameof(IsEnum)); Bind("il2cpp_class_value_size", "ValueSize", nameof(ValueSize));
            Bind("il2cpp_class_enum_basetype", "Ptr1", nameof(EnumBase));
            Bind("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable)); Bind("il2cpp_class_get_fields", "Ptr2", nameof(Fields));
            Bind("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField)); Bind("il2cpp_field_get_parent", "Ptr1", nameof(FieldParent));
            Bind("il2cpp_field_get_name", "Ptr1", nameof(FieldName)); Bind("il2cpp_field_get_type", "Ptr1", nameof(FieldType));
            Bind("il2cpp_field_get_flags", "Flags", nameof(FieldFlags)); Bind("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset));
            Bind("il2cpp_field_get_value", "FieldGet", nameof(ReadField)); Bind("il2cpp_field_static_get_value", "StaticGet", nameof(ReadStatic));
            Bind("il2cpp_class_get_rank", "Flags", nameof(Rank)); Bind("il2cpp_class_get_element_class", "Ptr1", nameof(ElementClass));
            Bind("il2cpp_array_element_size", "ArrayElementSize", nameof(ElementSize)); Bind("il2cpp_array_length", "ArraySize", nameof(ArrayLength));
            Bind("il2cpp_array_get_byte_length", "ArraySize", nameof(ArrayBytes)); Bind("il2cpp_object_get_size", "ArraySize", nameof(ObjectSize));
            Bind("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(HeaderSize));
            var invokableList = AddType("UnityEngine.Events.InvokableCallList", "UnityEngine.Events", "InvokableCallList", 18, false, 8, UnityImage);
            AddField(invokableList.Class, "m_ExecutingCalls", List.Type, 0x20);
            Api = new NativeRuntime(name => {
                if (name == MissingExport) throw new EntryPointNotFoundException("Synthetic absent " + name);
                return _exports.TryGetValue(name, out var value) ? Marshal.GetFunctionPointerForDelegate(value) : Base.ExportForListenerTests(name);
            }); Api.BindCurrentThread();
        }
        internal NativeExactReadField Field(FieldRow row) => Api.InspectExactDeclaredReadField(row.Class, row.Name, _types[row.Type].Name, (row.Flags & 0x10) != 0);
        internal void NoInvocations() => Check(Base.Invocations == 0 && Base.Unboxes == 0 && Base.ObjectFieldWrites == 0 && Base.ValueFieldWrites == 0);
        internal TypeRow AddType(string name, string ns, string shortName, int code, bool value, int size, IntPtr image, IntPtr parent = default)
        { var row = new TypeRow { Name = name, Namespace = ns, ShortName = shortName, Code = code, Value = value, Size = size, Image = image, Parent = parent, Type = Allocate(16), Class = Allocate(0x200), Inflated = code == 21 }; _types.Add(row.Type, row); _classes.Add(row.Class, row); _definitions.TryAdd((image, ns, shortName), row.Class); return row; }
        internal FieldRow AddField(IntPtr cls, string name, IntPtr type, int offset)
        { var row = new FieldRow { Pointer = Allocate(16), Class = cls, Name = name, Type = type, Offset = offset }; _fields.Add(row); return row; }
        private IntPtr Allocate(int size) { var pointer = Marshal.AllocHGlobal(size); _allocations.Add(pointer); Marshal.Copy(new byte[size], 0, pointer, size); return pointer; }
        private IntPtr Instance(IntPtr cls) { var value = Allocate(64); Marshal.WriteIntPtr(value, cls); return value; }
        private IntPtr Text(string value) { var pointer = Marshal.StringToHGlobalAnsi(value); _allocations.Add(pointer); return pointer; }
        private void Metadata() { ++MetadataReads; OnMetadata?.Invoke(); }
        private void Bind(string name, string type, string method) => _exports[name] = System.Delegate.CreateDelegate(typeof(NativeRuntime).GetNestedType(type, BindingFlags.NonPublic)!, this, GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!);
        private IntPtr Assemblies(IntPtr domain, IntPtr size) { Metadata(); Marshal.WriteIntPtr(size, new IntPtr(3)); return _assemblies; }
        private IntPtr Identity(IntPtr value) { Metadata(); return value; }
        private IntPtr ImageName(IntPtr image) { Metadata(); return Text(image == SystemImage ? "mscorlib.dll" : image == GameImage ? "Assembly-CSharp.dll" : "UnityEngine.CoreModule.dll"); }
        private IntPtr FindClass(IntPtr image, string ns, string name) { Metadata(); return _definitions.GetValueOrDefault((image, ns, name)); }
        private IntPtr GetObjectClass(IntPtr value) { if (!Base.SingleRooted(value)) UnrootedObjectReads.Add(value); Metadata(); return Marshal.ReadIntPtr(value); }
        private IntPtr Parent(IntPtr cls) { Metadata(); return _classes[cls].Parent; }
        private IntPtr ClassType(IntPtr cls) { Metadata(); return _classes[cls].Type; }
        private IntPtr TypeClass(IntPtr type) { Metadata(); return _types[type].Class; }
        private IntPtr ClassName(IntPtr cls) { Metadata(); return Text(_classes[cls].ShortName); }
        private IntPtr ClassNamespace(IntPtr cls) { Metadata(); return Text(_classes[cls].Namespace); }
        private IntPtr ClassImage(IntPtr cls) { Metadata(); return _classes[cls].Image; }
        private uint InstanceSize(IntPtr cls) { Metadata(); return _classes[cls].InstanceSize; }
        private bool Generic(IntPtr cls) { Metadata(); return _classes[cls].Generic; }
        private bool Inflated(IntPtr cls) { Metadata(); return _classes[cls].Inflated; }
        private IntPtr TypeName(IntPtr type) { Metadata(); return Marshal.StringToCoTaskMemUTF8(_types[type].Name); }
        private int TypeCode(IntPtr type) { Metadata(); return _types[type].Code; }
        private bool ByRef(IntPtr type) { Metadata(); return _types[type].ByRef; }
        private bool ValueType(IntPtr cls) { Metadata(); return _classes[cls].Value; }
        private bool IsEnum(IntPtr cls) { Metadata(); return _classes[cls].EnumUnderlying != IntPtr.Zero; }
        private IntPtr EnumBase(IntPtr cls) { Metadata(); return _classes[cls].EnumUnderlying; }
        private int ValueSize(IntPtr cls, out uint alignment) { Metadata(); alignment = 1; return _classes[cls].Size; }
        private bool Assignable(IntPtr expected, IntPtr actual) { Metadata(); for (var count = 0; actual != IntPtr.Zero && count < 32; ++count, actual = _classes[actual].Parent) if (actual == expected) return true; return false; }
        private IntPtr Fields(IntPtr cls, IntPtr iterator) { Metadata(); var rows = _fields.Where(row => row.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32(); if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer; }
        private IntPtr FindField(IntPtr cls, string name) { Metadata(); for (var count = 0; cls != IntPtr.Zero && count < 32; ++count, cls = _classes[cls].Parent) { var row = _fields.FirstOrDefault(field => field.Class == cls && field.Name == name); if (row is not null) return row.Pointer; } return IntPtr.Zero; }
        private FieldRow Row(IntPtr field) => _fields.Single(row => row.Pointer == field);
        private IntPtr FieldParent(IntPtr field) { Metadata(); var row = Row(field); return row.OwnerOverride == IntPtr.Zero ? row.Class : row.OwnerOverride; }
        private IntPtr FieldName(IntPtr field) { Metadata(); return Text(Row(field).Name); }
        private IntPtr FieldType(IntPtr field) { Metadata(); return Row(field).Type; }
        private uint FieldFlags(IntPtr field) { Metadata(); return Row(field).Flags; }
        private int FieldOffset(IntPtr field) { Metadata(); return Row(field).Offset; }
        private void ReadField(IntPtr owner, IntPtr field, IntPtr output) { ++FieldReads; var row = Row(field); var data = new byte[_types[row.Type].Size]; Marshal.Copy(IntPtr.Add(owner, row.Offset), data, 0, data.Length); Marshal.Copy(data, 0, output, data.Length); AfterStorageRead?.Invoke(); }
        private void ReadStatic(IntPtr field, IntPtr output) { ++FieldReads; Marshal.WriteIntPtr(output, Row(field).StaticValue); AfterStorageRead?.Invoke(); }
        private uint Rank(IntPtr cls) { Metadata(); return _classes[cls].Rank; }
        private IntPtr ElementClass(IntPtr cls) { Metadata(); return _classes[cls].Element; }
        private int ElementSize(IntPtr cls) { Metadata(); return ElementBytes; }
        private uint ArrayLength(IntPtr array) { Metadata(); return 4; }
        private uint ArrayBytes(IntPtr array) { Metadata(); return checked((uint)(32 + ArrayByteAdjustment)); }
        private uint ObjectSize(IntPtr array) { Metadata(); return checked((uint)(72 + ArrayObjectAdjustment)); }
        private uint HeaderSize() { Metadata(); return ArrayHeader; }
        internal NativeRuntime.RootHandle Root(IntPtr value) { BeforePublishedRoot?.Invoke(value); if (Retained.TryGetValue(value,out var existing)) return existing; var root=Api.Root(value); Retained.Add(value,root); return root; }
        internal VerifiedHookTarget Target => new("controlled.single",new(1),new(2),ManagerClass.Class,ExactHookAbi.OperationSingle1Void);
        internal NativeSingleExecutingList Capture() => Api.ReadSingleExecutingList(Root(Manager),Root(Scheduled),Target,Root);
        public void Dispose() { foreach(var root in Retained.Values) root.Dispose(); foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer); Base.Dispose(); GC.KeepAlive(_exports); }
        private static void Check(bool value) { if(!value) throw new InvalidOperationException("Single list fixture invariant failed."); }
    }
