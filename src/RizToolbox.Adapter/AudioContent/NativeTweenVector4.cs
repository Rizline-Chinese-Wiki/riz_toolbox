using System.Runtime.InteropServices;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>The exact four-float UnityEngine.Vector4 carrier for TweenInfo.fixedStart.
/// Color has a different native identity despite occupying the same number of bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeTweenVector4(float X, float Y, float Z, float W);
