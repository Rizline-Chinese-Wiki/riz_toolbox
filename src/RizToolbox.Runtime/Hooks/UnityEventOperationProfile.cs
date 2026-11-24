namespace RizToolbox.Runtime.Hooks;

internal enum UnityEventOperation
{
    AddListener, AddCall, RemoveListener, RemoveAllListeners, DirtyPersistentCalls, RebuildPersistentCalls,
    AddRuntimeCall, AddPersistentCall, RemoveCall, ClearRuntimeCalls, ClearPersistentCalls, InitializePersistentCalls,
    AddDelegate, RemoveDelegate
}
internal enum UnityEventOperationReceiver { Event, Calls, PersistentGroup, Invokable }
internal sealed record UnityEventOperationDeclaration(UnityEventOperation Operation, string Class, string Method,
    int Rva, UnityEventOperationReceiver Receiver, IReadOnlyList<string> Parameters)
{
    internal NativeOperationKind Kind => Parameters.Count switch {
        0 => NativeOperationKind.Void0, 1 => NativeOperationKind.Reference1, 2 => NativeOperationKind.Reference2,
        _ => throw new InvalidOperationException("An allowlisted operation has unsupported arity.") };
}

/// <summary>Non-generic methods in the pinned Unity build. Upper and lower
/// methods are both observed because several upper methods inline list writes.
/// This list does not assert all mutation writers or delegate leaves are closed.</summary>
internal static class UnityEventOperationProfile
{
    private static UnityEventOperationDeclaration Row(UnityEventOperation op, string cls, string method, int rva,
        UnityEventOperationReceiver receiver, params string[] parameters)
        => new(op, cls, method, rva, receiver, Array.AsReadOnly(parameters));
    internal static readonly IReadOnlyList<UnityEventOperationDeclaration> All = Array.AsReadOnly(new[] {
        Row(UnityEventOperation.AddListener, "UnityEvent", "AddListener", 0x15640A0, UnityEventOperationReceiver.Event, "UnityEngine.Events.UnityAction"),
        Row(UnityEventOperation.AddCall, "UnityEventBase", "AddCall", 0x15630E0, UnityEventOperationReceiver.Event, "UnityEngine.Events.BaseInvokableCall"),
        Row(UnityEventOperation.RemoveListener, "UnityEventBase", "RemoveListener", 0x1563C50, UnityEventOperationReceiver.Event, "System.Object", "System.Reflection.MethodInfo"),
        Row(UnityEventOperation.RemoveAllListeners, "UnityEventBase", "RemoveAllListeners", 0x1563B80, UnityEventOperationReceiver.Event),
        Row(UnityEventOperation.DirtyPersistentCalls, "UnityEventBase", "DirtyPersistentCalls", 0x15631A0, UnityEventOperationReceiver.Event),
        Row(UnityEventOperation.RebuildPersistentCalls, "UnityEventBase", "RebuildPersistentCallsIfNeeded", 0x1563B40, UnityEventOperationReceiver.Event),
        Row(UnityEventOperation.AddRuntimeCall, "InvokableCallList", "AddListener", 0x154DED0, UnityEventOperationReceiver.Calls, "UnityEngine.Events.BaseInvokableCall"),
        Row(UnityEventOperation.AddPersistentCall, "InvokableCallList", "AddPersistentInvokableCall", 0x154DF90, UnityEventOperationReceiver.Calls, "UnityEngine.Events.BaseInvokableCall"),
        Row(UnityEventOperation.RemoveCall, "InvokableCallList", "RemoveListener", 0x154E280, UnityEventOperationReceiver.Calls, "System.Object", "System.Reflection.MethodInfo"),
        Row(UnityEventOperation.ClearRuntimeCalls, "InvokableCallList", "Clear", 0x154E110, UnityEventOperationReceiver.Calls),
        Row(UnityEventOperation.ClearPersistentCalls, "InvokableCallList", "ClearPersistent", 0x154E050, UnityEventOperationReceiver.Calls),
        Row(UnityEventOperation.InitializePersistentCalls, "PersistentCallGroup", "Initialize", 0x1550EE0, UnityEventOperationReceiver.PersistentGroup,
            "UnityEngine.Events.InvokableCallList", "UnityEngine.Events.UnityEventBase"),
        Row(UnityEventOperation.AddDelegate, "InvokableCall", "add_Delegate", 0x154E890, UnityEventOperationReceiver.Invokable,
            "UnityEngine.Events.UnityAction"),
        Row(UnityEventOperation.RemoveDelegate, "InvokableCall", "remove_Delegate", 0x154E930, UnityEventOperationReceiver.Invokable,
            "UnityEngine.Events.UnityAction")
    });
    internal static UnityEventOperationDeclaration Get(UnityEventOperation operation)
        => All.SingleOrDefault(row => row.Operation == operation) ?? throw new UnsupportedSignatureException("UnityEvent mutation is not allowlisted.");
}
