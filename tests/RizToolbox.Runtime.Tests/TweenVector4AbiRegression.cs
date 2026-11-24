using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

internal static class TweenVector4AbiRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Tween Vector4 exact adapter carrier preserves all four float bit patterns and buffer boundaries", RoundTrip),
        ("Tween Vector4 field reads use actual native value size and x y z w metadata", NativeFields),
        ("Tween Vector4 refuses wrong native size field offsets names and scalar types before field entry", NativeRejection),
        ("Tween Vector4 cannot substitute Color or an unregistered same-size managed struct", DistinctIdentity)
    };

    private static NativeTypeLayout Layout() => new("UnityEngine.Vector4", NativeValueKind.Struct, 16, Fields: new[] {
        new NativeFieldLayout("x", "System.Single", 0), new NativeFieldLayout("y", "System.Single", 4),
        new NativeFieldLayout("z", "System.Single", 8), new NativeFieldLayout("w", "System.Single", 12) });

    private static void RoundTrip()
    {
        var bits = new[] { unchecked((int)0x80000000), 1, unchecked((int)0x7FC01234), BitConverter.SingleToInt32Bits(-123.25f) };
        var value = new NativeTweenVector4(BitConverter.Int32BitsToSingle(bits[0]), BitConverter.Int32BitsToSingle(bits[1]),
            BitConverter.Int32BitsToSingle(bits[2]), BitConverter.Int32BitsToSingle(bits[3]));
        Check(Marshal.SizeOf<NativeTweenVector4>() == 16 && NativeAbi.SizeOf(typeof(NativeTweenVector4)) == 16);
        NativeAbi.ValidateValue(typeof(NativeTweenVector4), Layout());
        using var argument = A.Value(value); NativeAbi.ValidateArgument(Layout(), argument);
        for (var index = 0; index < 4; ++index) Check(Marshal.ReadInt32(argument.Pointer, index * 4) == bits[index]);
        AssertBits(NativeRuntime.ReadValue<NativeTweenVector4>(argument.Pointer), bits);
        using var guarded = new A(32);
        for (var index = 0; index < 32; ++index) Marshal.WriteByte(guarded.Pointer, index, 0x5A);
        NativeAbi.Write(IntPtr.Add(guarded.Pointer, 8), value);
        AssertBits(NativeRuntime.ReadValue<NativeTweenVector4>(IntPtr.Add(guarded.Pointer, 8)), bits);
        for (var index = 0; index < 8; ++index)
            Check(Marshal.ReadByte(guarded.Pointer, index) == 0x5A && Marshal.ReadByte(guarded.Pointer, 24 + index) == 0x5A);
    }

    private static void NativeFields()
    {
        using var native = new FakeNative();
        native.FieldType = native.AddStruct("UnityEngine.Vector4", 16, Layout().Fields!.ToArray());
        var values = new[] { 0.125f, -0.25f, 4f, 128f }; native.FieldBytes = values.SelectMany(BitConverter.GetBytes).ToArray();
        var actual = native.Api.Read<NativeTweenVector4>(native.Instance, "fixedStart");
        AssertBits(actual, values.Select(BitConverter.SingleToInt32Bits).ToArray());
        Check(native.FieldReads == 1 && native.Invocations == 0 && native.NewRoots == 1 && native.FreedRoots == 1);
    }

    private static void NativeRejection()
    {
        foreach (var mode in new[] { "small", "large", "offset", "name", "type", "missing" })
        {
            using var native = new FakeNative(); var fields = Layout().Fields!.ToArray();
            var size = mode == "small" ? 12 : mode == "large" ? 20 : 16;
            if (mode == "offset") fields[3] = fields[3] with { Offset = 16 };
            if (mode == "name") fields[3] = fields[3] with { Name = "a" };
            if (mode == "type") fields[3] = fields[3] with { TypeName = "System.Int32" };
            if (mode == "missing") fields = fields.Take(3).ToArray();
            native.FieldType = native.AddStruct("UnityEngine.Vector4", size, fields); native.FieldBytes = new byte[16];
            Throws<UnsupportedSignatureException>(() => native.Api.Read<NativeTweenVector4>(native.Instance, "fixedStart"));
            Check(native.FieldReads == 0 && native.Invocations == 0 && native.NewRoots == native.FreedRoots);
        }
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(NativeTweenVector4), Layout() with { ByRef = true }));
    }

    private static void DistinctIdentity()
    {
        var color = new NativeTypeLayout("UnityEngine.Color", NativeValueKind.Struct, 16, Fields: new[] {
            new NativeFieldLayout("r", "System.Single", 0), new NativeFieldLayout("g", "System.Single", 4),
            new NativeFieldLayout("b", "System.Single", 8), new NativeFieldLayout("a", "System.Single", 12) });
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(Rgba), Layout()));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(NativeTweenVector4), color));
        using var vector = A.Value(new NativeTweenVector4(1, 2, 3, 4));
        using var rgba = A.Value(new Rgba(1, 2, 3, 4));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateArgument(color, vector));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateArgument(Layout(), rgba));
        Throws<UnsupportedSignatureException>(() => NativeAbi.SizeOf(typeof(UnregisteredFourFloats)));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateManagedStruct(typeof(WrongFloatOffsets), 4));
    }

    private static void AssertBits(NativeTweenVector4 value, IReadOnlyList<int> expected)
        => Check(new[] { value.X, value.Y, value.Z, value.W }.Select(BitConverter.SingleToInt32Bits).SequenceEqual(expected));
    [StructLayout(LayoutKind.Sequential)] private readonly record struct UnregisteredFourFloats(float X, float Y, float Z, float W);
    [StructLayout(LayoutKind.Explicit, Size = 20)] private struct WrongFloatOffsets
    { [FieldOffset(0)] public float X; [FieldOffset(4)] public float Y; [FieldOffset(8)] public float Z; [FieldOffset(16)] public float W; }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Tween Vector4 ABI invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
