using System.Text;
using System.Text.Json;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Copied first-cause text for bounded status rows. The original exception
/// remains in the error log; reading status never replaces it with a later health failure.</summary>
internal static class LocalRunFailureDiagnostics
{
    internal const int MaximumEncodedLength = 384;

    internal static string Describe(string context, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var cause = error;
        for (var depth = 0; depth < 8 && cause.InnerException is { } inner && !ReferenceEquals(cause, inner); ++depth)
            cause = inner;
        // Exception messages can include newlines or unbounded native details. Keep
        // one bounded line in capability/snapshot diagnostics, including escaped JSON.
        var message = cause.Message;
        var line = new StringBuilder(Math.Min(message.Length, MaximumEncodedLength));
        for (var index = 0; index < message.Length && index < MaximumEncodedLength; ++index)
            line.Append(char.IsControl(message[index]) ? ' ' : message[index]);
        return Limit(context + ": " + cause.GetType().Name + ": " + line);
    }

    private static string Limit(string value)
    {
        var count = Math.Min(value.Length, MaximumEncodedLength);
        if (count == value.Length && JsonEncodedText.Encode(value).EncodedUtf8Bytes.Length <= MaximumEncodedLength)
            return value;
        var low = 0;
        while (low < count)
        {
            var middle = (low + count + 1) / 2;
            if (JsonEncodedText.Encode(value.AsSpan(0, middle)).EncodedUtf8Bytes.Length <= MaximumEncodedLength - 3)
                low = middle;
            else count = middle - 1;
        }
        if (low != 0 && char.IsHighSurrogate(value[low - 1])) --low;
        return value[..low] + "...";
    }
}
