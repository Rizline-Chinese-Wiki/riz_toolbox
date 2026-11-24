namespace RizToolbox.Runtime;

/// <summary>A bounded round-robin cursor. It stores no array, object pointer or
/// unrooted candidate between discovery passes.</summary>
internal sealed class NativeArrayScanCursor
{
    internal const int MaximumArrayLength = 50_000;
    private int _position;

    internal IEnumerable<T> Read<T>(int length, int maximum, Func<int, T> read)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        ArgumentNullException.ThrowIfNull(read);
        _position = length == 0 ? 0 : _position % length;
        var remaining = Math.Min(length, maximum);
        while (remaining-- > 0)
        {
            var index = _position;
            // Advance before the native attempt. A failed element or a consumer
            // disposing mid-window must not restart at the same bad prefix.
            _position = index == length - 1 ? 0 : index + 1;
            yield return read(index);
        }
    }
}
