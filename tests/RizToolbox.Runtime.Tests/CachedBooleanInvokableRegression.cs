using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

internal static class CachedBooleanInvokableRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Cached Boolean projection copies the actual one-byte argument without invoking or transferring roots", Projection),
        ("Cached Boolean projection rejects open foreign and inconsistent closed-class metadata before reading storage", Classes),
        ("Cached Boolean projection requires official declared argument and delegate fields with exact storage", Fields),
        ("Cached Boolean projection validates the actual closed action Invoke signature without resolving its shared entry", InvokeSignature),
        ("Cached Boolean projection rejects changed argument delegate class and field metadata within its observation", Mutation),
        ("Cached Boolean field publication adopts the official field result before any post-read inspection", Publication),
        ("Cached Boolean field publication revalidates descriptive records and detects a changed published reference", PublicationChange),
        ("Cached Boolean projection bounds official field and method enumeration", Bounds),
        ("Cached Boolean observation rejects missing exports expired roots and worker access before storage reads", Admission)
    };

    private static void Projection()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Object);
        // Adjacent padding must not turn the false one-byte cached Boolean true.
        Marshal.WriteInt64(f.Object, 0x18, -256);
        var first = f.Project(root);
        Check(first.Object == f.Object && first.ActualClass == f.Cached.Class && first.InvokableParentClass == f.Parent.Class
            && first.ActionClass == f.Action.Class && first.ArgumentClass == f.Boolean.Class && !first.Argument
            && first.Delegate == f.Delegate && first.ActionInvokeMethod == f.Invoke.Pointer);
        Check(first.ArgumentField.Field == f.Argument.Pointer && first.ArgumentField.DeclaringClass == f.Cached.Class
            && first.DelegateField.Field == f.ActionField.Pointer && first.DelegateField.DeclaringClass == f.Parent.Class);
        Marshal.WriteByte(f.Object, 0x18, 1);
        var second = f.Project(root);
        Check(second.Argument && !first.Argument && first != second && second.Delegate == first.Delegate);
        Check(f.Base.ActiveRoots == 1 && f.Base.NewRoots == 1 && f.Base.FreedRoots == 0 && root.Target == f.Object);
        f.NoGameCalls();
    }

    private static void Classes()
    {
        foreach (var mode in new[] { "cached-name", "parent-name", "namespace", "image", "generic", "not-inflated", "code",
            "byref", "value", "roundtrip", "parent", "base-parent", "action-parent", "multicast-parent", "boolean-size", "boolean-code" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Object);
            if (mode == "cached-name") f.Cached.Name = "UnityEngine.Events.CachedInvokableCall<System.Int32>";
            if (mode == "parent-name") f.Parent.ShortName = "InvokableCall";
            if (mode == "namespace") f.Cached.Namespace = "Fixture";
            if (mode == "image") f.Cached.Image = f.SystemImage;
            if (mode == "generic") f.Cached.Generic = true;
            if (mode == "not-inflated") f.Cached.Inflated = false;
            if (mode == "code") f.Cached.Code = 18;
            if (mode == "byref") f.Cached.ByRef = true;
            if (mode == "value") f.Cached.Value = true;
            if (mode == "roundtrip") f.Cached.ReturnedClass = f.Parent.Class;
            if (mode == "parent") f.Cached.Parent = f.BaseClass.Class;
            if (mode == "base-parent") f.Parent.Parent = f.Multicast.Class;
            if (mode == "action-parent") f.Action.Parent = f.DelegateClass.Class;
            if (mode == "multicast-parent") f.Multicast.Parent = f.BaseClass.Class;
            if (mode == "boolean-size") f.Boolean.Size = 4;
            if (mode == "boolean-code") f.Boolean.Code = 8;
            Throws<UnsupportedSignatureException>(() => f.Project(root));
            Check(f.FieldReads == 0 && root.Target == f.Object && f.Base.ActiveRoots == 1); f.NoGameCalls();
        }
    }

    private static void Fields()
    {
        foreach (var mode in new[] { "argument-owner", "delegate-owner", "argument-static", "argument-literal", "argument-type",
            "argument-alias", "argument-byref", "delegate-type", "delegate-byref", "shadow", "duplicate", "missing",
            "argument-offset", "delegate-offset", "overlap", "object-bound", "object-size" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Object);
            if (mode == "argument-owner") f.Argument.OwnerOverride = f.Parent.Class;
            if (mode == "delegate-owner") f.ActionField.OwnerOverride = f.Cached.Class;
            if (mode == "argument-static") f.Argument.Flags = 0x10;
            if (mode == "argument-literal") f.Argument.Flags = 0x40;
            if (mode == "argument-type") f.Argument.Type = f.Action.Type;
            if (mode == "argument-alias") f.Argument.Type = f.AddType("System.Boolean", "System", "Boolean", 2, true, 1, f.SystemImage).Type;
            if (mode == "argument-byref") f.Boolean.ByRef = true;
            if (mode == "delegate-type") f.ActionField.Type = f.Boolean.Type;
            if (mode == "delegate-byref") f.Action.ByRef = true;
            if (mode == "shadow") f.AddField(f.Cached.Class, "Delegate", f.Action.Type, 0x10);
            if (mode == "duplicate") f.AddField(f.Cached.Class, "m_Arg1", f.Boolean.Type, 0x18);
            if (mode == "missing") f.Argument.Name = "OtherArgument";
            if (mode == "argument-offset") f.Argument.Offset = 0;
            if (mode == "delegate-offset") f.ActionField.Offset = 0;
            if (mode == "overlap") f.Argument.Offset = 0x10;
            if (mode == "object-bound") f.Argument.Offset = 0x20;
            if (mode == "object-size") f.ObjectBytes = 8192;
            Throws<UnsupportedSignatureException>(() => f.Project(root));
            Check(f.FieldReads == 0 && f.Base.ActiveRoots == 1); f.NoGameCalls();
        }
        // InitOnly is valid for a readonly observation of Unity's cached argument.
        using var valid = new Fixture(); using var owner = valid.Api.Root(valid.Object);
        valid.Argument.Flags = 0x20; Check(!valid.Project(owner).Argument); valid.NoGameCalls();
    }

    private static void InvokeSignature()
    {
        foreach (var mode in new[] { "owner", "name", "static", "abstract", "generic", "count", "return", "parameter", "parameter-alias", "duplicate" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Object);
            if (mode == "owner") f.Invoke.OwnerOverride = f.Parent.Class;
            if (mode == "name") f.Invoke.Name = "OtherInvoke";
            if (mode == "static") f.Invoke.Flags = 0x10;
            if (mode == "abstract") f.Invoke.Flags = 0x400;
            if (mode == "generic") f.Invoke.Generic = true;
            if (mode == "count") f.Invoke.Parameters = Array.Empty<IntPtr>();
            if (mode == "return") f.Invoke.Return = f.Boolean.Type;
            if (mode == "parameter") f.Invoke.Parameters = new[] { f.Action.Type };
            if (mode == "parameter-alias") f.Invoke.Parameters = new[] { f.AddType("System.Boolean", "System", "Boolean", 2, true, 1, f.SystemImage).Type };
            if (mode == "duplicate") f.AddMethod("Invoke");
            Throws<UnsupportedSignatureException>(() => f.Project(root));
            Check(f.FieldReads == 0 && f.Base.ActiveRoots == 1); f.NoGameCalls();
        }
    }

    private static void Mutation()
    {
        foreach (var mode in new[] { "argument", "delegate", "delegate-late", "class", "field", "invoke" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Object); var fired = false;
            f.AfterRead = field => {
                if (fired || field != (mode == "delegate-late" ? f.ActionField : f.Argument)) return; fired = true;
                if (mode == "argument") Marshal.WriteByte(f.Object, 0x18, 1);
                if (mode == "delegate") Marshal.WriteIntPtr(f.Object, 0x10, f.OtherDelegate);
                if (mode == "delegate-late") Marshal.WriteIntPtr(f.Object, 0x10, f.OtherDelegate);
                if (mode == "class") Marshal.WriteIntPtr(f.Object, f.Parent.Class);
                if (mode == "field") f.Argument.Offset = 0x19;
                if (mode == "invoke") f.Invoke.Parameters = Array.Empty<IntPtr>();
            };
            if (mode == "delegate")
            {
                // This mutation precedes the first Delegate read. The copied
                // value is the actual new reference, not the serialized candidate.
                var observed = f.Project(root); Check(observed.Delegate == f.OtherDelegate);
            }
            else Throws<UnsupportedSignatureException>(() => f.Project(root));
            Check(f.Base.ActiveRoots == 1 && root.Target == f.Object); f.NoGameCalls();
        }
    }

    private static void Publication()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Object); var expected = f.Project(root);
        var calls = NativePublicationCall.RetainedCallCount; NativeRuntime.RootHandle? adopted = null;
        var lastReferenceRead = false; var inspectedAfterRead = false; var referenceReads = 0;
        f.AfterRead = field => { if (field == f.ActionField && ++referenceReads == 3) lastReferenceRead = true; };
        f.OnMetadata = () => { if (lastReferenceRead) inspectedAfterRead = true; };
        var result = f.Api.ReadCachedBooleanDelegateWithPublication(root, expected, pointer => {
            Check(!inspectedAfterRead && pointer == f.Delegate); adopted = f.Api.Root(pointer);
            Check(adopted.Target == f.Delegate);
        });
        Check(result == f.Delegate && adopted is not null && adopted.Target == f.Delegate
            && f.Base.ActiveRoots == 2 && NativePublicationCall.RetainedCallCount == calls);
        adopted!.Dispose(); Check(root.Target == f.Object && f.Base.ActiveRoots == 1); f.NoGameCalls();

        Marshal.WriteIntPtr(f.Object, 0x10, IntPtr.Zero); f.AfterRead = null; f.OnMetadata = null;
        expected = f.Project(root); var notifications = 0;
        Check(f.Api.ReadCachedBooleanDelegateWithPublication(root, expected, value => { Check(value == IntPtr.Zero); ++notifications; }) == IntPtr.Zero);
        Check(notifications == 1 && NativePublicationCall.RetainedCallCount == calls && f.Base.ActiveRoots == 1);
    }

    private static void PublicationChange()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Object); var expected = f.Project(root); var notified = 0;
        var forged = expected with { ArgumentField = expected.ArgumentField with { Field = expected.DelegateField.Field } };
        Throws<UnsupportedSignatureException>(() => f.Api.ReadCachedBooleanDelegateWithPublication(root, forged, _ => ++notified));
        Marshal.WriteByte(f.Object, 0x18, 1);
        Throws<UnsupportedSignatureException>(() => f.Api.ReadCachedBooleanDelegateWithPublication(root, expected, _ => ++notified));
        Check(notified == 0 && f.Base.ActiveRoots == 1);

        expected = f.Project(root); var referenceReads = 0; NativeRuntime.RootHandle? adopted = null;
        f.BeforeRead = field => { if (field == f.ActionField && ++referenceReads == 3) Marshal.WriteIntPtr(f.Object, 0x10, f.OtherDelegate); };
        Throws<UnsupportedSignatureException>(() => f.Api.ReadCachedBooleanDelegateWithPublication(root, expected, pointer => {
            adopted = f.Api.Root(pointer); ++notified;
        }));
        Check(notified == 1 && adopted is not null && adopted.Target == f.OtherDelegate && f.Base.ActiveRoots == 2);
        adopted!.Dispose(); f.NoGameCalls();
    }

    private static void Bounds()
    {
        using var fields = new Fixture(); using var fieldRoot = fields.Api.Root(fields.Object);
        fields.EndlessFields = true; Throws<UnsupportedSignatureException>(() => fields.Project(fieldRoot));
        Check(fields.FieldEnumerations == 256 && fields.FieldReads == 0);
        using var methods = new Fixture(); using var methodRoot = methods.Api.Root(methods.Object);
        methods.EndlessMethods = true; Throws<UnsupportedSignatureException>(() => methods.Project(methodRoot));
        Check(methods.MethodEnumerations == 256 && methods.FieldReads == 0); fields.NoGameCalls(); methods.NoGameCalls();
    }

    private static void Admission()
    {
        foreach (var name in new[] { "il2cpp_class_get_image", "il2cpp_field_get_parent", "il2cpp_class_is_generic",
            "il2cpp_class_is_inflated", "il2cpp_object_get_size" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Object); f.MissingExport = name;
            Throws<UnsupportedSignatureException>(() => f.Project(root)); Check(f.FieldReads == 0 && f.Base.ActiveRoots == 1);
        }
        using var valid = new Fixture(); var expired = valid.Api.Root(valid.Object); expired.Dispose();
        Throws<TargetExpiredException>(() => valid.Project(expired));
        using var owner = valid.Api.Root(valid.Object); var reads = valid.MetadataReads;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => valid.Project(owner));
            Throws<InvalidOperationException>(() => valid.Api.ReadCachedBooleanDelegateWithPublication(owner, null!, _ => { }));
        }).GetAwaiter().GetResult();
        Check(valid.MetadataReads == reads && valid.FieldReads == 0 && owner.Target == valid.Object); valid.NoGameCalls();
    }

    internal sealed class Fixture : IDisposable
    {
        internal sealed class TypeRow
        {
            internal string Name = "", Namespace = "", ShortName = "";
            internal IntPtr Type, Class, Parent, Image, ReturnedClass;
            internal int Code, Size; internal bool Value, ByRef, Generic, Inflated;
        }
        internal sealed class FieldRow
        { internal string Name = ""; internal IntPtr Pointer, Class, Type, OwnerOverride; internal int Offset; internal uint Flags; }
        internal sealed class MethodRow
        { internal string Name = "Invoke"; internal IntPtr Pointer, Class, Return, OwnerOverride; internal IntPtr[] Parameters = Array.Empty<IntPtr>(); internal uint Flags = 0x40; internal bool Generic; }
        internal readonly FakeNative Base = new();
        private readonly List<IntPtr> _allocations = new(); private readonly Dictionary<string, System.Delegate> _exports = new();
        private readonly Dictionary<IntPtr, TypeRow> _types = new(), _classes = new();
        private readonly List<FieldRow> _fields = new(); private readonly List<MethodRow> _methods = new();
        private readonly Dictionary<(IntPtr, string, string), IntPtr> _definitions = new();
        private readonly IntPtr _assemblies;
        internal readonly IntPtr UnityImage, SystemImage, Object, Delegate, OtherDelegate;
        internal readonly TypeRow Boolean, BaseClass, Parent, Cached, Action, Multicast, DelegateClass, Void;
        internal readonly FieldRow Argument, ActionField;
        internal readonly MethodRow Invoke;
        internal readonly NativeRuntime Api;
        internal uint ObjectBytes = 32;
        internal int FieldReads, FieldEnumerations, MethodEnumerations, MetadataReads;
        internal bool EndlessFields, EndlessMethods;
        internal string? MissingExport;
        internal System.Action<FieldRow>? BeforeRead, AfterRead;
        internal System.Action? OnMetadata;
        private readonly FieldRow _ignoredField;
        private readonly MethodRow _ignoredMethod;

        internal Fixture(bool single = false)
        {
            var scalarName = single ? "System.Single" : "System.Boolean";
            var scalarShortName = single ? "Single" : "Boolean";
            var scalarCode = single ? 12 : 2;
            var scalarSize = single ? sizeof(float) : sizeof(byte);
            UnityImage = Allocate(16); SystemImage = Allocate(16); _assemblies = Allocate(16);
            Marshal.WriteIntPtr(_assemblies, 0, SystemImage); Marshal.WriteIntPtr(_assemblies, 8, UnityImage);
            Void = AddType("System.Void", "System", "Void", 1, true, 0, SystemImage);
            Boolean = AddType(scalarName, "System", scalarShortName, scalarCode, true, scalarSize, SystemImage);
            DelegateClass = AddType("System.Delegate", "System", "Delegate", 18, false, 8, SystemImage);
            Multicast = AddType("System.MulticastDelegate", "System", "MulticastDelegate", 18, false, 8, SystemImage, DelegateClass.Class);
            BaseClass = AddType("UnityEngine.Events.BaseInvokableCall", "UnityEngine.Events", "BaseInvokableCall", 18, false, 8, UnityImage);
            Parent = AddType("UnityEngine.Events.InvokableCall<" + scalarName + ">", "UnityEngine.Events", "InvokableCall`1", 21, false, 8, UnityImage, BaseClass.Class);
            Cached = AddType("UnityEngine.Events.CachedInvokableCall<" + scalarName + ">", "UnityEngine.Events", "CachedInvokableCall`1", 21, false, 8, UnityImage, Parent.Class);
            Action = AddType("UnityEngine.Events.UnityAction<" + scalarName + ">", "UnityEngine.Events", "UnityAction`1", 21, false, 8, UnityImage, Multicast.Class);
            Object = Allocate(32); Marshal.WriteIntPtr(Object, Cached.Class);
            Delegate = Allocate(128); Marshal.WriteIntPtr(Delegate, Action.Class);
            OtherDelegate = Allocate(128); Marshal.WriteIntPtr(OtherDelegate, Action.Class);
            Marshal.WriteIntPtr(Object, 0x10, Delegate);
            Argument = AddField(Cached.Class, "m_Arg1", Boolean.Type, 0x18);
            ActionField = AddField(Parent.Class, "Delegate", Action.Type, 0x10);
            Invoke = AddMethod("Invoke");
            _ignoredField = new() { Pointer = Allocate(16), Class = Cached.Class, Name = "Other", Type = Boolean.Type, Offset = 0x18 };
            _ignoredMethod = new() { Pointer = Allocate(16), Class = Action.Class, Name = "Other" };
            Bind("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies)); Bind("il2cpp_assembly_get_image", "Ptr1", nameof(Identity));
            Bind("il2cpp_image_get_name", "Ptr1", nameof(ImageName)); Bind("il2cpp_class_from_name", "ClassLookup", nameof(FindClass));
            Bind("il2cpp_object_get_class", "Ptr1", nameof(ObjectClass)); Bind("il2cpp_class_get_parent", "Ptr1", nameof(ParentClass));
            Bind("il2cpp_class_get_type", "Ptr1", nameof(ClassType)); Bind("il2cpp_class_from_type", "Ptr1", nameof(TypeClass));
            Bind("il2cpp_class_get_name", "Ptr1", nameof(ClassName)); Bind("il2cpp_class_get_namespace", "Ptr1", nameof(ClassNamespace));
            Bind("il2cpp_class_get_image", "Ptr1", nameof(ClassImage)); Bind("il2cpp_class_is_generic", "Predicate", nameof(GenericClass));
            Bind("il2cpp_class_is_inflated", "Predicate", nameof(InflatedClass)); Bind("il2cpp_type_get_name", "Ptr1", nameof(TypeName));
            Bind("il2cpp_type_get_type", "TypeCode", nameof(TypeCode)); Bind("il2cpp_type_is_byref", "Predicate", nameof(ByRef));
            Bind("il2cpp_class_is_valuetype", "Predicate", nameof(ValueType)); Bind("il2cpp_class_is_enum", "Predicate", nameof(IsEnum));
            Bind("il2cpp_class_value_size", "ValueSize", nameof(ValueSize)); Bind("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable));
            Bind("il2cpp_class_get_fields", "Ptr2", nameof(Fields)); Bind("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField));
            Bind("il2cpp_field_get_name", "Ptr1", nameof(FieldName)); Bind("il2cpp_field_get_type", "Ptr1", nameof(FieldType));
            Bind("il2cpp_field_get_parent", "Ptr1", nameof(FieldParent)); Bind("il2cpp_field_get_flags", "Flags", nameof(FieldFlags));
            Bind("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset)); Bind("il2cpp_field_get_value", "FieldGet", nameof(ReadField));
            Bind("il2cpp_class_get_methods", "Ptr2", nameof(Methods)); Bind("il2cpp_method_get_name", "Ptr1", nameof(MethodName));
            Bind("il2cpp_method_get_class", "Ptr1", nameof(MethodClass)); Bind("il2cpp_method_get_return_type", "Ptr1", nameof(MethodReturn));
            Bind("il2cpp_method_get_flags", "MethodFlags", nameof(MethodFlags)); Bind("il2cpp_method_get_param_count", "Flags", nameof(ParameterCount));
            Bind("il2cpp_method_get_param", "ParameterType", nameof(ParameterType)); Bind("il2cpp_method_is_generic", "Predicate", nameof(GenericMethod));
            Bind("il2cpp_object_get_size", "ArraySize", nameof(ObjectSize));
            Api = new NativeRuntime(name => {
                if (name == MissingExport) throw new EntryPointNotFoundException("Synthetic absent " + name);
                return _exports.TryGetValue(name, out var value) ? Marshal.GetFunctionPointerForDelegate(value) : Base.ExportForListenerTests(name);
            }); Api.BindCurrentThread();
        }
        internal NativeCachedBooleanInvokable Project(NativeRuntime.RootHandle root) => Api.InspectRootedCachedBooleanInvokable(root);
        internal NativeCachedSingleInvokable ProjectSingle(NativeRuntime.RootHandle root) => Api.InspectRootedCachedSingleInvokable(root);
        internal void NoGameCalls() => Check(Base.Invocations == 0 && Base.Unboxes == 0 && Base.ObjectFieldWrites == 0 && Base.ValueFieldWrites == 0);
        internal TypeRow AddType(string name, string ns, string shortName, int code, bool value, int size, IntPtr image, IntPtr parent = default)
        {
            var row = new TypeRow { Name = name, Namespace = ns, ShortName = shortName, Type = Allocate(16), Class = Allocate(0x200), Code = code,
                Value = value, Size = size, Image = image, Parent = parent, Inflated = code == 21 };
            _types.Add(row.Type, row); _classes.Add(row.Class, row); _definitions.TryAdd((image, ns, shortName), row.Class); return row;
        }
        internal FieldRow AddField(IntPtr cls, string name, IntPtr type, int offset)
        { var row = new FieldRow { Pointer = Allocate(16), Class = cls, Name = name, Type = type, Offset = offset }; _fields.Add(row); return row; }
        internal MethodRow AddMethod(string name)
        { var row = new MethodRow { Pointer = Allocate(16), Class = Action.Class, Name = name, Return = Void.Type, Parameters = new[] { Boolean.Type } }; _methods.Add(row); return row; }
        private IntPtr Allocate(int size) { var pointer = Marshal.AllocHGlobal(size); _allocations.Add(pointer); Marshal.Copy(new byte[size], 0, pointer, size); return pointer; }
        private IntPtr Text(string value) { var pointer = Marshal.StringToHGlobalAnsi(value); _allocations.Add(pointer); return pointer; }
        private void Bind(string name, string type, string method)
            => _exports[name] = System.Delegate.CreateDelegate(typeof(NativeRuntime).GetNestedType(type, BindingFlags.NonPublic)!, this,
                GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!);
        private void Metadata() { ++MetadataReads; OnMetadata?.Invoke(); }
        private IntPtr Assemblies(IntPtr domain, IntPtr size) { Metadata(); Marshal.WriteIntPtr(size, new IntPtr(2)); return _assemblies; }
        private IntPtr Identity(IntPtr value) { Metadata(); return value; }
        private IntPtr ImageName(IntPtr image) { Metadata(); return Text(image == UnityImage ? "UnityEngine.CoreModule.dll" : "mscorlib.dll"); }
        private IntPtr FindClass(IntPtr image, string ns, string name) { Metadata(); return _definitions.GetValueOrDefault((image, ns, name)); }
        private IntPtr ObjectClass(IntPtr value) { Metadata(); return Marshal.ReadIntPtr(value); }
        private IntPtr ParentClass(IntPtr cls) { Metadata(); return _classes[cls].Parent; }
        private IntPtr ClassType(IntPtr cls) { Metadata(); return _classes[cls].Type; }
        private IntPtr TypeClass(IntPtr type) { Metadata(); var row = _types[type]; return row.ReturnedClass == IntPtr.Zero ? row.Class : row.ReturnedClass; }
        private IntPtr ClassName(IntPtr cls) { Metadata(); return Text(_classes[cls].ShortName); }
        private IntPtr ClassNamespace(IntPtr cls) { Metadata(); return Text(_classes[cls].Namespace); }
        private IntPtr ClassImage(IntPtr cls) { Metadata(); return _classes[cls].Image; }
        private bool GenericClass(IntPtr cls) { Metadata(); return _classes[cls].Generic; }
        private bool InflatedClass(IntPtr cls) { Metadata(); return _classes[cls].Inflated; }
        private IntPtr TypeName(IntPtr type) { Metadata(); return Marshal.StringToCoTaskMemUTF8(_types[type].Name); }
        private int TypeCode(IntPtr type) { Metadata(); return _types[type].Code; }
        private bool ByRef(IntPtr type) { Metadata(); return _types[type].ByRef; }
        private bool ValueType(IntPtr cls) { Metadata(); return _classes[cls].Value; }
        private bool IsEnum(IntPtr cls) { Metadata(); return false; }
        private int ValueSize(IntPtr cls, out uint alignment) { Metadata(); alignment = 1; return _classes[cls].Size; }
        private bool Assignable(IntPtr expected, IntPtr actual) { Metadata(); for (var count = 0; actual != IntPtr.Zero && count < 32; ++count, actual = _classes[actual].Parent) if (actual == expected) return true; return false; }
        private FieldRow Field(IntPtr field) => field == _ignoredField.Pointer ? _ignoredField : _fields.Single(row => row.Pointer == field);
        private IntPtr Fields(IntPtr cls, IntPtr iterator) { Metadata(); ++FieldEnumerations; if (EndlessFields && cls == Cached.Class) return _ignoredField.Pointer; var rows = _fields.Where(row => row.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32(); if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer; }
        private IntPtr FindField(IntPtr cls, string name) { Metadata(); for (var count = 0; cls != IntPtr.Zero && count < 32; ++count, cls = _classes[cls].Parent) { var row = _fields.FirstOrDefault(field => field.Class == cls && field.Name == name); if (row is not null) return row.Pointer; } return IntPtr.Zero; }
        private IntPtr FieldName(IntPtr field) { Metadata(); return Text(Field(field).Name); }
        private IntPtr FieldType(IntPtr field) { Metadata(); return Field(field).Type; }
        private IntPtr FieldParent(IntPtr field) { Metadata(); var row = Field(field); return row.OwnerOverride == IntPtr.Zero ? row.Class : row.OwnerOverride; }
        private uint FieldFlags(IntPtr field) { Metadata(); return Field(field).Flags; }
        private int FieldOffset(IntPtr field) { Metadata(); return Field(field).Offset; }
        private void ReadField(IntPtr owner, IntPtr field, IntPtr output) { ++FieldReads; var row = Field(field); BeforeRead?.Invoke(row); var bytes = new byte[_types[row.Type].Size]; Marshal.Copy(IntPtr.Add(owner, row.Offset), bytes, 0, bytes.Length); Marshal.Copy(bytes, 0, output, bytes.Length); AfterRead?.Invoke(row); }
        private MethodRow Method(IntPtr method) => method == _ignoredMethod.Pointer ? _ignoredMethod : _methods.Single(row => row.Pointer == method);
        private IntPtr Methods(IntPtr cls, IntPtr iterator) { Metadata(); ++MethodEnumerations; if (EndlessMethods) return _ignoredMethod.Pointer; var rows = _methods.Where(row => row.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32(); if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer; }
        private IntPtr MethodName(IntPtr method) { Metadata(); return Text(Method(method).Name); }
        private IntPtr MethodClass(IntPtr method) { Metadata(); var row = Method(method); return row.OwnerOverride == IntPtr.Zero ? row.Class : row.OwnerOverride; }
        private IntPtr MethodReturn(IntPtr method) { Metadata(); return Method(method).Return; }
        private uint MethodFlags(IntPtr method, out uint implementation) { Metadata(); implementation = 0; return Method(method).Flags; }
        private uint ParameterCount(IntPtr method) { Metadata(); return (uint)Method(method).Parameters.Length; }
        private IntPtr ParameterType(IntPtr method, uint index) { Metadata(); return Method(method).Parameters[index]; }
        private bool GenericMethod(IntPtr method) { Metadata(); return Method(method).Generic; }
        private uint ObjectSize(IntPtr value) { Metadata(); return ObjectBytes; }
        public void Dispose() { foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer); Base.Dispose(); GC.KeepAlive(_exports); }
    }

    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Cached Boolean observation invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
