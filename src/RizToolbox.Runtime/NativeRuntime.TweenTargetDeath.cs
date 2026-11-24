namespace RizToolbox.Runtime;

// This is a concrete Unity Object sample, not a TweenInfo/source death ticket.
internal readonly record struct NativeTweenTargetDeathObservation(long Object, IntPtr Class,
    IntPtr CachedPointerField, long CachedNative, bool Alive, int Frame);

internal sealed partial class NativeRuntime
{
    internal NativeTweenTargetDeathObservation ObserveRootedTweenTargetDeath(RootHandle root, IntPtr module)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(root);
        var identity = root.Target;
        if (IntPtr.Size != 8 || module == IntPtr.Zero || identity == IntPtr.Zero)
            throw new UnsupportedSignatureException("Target death needs its actual held Unity Object and pinned x64 module.");
        var unityObject = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        var actualClass = ObjectClass(identity); RequireAssignable(unityObject, actualClass);
        var cached = InspectExactDeclaredReadField(unityObject, "m_CachedPtr", "System.IntPtr", false, 0x10);
        var alive = InspectDeclaredStaticMethod(unityObject, "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", module);
        var time = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
        var frame = InspectDeclaredStaticMethod(time, "get_frameCount", Array.Empty<string>(), "System.Int32", module);
        RequireTweenDeathMethod(alive, 0x15409B0, "op_Implicit"); RequireTweenDeathMethod(frame, 0x155EFD0, "get_frameCount");
        var firstFrame = InvokeValueWithPublication<int>(frame.MethodInfo, IntPtr.Zero);
        var firstCached = ReadExactField<IntPtr>(root, cached);
        var firstAlive = InvokeValueWithPublication<bool>(alive.MethodInfo, IntPtr.Zero, Arg.Ref(identity));
        var secondAlive = InvokeValueWithPublication<bool>(alive.MethodInfo, IntPtr.Zero, Arg.Ref(identity));
        var secondCached = ReadExactField<IntPtr>(root, cached);
        var secondFrame = InvokeValueWithPublication<int>(frame.MethodInfo, IntPtr.Zero);
        if (firstFrame < 0 || firstFrame != secondFrame || firstCached != secondCached || firstAlive != secondAlive
            || firstAlive != (firstCached != IntPtr.Zero) || root.Target != identity || ObjectClass(identity) != actualClass
            || InspectExactDeclaredReadField(unityObject, "m_CachedPtr", "System.IntPtr", false, 0x10) != cached)
            throw new InvalidOperationException("Target root/class/cached pointer/alive/frame changed across the concrete read.");
        return new(identity.ToInt64(), actualClass, cached.Field, firstCached.ToInt64(), firstAlive, firstFrame);
    }
}
