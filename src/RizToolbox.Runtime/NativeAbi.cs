using System.Reflection;
using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal enum NativeValueKind { Unsupported, Void, Boolean, SByte, Byte, Int16, UInt16, Char, Int32, UInt32, Int64, UInt64, IntPtr, UIntPtr, Single, Double, Reference, Enum, Struct }
internal sealed record NativeFieldLayout(string Name, string TypeName, int Offset);
internal sealed record NativeTypeLayout(string Name, NativeValueKind Kind, int Size, bool ByRef = false,
    IntPtr Class = default, NativeTypeLayout? Underlying = null, IReadOnlyList<NativeFieldLayout>? Fields = null);

/// <summary>Internal diagnostics remain exceptions; this is not a new public SDK result contract.</summary>
internal sealed class UnsupportedSignatureException : NotSupportedException
{
    internal UnsupportedSignatureException(string message) : base("UnsupportedSignature: " + message) { }
}
internal sealed class TargetExpiredException : InvalidOperationException
{
    internal TargetExpiredException(string message) : base("TargetExpired: " + message) { }
}

/// <summary>Exact allowlist for the runtime_invoke buffer ABI. Never infer a layout from Marshal.SizeOf alone.</summary>
internal static class NativeAbi
{
    private static readonly Dictionary<Type, (NativeValueKind Kind, string Name, int Size)> Scalars = new()
    {
        [typeof(bool)] = (NativeValueKind.Boolean, "System.Boolean", 1),
        [typeof(sbyte)] = (NativeValueKind.SByte, "System.SByte", 1), [typeof(byte)] = (NativeValueKind.Byte, "System.Byte", 1),
        [typeof(short)] = (NativeValueKind.Int16, "System.Int16", 2), [typeof(ushort)] = (NativeValueKind.UInt16, "System.UInt16", 2),
        [typeof(char)] = (NativeValueKind.Char, "System.Char", 2),
        [typeof(int)] = (NativeValueKind.Int32, "System.Int32", 4), [typeof(uint)] = (NativeValueKind.UInt32, "System.UInt32", 4),
        [typeof(long)] = (NativeValueKind.Int64, "System.Int64", 8), [typeof(ulong)] = (NativeValueKind.UInt64, "System.UInt64", 8),
        [typeof(float)] = (NativeValueKind.Single, "System.Single", 4), [typeof(double)] = (NativeValueKind.Double, "System.Double", 8),
        [typeof(IntPtr)] = (NativeValueKind.IntPtr, "System.IntPtr", IntPtr.Size),
        [typeof(UIntPtr)] = (NativeValueKind.UIntPtr, "System.UIntPtr", IntPtr.Size)
    };
    internal static NativeTypeLayout? Scalar(string nativeName)
    {
        foreach (var scalar in Scalars.Values)
            if (scalar.Name == nativeName) return new(scalar.Name, scalar.Kind, scalar.Size);
        return null;
    }
    internal static int SizeOf(Type managed)
    {
        if (managed.IsEnum) return SizeOf(Enum.GetUnderlyingType(managed));
        if (Scalars.TryGetValue(managed, out var scalar)) return scalar.Size;
        var binding = StructBinding(managed);
        if (binding is null) throw new UnsupportedSignatureException($"Managed value '{managed}' has no registered ABI layout.");
        ValidateManagedStruct(managed, binding.Value.Count);
        return binding.Value.Count * sizeof(float);
    }
    private static (string NativeName, int Count)? StructBinding(Type managed)
    {
        if (managed.Assembly.GetName().Name != "RizToolbox.Adapter") return null;
        return managed.FullName switch
        {
            "RizToolbox.Adapter.Vec2" => ("UnityEngine.Vector2", 2),
            "RizToolbox.Adapter.Rgba" => ("UnityEngine.Color", 4),
            "RizToolbox.Adapter.Visual.NativeRect" => ("UnityEngine.Rect", 4),
            "RizToolbox.Adapter.UiInput.NativeVector3" => ("UnityEngine.Vector3", 3),
            "RizToolbox.Adapter.AudioContent.NativeTweenVector4" => ("UnityEngine.Vector4", 4),
            "RizToolbox.Adapter.Gameplay.NativeRendererQuaternion" => ("UnityEngine.Quaternion", 4),
            _ => null
        };
    }
    internal static void ValidateManagedStruct(Type managed, int floatCount)
    {
        var fields = managed.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (!managed.IsValueType || managed.IsAutoLayout || fields.Length != floatCount ||
            fields.Any(f => f.FieldType != typeof(float)) || Marshal.SizeOf(managed) != floatCount * sizeof(float))
            throw new UnsupportedSignatureException($"Managed struct '{managed}' differs from its explicitly supported float layout.");
        var offsets = fields.Select(f => Marshal.OffsetOf(managed, f.Name).ToInt32()).OrderBy(x => x).ToArray();
        if (!offsets.SequenceEqual(Enumerable.Range(0, floatCount).Select(x => x * sizeof(float))))
            throw new UnsupportedSignatureException($"Managed struct '{managed}' has unsupported field offsets.");
    }
    internal static bool IsKnownStruct(string name) => name is "UnityEngine.Vector2" or "UnityEngine.Vector3" or "UnityEngine.Vector4" or "UnityEngine.Quaternion" or "UnityEngine.Color" or "UnityEngine.Rect" or "UnityEngine.SceneManagement.Scene";
    internal static void ValidateNativeStruct(NativeTypeLayout native)
    {
        var expected = native.Name switch
        {
            "UnityEngine.Vector2" => new[] { new NativeFieldLayout("x", "System.Single", 0), new NativeFieldLayout("y", "System.Single", 4) },
            "UnityEngine.Vector3" => new[] { new NativeFieldLayout("x", "System.Single", 0), new NativeFieldLayout("y", "System.Single", 4), new NativeFieldLayout("z", "System.Single", 8) },
            "UnityEngine.Vector4" => new[] { new NativeFieldLayout("x", "System.Single", 0), new NativeFieldLayout("y", "System.Single", 4), new NativeFieldLayout("z", "System.Single", 8), new NativeFieldLayout("w", "System.Single", 12) },
            "UnityEngine.Quaternion" => new[] { new NativeFieldLayout("x", "System.Single", 0), new NativeFieldLayout("y", "System.Single", 4), new NativeFieldLayout("z", "System.Single", 8), new NativeFieldLayout("w", "System.Single", 12) },
            "UnityEngine.Color" => new[] { new NativeFieldLayout("r", "System.Single", 0), new NativeFieldLayout("g", "System.Single", 4), new NativeFieldLayout("b", "System.Single", 8), new NativeFieldLayout("a", "System.Single", 12) },
            "UnityEngine.Rect" => new[] { new NativeFieldLayout("m_XMin", "System.Single", 0), new NativeFieldLayout("m_YMin", "System.Single", 4), new NativeFieldLayout("m_Width", "System.Single", 8), new NativeFieldLayout("m_Height", "System.Single", 12) },
            "UnityEngine.SceneManagement.Scene" => new[] { new NativeFieldLayout("m_Handle", "System.Int32", 0) },
            _ => throw new UnsupportedSignatureException($"Native struct '{native.Name}' is not whitelisted.")
        };
        if (native.Kind != NativeValueKind.Struct || native.Size != expected.Length * sizeof(int) || native.Fields is null ||
            !native.Fields.OrderBy(f => f.Offset).SequenceEqual(expected))
            throw new UnsupportedSignatureException($"Native struct '{native.Name}' failed size/field/offset verification.");
    }
    internal static void ValidateReturn(NativeTypeLayout native)
    {
        if (native.ByRef || native.Kind == NativeValueKind.Unsupported)
            throw new UnsupportedSignatureException($"Return '{native.Name}' has no supported layout.");
        if (native.Kind == NativeValueKind.Enum)
        {
            if (native.Underlying is null || !IsEnumStorage(native.Underlying.Kind) || native.Size != native.Underlying.Size)
                throw new UnsupportedSignatureException($"Enum '{native.Name}' has invalid underlying storage.");
        }
        if (native.Kind == NativeValueKind.Struct) ValidateNativeStruct(native);
    }
    private static bool IsEnumStorage(NativeValueKind kind) => kind is NativeValueKind.SByte or NativeValueKind.Byte or NativeValueKind.Int16 or NativeValueKind.UInt16 or NativeValueKind.Int32 or NativeValueKind.UInt32 or NativeValueKind.Int64 or NativeValueKind.UInt64;
    internal static void ValidateValue(Type managed, NativeTypeLayout native, bool allowSceneProjection = false)
    {
        ValidateReturn(native);
        var size = SizeOf(managed);
        if (allowSceneProjection && managed == typeof(int) && native.Name == "UnityEngine.SceneManagement.Scene")
        { ValidateNativeStruct(native); return; }
        if (native.Kind == NativeValueKind.Reference && managed == typeof(IntPtr) && native.Size == IntPtr.Size) return;
        if (native.Kind == NativeValueKind.Enum)
        {
            // RenderMode is the one existing adapter setter that explicitly uses an Int32 carrier.
            if (managed == typeof(int) && native.Name == "UnityEngine.RenderMode" && native.Underlying!.Kind == NativeValueKind.Int32) return;
            if (!managed.IsEnum || managed.FullName != native.Name)
                throw new UnsupportedSignatureException($"'{managed}' is not the declared enum '{native.Name}'.");
            ValidateValue(Enum.GetUnderlyingType(managed), native.Underlying!);
            return;
        }
        if (managed.IsEnum) throw new UnsupportedSignatureException($"Enum '{managed}' cannot substitute for '{native.Name}'.");
        if (Scalars.TryGetValue(managed, out var scalar) && native.Kind == scalar.Kind && native.Name == scalar.Name && native.Size == size) return;
        var binding = StructBinding(managed);
        if (binding is not null && binding.Value.NativeName == native.Name && size == native.Size)
        { ValidateNativeStruct(native); return; }
        throw new UnsupportedSignatureException($"Managed '{managed}' does not match native '{native.Name}' ({native.Size} bytes).");
    }
    internal static void ValidateArgument(NativeTypeLayout native, NativeRuntime.Arg argument)
    {
        argument.VerifyActive();
        if (argument.IsReference)
        {
            if (native.ByRef || native.Kind != NativeValueKind.Reference)
                throw new UnsupportedSignatureException($"Object reference cannot be passed to '{native.Name}'.");
            return;
        }
        if (native.Kind == NativeValueKind.Reference)
            throw new UnsupportedSignatureException("A value buffer cannot be passed as an object reference; use Arg.Ref or Arg.Reference.");
        if (argument.ManagedType is null) throw new UnsupportedSignatureException("Raw buffer cannot be used as a method value argument.");
        ValidateValue(argument.ManagedType, native);
        if (argument.Size != native.Size) throw new UnsupportedSignatureException("Argument storage size differs from native metadata.");
    }
    internal static T Read<T>(IntPtr pointer) where T : struct => (T)Read(pointer, typeof(T));
    private static object Read(IntPtr pointer, Type type)
    {
        if (pointer == IntPtr.Zero) throw new ArgumentNullException(nameof(pointer));
        _ = SizeOf(type);
        if (type.IsEnum) return Enum.ToObject(type, Read(pointer, Enum.GetUnderlyingType(type)));
        if (type == typeof(bool)) return Marshal.ReadByte(pointer) != 0;
        if (type == typeof(byte)) return Marshal.ReadByte(pointer);
        if (type == typeof(sbyte)) return unchecked((sbyte)Marshal.ReadByte(pointer));
        if (type == typeof(short)) return Marshal.ReadInt16(pointer);
        if (type == typeof(ushort)) return unchecked((ushort)Marshal.ReadInt16(pointer));
        if (type == typeof(char)) return unchecked((char)Marshal.ReadInt16(pointer));
        if (type == typeof(int)) return Marshal.ReadInt32(pointer);
        if (type == typeof(uint)) return unchecked((uint)Marshal.ReadInt32(pointer));
        if (type == typeof(long)) return Marshal.ReadInt64(pointer);
        if (type == typeof(ulong)) return unchecked((ulong)Marshal.ReadInt64(pointer));
        if (type == typeof(IntPtr)) return Marshal.ReadIntPtr(pointer);
        if (type == typeof(UIntPtr)) return unchecked((UIntPtr)(ulong)Marshal.ReadIntPtr(pointer).ToInt64());
        if (type == typeof(float)) return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(pointer));
        if (type == typeof(double)) return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(pointer));
        return Marshal.PtrToStructure(pointer, type)!; // Only explicitly registered, reference-free adapter structs reach this line.
    }
    internal static void Write<T>(IntPtr pointer, T value) where T : struct => Write(pointer, value, typeof(T));
    private static void Write(IntPtr pointer, object value, Type type)
    {
        if (pointer == IntPtr.Zero) throw new ArgumentNullException(nameof(pointer));
        _ = SizeOf(type); // Reject reference-bearing, auto-layout, decimal, DateTime, and arbitrary structs before touching memory.
        if (type.IsEnum) { var storage = Enum.GetUnderlyingType(type); Write(pointer, Convert.ChangeType(value, storage), storage); return; }
        if (type == typeof(bool)) Marshal.WriteByte(pointer, (bool)value ? (byte)1 : (byte)0);
        else if (type == typeof(byte)) Marshal.WriteByte(pointer, (byte)value);
        else if (type == typeof(sbyte)) Marshal.WriteByte(pointer, unchecked((byte)(sbyte)value));
        else if (type == typeof(short)) Marshal.WriteInt16(pointer, (short)value);
        else if (type == typeof(ushort)) Marshal.WriteInt16(pointer, unchecked((short)(ushort)value));
        else if (type == typeof(char)) Marshal.WriteInt16(pointer, unchecked((short)(char)value));
        else if (type == typeof(int)) Marshal.WriteInt32(pointer, (int)value);
        else if (type == typeof(uint)) Marshal.WriteInt32(pointer, unchecked((int)(uint)value));
        else if (type == typeof(long)) Marshal.WriteInt64(pointer, (long)value);
        else if (type == typeof(ulong)) Marshal.WriteInt64(pointer, unchecked((long)(ulong)value));
        else if (type == typeof(IntPtr)) Marshal.WriteIntPtr(pointer, (IntPtr)value);
        else if (type == typeof(UIntPtr)) Marshal.WriteIntPtr(pointer, unchecked((IntPtr)(long)((UIntPtr)value).ToUInt64()));
        else if (type == typeof(float)) Marshal.WriteInt32(pointer, BitConverter.SingleToInt32Bits((float)value));
        else if (type == typeof(double)) Marshal.WriteInt64(pointer, BitConverter.DoubleToInt64Bits((double)value));
        else Marshal.StructureToPtr(value, pointer, false);
    }
}

internal sealed class NativeThreadAccess
{
    private int _owner;
    internal void BindCurrentThread()
    {
        var current = Environment.CurrentManagedThreadId;
        var previous = Interlocked.CompareExchange(ref _owner, current, 0);
        if (previous != 0 && previous != current) throw new InvalidOperationException("Native runtime is already bound to a different Unity thread.");
    }
    internal void Verify()
    {
        if (Volatile.Read(ref _owner) == 0 || _owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Native Unity operation requires the verified lifecycle thread.");
    }
}
