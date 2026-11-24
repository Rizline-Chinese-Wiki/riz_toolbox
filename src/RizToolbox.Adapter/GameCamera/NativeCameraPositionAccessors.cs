using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.GameCamera;

/// <summary>Exact declared non-injected Vector3 property; X writes always preserve a fresh Y/Z.</summary>
internal sealed record NativeCameraPositionAccessors(IntPtr Read, IntPtr Write)
{
    internal NativeVector3 ReadPosition(NativeRuntime api, NativeRuntime.RootHandle transform)
        => api.InvokeValueWithPublication<NativeVector3>(Read, transform.Target);
    internal void WriteX(NativeRuntime api, NativeRuntime.RootHandle transform, float value, Action requireCurrent)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Camera X must be finite.");
        requireCurrent();
        var fresh = ReadPosition(api, transform);
        // A native getter can reenter game code. Recheck before the subsequent setter.
        requireCurrent();
        api.InvokeVoidWithPublication(Write, transform.Target, NativeRuntime.Arg.Value(new NativeVector3(value, fresh.Y, fresh.Z)));
    }
    internal static NativeCameraPositionAccessors Bind(NativeRuntime api, IntPtr transformClass)
    {
        var read = api.Method(transformClass, "get_localPosition", 0, false, Array.Empty<string>());
        var write = api.Method(transformClass, "set_localPosition", 1, false, new[] { "UnityEngine.Vector3" });
        // Method lookup may be cached. Inspect the complete current metadata as well;
        // a saved pointer is never evidence of today's parameter signature.
        var readMetadata = api.InspectDeclaredMethod(transformClass, "get_localPosition", Array.Empty<string>(), "UnityEngine.Vector3", IntPtr.Zero);
        var writeMetadata = api.InspectDeclaredMethod(transformClass, "set_localPosition", new[] { "UnityEngine.Vector3" }, "System.Void", IntPtr.Zero);
        if (!api.IsMethod(read, "UnityEngine", "Transform", "get_localPosition")
            || !api.IsMethod(write, "UnityEngine", "Transform", "set_localPosition")
            || api.DescribeReturnType(read) != "UnityEngine.Vector3" || api.DescribeReturnType(write) != "System.Void"
            || readMetadata.ParameterTypes.Count != 0 || !writeMetadata.ParameterTypes.SequenceEqual(new[] { "UnityEngine.Vector3" })
            || (readMetadata.Flags & 0x10) != 0 || (writeMetadata.Flags & 0x10) != 0
            || readMetadata.GenericMethod || writeMetadata.GenericMethod || readMetadata.InflatedMethod || writeMetadata.InflatedMethod)
            throw new UnsupportedSignatureException("Camera localPosition requires exact Transform Vector3/void accessors.");
        return new(read, write);
    }
}
