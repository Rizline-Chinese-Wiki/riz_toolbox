using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

internal static class RendererQuaternionRegression
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Renderer quaternion carrier preserves four exact float slots and buffer boundaries", Carrier),
        ("Renderer quaternion publication copies the actual boxed native return", NativeReturn),
        ("Renderer quaternion native setter receives one exact value buffer after its final guard", NativeSetter),
        ("Renderer quaternion invalid size fields offsets and byref metadata reject before native entry", InvalidMetadata),
        ("Renderer quaternion cannot substitute same-layout Vector4 Color or an unregistered carrier", DistinctIdentity),
        ("Renderer quaternion wrong boxed return class or layout rejects before unboxing", WrongBox),
        ("Renderer quaternion unknown receiver and boxed-result frees retain exact owners without retry", UnknownFree),
        ("Renderer quaternion worker rejection performs no exports and preserves caller input", WrongThread)
    };
    private sealed record Responsibility(FakeNative Storage, object Owner, Exception Failure);
    private static readonly List<Responsibility> UnknownOwners = new();
    private static NativeTypeLayout Layout() => new("UnityEngine.Quaternion", NativeValueKind.Struct, 16, Fields: new[]
    {
        new NativeFieldLayout("x", "System.Single", 0), new NativeFieldLayout("y", "System.Single", 4),
        new NativeFieldLayout("z", "System.Single", 8), new NativeFieldLayout("w", "System.Single", 12)
    });
    private static int[] Bits(NativeRendererQuaternion value)
        => new[] { value.X, value.Y, value.Z, value.W }.Select(BitConverter.SingleToInt32Bits).ToArray();
    private static NativeRendererQuaternion Value(IReadOnlyList<int> bits) => new(BitConverter.Int32BitsToSingle(bits[0]),
        BitConverter.Int32BitsToSingle(bits[1]), BitConverter.Int32BitsToSingle(bits[2]), BitConverter.Int32BitsToSingle(bits[3]));
    private static byte[] Bytes(IReadOnlyList<int> bits) => bits.SelectMany(BitConverter.GetBytes).ToArray();
    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object[] Items(object value) => ((System.Collections.IEnumerable)value).Cast<object>().ToArray();
    private static object[] Owners() => Items(typeof(NativePublicationCall).GetField("ProcessOwners", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static T Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Balanced(FakeNative native, int calls)
        => Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots && NativePublicationCall.RetainedCallCount == calls,
            "Quaternion operation leaked known root or publication responsibility.");
    private static void Carrier()
    {
        // Raw ABI transport does not certify finite/unit rotation semantics.
        var bits = new[] { unchecked((int)0x80000000), 1, unchecked((int)0x7FC01234), BitConverter.SingleToInt32Bits(-123.25f) };
        var value = Value(bits);
        Check(Marshal.SizeOf<NativeRendererQuaternion>() == 16 && NativeAbi.SizeOf(typeof(NativeRendererQuaternion)) == 16,
            "The exact adapter Quaternion carrier does not occupy four floats.");
        NativeAbi.ValidateValue(typeof(NativeRendererQuaternion), Layout());
        using var argument = A.Value(value); NativeAbi.ValidateArgument(Layout(), argument);
        Check(argument.Size == 16 && Bits(NativeRuntime.ReadValue<NativeRendererQuaternion>(argument.Pointer)).SequenceEqual(bits), "Quaternion argument changed float bits.");
        using var guarded = new A(32);
        for (var index = 0; index < 32; ++index) Marshal.WriteByte(guarded.Pointer, index, 0x5A);
        NativeAbi.Write(IntPtr.Add(guarded.Pointer, 8), value);
        Check(Bits(NativeRuntime.ReadValue<NativeRendererQuaternion>(IntPtr.Add(guarded.Pointer, 8))).SequenceEqual(bits), "Quaternion read/write changed float slots.");
        for (var index = 0; index < 8; ++index)
            Check(Marshal.ReadByte(guarded.Pointer, index) == 0x5A && Marshal.ReadByte(guarded.Pointer, 24 + index) == 0x5A, "Quaternion storage crossed its 16-byte boundary.");
    }
    private static void NativeReturn()
    {
        using var native = new FakeNative(); var calls = NativePublicationCall.RetainedCallCount;
        var type = native.AddStruct(Layout().Name, 16, Layout().Fields!.ToArray());
        var expected = new[] { BitConverter.SingleToInt32Bits(.125f), unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(-4f), BitConverter.SingleToInt32Bits(1f) };
        native.ReturnType = type; native.Result = native.Box(type, Bytes(expected));
        var actual = native.Api.InvokeValueWithPublication<NativeRendererQuaternion>(native.Method, native.Instance);
        Check(Bits(actual).SequenceEqual(expected) && native.Invocations == 1 && native.Unboxes == 1
            && native.NewRoots == 2, "Actual Quaternion return did not validate, root and copy its boxed result."); Balanced(native, calls);
    }
    private static void NativeSetter()
    {
        using var native = new FakeNative(); var api = native.RendererQuaternionCaptureApi(); var calls = NativePublicationCall.RetainedCallCount;
        var type = native.AddStruct(Layout().Name, 16, Layout().Fields!.ToArray()); native.Parameters = new[] { type }; native.ReturnType = native.Void;
        var expected = new[] { BitConverter.SingleToInt32Bits(-.25f), BitConverter.SingleToInt32Bits(.5f), unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(.75f) };
        using var argument = A.Value(Value(expected)); var guards = 0;
        native.DuringRendererQuaternionSetter = () => Check(native.RendererQuaternionArgumentPointer == argument.Pointer
            && native.RendererQuaternionArgumentBits.SequenceEqual(expected), "The native setter received a reference or another parameter buffer.");
        api.InvokeVoidWithPublicationGuarded(native.Method, native.Instance, () =>
        {
            ++guards; Check(native.RendererQuaternionSetterCalls == 0 && native.Invocations == 0 && native.HasRootFor(native.Instance),
                "The final guard did not precede the actual rooted setter.");
        }, argument);
        Check(guards == 1 && native.RendererQuaternionSetterCalls == 1 && native.Invocations == 1 && native.RendererQuaternionReceiverRooted,
            "The Quaternion setter guard/native-entry count differs.");
        Expect<ObjectDisposedException>(() => _ = argument.Pointer); Balanced(native, calls);
        using var rejected = A.Value(Value(expected)); var entered = native.RendererQuaternionSetterCalls;
        Expect<IOException>(() => api.InvokeVoidWithPublicationGuarded(native.Method, native.Instance,
            () => throw new IOException("Withdraw before Quaternion setter."), rejected));
        Check(native.RendererQuaternionSetterCalls == entered && native.Invocations == 1, "A throwing final guard dispatched a Quaternion setter."); Balanced(native, calls);
    }
    private static void InvalidMetadata()
    {
        foreach (var mode in new[] { "small", "large", "offset", "name", "type", "missing", "byref" })
        foreach (var setter in new[] { false, true })
        {
            using var native = new FakeNative(); var api = setter ? native.RendererQuaternionCaptureApi() : native.Api;
            var calls = NativePublicationCall.RetainedCallCount; var fields = Layout().Fields!.ToArray();
            if (mode == "offset") fields[3] = fields[3] with { Offset = 8 };
            if (mode == "name") fields[3] = fields[3] with { Name = "a" };
            if (mode == "type") fields[3] = fields[3] with { TypeName = "System.Int32" };
            if (mode == "missing") fields = fields.Take(3).ToArray();
            var type = mode == "byref" ? native.AddType(Layout().Name, 17, true, 16, byRef: true)
                : native.AddStruct(Layout().Name, mode == "small" ? 12 : mode == "large" ? 20 : 16, fields);
            if (setter)
            {
                native.ReturnType = native.Void; native.Parameters = new[] { type }; using var input = A.Value(new NativeRendererQuaternion(1, 2, 3, 4));
                Expect<UnsupportedSignatureException>(() => api.InvokeVoidWithPublicationGuarded(native.Method, native.Instance, () => { }, input));
            }
            else
            {
                native.ReturnType = type; native.Result = native.Box(type, new byte[16]);
                Expect<UnsupportedSignatureException>(() => api.InvokeValueWithPublication<NativeRendererQuaternion>(native.Method, native.Instance));
            }
            Check(native.Invocations == 0 && native.RendererQuaternionSetterCalls == 0 && native.Unboxes == 0, "Malformed Quaternion metadata entered native code: " + mode);
            Balanced(native, calls);
        }
    }
    private static void DistinctIdentity()
    {
        using var native = new FakeNative(); var api = native.RendererQuaternionCaptureApi(); var calls = NativePublicationCall.RetainedCallCount;
        var quaternion = native.AddStruct(Layout().Name, 16, Layout().Fields!.ToArray());
        var vector = native.AddStruct("UnityEngine.Vector4", 16, Layout().Fields!.ToArray());
        var colorFields = new[] { new NativeFieldLayout("r", "System.Single", 0), new NativeFieldLayout("g", "System.Single", 4),
            new NativeFieldLayout("b", "System.Single", 8), new NativeFieldLayout("a", "System.Single", 12) };
        var color = native.AddStruct("UnityEngine.Color", 16, colorFields);
        native.ReturnType = native.Void; native.Parameters = new[] { quaternion };
        using var vectorInput = A.Value(new NativeTweenVector4(1, 2, 3, 4)); using var colorInput = A.Value(new Rgba(1, 2, 3, 4));
        Expect<UnsupportedSignatureException>(() => api.InvokeVoidWithPublication(native.Method, native.Instance, vectorInput));
        Expect<UnsupportedSignatureException>(() => api.InvokeVoidWithPublication(native.Method, native.Instance, colorInput));
        foreach (var other in new[] { vector, color })
        {
            native.ReturnType = native.Void; native.Parameters = new[] { other }; using var q = A.Value(new NativeRendererQuaternion(1, 2, 3, 4));
            Expect<UnsupportedSignatureException>(() => api.InvokeVoidWithPublication(native.Method, native.Instance, q));
            native.Parameters = Array.Empty<IntPtr>(); native.ReturnType = other; native.Result = native.Box(other, new byte[16]);
            Expect<UnsupportedSignatureException>(() => api.InvokeValueWithPublication<NativeRendererQuaternion>(native.Method, native.Instance));
        }
        Expect<UnsupportedSignatureException>(() => NativeAbi.SizeOf(typeof(UnregisteredFourFloats)));
        Check(native.Invocations == 0 && native.RendererQuaternionSetterCalls == 0 && native.Unboxes == 0,
            "A same-size Quaternion/Vector4/Color identity substitution crossed native entry."); Balanced(native, calls);
    }
    private static void WrongBox()
    {
        foreach (var mode in new[] { "vector", "color", "reference", "bad-quaternion" })
        {
            using var native = new FakeNative(); var calls = NativePublicationCall.RetainedCallCount;
            native.ReturnType = native.AddStruct(Layout().Name, 16, Layout().Fields!.ToArray());
            var boxedType = mode switch
            {
                "vector" => native.AddStruct("UnityEngine.Vector4", 16, Layout().Fields!.ToArray()),
                "color" => native.AddStruct("UnityEngine.Color", 16, new("r", "System.Single", 0), new("g", "System.Single", 4), new("b", "System.Single", 8), new("a", "System.Single", 12)),
                "reference" => native.Object,
                _ => native.AddStruct(Layout().Name, 20, Layout().Fields!.ToArray())
            };
            native.Result = native.Box(boxedType, new byte[20]);
            Expect<UnsupportedSignatureException>(() => native.Api.InvokeValueWithPublication<NativeRendererQuaternion>(native.Method, native.Instance));
            Check(native.Invocations == 1 && native.Unboxes == 0 && native.NewRoots == 2, "A wrong actual boxed result was unsafely unboxed: " + mode); Balanced(native, calls);
        }
    }
    private static void UnknownFree()
    {
        foreach (var failedHandle in new uint[] { 1, 2 })
        {
            var native = new FakeNative(); native.ReturnType = native.AddStruct(Layout().Name, 16, Layout().Fields!.ToArray());
            native.Result = native.Box(native.ReturnType, Bytes(new[] { 1, 2, 3, 4 })); native.FailFreeHandle = failedHandle;
            var before = Owners(); var calls = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
            var error = Expect<AggregateException>(() => native.Api.InvokeValueWithPublication<NativeRendererQuaternion>(native.Method, native.Instance));
            var owner = Owners().Except(before).Single(); var failures = Items(Field(owner, "_failures")).Where(value => value is not null).ToArray();
            Check(native.Invocations == 1 && native.Unboxes == 1 && native.ActiveRoots == 0 && native.FreeHandleAttempts == 2
                && NativePublicationCall.RetainedCallCount == calls + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1
                && Items(Field(owner, "_roots")).Count(value => value is not null) == 1 && failures.Length == 1 && failures[0] is IOException
                && error.InnerExceptions.Any(value => ReferenceEquals(value, failures[0])), "Unknown Quaternion temporary free lost its original root/error responsibility.");
            var frees = native.FreeHandleAttempts; Expect<InvalidOperationException>(((NativePublicationCall)owner).Dispose);
            Check(native.FreeHandleAttempts == frees, "Unknown Quaternion temporary free was retried."); UnknownOwners.Add(new(native, owner, error));
        }
    }
    private static void WrongThread()
    {
        using var native = new FakeNative(); var api = native.RendererQuaternionCaptureApi(); var calls = NativePublicationCall.RetainedCallCount;
        var type = native.AddStruct(Layout().Name, 16, Layout().Fields!.ToArray()); native.ReturnType = type; native.Result = native.Box(type, new byte[16]);
        using var input = A.Value(new NativeRendererQuaternion(1, 2, 3, 4)); native.BeginLowerIl2CppAudit();
        try { Task.Run(() => Expect<InvalidOperationException>(() => api.InvokeValueWithPublication<NativeRendererQuaternion>(native.Method, native.Instance))).GetAwaiter().GetResult(); }
        finally { native.EndLowerIl2CppAudit(); }
        Check(native.LowerIl2CppCalls == 0 && native.NewRoots == 0 && NativePublicationCall.RetainedCallCount == calls, "A worker Quaternion return touched an export.");
        native.ReturnType = native.Void; native.Result = IntPtr.Zero; native.Parameters = new[] { type }; var guards = 0; native.BeginLowerIl2CppAudit();
        try { Task.Run(() => Expect<InvalidOperationException>(() => api.InvokeVoidWithPublicationGuarded(native.Method, native.Instance, () => ++guards, input))).GetAwaiter().GetResult(); }
        finally { native.EndLowerIl2CppAudit(); }
        Check(native.LowerIl2CppCalls == 0 && native.NewRoots == 0 && native.Invocations == 0 && guards == 0
            && input.Pointer != IntPtr.Zero && NativePublicationCall.RetainedCallCount == calls, "Worker rejection consumed a Quaternion input or reached native entry.");
        api.InvokeVoidWithPublicationGuarded(native.Method, native.Instance, () => ++guards, input);
        Check(guards == 1 && native.RendererQuaternionSetterCalls == 1, "Worker rejection consumed owner-thread Quaternion write admission."); Balanced(native, calls);
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct UnregisteredFourFloats(float X, float Y, float Z, float W);
}

// Test-local raw setter observation. Shared FakeNative support stays unchanged.
sealed partial class FakeNative
{
    internal int RendererQuaternionSetterCalls;
    internal IntPtr RendererQuaternionArgumentPointer;
    internal int[] RendererQuaternionArgumentBits = Array.Empty<int>();
    internal bool RendererQuaternionReceiverRooted = true;
    internal Action? DuringRendererQuaternionSetter;
    internal NativeRuntime RendererQuaternionCaptureApi()
    {
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(RendererQuaternionInvoke));
        var runtime = new NativeRuntime(ExportPointer); runtime.BindCurrentThread(); return runtime;
    }
    private IntPtr RendererQuaternionInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        ++RendererQuaternionSetterCalls; RendererQuaternionReceiverRooted &= HasRootFor(receiver);
        RendererQuaternionArgumentPointer = Marshal.ReadIntPtr(arguments);
        RendererQuaternionArgumentBits = Enumerable.Range(0, 4).Select(index => Marshal.ReadInt32(RendererQuaternionArgumentPointer, index * sizeof(float))).ToArray();
        DuringRendererQuaternionSetter?.Invoke(); return Invoke(method, receiver, arguments, exception);
    }
}
