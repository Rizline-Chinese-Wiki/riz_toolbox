using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;

var checks = 0;
void Check(bool condition, string reason)
{ ++checks; if (!condition) throw new InvalidOperationException(reason); }
void Reject<T>(Action action) where T : Exception
{ ++checks; try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
try
{
    if (args.Length != 1) throw new ArgumentException("Supply the separately frozen owned Unity backing r2 fixture path.");
    var path = Path.GetFullPath(args[0]);
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(
        "22c3527d3a0be10ec1b5681f70855e0690c6396b7738533bbd62e841bae35c87", StringComparison.OrdinalIgnoreCase), "Wrong fixture image.");
    var module = NativeLibrary.Load(path);
    using var fixture = new FakeNative();
    fixture.ConfigureUnityBacking(module);
    var api = fixture.GameplayApi;
    var source = fixture.GameplaySource; var clip = fixture.GameplayClip;
    var sourceClass = api.ObjectClass(source); var clipClass = api.ObjectClass(clip);
    Check(sourceClass.ToInt64() > 0x10000 && clipClass.ToInt64() > 0x10000 && fixture.UnityObjectClass.ToInt64() > 0x10000
        && sourceClass != clipClass && sourceClass != fixture.UnityObjectClass && clipClass != fixture.UnityObjectClass,
        "The three exact Unity classes require distinct real allocations.");
    Check(Marshal.ReadIntPtr(fixture.UnityObjectClass, 0xb8) == fixture.UnityStaticFields
        && Marshal.ReadInt32(fixture.UnityStaticFields) == 8, "Object class/static backing is not the actual initialized native allocation.");
    foreach (var host in new[] { fixture.GameplayController, fixture.GameplayInformation, fixture.GameplayProgress, fixture.GameplayPause })
        Check(api.Unbox<bool>(api.CallStatic(fixture.UnityObjectClass, "op_Implicit", NativeRuntime.Arg.Ref(host))),
            "The owned scene host lacks its exact Component/Behaviour/MonoBehaviour/Object metadata ancestry.");
    Check(!api.Unbox<bool>(api.CallStatic(fixture.UnityObjectClass, "op_Implicit", NativeRuntime.Arg.Ref(IntPtr.Zero))),
        "The fixture Unity Object null conversion returned true.");
    Reject<UnsupportedSignatureException>(() => api.CallStatic(fixture.UnityObjectClass, "op_Implicit", NativeRuntime.Arg.Ref(fixture.Instance)));
    using (var sourceRoot = api.Root(source))
    using (var clipRoot = api.Root(clip))
    {
        var rootCount = fixture.NewRoots;
        var sourceObservation = api.ReadUnityObjectBackingObservation(sourceRoot, source.ToInt64(), sourceClass);
        var clipObservation = api.ReadUnityObjectBackingObservation(clipRoot, clip.ToInt64(), clipClass);
        Check(sourceObservation.CachedNativePointer == Marshal.ReadIntPtr(source, 16).ToInt64()
            && clipObservation.CachedNativePointer == Marshal.ReadIntPtr(clip, 16).ToInt64()
            && sourceObservation.NativeInstanceIdOffset == 8 && clipObservation.NativeInstanceIdOffset == 8
            && sourceObservation.UnityObjectClass == fixture.UnityObjectClass && fixture.NewRoots == rootCount && fixture.ActiveRoots == 2,
            "The official projection did not read both actual wrapper fields without inventing roots.");
        Check(api.Unbox<float>(api.Call(source, "get_pitch")) == 1
            && api.Unbox<float>(api.Call(source, "get_time")) == 3
            && api.Call(source, "get_clip") == clip
            && api.Unbox<float>(api.Call(clip, "get_length")) == 120,
            "Audio API readback did not use the shifted actual native source/clip payloads.");
        var oldSourceBacking = Marshal.ReadIntPtr(source, 16);
        Check(fixture.MutateUnitySource(2, 702) && Marshal.ReadIntPtr(source, 16) != oldSourceBacking
            && Marshal.ReadInt32(Marshal.ReadIntPtr(source, 16), 8) == 702
            && api.ReadUnityObjectBackingObservation(sourceRoot, source.ToInt64(), sourceClass).CachedNativePointer == Marshal.ReadIntPtr(source, 16).ToInt64(),
            "Nonzero source backing replacement did not preserve the wrapper and use fresh real storage.");
        var oldClipBacking = Marshal.ReadIntPtr(clip, 16);
        Check(fixture.MutateUnityClip(4, -704) && Marshal.ReadIntPtr(clip, 16) == oldClipBacking
            && Marshal.ReadInt32(oldClipBacking, 8) == -704
            && api.ReadUnityObjectBackingObservation(clipRoot, clip.ToInt64(), clipClass).CachedNativePointer == oldClipBacking.ToInt64(),
            "Clip ID-only mutation did not preserve its native pointer and write an actual Int32 ID.");
        Check(fixture.MutateUnitySource(1, 0), "Source detach failed.");
        Reject<TargetExpiredException>(() => api.ReadUnityObjectBackingObservation(sourceRoot, source.ToInt64(), sourceClass));
        Check(fixture.MutateUnitySource(3, 0) && api.ReadUnityObjectBackingObservation(sourceRoot, source.ToInt64(), sourceClass).NativeInstanceIdOffset == 8,
            "The actual detached backing could not be reattached.");
        fixture.SetUnityInstanceIdOffset(-1);
        Reject<UnsupportedSignatureException>(() => api.ReadUnityObjectBackingObservation(sourceRoot, source.ToInt64(), sourceClass));
        fixture.SetUnityInstanceIdOffset(8);
        fixture.SetUnityBackingMetadata(cached: true, offset: 24);
        Reject<UnsupportedSignatureException>(() => api.ReadUnityObjectBackingObservation(sourceRoot, source.ToInt64(), sourceClass));
        fixture.SetUnityBackingMetadata(cached: true, offset: 16);
        Check(api.ReadUnityObjectBackingObservation(sourceRoot, source.ToInt64(), sourceClass).CachedNativePointer != 0
            && fixture.AllGameplayReceiversRooted && fixture.ActiveRoots == 2, "Exact metadata restoration or root discipline failed.");
    }
    Check(fixture.ActiveRoots == 0 && fixture.NewRoots == fixture.FreedRoots, "Known spotcheck root cleanup failed.");
    fixture.ReleaseUnityBackingStorage();
    Check(fixture.UnityFixtureRead(0) == fixture.UnityFixtureRead(1) && fixture.UnityFixtureRead(2) == fixture.UnityFixtureRead(3),
        "Owned wrapper/class/static/native backing allocations were not all freed exactly once.");
    Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, fixture.UnityCachedReads, fixture.UnityStaticReads,
        activeRoots = fixture.ActiveRoots, sdkExercised = false, nativeWatchExercised = false, gameExecuted = false, productionAdmission = false }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Console.WriteLine(JsonSerializer.Serialize(new { passed = false, checks, failure = error.ToString() }));
    return 1;
}
