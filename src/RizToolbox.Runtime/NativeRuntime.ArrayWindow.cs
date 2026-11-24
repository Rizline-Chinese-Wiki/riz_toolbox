namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Reads at most maximum elements from the current rooted array.
    /// A cursor wraps directly by index; skipped prefixes never call GetValue.</summary>
    internal IEnumerable<IntPtr> ArrayItems(IntPtr array, NativeArrayScanCursor cursor, int maximum)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        VerifyAccess();
        if (array == IntPtr.Zero)
        {
            // Reset a now-empty discovery source without retaining its old array.
            foreach (var unused in cursor.Read(0, maximum, _ => IntPtr.Zero)) yield return unused;
            yield break;
        }
        using var root = Root(array);
        var length = ValidatedArrayLength(root.Target);
        foreach (var value in cursor.Read(length, maximum,
            index => CallTyped(root.Target, "GetValue", new[] { "System.Int32" }, Arg.Value(index))))
            yield return value;
    }

    private int ValidatedArrayLength(IntPtr array)
    {
        if (_typeCode(_classType(_objectClass(array))) is not (20 or 29))
            throw new UnsupportedSignatureException("ArrayItems requires an actual native array.");
        if (Unbox<int>(Call(array, "get_Rank")) != 1)
            throw new UnsupportedSignatureException("Only one-dimensional arrays are supported.");
        var length = Unbox<int>(Call(array, "get_Length"));
        if (length < 0 || length > NativeArrayScanCursor.MaximumArrayLength)
            throw new InvalidOperationException("Unexpected array size.");
        return length;
    }
}
