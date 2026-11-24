using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

/// <summary>Internal synchronous handoff when a helper must throw across an
/// adapter implementation boundary. Preserve the original portable failure;
/// an outer Result must not recategorize a conflict as a resource load error.</summary>
internal sealed class AdapterOperationException : InvalidOperationException
{
    internal OperationError Error { get; }
    internal AdapterOperationException(OperationError error) : base(error.Message) => Error = error;
}
