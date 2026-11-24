using System.Runtime.InteropServices;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

// Exact static value observation through the real NativeRuntime and official-export
// fake. No actual Canvas source capture, Unity execution, native root owner or write
// is represented by this small Runtime prerequisite.
internal static class ExactStaticFieldReadRegression
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Exact static field Single raw bits and one-byte Boolean come from official storage", ScalarStorage),
        ("Exact static Vector3 copies three actual value slots without invoking Unity or constructors", VectorStorage),
        ("Exact static value reads reject changed saved descriptors and incompatible managed carriers", SavedDescriptor),
        ("Exact static value reads revalidate current metadata while permitting readonly observation", CurrentMetadata),
        ("Exact static value reader denies workers before any export and preserves main-thread access", WrongThread),
        ("Exact static reference fields reject IntPtr carriers rather than bypassing publication ownership", ReferenceRejection)
    };

    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }
    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static byte[] FloatBytes(params int[] bits) => bits.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] PointerBytes(IntPtr value) => IntPtr.Size == 8
        ? BitConverter.GetBytes(value.ToInt64()) : BitConverter.GetBytes(value.ToInt32());
    private static int[] VectorBits(NativeVector3 value) => new[] { value.X, value.Y, value.Z }
        .Select(BitConverter.SingleToInt32Bits).ToArray();
    private static IntPtr AddVector(FakeNative native) => native.AddStruct("UnityEngine.Vector3", 12,
        new NativeFieldLayout("x", "System.Single", 0),
        new NativeFieldLayout("y", "System.Single", 4),
        new NativeFieldLayout("z", "System.Single", 8));

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly NativeRuntime Api;
        internal readonly NativeExactReadField Field;
        private readonly int _retainedCalls = NativePublicationCall.RetainedCallCount;
        internal Fixture(Func<FakeNative, IntPtr>? type = null, byte[]? bytes = null, uint flags = 0)
        {
            Api = Native.ConfigureExactStaticRead(type?.Invoke(Native) ?? Native.Single,
                bytes ?? FloatBytes(BitConverter.SingleToInt32Bits(.625f)), flags);
            Field = Api.InspectExactDeclaredReadField(Native.ClassOf(Native.Receiver), "field",
                Native.ExactStaticTypeName, true, 0);
            Check(Field.IsStatic && Field.Offset == 0 && Field.Field == Native.Field
                && Field.DeclaringClass == Native.ClassOf(Native.Receiver)
                && Field.FieldClass == Native.ClassOf(Native.FieldType),
                "Fixture did not capture the real exact declared static descriptor.");
            ReadOnly(0);
        }
        internal void ReadOnly(int getters)
        {
            Check(Native.ExactStaticGets == getters && Native.ExactStaticInstanceGets == 0
                && Native.FieldReads == getters,
                "Operation entered an unexpected getter or bypassed the official static getter.");
            Check(Native.FieldWrites == 0 && Native.Invocations == 0 && Native.ObjectAllocations == 0
                && Native.ArrayAllocations == 0 && Native.Unboxes == 0,
                "Static observation wrote a field, invoked native behavior, allocated an object or unboxed.");
            Check(Native.NewRoots == 0 && Native.FreedRoots == 0 && Native.ActiveRoots == 0
                && Native.FreeHandleAttempts == 0 && NativePublicationCall.RetainedCallCount == _retainedCalls,
                "Value observation adopted a root or retained native publication responsibility.");
            Check(Native.ExactStaticStorageUnchanged(), "Value observation altered storage or its adjacent guard bytes.");
        }
        internal void Reject(Action action, string reason) => Reject<UnsupportedSignatureException>(action, reason);
        internal void Reject<T>(Action action, string reason) where T : Exception
        {
            var getters = Native.ExactStaticGets;
            Native.ExactStaticForbidGet = true;
            try { Expect<T>(action); }
            finally { Native.ExactStaticForbidGet = false; }
            Check(Native.ExactStaticGets == getters, reason + " reached the static output buffer.");
            ReadOnly(getters);
        }
        public void Dispose()
        {
            try { ReadOnly(Native.ExactStaticGets); }
            finally { Native.Dispose(); }
        }
    }

    private static void ScalarStorage()
    {
        using (var f = new Fixture())
        {
            var bits = new[] { 0, unchecked((int)0x80000000), 1, unchecked((int)0x80000001),
                BitConverter.SingleToInt32Bits(-123.25f), unchecked((int)0x7fc12345),
                unchecked((int)0xffc54321), 0x7f800000, unchecked((int)0xff800000) };
            for (var index = 0; index < bits.Length; ++index)
            {
                f.Native.SetExactStaticStorage(FloatBytes(bits[index]));
                var observed = f.Api.ReadExactStaticField<float>(f.Field);
                Check(BitConverter.SingleToInt32Bits(observed) == bits[index],
                    "Single observation changed sign, subnormal, infinity or NaN payload bits.");
                f.ReadOnly(index + 1);
            }
        }
        using (var f = new Fixture(native => native.Boolean, new byte[] { 0 }))
        {
            var bytes = new byte[] { 0, 1, 0x80, 0xff, 0 };
            for (var index = 0; index < bytes.Length; ++index)
            {
                // Real storage has nonzero guards adjacent to the one-byte false
                // value. The getter copies ONLY its declared byte, never four
                // bytes into a one-byte managed ABI output buffer.
                f.Native.SetExactStaticStorage(new[] { bytes[index] });
                Check(f.Api.ReadExactStaticField<bool>(f.Field) == (bytes[index] != 0),
                    "Boolean observation did not use the one-byte IL2CPP value.");
                f.ReadOnly(index + 1);
            }
        }
    }

    private static void VectorStorage()
    {
        var first = new[] { unchecked((int)0x80000000), unchecked((int)0x7fc54321), 1 };
        using var f = new Fixture(AddVector, FloatBytes(first), 0x20u);
        Check(f.Field.TypeName == "UnityEngine.Vector3" && f.Field.Size == 12 && f.Field.Flags == 0x30u,
            "Vector fixture is not exact readonly static Vector3 metadata.");
        var saved = f.Api.ReadExactStaticField<NativeVector3>(f.Field);
        Check(VectorBits(saved).SequenceEqual(first), "Vector read changed one of its three raw Single slots.");
        f.ReadOnly(1);
        var second = new[] { BitConverter.SingleToInt32Bits(-2f), BitConverter.SingleToInt32Bits(.5f),
            BitConverter.SingleToInt32Bits(4f) };
        f.Native.SetExactStaticStorage(FloatBytes(second));
        var current = f.Api.ReadExactStaticField<NativeVector3>(f.Field);
        Check(VectorBits(current).SequenceEqual(second) && VectorBits(saved).SequenceEqual(first),
            "Static Vector3 was cached or returned an alias instead of an independent managed copy.");
        f.ReadOnly(2);
        f.Reject(() => f.Api.ReadExactStaticField<float>(f.Field), "Vector3-to-Single substitution");
    }

    private static void SavedDescriptor()
    {
        using var f = new Fixture();
        var bad = new[]
        {
            f.Field with { IsStatic = false },
            f.Field with { Field = new IntPtr(987654) },
            f.Field with { Type = f.Native.Int32 },
            f.Field with { DeclaringClass = f.Native.ClassOf(f.Native.String) },
            f.Field with { DeclaringClass = IntPtr.Zero },
            f.Field with { FieldClass = f.Native.ClassOf(f.Native.Int32) },
            f.Field with { Name = "absent" },
            f.Field with { TypeName = "System.Int32" },
            f.Field with { Offset = 4 },
            f.Field with { Size = 1 },
            f.Field with { Flags = 0x30u }
        };
        foreach (var field in bad)
            f.Reject(() => f.Api.ReadExactStaticField<float>(field), "Saved descriptor mutation");
        f.Reject<ArgumentNullException>(() => f.Api.ReadExactStaticField<float>(null!), "Absent exact descriptor");
        f.Reject(() => f.Api.ReadExactStaticField<int>(f.Field), "Same-width Single-to-Int32 substitution");
        f.Reject(() => f.Api.ReadExactStaticField<bool>(f.Field), "Single-to-one-byte Boolean substitution");
        f.Reject(() => f.Api.ReadExactStaticField<NativeVector3>(f.Field), "Single-to-Vector3 substitution");
        Check(f.Api.ReadExactStaticField<float>(f.Field) == .625f,
            "Rejected descriptor or ABI validation consumed the valid static observation.");
        f.ReadOnly(1);
        using var boolean = new Fixture(native => native.Boolean, new byte[] { 1 });
        boolean.Reject(() => boolean.Api.ReadExactStaticField<int>(boolean.Field), "Boolean-to-Int32 substitution");
        boolean.Reject(() => boolean.Api.ReadExactStaticField<byte>(boolean.Field), "Same-width Boolean-to-Byte substitution");
    }

    private static void CurrentMetadata()
    {
        // InitOnly is legal for observation. The complete saved flags must still
        // equal CURRENT official flags, so changing them invalidates a descriptor.
        using (var f = new Fixture(flags: 0x20u))
        {
            Check(f.Field.Flags == 0x30u && f.Api.ReadExactStaticField<float>(f.Field) == .625f,
                "Readonly static fields were treated as prohibited writes rather than legal reads.");
            f.ReadOnly(1);
        }
        foreach (var mode in new[] { "static", "readonly", "literal", "other-flags", "type", "same-name-type",
                     "type-name", "width", "byref", "valuetype", "offset", "negative-offset", "parent",
                     "roundtrip", "name", "absent", "duplicate" })
        {
            using var f = new Fixture();
            // Warm the type layout before drift: changed width/byref/current
            // descriptor cannot be accepted from the cached type layout.
            Check(f.Api.ReadExactStaticField<float>(f.Field) == .625f, "Baseline static read failed.");
            f.ReadOnly(1);
            f.Native.MutateExactStaticMetadata(mode);
            f.Reject(() => f.Api.ReadExactStaticField<float>(f.Field), "Current metadata drift: " + mode);
        }
    }

    private static void WrongThread()
    {
        using var f = new Fixture();
        var requested = f.Native.RequestedExports.Count;
        var parents = f.Native.FieldParentQueries;
        f.Native.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() => {
                Expect<InvalidOperationException>(() => f.Api.ReadExactStaticField<float>(f.Field));
                Expect<InvalidOperationException>(() => f.Api.ReadExactStaticField<bool>(f.Field));
                Expect<InvalidOperationException>(() => f.Api.ReadExactStaticField<float>(null!));
                Expect<InvalidOperationException>(() => f.Api.ReadExactStaticField<float>(f.Field with { IsStatic = false }));
            }).GetAwaiter().GetResult();
            Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0
                && f.Native.RequestedExports.Count == requested && f.Native.FieldParentQueries == parents,
                "Worker entered an export, including lazy lookup, before exact static thread rejection.");
            f.ReadOnly(0);
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Api.ReadExactStaticField<float>(f.Field) == .625f,
            "Worker rejection consumed the bound main-thread static reader.");
        f.ReadOnly(1);
    }

    private static void ReferenceRejection()
    {
        foreach (var reference in new Func<FakeNative, IntPtr>[] { native => native.Object, native => native.String, native => native.Receiver })
            foreach (var token in new[] { IntPtr.Zero, new IntPtr(0x12345678) })
            {
                using var f = new Fixture(reference, PointerBytes(token));
                Check(f.Field.Size == IntPtr.Size && f.Field.IsStatic,
                    "Reference fixture lacks exact static pointer-sized metadata.");
                f.Reject(() => f.Api.ReadExactStaticField<IntPtr>(f.Field), "Reference-to-IntPtr publication bypass");
                f.Reject(() => f.Api.ReadExactStaticField<UIntPtr>(f.Field), "Reference-to-UIntPtr publication bypass");
                f.ReadOnly(0);
            }
        // A declared System.IntPtr VALUE is distinct from an object reference.
        // The NativeAbi scalar allowlist, not a blanket pointer-carrier ban, applies.
        var value = new IntPtr(0x12345678);
        using var scalar = new Fixture(native => native.AddType("System.IntPtr", 24, true, IntPtr.Size), PointerBytes(value));
        Check(scalar.Api.ReadExactStaticField<IntPtr>(scalar.Field) == value,
            "A genuine native IntPtr value was confused with reference publication.");
        scalar.ReadOnly(1);
    }
}

