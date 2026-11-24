namespace RizToolbox.Runtime;

/// <summary>A stable observation of a strongly held TweenInfo wrapper. Native
/// Unity death, IL2CPP wrapper collection and root retirement are separate facts.</summary>
internal readonly record struct NativeTweenDeathObservation(long ManagedReceiver, IntPtr ManagedClass,
    IntPtr CachedPointerField, long CachedNative, bool Alive, int Frame);

internal sealed partial class NativeRuntime
{
    /// <summary>Reads official cached-pointer metadata and the exact pinned Unity
    /// alive/frame methods. The caller owns the existing root throughout. This
    /// never destroys an object, reads an old native address, or releases that root.</summary>
    internal NativeTweenDeathObservation ObserveRootedTweenDeath(RootHandle root, IntPtr module)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(root);
        var receiver = root.Target;
        if (IntPtr.Size != 8 || module == IntPtr.Zero || receiver == IntPtr.Zero)
            throw new UnsupportedSignatureException("Tween death observation requires this exact rooted x64 receiver and pinned module.");
        var metadata = InspectTweenAdmissionMetadata(module);
        if (ObjectClass(receiver) != metadata.TweenClass)
            throw new UnsupportedSignatureException("The retained death receiver is not the exact official TweenInfo class.");
        var unityObject = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        var time = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
        var alive = InspectDeclaredStaticMethod(unityObject, "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", module);
        var frame = InspectDeclaredStaticMethod(time, "get_frameCount", Array.Empty<string>(), "System.Int32", module);
        RequireTweenDeathMethod(alive, 0x15409B0, "op_Implicit"); RequireTweenDeathMethod(frame, 0x155EFD0, "get_frameCount");
        var beforeFrame = InvokeValueWithPublication<int>(frame.MethodInfo, IntPtr.Zero);
        var before = ObserveUnityCoroutineOwner(root, receiver.ToInt64());
        var beforeAlive = InvokeValueWithPublication<bool>(alive.MethodInfo, IntPtr.Zero, Arg.Ref(receiver));
        var afterAlive = InvokeValueWithPublication<bool>(alive.MethodInfo, IntPtr.Zero, Arg.Ref(receiver));
        var after = ObserveUnityCoroutineOwner(root, receiver.ToInt64());
        var afterFrame = InvokeValueWithPublication<int>(frame.MethodInfo, IntPtr.Zero);
        if (beforeFrame < 0 || beforeFrame != afterFrame || before != after || beforeAlive != afterAlive
            || before.ManagedClass != metadata.TweenClass || before.ManagedReceiver != receiver.ToInt64()
            || root.Target != receiver || ObjectClass(receiver) != metadata.TweenClass
            || ReadExactField<IntPtr>(root, metadata.CachedPointerField).ToInt64() != before.CachedNativeOwner
            || InspectTweenAdmissionMetadata(module) != metadata || root.Target != receiver)
            throw new InvalidOperationException("Tween frame, root, official field, class or alive/cached values changed across the exact observation.");
        if (beforeAlive != (before.CachedNativeOwner != 0))
            throw new InvalidOperationException("The exact TweenInfo alive result disagrees with its stable official cached pointer.");
        return new(receiver.ToInt64(), metadata.TweenClass, metadata.CachedPointerField.Field,
            before.CachedNativeOwner, beforeAlive, beforeFrame);
    }
    private static void RequireTweenDeathMethod(NativeMetadataMethod method, long rva, string name)
    {
        if (method.Entry?.Rva != rva || (method.Flags & 0x10u) == 0 || (method.Flags & 0x400u) != 0
            || method.GenericMethod || method.InflatedMethod)
            throw new UnsupportedSignatureException("Tween death observation requires the exact non-generic static Unity method: " + name);
    }
}
