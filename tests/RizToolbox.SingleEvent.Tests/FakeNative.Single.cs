using System.Runtime.InteropServices;
sealed partial class FakeNative
{
    internal IntPtr ExportForListenerTests(string name) => ExportPointer(name);
    internal bool SingleRooted(IntPtr value) => _handles.Values.Contains(value);
}