sealed partial class FakeNative
{
    private IntPtr _exactStaticStorage, _exactStaticValue;
    private byte[] _exactStaticSnapshot = Array.Empty<byte>();
    internal int ExactStaticGets, ExactStaticInstanceGets;
    internal bool ExactStaticForbidGet;
    internal string ExactStaticTypeName => _types[FieldType].Name;

    internal NativeRuntime ConfigureExactStaticRead(IntPtr type, byte[] bytes, uint flags)
    {
        FieldType = type; StaticField = true; ExtraFieldFlags = flags;
        FieldParentClass = ClassOf(Receiver);
        _structFields[FieldParentClass] = new List<IntPtr> { Field };
        _fieldMetadata[Field] = ("field", type, 0);
        _exactStaticStorage = Marshal.AllocHGlobal(checked(bytes.Length + 8));
        _allocations.Add(_exactStaticStorage); _exactStaticValue = IntPtr.Add(_exactStaticStorage, 4);
        _exactStaticSnapshot = Enumerable.Repeat((byte)0xa5, bytes.Length + 8).ToArray();
        Marshal.Copy(_exactStaticSnapshot, 0, _exactStaticStorage, _exactStaticSnapshot.Length);
        SetExactStaticStorage(bytes);
        Add("il2cpp_field_static_get_value", "StaticGet", nameof(ExactStaticGetValue));
        Add("il2cpp_field_get_value", "FieldGet", nameof(ExactStaticRejectInstanceGet));
        // Constructor-cached delegates must be the newly audited official getters.
        var runtime = new NativeRuntime(ExportPointer); runtime.BindCurrentThread(); return runtime;
    }
    internal void SetExactStaticStorage(byte[] bytes)
    {
        if (bytes.Length != _types[FieldType].Size || bytes.Length + 8 != _exactStaticSnapshot.Length)
            throw new InvalidOperationException("Synthetic static storage differs from its declared ABI width.");
        Marshal.Copy(bytes, 0, _exactStaticValue, bytes.Length);
        Array.Copy(bytes, 0, _exactStaticSnapshot, 4, bytes.Length);
    }
    internal bool ExactStaticStorageUnchanged()
    {
        var current = new byte[_exactStaticSnapshot.Length];
        Marshal.Copy(_exactStaticStorage, current, 0, current.Length);
        return current.SequenceEqual(_exactStaticSnapshot);
    }
    private void ExactStaticGetValue(IntPtr field, IntPtr output)
    {
        ++ExactStaticGets; ++FieldReads;
        if (ExactStaticForbidGet || field != Field || !StaticField)
            throw new InvalidOperationException("Rejected exact static read entered its native output buffer.");
        var count = _types[FieldType].Size;
        if (count + 8 != _exactStaticSnapshot.Length)
            throw new InvalidOperationException("Changed static width reached a getter.");
        var actual = new byte[count]; Marshal.Copy(_exactStaticValue, actual, 0, count);
        Marshal.Copy(actual, 0, output, count);
    }
    private void ExactStaticRejectInstanceGet(IntPtr owner, IntPtr field, IntPtr output)
    {
        ++ExactStaticInstanceGets; ++FieldReads;
        throw new InvalidOperationException("Static value observation must not substitute an instance getter.");
    }
    internal void MutateExactStaticMetadata(string mode)
    {
        var row = _fieldMetadata[Field]; var type = _types[FieldType];
        switch (mode)
        {
            case "static": StaticField = false; break;
            case "readonly": ExtraFieldFlags = 0x20u; break;
            case "literal": ExtraFieldFlags = 0x40u; break;
            case "other-flags": ExtraFieldFlags = 1u; break;
            case "type": FieldType = Int32; break;
            case "same-name-type": FieldType = AddType("System.Single", 12, true, 4); break;
            case "type-name": _types[FieldType] = type with { Name = "System.Int32" }; break;
            case "width": _types[FieldType] = type with { Size = 8 }; break;
            case "byref": _types[FieldType] = type with { ByRef = true }; break;
            case "valuetype": _types[FieldType] = type with { ValueType = false }; break;
            case "offset": _fieldMetadata[Field] = (row.Name, row.Type, 4); break;
            case "negative-offset": _fieldMetadata[Field] = (row.Name, row.Type, -1); break;
            case "parent": FieldParentClass = ClassOf(String); break;
            case "roundtrip": _classes[ClassOf(FieldType)] = Int32; break;
            case "name": _fieldMetadata[Field] = ("renamed", row.Type, row.Offset); break;
            case "absent": _structFields[ClassOf(Receiver)].Clear(); break;
            case "duplicate": _structFields[ClassOf(Receiver)].Add(Field); break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
