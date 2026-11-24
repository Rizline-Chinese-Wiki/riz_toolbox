namespace RizToolbox.Runtime.Hooks;

internal enum CoroutineStopOperation { Enumerator, Handle, All, ManagedHandle, ManagedEnumerator, Named }
internal sealed record CoroutineStopDeclaration(CoroutineStopOperation Operation, string Method, int Rva, string? Parameter)
{
    internal NativeOperationKind Kind => Parameter is null ? NativeOperationKind.Void0 : NativeOperationKind.Reference1;
}

/// <summary>Public wrappers inline the icall dispatch; observing internal
/// wrappers alone misses them. These declarations record requests only.</summary>
internal static class CoroutineStopProfile
{
    internal static readonly IReadOnlyList<CoroutineStopDeclaration> All = Array.AsReadOnly(new[] {
        new CoroutineStopDeclaration(CoroutineStopOperation.Enumerator, "StopCoroutine", 0x153D900, "System.Collections.IEnumerator"),
        new CoroutineStopDeclaration(CoroutineStopOperation.Handle, "StopCoroutine", 0x153D7F0, "UnityEngine.Coroutine"),
        new CoroutineStopDeclaration(CoroutineStopOperation.All, "StopAllCoroutines", 0x153D6C0, null),
        new CoroutineStopDeclaration(CoroutineStopOperation.ManagedHandle, "StopCoroutineManaged", 0x153D750, "UnityEngine.Coroutine"),
        new CoroutineStopDeclaration(CoroutineStopOperation.ManagedEnumerator, "StopCoroutineFromEnumeratorManaged", 0x153D700, "System.Collections.IEnumerator"),
        new CoroutineStopDeclaration(CoroutineStopOperation.Named, "StopCoroutine", 0x153D7A0, "System.String")
    });
    internal static CoroutineStopDeclaration Get(CoroutineStopOperation operation)
        => All.SingleOrDefault(value => value.Operation == operation) ?? throw new UnsupportedSignatureException("Coroutine stop declaration is not allowlisted.");
}
