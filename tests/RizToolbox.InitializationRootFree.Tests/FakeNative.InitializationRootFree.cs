sealed partial class FakeNative
{
    internal uint LatestInitializationRootFor(IntPtr target)
        => _handles.Where(row => row.Value == target).Max(row => row.Key);
}
