using System.Runtime.InteropServices;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Exact four-float UnityEngine.Quaternion carrier for native renderer
/// pool transforms. Keep raw components: normalization would change a captured
/// baseline, and a same-sized Vector4 or Color is not this ABI type.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeRendererQuaternion(float X, float Y, float Z, float W);
