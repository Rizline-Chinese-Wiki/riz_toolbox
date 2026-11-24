using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.GameCamera;

/// <summary>Exact declared non-injected localScale Vector3; X writes preserve a fresh Y/Z.</summary>
internal sealed record NativeCameraScaleAccessors(IntPtr Read, IntPtr Write)
{
    internal NativeVector3 ReadScale(NativeRuntime api, NativeRuntime.RootHandle transform)
        => api.InvokeValueWithPublication<NativeVector3>(Read, transform.Target);

    internal void WriteX(NativeRuntime api, NativeRuntime.RootHandle transform, float value, Action requireCurrent)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Camera local scale X must be finite.");
        requireCurrent();
        var fresh = ReadScale(api, transform);
        // A native getter can reenter game code. Recheck the complete graph before the setter.
        requireCurrent();
        api.InvokeVoidWithPublication(Write, transform.Target, NativeRuntime.Arg.Value(new NativeVector3(value, fresh.Y, fresh.Z)));
    }

    internal static NativeCameraScaleAccessors Bind(NativeRuntime api, IntPtr transformClass)
    {
        var read = api.Method(transformClass, "get_localScale", 0, false, Array.Empty<string>());
        var write = api.Method(transformClass, "set_localScale", 1, false, new[] { "UnityEngine.Vector3" });
        // Invocation lookup may be cached. Fresh enumeration must still prove unique current
        // declarations, and those declarations must be the same MethodInfos we will invoke.
        var readMetadata = api.InspectDeclaredMethod(transformClass, "get_localScale", Array.Empty<string>(), "UnityEngine.Vector3", IntPtr.Zero);
        var writeMetadata = api.InspectDeclaredMethod(transformClass, "set_localScale", new[] { "UnityEngine.Vector3" }, "System.Void", IntPtr.Zero);
        if (readMetadata.MethodInfo != read || writeMetadata.MethodInfo != write
            || !api.IsMethod(read, "UnityEngine", "Transform", "get_localScale")
            || !api.IsMethod(write, "UnityEngine", "Transform", "set_localScale")
            || api.DescribeReturnType(read) != "UnityEngine.Vector3" || api.DescribeReturnType(write) != "System.Void"
            || readMetadata.ParameterTypes.Count != 0 || !writeMetadata.ParameterTypes.SequenceEqual(new[] { "UnityEngine.Vector3" })
            || (readMetadata.Flags & 0x10) != 0 || (writeMetadata.Flags & 0x10) != 0
            || readMetadata.GenericMethod || writeMetadata.GenericMethod || readMetadata.InflatedMethod || writeMetadata.InflatedMethod)
            throw new UnsupportedSignatureException("Camera localScale requires exact current Transform Vector3/void accessors.");
        return new(read, write);
    }
}
