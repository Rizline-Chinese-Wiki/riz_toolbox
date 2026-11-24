using System.Runtime.InteropServices;
using System.Reflection;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

if (args.Length == 1 && args[0] == "--line-setpositions-checks")
{
    var cases = ListenerMetadataRegression.LineSetPositionsCases;
    if (cases.Length != 9) return 2;
    foreach (var item in cases)
    {
        Console.WriteLine("RUN " + item.Name);
        try { item.Run(); }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + item.Name + ": " + error); return 1; }
        Console.WriteLine("PASS " + item.Name);
    }
    Console.WriteLine($"SetPositions checks: {cases.Length}/{cases.Length} passed.");
    return 0;
}

if (args.Length == 1 && args[0] == "--vector3-buffer-checks")
{
    var cases = Vector3BufferRegression.Cases;
    if (cases.Length != 10) { Console.Error.WriteLine("Unexpected Vector3 buffer case registry length."); return 2; }
    foreach (var item in cases)
    {
        Console.WriteLine("RUN " + item.Name);
        try { item.Run(); }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + item.Name + ": " + error); return 1; }
        Console.WriteLine("PASS " + item.Name);
    }
    Console.WriteLine($"Vector3 buffer checks: {cases.Length}/{cases.Length} passed.");
    return 0;
}

if (args.Length == 1 && args[0] is "--vector3-array-checks" or "--single-array-checks")
{
    var vector = args[0] == "--vector3-array-checks";
    var cases = vector ? Vector3ArrayPublicationRegression.Cases : SingleArrayPublicationRegression.Cases;
    var expected = vector ? 7 : 3;
    if (cases.Length != expected) { Console.Error.WriteLine("Unexpected array case registry length."); return 2; }
    foreach (var item in cases)
    {
        Console.WriteLine("RUN " + item.Name);
        try { item.Run(); }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + item.Name + ": " + error); return 1; }
        Console.WriteLine("PASS " + item.Name);
    }
    Console.WriteLine($"Array shadow checks: {expected}/{expected} passed.");
    return 0;
}

