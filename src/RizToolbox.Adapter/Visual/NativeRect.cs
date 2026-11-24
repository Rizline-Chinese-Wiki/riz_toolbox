using System.Runtime.InteropServices;
namespace RizToolbox.Adapter.Visual;
[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRect
{
    internal readonly float X, Y, Width, Height;
    internal NativeRect(float x,float y,float width,float height){X=x;Y=y;Width=width;Height=height;}
}
