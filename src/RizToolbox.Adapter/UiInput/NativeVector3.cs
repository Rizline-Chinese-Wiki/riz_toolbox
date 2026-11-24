using System.Runtime.InteropServices;
namespace RizToolbox.Adapter.UiInput;
[StructLayout(LayoutKind.Sequential)] internal readonly record struct NativeVector3(float X, float Y, float Z);
