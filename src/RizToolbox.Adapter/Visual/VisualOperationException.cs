using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Visual;

// Internal failures keep their machine-readable identity until the public
// result boundary. Human-readable exception text is never parsed for a code.
internal sealed class VisualOperationException : InvalidOperationException
{
    internal ErrorCode Code { get; }
    internal bool Retryable { get; }
    internal VisualOperationException(ErrorCode code, string message, bool retryable = false) : base(message)
        => (Code, Retryable) = (code, retryable);
}