if (args.Length == 1 && args[0] == "--exact-static-field-read-checks")
{
    var exactStaticFailed = 0;
    foreach (var check in ExactStaticFieldReadRegression.Cases)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { ++exactStaticFailed; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    Console.WriteLine($"Exact static field read checks: {ExactStaticFieldReadRegression.Cases.Length - exactStaticFailed}/{ExactStaticFieldReadRegression.Cases.Length} passed.");
    return exactStaticFailed == 0 ? 0 : 1;
}

if (args.Length == 1 && args[0] == "--seek-reference-publication-checks")
{
    var tests = ReferencePublicationAdmissionRegression.Cases.Concat(NativePublicationRegression.Cases)
        .Concat(VoidPublicationRegression.Cases).Concat(ExactFieldWriteRegression.Cases).Concat(GuardedFieldPublicationRegression.Cases).ToArray();
    var referenceAdmissionFailed = 0;
    foreach (var test in tests)
    {
        try { test.Run(); Console.WriteLine("PASS " + test.Name); }
        catch (Exception error) { ++referenceAdmissionFailed; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
    }
    Console.WriteLine($"Seek reference publication checks: {tests.Length - referenceAdmissionFailed}/{tests.Length} passed.");
    return referenceAdmissionFailed == 0 ? 0 : 1;
}

if (args.Length == 1 && args[0] is "--void-publication-checks" or "--prepared-write-publication-checks" or "--renderer-abi-checks")
{
    var preparedWriteChecks = VoidPublicationRegression.Cases.Concat(ExactFieldWriteRegression.Cases).Concat(GuardedFieldPublicationRegression.Cases).ToArray();
    var publicationChecks = args[0] switch {
        "--void-publication-checks" => VoidPublicationRegression.Cases,
        "--renderer-abi-checks" => preparedWriteChecks.Concat(RendererQuaternionRegression.Cases).Concat(TweenGraphReadRegression.Cases).ToArray(),
        _ => preparedWriteChecks
    };
    var publicationFailed = 0;
    foreach (var check in publicationChecks)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { ++publicationFailed; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    var label = args[0] switch { "--void-publication-checks" => "Void publication checks", "--renderer-abi-checks" => "Renderer ABI checks", _ => "Prepared write publication checks" };
    Console.WriteLine($"{label}: {publicationChecks.Length - publicationFailed}/{publicationChecks.Length} passed.");
    return publicationFailed == 0 ? 0 : 1;
}

if (args.Length == 5 && args[0] == "--playback-stock-bridge-checks")
    return NativePlaybackStockSourceBridgeChecks.Run(args[1], args[2], args[3], args[4]);

if (args.Length == 1 && args[0] == "--engine-transport-publication-checks")
{
    var transportPublicationFailed = 0;
    foreach (var check in NativeEngineTransportPublicationTests.Cases)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { ++transportPublicationFailed; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    Console.WriteLine($"Engine transport publication checks: {NativeEngineTransportPublicationTests.Cases.Length - transportPublicationFailed}/{NativeEngineTransportPublicationTests.Cases.Length} passed.");
    return transportPublicationFailed == 0 ? 0 : 1;
}

if (args.Length == 1 && args[0] == "--engine-transport-layout-checks")
{
    var transportFailed = 0;
    var transportChecks = NativeEngineTransportPublicationLayoutTests.Cases
        .Append((Name: "Actual native V5 layout receipt agrees with the CLR wire", Run: (Action)NativeEngineTransportPublicationLayoutTests.RequireNativeLayoutEvidence)).ToArray();
    foreach (var check in transportChecks)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { ++transportFailed; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    Console.WriteLine($"Engine transport layout checks: {transportChecks.Length - transportFailed}/{transportChecks.Length} passed.");
    return transportFailed == 0 ? 0 : 1;
}

if (args.Length == 6 && args[0] == "--engine-compiled-profile-checks")
{
    var compiled = NativeEngineProductionProfile.Read()
        ?? throw new InvalidOperationException("The test build did not include the generated production fingerprints.");
    for (var index = 0; index < 5; ++index)
        Check(string.Equals(compiled.Hash(index), args[index + 1], StringComparison.Ordinal));
    for (var index = 0; index < 4; ++index)
        Check(compiled.FunctionRva((NativeEngineNodeKind)index) == NativeEngineNodeProfile.Rva((NativeEngineNodeKind)index));
    Check(!NativeEngineFirstInstall.ProductionStartupAdmissionKnown);
    Throws<UnsupportedSignatureException>(() => NativeEngineFirstInstall.RequireReady());
    Console.WriteLine("PASS Compiled production runtime matches five final image fingerprints and four fixed RVAs; admission remains unavailable (11 checks).");
    return 0;
}

if (args.Length == 1 && args[0] is "--engine-publication-model-checks" or "--engine-runtime-coordinator-checks" or "--unity-coroutine-owner-checks")
{
    var modelFailed = 0;
    var modelChecks = args[0] switch
    {
        "--engine-publication-model-checks" => NativeEnginePublicationTests.Cases,
        "--unity-coroutine-owner-checks" => UnityCoroutineOwnerRegression.Cases,
        _ => NativeEngineRuntimeCoordinatorTests.Cases.Concat(NativeEngineRuntimeSubscriptionLifetimeTests.Cases)
            .Concat(NativeEngineRuntimeSchedulerTests.Cases).ToArray()
    };
    foreach (var check in modelChecks)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { ++modelFailed; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    Console.WriteLine($"Engine managed checks: {modelChecks.Length - modelFailed}/{modelChecks.Length} passed.");
    return modelFailed == 0 ? 0 : 1;
}

if (args.Length > 0 && args[0] == NativeFirstInstallMappedRegression.TickAdmissionModeArgument)
    return NativeFirstInstallMappedRegression.RunTickAdmissionChecks();
if (args.Length > 0 && args[0] == RuntimeTickNativeAdmissionRegression.ModeArgument)
    return RuntimeTickNativeAdmissionRegression.RunChild(args);
if (args.Length > 0 && args[0] == NativeFirstInstallMappedRegression.ModeArgument)
    return NativeFirstInstallMappedRegression.RunChild(args);
if (args.Length > 0 && args[0] == NativeAdoptedBrokerRegression.ModeArgument)
    return NativeAdoptedBrokerRegression.RunChild(args);
if (args.Length > 0 && args[0] == NativeListenerManagedRegression.ModeArgument)
    return NativeListenerManagedRegression.RunChild(args);
if (args.Length > 0 && args[0] == NativeOperationManagedRegression.ModeArgument)
    return NativeOperationManagedRegression.RunChild(args);
if (args.Length > 0 && args[0] == NativeOperationBootstrapManagedRegression.ModeArgument)
    return NativeOperationBootstrapManagedRegression.RunChild(args);
if (args.Length > 0 && args[0] == NativeEngineNodeManagedRegression.ModeArgument)
    return NativeEngineNodeManagedRegression.RunChild(args);
if (args.Length > 0 && args[0] == NativeOperationLedgerRegression.ModeArgument)
    return NativeOperationLedgerRegression.RunChild(args);

var checks = new (string Name, Action Run)[]
{
    ("Boolean read ignores adjacent nonzero bytes", () => {
        using var data = new A(8); Marshal.WriteInt64(data.Pointer, -256);
        Check(!NativeRuntime.ReadValue<bool>(data.Pointer));
        Marshal.WriteByte(data.Pointer, 1); Check(NativeRuntime.ReadValue<bool>(data.Pointer));
    }),
    ("Boolean writes leave adjacent bytes untouched", () => {
        using var data = new A(8); Marshal.WriteInt64(data.Pointer, -1);
        NativeAbi.Write(data.Pointer, false); Equal((byte)0, Marshal.ReadByte(data.Pointer));
        for (var i = 1; i < 8; i++) Equal((byte)255, Marshal.ReadByte(data.Pointer, i));
        Equal(1, NativeAbi.SizeOf(typeof(bool)));
    }),
    ("Every supported scalar round trips exactly", () => {
        RoundTrip(true); RoundTrip(false); RoundTrip(sbyte.MinValue); RoundTrip(byte.MaxValue);
        RoundTrip(short.MinValue); RoundTrip(ushort.MaxValue); RoundTrip('\u7eff');
        RoundTrip(int.MinValue); RoundTrip(uint.MaxValue); RoundTrip(long.MinValue); RoundTrip(ulong.MaxValue);
        RoundTrip(float.Epsilon); RoundTrip(-0.125); RoundTrip(new IntPtr(0x12345678)); RoundTrip(new UIntPtr(0x12345678));
    }),
    ("Enums use their actual byte and signed long storage", () => {
        RoundTrip(ByteEnum.High); RoundTrip(LongEnum.Low);
        Equal(1, NativeAbi.SizeOf(typeof(ByteEnum))); Equal(8, NativeAbi.SizeOf(typeof(LongEnum)));
    }),
    ("Unknown structs and managed references are refused before a write", () => {
        using var data = new A(16); Marshal.WriteInt32(data.Pointer, 12345);
        Throws<UnsupportedSignatureException>(() => NativeAbi.Write(data.Pointer, new ReferenceStruct("unsafe")));
        Throws<UnsupportedSignatureException>(() => NativeAbi.Write(data.Pointer, DateTime.UnixEpoch));
        Throws<UnsupportedSignatureException>(() => NativeAbi.Write(data.Pointer, 1m));
        Equal(12345, Marshal.ReadInt32(data.Pointer));
    }),
    ("Matching size cannot substitute an unrelated scalar", () => {
        using var value = A.Value(42);
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateArgument(new("System.Single", NativeValueKind.Single, 4), value));
    }),
    ("Wrong scalar size is refused", () => {
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(bool), new("System.Boolean", NativeValueKind.Boolean, 4)));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(int), new("System.Int32", NativeValueKind.Int32, 8)));
    }),
    ("Known Color and Vector2 field names, sizes, offsets are checked", () => {
        NativeAbi.ValidateNativeStruct(Color());
        NativeAbi.ValidateNativeStruct(new("UnityEngine.Vector2", NativeValueKind.Struct, 8, Fields: new[] { new NativeFieldLayout("x", "System.Single", 0), new NativeFieldLayout("y", "System.Single", 4) }));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateNativeStruct(Color() with { Size = 20 }));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateNativeStruct(Color() with { Fields = Color().Fields!.Select(f => f with { Offset = f.Offset + 16 }).ToArray() }));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateNativeStruct(Color() with { Fields = Color().Fields!.Select(f => f.Name == "a" ? f with { TypeName = "System.Int32" } : f).ToArray() }));
    }),
    ("Actual adapter Color and Vector2 values round trip through whitelisted layouts", () => {
        AdapterStructRoundTrip("Rgba",new object[]{0.125f,0.25f,0.5f,1f},Color());
        AdapterStructRoundTrip("Vec2",new object[]{0.125f,-0.25f},new("UnityEngine.Vector2",NativeValueKind.Struct,8,
            Fields:new[]{new NativeFieldLayout("x","System.Single",0),new NativeFieldLayout("y","System.Single",4)}));
    }),
    ("Rect native fields, size and value offsets are exact", () => {
        var rect = new NativeTypeLayout("UnityEngine.Rect",NativeValueKind.Struct,16,Fields:new[] {
            new NativeFieldLayout("m_XMin","System.Single",0),new NativeFieldLayout("m_YMin","System.Single",4),
            new NativeFieldLayout("m_Width","System.Single",8),new NativeFieldLayout("m_Height","System.Single",12)});
        NativeAbi.ValidateNativeStruct(rect);
        Throws<UnsupportedSignatureException>(()=>NativeAbi.ValidateNativeStruct(rect with { Size=20 }));
        Throws<UnsupportedSignatureException>(()=>NativeAbi.ValidateNativeStruct(rect with { Fields=rect.Fields!.Select(x=>x with { Offset=x.Offset+16 }).ToArray() }));
        Throws<UnsupportedSignatureException>(()=>NativeAbi.ValidateNativeStruct(rect with { Fields=rect.Fields!.Select(x=>x.Name=="m_Width"?x with { TypeName="System.Int32" }:x).ToArray() }));
        AdapterStructRoundTrip("Visual.NativeRect",new object[]{-3f,4f,256f,128f},rect);
    }),
    ("Struct resemblance alone does not register a managed ABI", () => {
        NativeAbi.ValidateManagedStruct(typeof(FloatPair), 2);
        Throws<UnsupportedSignatureException>(() => NativeAbi.SizeOf(typeof(FloatPair)));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateManagedStruct(typeof(ReferenceStruct), 1));
    }),
    ("Scene handle is the only approved native struct scalar projection", () => {
        var scene = new NativeTypeLayout("UnityEngine.SceneManagement.Scene", NativeValueKind.Struct, 4,
            Fields: new[] { new NativeFieldLayout("m_Handle", "System.Int32", 0) });
        NativeAbi.ValidateValue(typeof(int), scene, allowSceneProjection: true);
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(int), scene));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(int), scene with { Size = 8 }, true));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateValue(typeof(int), Color(), true));
    }),
    ("ref/out and unknown return layouts are refused", () => {
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateReturn(new("System.Int32&", NativeValueKind.Int32, 4, true)));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateReturn(new("Unknown", NativeValueKind.Unsupported, 4)));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateReturn(new("FakeStruct", NativeValueKind.Struct, 4)));
    }),
    ("Reference arguments cannot stand in for scalar buffers", () => {
        using var argument = A.Ref(new IntPtr(123));
        Throws<UnsupportedSignatureException>(() => NativeAbi.ValidateArgument(new("System.Int32", NativeValueKind.Int32, 4), argument));
    }),
    ("IntPtr value buffers cannot stand in for object references", () => {
        using var argument=A.Value(new IntPtr(123));
        Throws<UnsupportedSignatureException>(()=>NativeAbi.ValidateArgument(new("System.Object",NativeValueKind.Reference,IntPtr.Size),argument));
        using var native=new FakeNative();native.Parameters=new[]{native.Object};
        Throws<UnsupportedSignatureException>(()=>native.Api.Invoke(native.Method,native.Instance,A.Value(new IntPtr(123))));Equal(0,native.Invocations);
    }),
    ("Null references are valid active arguments", () => {
        using var argument = A.Ref(IntPtr.Zero);
        Equal(IntPtr.Zero, argument.Pointer); NativeAbi.ValidateArgument(new("System.Object", NativeValueKind.Reference, IntPtr.Size), argument);
    }),
    ("Owned native buffers are zeroed, bounded and idempotently released", () => {
        var argument = new A(32);
        for (var i = 0; i < 32; i++) Equal((byte)0, Marshal.ReadByte(argument.Pointer, i));
        argument.Dispose(); argument.Dispose(); Throws<ObjectDisposedException>(() => { _ = argument.Pointer; });
        Throws<ArgumentOutOfRangeException>(() => new A(-1)); Throws<ArgumentOutOfRangeException>(() => new A(16 * 1024 * 1024 + 1));
    }),
    ("Arguments release an attached root exactly once", () => {
        var resource = new CountingDisposable(); var argument = A.Reference(new IntPtr(1), resource);
        argument.Dispose(); argument.Dispose(); Equal(1, resource.Count);
    }),
    ("Disposed argument is rejected before invocation", () => {
        using var native = new FakeNative(); native.Parameters = new[] { native.Int32 };
        var arg = A.Value(1); arg.Dispose();
        Throws<ObjectDisposedException>(() => native.Api.Invoke(native.Method, native.Instance, arg)); Equal(0, native.Invocations);
    }),
    ("Native field type is checked before native output writes", () => {
        using var native = new FakeNative(); native.FieldType = native.UnknownStruct; native.FieldBytes = new byte[128];
        Throws<UnsupportedSignatureException>(() => native.Api.Read<int>(native.Instance, "field"));
        Equal(0, native.FieldReads); Equal(0, native.ActiveRoots);
    }),
    ("Native Boolean field reads use one-byte output storage", () => {
        using var native = new FakeNative(); native.FieldBytes = new byte[] { 0 };
        Check(!native.Api.Read<bool>(native.Instance, "flag")); Equal(1, native.FieldReads); Equal(0, native.ActiveRoots);
    }),
    ("Static field mismatches fail before native field_get_value", () => {
        using var native = new FakeNative(); native.StaticField = true;
        Throws<UnsupportedSignatureException>(() => native.Api.Read<bool>(native.Instance, "flag")); Equal(0, native.FieldReads);
        Check(!native.Api.ReadStatic<bool>(new IntPtr(1), "flag")); Equal(1, native.FieldReads);
        native.StaticField = false;
        Throws<UnsupportedSignatureException>(() => native.Api.ReadStatic<bool>(new IntPtr(1), "flag")); Equal(1, native.FieldReads);
    }),
    ("Wrong field scalar type fails before native copy", () => {
        using var native = new FakeNative(); native.FieldType = native.Single; native.FieldBytes = BitConverter.GetBytes(1f);
        Throws<UnsupportedSignatureException>(() => native.Api.Read<int>(native.Instance, "float")); Equal(0, native.FieldReads);
    }),
    ("Argument type mismatch fails before runtime_invoke", () => {
        using var native = new FakeNative(); native.Parameters = new[] { native.Single }; var argument = A.Value(12);
        Throws<UnsupportedSignatureException>(() => native.Api.Invoke(native.Method, native.Instance, argument));
        Equal(0, native.Invocations); Throws<ObjectDisposedException>(() => { _ = argument.Pointer; });
    }),
    ("Argument count and static receiver mismatches are refused", () => {
        using var native = new FakeNative(); native.Parameters = new[] { native.Int32 };
        Throws<UnsupportedSignatureException>(() => native.Api.Invoke(native.Method, native.Instance));
        native.IsStatic = true;
        Throws<UnsupportedSignatureException>(() => native.Api.Invoke(native.Method, native.Instance, A.Value(1))); Equal(0, native.Invocations);
    }),
    ("ref/out metadata fails before any method side effect", () => {
        using var native = new FakeNative(); native.Parameters = new[] { native.AddType("System.Int32&", 8, true, 4, true) };
        Throws<UnsupportedSignatureException>(() => native.Api.Invoke(native.Method, native.Instance, A.Value(1))); Equal(0, native.Invocations);
    }),
    ("Unknown return struct fails before original method executes", () => {
        using var native = new FakeNative(); native.ReturnType = native.UnknownStruct;
        Throws<UnsupportedSignatureException>(() => native.Api.Invoke(native.Method, native.Instance)); Equal(0, native.Invocations);
    }),
    ("Open generic methods and byref returns are refused", () => {
        using var native=new FakeNative();native.GenericMethod=true;
        Throws<UnsupportedSignatureException>(()=>native.Api.Invoke(native.Method,native.Instance));Equal(0,native.Invocations);
        native.InflatedMethod=true;native.Api.Invoke(native.Method,native.Instance);Equal(1,native.Invocations);
        native.ReturnType=native.AddType("System.Int32&",8,true,4,true);
        Throws<UnsupportedSignatureException>(()=>native.Api.Invoke(native.Method,native.Instance));Equal(1,native.Invocations);
    }),
    ("Receiver assignability and null static receiver are verified", () => {
        using var native=new FakeNative();
        Throws<UnsupportedSignatureException>(()=>native.Api.Invoke(native.Method,native.OtherObject));Equal(0,native.Invocations);
        native.IsStatic=true;native.Api.Invoke(native.Method,IntPtr.Zero);Equal(1,native.Invocations);
    }),
    ("Known struct metadata is validated through native export path", () => {
        using var native=new FakeNative();var color=Color();
        native.ReturnType=native.AddStruct(color.Name,color.Size,color.Fields!.ToArray());
        native.Api.Invoke(native.Method,native.Instance);Equal(1,native.Invocations);
        native.ReturnType=native.AddStruct(color.Name,20,color.Fields!.ToArray());
        Throws<UnsupportedSignatureException>(()=>native.Api.Invoke(native.Method,native.Instance));Equal(1,native.Invocations);
    }),
    ("Unbox checks the actual boxed type before reading its value", () => {
        using var native=new FakeNative();var boxed=native.Box(native.Boolean,new byte[]{0,255,255,255});
        Check(!native.Api.Unbox<bool>(boxed));Equal(1,native.Unboxes);
        Throws<UnsupportedSignatureException>(()=>native.Api.Unbox<int>(boxed));Equal(1,native.Unboxes);Equal(0,native.ActiveRoots);
        Throws<UnsupportedSignatureException>(()=>native.Api.Unbox<int>(native.OtherObject));
        Throws<TargetExpiredException>(()=>native.Api.Unbox<int>(IntPtr.Zero));
    }),
    ("Scene projection verifies actual native instance field metadata", () => {
        using var native=new FakeNative();
        var scene=native.AddStruct("UnityEngine.SceneManagement.Scene",4,new NativeFieldLayout("m_Handle","System.Int32",0));
        Equal(123,native.Api.Unbox<int>(native.Box(scene,BitConverter.GetBytes(123))));
        var bad=native.AddStruct("UnityEngine.SceneManagement.Scene",4,new NativeFieldLayout("other","System.Int32",0));
        Throws<UnsupportedSignatureException>(()=>native.Api.Unbox<int>(native.Box(bad,BitConverter.GetBytes(123))));Equal(1,native.Unboxes);
    }),
    ("Enum metadata permits exact managed enum identity and the known RenderMode carrier", () => {
        using var native=new FakeNative();
        var storage=native.AddType("System.Byte",5,true,1);
        native.Parameters=new[]{native.AddType(typeof(ByteEnum).FullName!,17,true,1,enumBase:storage)};
        native.Api.Invoke(native.Method,native.Instance,A.Value(ByteEnum.High));
        native.Parameters=new[]{native.AddType("UnityEngine.RenderMode",17,true,4,enumBase:native.Int32)};
        native.Api.Invoke(native.Method,native.Instance,A.Value(0));
        native.Parameters=new[]{native.AddType("Fake.OtherEnum",17,true,4,enumBase:native.Int32)};
        Throws<UnsupportedSignatureException>(()=>native.Api.Invoke(native.Method,native.Instance,A.Value(0)));Equal(2,native.Invocations);
    }),
    ("Reference assignability is verified before invocation", () => {
        using var native = new FakeNative(); native.Parameters = new[] { native.Receiver };
        Throws<UnsupportedSignatureException>(() => native.Api.Invoke(native.Method, native.Instance, A.Ref(native.OtherObject))); Equal(0, native.Invocations);
    }),
    ("Array enumeration refuses non-array objects before calling methods", () => {
        using var native=new FakeNative();
        Throws<UnsupportedSignatureException>(()=>native.Api.ArrayItems(native.OtherObject).ToArray());
        Equal(0,native.Invocations);Equal(0,native.ActiveRoots);Equal(0,native.Api.ArrayItems(IntPtr.Zero).Count());
    }),
    ("Oversized native string allocation is refused before native code", () => {
        using var native=new FakeNative();
        Throws<ArgumentOutOfRangeException>(()=>native.Api.String(new string('x',4*1024*1024+1)));
    }),
    ("Pinned Single arrays use exported dynamic header sizes without overwriting sentinels", () => {
        using var native=new FakeNative(); native.ArrayHeaderBytes=48;
        using var array=native.Api.CreateSingleArrayForClass(new[]{-1f,.25f,1f},native.ClassOf(native.Single));
        var copied=new float[3];Marshal.Copy(IntPtr.Add(array.Target,48),copied,0,3);
        Check(copied.SequenceEqual(new[]{-1f,.25f,1f}));Equal((byte)0x5a,Marshal.ReadByte(array.Target,47));Equal((byte)0x5a,Marshal.ReadByte(array.Target,60));
        Equal(2,native.ArrayAllocations);Equal(2,native.PinnedRoots);Equal(1,native.ActiveRoots);
        array.Dispose();Equal(0,native.ActiveRoots);
    }),
    ("Single array shape and byte-size mismatch are rejected before copying and release roots", () => {
        using var native=new FakeNative();native.CorruptArrayByteLength=true;
        Throws<UnsupportedSignatureException>(()=>native.Api.CreateSingleArrayForClass(new[]{.5f},native.ClassOf(native.Single)));
        Equal(0,native.ActiveRoots);Equal((byte)0x5a,Marshal.ReadByte(native.LastArray,native.ArrayHeaderBytes));
        native.CorruptArrayByteLength=false;native.CorruptArrayObjectSize=true;
        Throws<UnsupportedSignatureException>(()=>native.Api.CreateSingleArrayForClass(new[]{.5f},native.ClassOf(native.Single)));Equal(0,native.ActiveRoots);
        native.CorruptArrayObjectSize=false;native.ReturnWrongArrayClass=true;
        Throws<UnsupportedSignatureException>(()=>native.Api.CreateSingleArrayForClass(new[]{.5f},native.ClassOf(native.Single)));Equal(0,native.ActiveRoots);
    }),
    ("Single arrays refuse nonfloat elements and nonfinite samples before allocating", () => {
        using var native=new FakeNative();
        Throws<ArgumentException>(()=>native.Api.CreateSingleArrayForClass(new[]{float.NaN},native.ClassOf(native.Single)));
        Throws<UnsupportedSignatureException>(()=>native.Api.CreateSingleArrayForClass(new[]{.5f},native.ClassOf(native.Int32)));
        native.ArrayElementBytes=8;
        Throws<UnsupportedSignatureException>(()=>native.Api.CreateSingleArrayForClass(new[]{.5f},native.ClassOf(native.Single)));Equal(0,native.ArrayAllocations);
    }),
    ("Single arrays require a valid main thread and permit empty bounded buffers", () => {
        using var native=new FakeNative();Exception? cross=null;
        var thread=new Thread(()=>{try{native.Api.CreateSingleArrayForClass(new[]{1f},native.ClassOf(native.Single));}catch(Exception ex){cross=ex;}});thread.Start();thread.Join();
        Check(cross is InvalidOperationException);Equal(0,native.ArrayAllocations);
        using var empty=native.Api.CreateSingleArrayForClass(Array.Empty<float>(),native.ClassOf(native.Single));Check(empty.Target!=IntPtr.Zero);
    }),
    ("Successful call roots receiver and references only for invocation lifetime", () => {
        using var native = new FakeNative(); native.Parameters = new[] { native.Object };
        native.Api.Invoke(native.Method, native.Instance, A.Ref(native.OtherObject));
        Equal(1, native.Invocations); Equal(2, native.NewRoots); Equal(2, native.FreedRoots); Equal(0, native.ActiveRoots);
    }),
    ("Native exception result releases roots and consumed arguments", () => {
        using var native = new FakeNative(); native.ExceptionOnInvoke = new IntPtr(99); native.Parameters = new[] { native.Int32 };
        var argument = A.Value(1); Throws<InvalidOperationException>(() => native.Api.Invoke(native.Method, native.Instance, argument));
        Equal(1, native.Invocations); Equal(0, native.ActiveRoots); Throws<ObjectDisposedException>(() => { _ = argument.Pointer; });
    }),
    ("Managed invocation exception releases roots without retry", () => {
        using var native = new FakeNative(); native.ThrowOnInvoke = true;
        Throws<InvalidOperationException>(() => native.Api.Invoke(native.Method, native.Instance)); Equal(1, native.Invocations); Equal(0, native.ActiveRoots);
    }),
    ("Verified runtime rejects calls from other threads", () => {
        using var native = new FakeNative(); Exception? caught = null;
        var thread = new Thread(() => { try { native.Api.Invoke(native.Method, native.Instance); } catch (Exception ex) { caught = ex; } });
        thread.Start(); thread.Join(); Check(caught is InvalidOperationException); Equal(0, native.Invocations);
    }),
    ("Thread access rejects unbound use and cannot be rebound", () => {
        var access = new NativeThreadAccess(); Throws<InvalidOperationException>(access.Verify); access.BindCurrentThread(); access.Verify();
        Exception? caught = null; var thread = new Thread(() => { try { access.BindCurrentThread(); } catch (Exception ex) { caught = ex; } });
        thread.Start(); thread.Join(); Check(caught is InvalidOperationException);
    }),
    ("Root handle target validates invalidation and frees once", () => {
        var frees = 0; var target = new IntPtr(123);
        var root = new NativeRuntime.RootHandle(7, _ => frees++, _ => target); Equal(target, root.Target);
        target = IntPtr.Zero; Throws<TargetExpiredException>(() => { _ = root.Target; });
        root.Dispose(); root.Dispose(); Equal(1, frees); Throws<TargetExpiredException>(() => { _ = root.Target; });
        Throws<TargetExpiredException>(() => new NativeRuntime.RootHandle(0, _ => frees++));
    }),
    ("Root owner prevents a wrong-thread free and permits owner cleanup", () => {
        var owner = new NativeThreadAccess(); owner.BindCurrentThread(); var frees = 0;
        var root = new NativeRuntime.RootHandle(9, _ => frees++, _ => new IntPtr(5), owner.Verify);
        Exception? caught = null; var thread = new Thread(() => { try { root.Dispose(); } catch (Exception ex) { caught = ex; } });
        thread.Start(); thread.Join(); Check(caught is InvalidOperationException); Equal(0, frees);
        Equal(new IntPtr(5), root.Target); root.Dispose(); Equal(1, frees);
    }),
    ("Strong root target survives managed collection in handle model", () => {
        var handle = GCHandle.Alloc(new object(), GCHandleType.Normal); var weak = new WeakReference(handle.Target!);
        var root = new NativeRuntime.RootHandle(1, _ => handle.Free(), _ => handle.IsAllocated && handle.Target is not null ? new IntPtr(1) : IntPtr.Zero);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Check(weak.IsAlive); Equal(new IntPtr(1), root.Target); root.Dispose();
    })
};
checks = checks.Concat(ArrayWindowRegression.Cases).Concat(ExactConstructionRegression.Cases)
    .Concat(ExactFieldWriteRegression.Cases).Concat(ArgumentThreadCleanupRegression.Cases).Concat(StaticMetadataRegression.Cases)
    .Concat(SingleArrayPublicationRegression.Cases).Concat(Vector3ArrayPublicationRegression.Cases).Concat(Vector3BufferRegression.Cases).Concat(ListenerMetadataRegression.LineSetPositionsCases).Concat(NativePublicationRegression.Cases).Concat(VoidPublicationRegression.Cases)
    .Concat(GuardedFieldPublicationRegression.Cases)
    .Concat(ReferencePublicationAdmissionRegression.Cases)
    .Concat(RendererQuaternionRegression.Cases)
    .Concat(ValuePublicationRegression.Cases).Concat(NativeInitializationAbiFixtureRegression.Cases).Concat(NativeJudgeActivityRegression.Cases).Concat(NativeCoroutineAbiRegression.Cases)
    .Concat(ListenerMetadataRegression.Cases)
    .Concat(ListenerMetadataRegression.TweenTargetDeathCases)
    .Concat(NativeTweenRetirementContextRegression.Cases)
    .Concat(NativeVoidAbiRegression.Cases).Concat(NativeBootstrappedVoidRegression.Cases).Concat(NativeEngineNodeDispatcherTests.Cases)
    .Concat(NativeEnginePublicationTests.Cases)
    .Concat(NativeEngineRuntimeCoordinatorTests.Cases)
    .Concat(NativeEngineRuntimeSubscriptionLifetimeTests.Cases)
    .Concat(UnityCoroutineOwnerRegression.Cases)
    .Concat(UnityObjectBackingRegression.Cases).Concat(NativeFirstInstallMappedRegression.TickAdmissionCases).ToArray();
var failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + check.Name + ": " + ex); }
}
Console.WriteLine($"Native runtime checks: {checks.Length - failed}/{checks.Length} passed.");
return failed == 0 ? 0 : 1;

static NativeTypeLayout Color() => new("UnityEngine.Color", NativeValueKind.Struct, 16, Fields: new[] {
    new NativeFieldLayout("r", "System.Single", 0), new NativeFieldLayout("g", "System.Single", 4),
    new NativeFieldLayout("b", "System.Single", 8), new NativeFieldLayout("a", "System.Single", 12) });
static void RoundTrip<T>(T value) where T : struct { using var argument = A.Value(value); Equal(value, NativeRuntime.ReadValue<T>(argument.Pointer)); }
static void AdapterStructRoundTrip(string name,object[] fields,NativeTypeLayout native)
{
    var type=Assembly.Load("RizToolbox.Adapter").GetType("RizToolbox.Adapter."+name,throwOnError:true)!;
    var value=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,fields,null)!;
    using var argument=(A)typeof(A).GetMethod("Value",BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(type).Invoke(null,new[]{value})!;
    NativeAbi.ValidateArgument(native,argument);
    var actual=typeof(NativeRuntime).GetMethod("ReadValue",BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(type).Invoke(null,new object[]{argument.Pointer});
    Equal(value,actual);
}
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Check failed."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
enum ByteEnum : byte { High = 255 }
enum LongEnum : long { Low = long.MinValue }
readonly record struct ReferenceStruct(string Text);
[StructLayout(LayoutKind.Sequential)] readonly record struct FloatPair(float X, float Y);
sealed class CountingDisposable : IDisposable { internal int Count; public void Dispose() => Count++; }
